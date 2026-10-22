using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>
/// The test that an event query runs on each event of an inbox. The inbox's scans take the filter as a type parameter,
/// so that the chain from <see cref="EventList.Of{T}"/>, a stored <see cref="EventQuery{T}"/> run with
/// <see cref="EventList.Find{T}(EventQuery{T})"/> and the chain from <see cref="EventList.Where"/> share one scan.
/// </summary>
internal interface IEventFilter
{
    /// <summary>False if no event of an inbox with this mask can match. The inbox then skips the scan.</summary>
    bool MayMatch(in EventMask mask);

    /// <summary>True if the event passes the filter.</summary>
    bool Matches(ref Event e);
}

/// <summary>
/// A stored query over the events of type <typeparamref name="T"/> in an inbox, with an optional sender and an
/// optional predicate. The default value matches every event of type <typeparamref name="T"/>. Each word returns a new
/// query and leaves this one as it was, so a query can live in a field and run on every tick with
/// <see cref="EventList.Find{T}(EventQuery{T})"/>. <see cref="EventList.Of{T}"/> builds the same query as a chain.
/// </summary>
/// <remarks>
/// A query holds one sender and one predicate. A second <see cref="From"/> or a second <c>Where</c> throws: an event
/// has one sender, and two conditions go in one lambda.
/// </remarks>
public readonly struct EventQuery<T> : IEventFilter where T : unmanaged, IEventPayload
{
    private readonly Entity? sender;
    private readonly RefPredicate<T>? predicate;

    private EventQuery(Entity? sender, RefPredicate<T>? predicate)
    {
        this.sender = sender;
        this.predicate = predicate;
    }

    /// <summary>Keeps the events that <paramref name="sender"/> sent. <see cref="Entity.Null"/> keeps the events without a sender.</summary>
    public EventQuery<T> From(Entity sender)
    {
        if (this.sender is not null)
            QueryErrors.ThrowSecondSender();
        return new(sender, predicate);
    }

    /// <summary>Keeps the events whose payload passes <paramref name="predicate"/>. Use a static lambda, so that it allocates nothing.</summary>
    public EventQuery<T> Where(RefPredicate<T> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (this.predicate is not null)
            QueryErrors.ThrowSecondPredicate();
        return new(sender, predicate);
    }

    /// <summary>
    /// Keeps the events whose payload passes <paramref name="predicate"/>, which gets <paramref name="state"/> as its
    /// second argument. Use a static lambda: the state replaces a capture, so nothing allocates.
    /// </summary>
    public EventQuery<T, TState> Where<TState>(TState state, RefPredicate<T, TState> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (this.predicate is not null)
            QueryErrors.ThrowSecondPredicate();
        return new(this, state, predicate);
    }

    bool IEventFilter.MayMatch(in EventMask mask) => MayMatch(mask);

    bool IEventFilter.Matches(ref Event e) => Matches(ref e);

    internal static bool MayMatch(in EventMask mask) => mask.Contains(EventType<T>.Id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Matches(ref Event e) =>
        e.Type == EventType<T>.Id
        && (sender is not Entity from || e.Sender == from)
        && (predicate is null || predicate(ref Unsafe.As<PayloadBytes, T>(ref e.Payload)));
}

/// <summary>
/// A stored query over the events of type <typeparamref name="T"/> whose predicate takes one value of state. It comes
/// from <see cref="EventQuery{T}.Where{TState}"/>, and <see cref="EventList.Find{T, TState}"/> runs it.
/// </summary>
public readonly struct EventQuery<T, TState> : IEventFilter where T : unmanaged, IEventPayload
{
    // The type and the sender. It has no predicate of its own: the query holds one, and it is 'predicate'.
    private readonly EventQuery<T> query;
    private readonly TState state;
    private readonly RefPredicate<T, TState>? predicate;

    internal EventQuery(EventQuery<T> query, TState state, RefPredicate<T, TState> predicate)
    {
        this.query = query;
        this.state = state;
        this.predicate = predicate;
    }

    /// <summary>Keeps the events that <paramref name="sender"/> sent. <see cref="Entity.Null"/> keeps the events without a sender.</summary>
    public EventQuery<T, TState> From(Entity sender) => new(query.From(sender), state, predicate!);

    bool IEventFilter.MayMatch(in EventMask mask) => EventQuery<T>.MayMatch(mask);

    // A default value has no predicate, and then it matches what its type and sender match.
    bool IEventFilter.Matches(ref Event e) =>
        query.Matches(ref e) && (predicate is null || predicate(ref Unsafe.As<PayloadBytes, T>(ref e.Payload), state));
}

/// <summary>The filter of <see cref="EventsOfAnyType"/>: an optional sender and an optional predicate over whole events.</summary>
internal readonly struct AnyTypeFilter : IEventFilter
{
    private readonly Entity? sender;
    private readonly RefPredicate<Event>? predicate;

    private AnyTypeFilter(Entity? sender, RefPredicate<Event>? predicate)
    {
        this.sender = sender;
        this.predicate = predicate;
    }

    public AnyTypeFilter From(Entity sender)
    {
        if (this.sender is not null)
            QueryErrors.ThrowSecondSender();
        return new(sender, predicate);
    }

    public AnyTypeFilter Where(RefPredicate<Event> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (this.predicate is not null)
            QueryErrors.ThrowSecondPredicate();
        return new(sender, predicate);
    }

    // A query over every type has no type to look up in the mask.
    public bool MayMatch(in EventMask mask) => true;

    public bool Matches(ref Event e) =>
        (sender is not Entity from || e.Sender == from) && (predicate is null || predicate(ref e));
}

internal static class QueryErrors
{
    [DoesNotReturn]
    public static void ThrowSecondSender() =>
        throw new InvalidOperationException(
            "The query already has a sender. An event has one sender, so a query takes one From.");

    [DoesNotReturn]
    public static void ThrowSecondPredicate() =>
        throw new InvalidOperationException("The query already has a predicate. Test both conditions in one Where.");
}
