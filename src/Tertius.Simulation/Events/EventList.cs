using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>
/// Reusable storage for an <see cref="EventList"/>. The scheduler keeps one per worker thread. Once the buffer has
/// grown to the longest list it has held, filling it again never allocates.
/// </summary>
public sealed class EventBuffer
{
    internal Event[] Items;
    internal int Count;
    internal EventMask Mask;
    internal int Version;

    /// <summary>Creates a buffer for <paramref name="capacity"/> events. It grows when needed.</summary>
    public EventBuffer(int capacity = 32) => Items = new Event[Math.Max(capacity, 4)];

    /// <summary>The number of events the buffer holds before it grows.</summary>
    public int Capacity => Items.Length;

    /// <summary>Clears the buffer and returns a list over it. Events that the list adds target <paramref name="owner"/>.</summary>
    public EventList Begin(Entity owner = default)
    {
        Clear();
        return new EventList(this, owner);
    }

    /// <summary>A list over the events already in the buffer, without clearing them. For benchmarks and tests.</summary>
    internal EventList View(Entity owner = default) => new(this, owner);

    internal void Clear()
    {
        // Drop the Extra references so that the buffer does not keep garbage alive.
        Array.Clear(Items, 0, Count);
        Count = 0;
        Mask = default;
        Version++;
    }

    internal ref Event Append()
    {
        if (Count == Items.Length)
            Array.Resize(ref Items, Items.Length * 2);
        Version++;
        return ref Items[Count++];
    }
}

/// <summary>
/// The events of one entity run, in delivery order. Components edit the list in place: they change payloads, remove
/// events and add events. An added event is visible only to the components after the one that added it.
/// The list never allocates in steady state. Enumerating with <see cref="Of{T}"/> gives references into the payloads:
/// <code>
/// foreach (ref var damage in events.Of&lt;Damage&gt;())
///     damage.Amount = MathF.Max(0, damage.Amount - rating);
/// events.RemoveAll(static (ref Damage d) =&gt; d.Amount == 0);
/// </code>
/// </summary>
public ref struct EventList
{
    private readonly EventBuffer buffer;
    private readonly Entity owner;

    internal EventList(EventBuffer buffer, Entity owner)
    {
        this.buffer = buffer;
        this.owner = owner;
    }

    /// <summary>The number of events in the list.</summary>
    public readonly int Count => buffer.Count;

    /// <summary>The running entity. Events that <see cref="Add{T}"/> creates target it.</summary>
    public readonly Entity Owner => owner;

    /// <summary>
    /// A conservative superset of the event types in the list. Adds set bits, and removals recompute it, so after
    /// adds and removals it is exact. The scheduler skips a component whose wants do not overlap it.
    /// </summary>
    public readonly EventMask Mask => buffer.Mask;

    /// <summary>
    /// A reference to an event, to read or edit it in place. The mask does not see a change of <see cref="Event.Type"/>
    /// made through this reference; to turn an event into another type, remove it and add the new one.
    /// </summary>
    public readonly ref Event this[int index]
    {
        get
        {
            if ((uint)index >= (uint)buffer.Count)
                ThrowIndex(index);
            return ref buffer.Items[index];
        }
    }

    /// <summary>The events as a span. It is valid until the list changes.</summary>
    public readonly Span<Event> AsSpan() => buffer.Items.AsSpan(0, buffer.Count);

    /// <summary>Appends an event with this payload. Its target is the running entity.</summary>
    public readonly void Add<T>(in T payload, Entity sender = default) where T : unmanaged, IEventPayload
    {
        int type = EventType<T>.Id;
        ref Event e = ref buffer.Append();
        e = default;
        e.Target = owner;
        e.Sender = sender;
        e.Type = type;
        Unsafe.As<PayloadBytes, T>(ref e.Payload) = payload;
        buffer.Mask.Add(type);
    }

    /// <summary>Appends a copy of an event, with its own target and sender.</summary>
    public readonly void Add(in Event e)
    {
        int type = e.Type;
        buffer.Append() = e;
        buffer.Mask.Add(type);
    }

    /// <summary>Removes the event at <paramref name="index"/>. The other events keep their order.</summary>
    public readonly void RemoveAt(int index)
    {
        int count = buffer.Count;
        if ((uint)index >= (uint)count)
            ThrowIndex(index);
        Event[] items = buffer.Items;
        if (index < count - 1)
            Array.Copy(items, index + 1, items, index, count - index - 1);
        items[count - 1] = default;
        buffer.Count = count - 1;
        buffer.Version++;
        RecomputeMask();
    }

    /// <summary>The payloads of type <typeparamref name="T"/>, as references. Do not add or remove events while enumerating.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly EventsOf<T> Of<T>() where T : unmanaged, IEventPayload => new(buffer);

    /// <summary>Removes every event of type <typeparamref name="T"/> whose payload matches. Returns the number removed.</summary>
    public readonly int RemoveAll<T>(RefPredicate<T> match) where T : unmanaged, IEventPayload
    {
        ArgumentNullException.ThrowIfNull(match);
        int type = EventType<T>.Id;
        if (!buffer.Mask.Contains(type))
            return 0;

        Event[] items = buffer.Items;
        int count = buffer.Count, write = 0, read = 0;
        try
        {
            for (; read < count; read++)
            {
                ref Event e = ref items[read];
                if (e.Type == type && match(ref Unsafe.As<PayloadBytes, T>(ref e.Payload)))
                    continue;
                if (write != read)
                    items[write] = e;
                write++;
            }
        }
        finally
        {
            Compact(items, count, read, write);
        }
        return count - buffer.Count;
    }

    /// <summary>Removes every event that matches. Returns the number removed.</summary>
    public readonly int RemoveAll(RefPredicate<Event> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        Event[] items = buffer.Items;
        int count = buffer.Count, write = 0, read = 0;
        try
        {
            for (; read < count; read++)
            {
                ref Event e = ref items[read];
                if (match(ref e))
                    continue;
                if (write != read)
                    items[write] = e;
                write++;
            }
        }
        finally
        {
            Compact(items, count, read, write);
        }
        return count - buffer.Count;
    }

    /// <summary>True if the list holds an event of type <typeparamref name="T"/>.</summary>
    public readonly bool Contains<T>() where T : unmanaged, IEventPayload => IndexOf<T>() >= 0;

    /// <summary>The index of the first event of type <typeparamref name="T"/> at or after <paramref name="start"/>, or -1.</summary>
    public readonly int IndexOf<T>(int start = 0) where T : unmanaged, IEventPayload
    {
        int type = EventType<T>.Id;
        if (!buffer.Mask.Contains(type))
            return -1;
        Event[] items = buffer.Items;
        for (int i = Math.Max(start, 0); i < buffer.Count; i++)
            if (items[i].Type == type)
                return i;
        return -1;
    }

    /// <summary>The number of events of type <typeparamref name="T"/>.</summary>
    public readonly int CountOf<T>() where T : unmanaged, IEventPayload
    {
        int type = EventType<T>.Id;
        if (!buffer.Mask.Contains(type))
            return 0;
        int n = 0;
        Event[] items = buffer.Items;
        for (int i = 0; i < buffer.Count; i++)
            if (items[i].Type == type)
                n++;
        return n;
    }

    // Finishes a removal pass. If the predicate threw at 'read', the events from there on are kept unchanged.
    private readonly void Compact(Event[] items, int count, int read, int write)
    {
        if (read < count && write != read)
            Array.Copy(items, read, items, write, count - read);
        int newCount = write + (count - read);
        if (newCount < count)
        {
            Array.Clear(items, newCount, count - newCount);
            buffer.Count = newCount;
            RecomputeMask();
        }
        buffer.Version++;
    }

    private readonly void RecomputeMask()
    {
        EventMask mask = default;
        Event[] items = buffer.Items;
        for (int i = 0; i < buffer.Count; i++)
            mask.Add(items[i].Type);
        buffer.Mask = mask;
    }

    [DoesNotReturn]
    private readonly void ThrowIndex(int index) =>
        throw new ArgumentOutOfRangeException(nameof(index), index, $"The event list has {buffer.Count} events.");
}

/// <summary>The payloads of one type in an <see cref="EventList"/>. Its enumerator yields references, so <c>foreach (ref var p in ...)</c> edits in place.</summary>
public readonly ref struct EventsOf<T> where T : unmanaged, IEventPayload
{
    private readonly EventBuffer buffer;

    internal EventsOf(EventBuffer buffer) => this.buffer = buffer;

    /// <summary>Returns the enumerator that <c>foreach</c> uses.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new(buffer);

    /// <summary>The number of events of type <typeparamref name="T"/>.</summary>
    public int Count()
    {
        int n = 0;
        foreach (ref T _ in this)
            n++;
        return n;
    }

    /// <summary>Enumerates references to the payloads of type <typeparamref name="T"/>.</summary>
    public ref struct Enumerator
    {
        private readonly EventBuffer buffer;
        private readonly Span<Event> items;
        private readonly int type;
        private readonly int version;
        private int index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(EventBuffer buffer)
        {
            this.buffer = buffer;
            type = EventType<T>.Id;
            items = buffer.Mask.Contains(type) ? buffer.Items.AsSpan(0, buffer.Count) : default;
            version = buffer.Version;
            index = -1;
        }

        /// <summary>Moves to the next event of type <typeparamref name="T"/>. Throws if the list changed.</summary>
        // Inlined into the foreach loop: the JIT does not inline a method with a loop on its own.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (buffer.Version != version)
                ThrowModified();
            while (++index < items.Length)
                if (items[index].Type == type)
                    return true;
            return false;
        }

        /// <summary>A reference to the current payload. Writes go straight into the list.</summary>
        public readonly ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Unsafe.As<PayloadBytes, T>(ref items[index].Payload);
        }

        [DoesNotReturn]
        private static void ThrowModified() =>
            throw new InvalidOperationException("The event list changed during enumeration. Use RemoveAll to remove events by payload.");
    }
}
