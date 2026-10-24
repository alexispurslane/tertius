using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>
/// The events of type <typeparamref name="T"/> in an <see cref="EventList"/> that a query matches, in inbox order.
/// <see cref="EventList.Of{T}"/> starts the chain, <see cref="From"/> and <c>Where</c> narrow it, and an ending runs it.
/// The enumerator yields references, so <c>foreach (ref var p in ...)</c> edits the payloads in place. Do not add or
/// remove events while enumerating.
/// </summary>
public readonly ref struct EventsOf<T>
    where T : unmanaged, IEventPayload
{
    private readonly EventBuffer buffer;
    private readonly EventQuery<T> query;

    internal EventsOf(EventBuffer buffer, EventQuery<T> query)
    {
        this.buffer = buffer;
        this.query = query;
    }

    /// <summary>Keeps the events that <paramref name="sender"/> sent. <see cref="Entity.Null"/> keeps the events without a sender.</summary>
    public EventsOf<T> From(Entity sender) => new(buffer, query.From(sender));

    /// <summary>Keeps the events whose payload passes <paramref name="predicate"/>. Use a static lambda, so that it allocates nothing.</summary>
    public EventsOf<T> Where(RefPredicate<T> predicate) => new(buffer, query.Where(predicate));

    /// <summary>Keeps the events whose payload passes <paramref name="predicate"/>, which gets <paramref name="state"/> as its second argument.</summary>
    public EventsOf<T, TState> Where<TState>(TState state, RefPredicate<T, TState> predicate) =>
        new(buffer, query.Where(state, predicate));

    /// <summary>Returns the enumerator that <c>foreach</c> uses.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new(buffer, query);

    /// <summary>The number of matching events.</summary>
    public int Count() => buffer.CountMatches(query);

    /// <summary>True if an event matches.</summary>
    public bool Any() => buffer.FirstMatch(query) >= 0;

    /// <summary>A copy of the payload of the first matching event. Null if none matches.</summary>
    public T? First() => buffer.First<EventQuery<T>, T>(query);

    /// <summary>Removes the matching events from the inbox and returns their number. The other events keep their order.</summary>
    public int Remove() => buffer.RemoveMatches(query);

    /// <summary>Enumerates references to the payloads of the matching events.</summary>
    public ref struct Enumerator
    {
        private EventCursor<EventQuery<T>> cursor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(EventBuffer buffer, EventQuery<T> query)
        {
            cursor = new(buffer, query);
        }

        /// <summary>A reference to the current payload. Writes go straight into the list.</summary>
        public readonly ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Unsafe.As<PayloadBytes, T>(ref cursor.Current.Payload);
        }

        /// <summary>Moves to the next matching event. Throws if the list changed.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => cursor.MoveNext();
    }
}

/// <summary>
/// The events of type <typeparamref name="T"/> in an <see cref="EventList"/> that a query with a state predicate
/// matches, in inbox order. It comes from <see cref="EventsOf{T}.Where{TState}"/> and has the same endings.
/// </summary>
public readonly ref struct EventsOf<T, TState>
    where T : unmanaged, IEventPayload
{
    private readonly EventBuffer buffer;
    private readonly EventQuery<T, TState> query;

    internal EventsOf(EventBuffer buffer, EventQuery<T, TState> query)
    {
        this.buffer = buffer;
        this.query = query;
    }

    /// <summary>Keeps the events that <paramref name="sender"/> sent. <see cref="Entity.Null"/> keeps the events without a sender.</summary>
    public EventsOf<T, TState> From(Entity sender) => new(buffer, query.From(sender));

    /// <summary>Returns the enumerator that <c>foreach</c> uses.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new(buffer, query);

    /// <summary>The number of matching events.</summary>
    public int Count() => buffer.CountMatches(query);

    /// <summary>True if an event matches.</summary>
    public bool Any() => buffer.FirstMatch(query) >= 0;

    /// <summary>A copy of the payload of the first matching event. Null if none matches.</summary>
    public T? First() => buffer.First<EventQuery<T, TState>, T>(query);

    /// <summary>Removes the matching events from the inbox and returns their number. The other events keep their order.</summary>
    public int Remove() => buffer.RemoveMatches(query);

    /// <summary>Enumerates references to the payloads of the matching events.</summary>
    public ref struct Enumerator
    {
        private EventCursor<EventQuery<T, TState>> cursor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(EventBuffer buffer, EventQuery<T, TState> query)
        {
            cursor = new(buffer, query);
        }

        /// <summary>A reference to the current payload. Writes go straight into the list.</summary>
        public readonly ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Unsafe.As<PayloadBytes, T>(ref cursor.Current.Payload);
        }

        /// <summary>Moves to the next matching event. Throws if the list changed.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => cursor.MoveNext();
    }
}

/// <summary>
/// The events of every type in an <see cref="EventList"/> that a sender or a predicate over whole events matches, in
/// inbox order. <see cref="EventList.Where"/> and <see cref="EventList.From"/> start it. The enumerator yields
/// references to the events.
/// </summary>
public readonly ref struct EventsOfAnyType
{
    private readonly EventBuffer buffer;
    private readonly AnyTypeFilter filter;

    internal EventsOfAnyType(EventBuffer buffer, AnyTypeFilter filter)
    {
        this.buffer = buffer;
        this.filter = filter;
    }

    /// <summary>Keeps the events that <paramref name="sender"/> sent. <see cref="Entity.Null"/> keeps the events without a sender.</summary>
    public EventsOfAnyType From(Entity sender) => new(buffer, filter.From(sender));

    /// <summary>Keeps the events that pass <paramref name="predicate"/>. Use a static lambda, so that it allocates nothing.</summary>
    public EventsOfAnyType Where(RefPredicate<Event> predicate) => new(buffer, filter.Where(predicate));

    /// <summary>Returns the enumerator that <c>foreach</c> uses.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new(buffer, filter);

    /// <summary>The number of matching events.</summary>
    public int Count() => buffer.CountMatches(filter);

    /// <summary>True if an event matches.</summary>
    public bool Any() => buffer.FirstMatch(filter) >= 0;

    /// <summary>A copy of the first matching event. Null if none matches.</summary>
    public Event? First() => buffer.First(filter);

    /// <summary>Removes the matching events from the inbox and returns their number. The other events keep their order.</summary>
    public int Remove() => buffer.RemoveMatches(filter);

    /// <summary>Enumerates references to the matching events.</summary>
    public ref struct Enumerator
    {
        private EventCursor<AnyTypeFilter> cursor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(EventBuffer buffer, AnyTypeFilter filter)
        {
            cursor = new(buffer, filter);
        }

        /// <summary>A reference to the current event. Writes go straight into the list.</summary>
        public readonly ref Event Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref cursor.Current;
        }

        /// <summary>Moves to the next matching event. Throws if the list changed.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => cursor.MoveNext();
    }
}

/// <summary>The walk over an inbox that every query's enumerator runs: it yields the events that a filter matches, in order.</summary>
internal ref struct EventCursor<TFilter>
    where TFilter : struct, IEventFilter
{
    private readonly EventBuffer buffer;
    private readonly Span<Event> items;

    private readonly int version;

    // Not readonly: a call on a readonly field of a generic struct type would copy the filter first.
#pragma warning disable IDE0044
    private TFilter filter;
#pragma warning restore IDE0044
    private int index;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal EventCursor(EventBuffer buffer, TFilter filter)
    {
        this.buffer = buffer;
        this.filter = filter;
        items = filter.MayMatch(buffer.Mask) ? buffer.Items.AsSpan(0, buffer.Count) : default;
        version = buffer.Version;
        index = -1;
    }

    public readonly ref Event Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref items[index];
    }

    /// <summary>Moves to the next matching event. Throws if the list changed.</summary>
    // Inlined into the foreach loop: the JIT does not inline a method with a loop on its own.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (buffer.Version != version)
            ThrowModified();
        while (++index < items.Length)
            if (filter.Matches(ref items[index]))
                return true;
        return false;
    }

    [DoesNotReturn]
    private static void ThrowModified() =>
        throw new InvalidOperationException(
            "The event list changed during enumeration. Use Where(...).Remove() to remove events by payload."
        );
}
