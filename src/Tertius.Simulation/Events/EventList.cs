using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>
/// Reusable storage for an <see cref="EventList"/>. The scheduler keeps one per worker thread. Once the buffer has
/// grown to the longest list it has held, filling it again never allocates.
/// </summary>
/// <remarks>Creates a buffer for <paramref name="capacity"/> events. It grows when needed.</remarks>
public sealed class EventBuffer(int capacity = 32)
{
    internal Event[] Items = new Event[Math.Max(capacity, 4)];
    internal int Count;
    internal EventMask Mask;
    internal int Version;

    /// <summary>The number of events the buffer holds before it grows.</summary>
    public int Capacity => Items.Length;

    /// <summary>Clears the buffer and returns a list over it. Events that the list adds target <paramref name="owner"/>.</summary>
    public EventList Begin(Entity owner = default)
    {
        Clear();
        return new(this, owner);
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

    // ---- The scans that every event query runs ---------------------------------------------------------------------

    /// <summary>The index of the first event that <paramref name="filter"/> matches, or -1.</summary>
    internal int FirstMatch<TFilter>(TFilter filter)
        where TFilter : struct, IEventFilter
    {
        if (!filter.MayMatch(Mask))
            return -1;
        Span<Event> items = Items.AsSpan(0, Count);
        for (int i = 0; i < items.Length; i++)
            if (filter.Matches(ref items[i]))
                return i;
        return -1;
    }

    /// <summary>A copy of the payload of the first event that <paramref name="filter"/> matches. Null if none matches.</summary>
    internal T? First<TFilter, T>(TFilter filter)
        where TFilter : struct, IEventFilter
        where T : unmanaged, IEventPayload
    {
        int index = FirstMatch(filter);
        return index >= 0 ? Unsafe.As<PayloadBytes, T>(ref Items[index].Payload) : null;
    }

    /// <summary>A copy of the first event that <paramref name="filter"/> matches. Null if none matches.</summary>
    internal Event? First<TFilter>(TFilter filter)
        where TFilter : struct, IEventFilter
    {
        int index = FirstMatch(filter);
        return index >= 0 ? Items[index] : null;
    }

    /// <summary>The number of events that <paramref name="filter"/> matches.</summary>
    internal int CountMatches<TFilter>(TFilter filter)
        where TFilter : struct, IEventFilter
    {
        if (!filter.MayMatch(Mask))
            return 0;
        int n = 0;
        Span<Event> items = Items.AsSpan(0, Count);
        for (int i = 0; i < items.Length; i++)
            if (filter.Matches(ref items[i]))
                n++;
        return n;
    }

    /// <summary>
    /// Removes the events that <paramref name="filter"/> matches and returns their number. The other events keep their
    /// order. If the filter throws, the events from the one it threw on are kept unchanged.
    /// </summary>
    internal int RemoveMatches<TFilter>(TFilter filter)
        where TFilter : struct, IEventFilter
    {
        if (!filter.MayMatch(Mask))
            return 0;
        Event[] items = Items;
        int count = Count,
            write = 0,
            read = 0;
        try
        {
            for (; read < count; read++)
            {
                ref Event e = ref items[read];
                if (filter.Matches(ref e))
                    continue;
                if (write != read)
                    items[write] = e;
                write++;
            }
        }
        finally
        {
            Compact(count, read, write);
        }

        return count - Count;
    }

    // Finishes a removal pass. If the filter threw at 'read', the events from there on are kept unchanged.
    private void Compact(int count, int read, int write)
    {
        if (read < count && write != read)
            Array.Copy(
                sourceArray: Items,
                sourceIndex: read,
                destinationArray: Items,
                destinationIndex: write,
                length: count - read
            );
        int newCount = write + (count - read);
        if (newCount < count)
        {
            Array.Clear(Items, newCount, count - newCount);
            Count = newCount;
            RecomputeMask();
        }

        Version++;
    }

    internal void RecomputeMask()
    {
        EventMask mask = default;
        for (int i = 0; i < Count; i++)
            mask.Add(Items[i].Type);
        Mask = mask;
    }
}

/// <summary>
/// The events of one entity run, in delivery order. Components edit the list in place: they change payloads, remove
/// events and add events. An added event is visible only to the components after the one that added it.
/// The list never allocates in steady state. A query picks events with <see cref="Of{T}"/>, <c>From</c> and
/// <c>Where</c>, and an ending runs it. Enumerating gives references into the payloads:
/// <code>
/// foreach (ref var damage in events.Of&lt;Damage&gt;())
///     damage.Amount = MathF.Max(0, damage.Amount - rating);
/// events.Of&lt;Damage&gt;().Where(static (ref Damage d) =&gt; d.Amount == 0).Remove();
/// </code>
/// </summary>
public readonly ref struct EventList
{
    private readonly EventBuffer buffer;

    internal EventList(EventBuffer buffer, Entity owner)
    {
        this.buffer = buffer;
        Owner = owner;
    }

    /// <summary>The number of events in the list.</summary>
    public readonly int Count => buffer.Count;

    /// <summary>The running entity. Events that <see cref="Add{T}"/> creates target it.</summary>
    public Entity Owner { get; }

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
    public readonly void Add<T>(in T payload, Entity sender = default)
        where T : unmanaged, IEventPayload
    {
        int type = EventType<T>.Id;
        ref Event e = ref buffer.Append();
        e = default;
        e.Target = Owner;
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
            Array.Copy(
                sourceArray: items,
                sourceIndex: index + 1,
                destinationArray: items,
                destinationIndex: index,
                length: count - index - 1
            );
        items[count - 1] = default;
        buffer.Count = count - 1;
        buffer.Version++;
        buffer.RecomputeMask();
    }

    /// <summary>
    /// The events of type <typeparamref name="T"/>, as a query: <c>From</c> and <c>Where</c> narrow it, and an ending runs
    /// it. A query for a type that <see cref="Mask"/> lacks scans nothing. Do not add or remove events while enumerating.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly EventsOf<T> Of<T>()
        where T : unmanaged, IEventPayload => Find(new EventQuery<T>());

    /// <summary>The events that a stored query matches. It gives the same chain as <see cref="Of{T}"/> with the same words.</summary>
    public readonly EventsOf<T> Find<T>(EventQuery<T> query)
        where T : unmanaged, IEventPayload => new(buffer, query);

    /// <summary>The events that a stored query with a state predicate matches.</summary>
    public readonly EventsOf<T, TState> Find<T, TState>(EventQuery<T, TState> query)
        where T : unmanaged, IEventPayload => new(buffer, query);

    /// <summary>The events of every type that pass <paramref name="predicate"/>. Use a static lambda, so that it allocates nothing.</summary>
    public readonly EventsOfAnyType Where(RefPredicate<Event> predicate) =>
        new(buffer, new AnyTypeFilter().Where(predicate));

    /// <summary>The events of every type that <paramref name="sender"/> sent. <see cref="Entity.Null"/> keeps the events without a sender.</summary>
    public readonly EventsOfAnyType From(Entity sender) => new(buffer, new AnyTypeFilter().From(sender));

    [DoesNotReturn]
    private readonly void ThrowIndex(int index) =>
        throw new ArgumentOutOfRangeException(nameof(index), index, $"The event list has {buffer.Count} events.");
}
