namespace Tertius.Simulation;

/// <summary>
/// One piece of entity behavior. When an entity runs, the scheduler calls <see cref="Handle"/> on each of its
/// components in <see cref="Priority"/> order, lowest first. Components with the same priority keep the order in
/// which they were added. A component whose <see cref="Wants"/> has no type in common with the current event list is
/// skipped.
/// </summary>
/// <remarks>
/// A component changes only its own state and the rows of its own entity. It reads other entities only through
/// <see cref="Self.Snapshot"/> and talks to them only through messages in its outbox. It never stores
/// <see cref="Self"/>, the event list, the outbox or a reference into a table beyond the call.
/// </remarks>
public interface IComponent
{
    /// <summary>The position in the entity's pipeline. Lower runs first. Must not change while the component is attached.</summary>
    int Priority { get; }

    /// <summary>The event types this component handles. The entity caches it, so it must not change while attached.</summary>
    EventMask Wants { get; }

    /// <summary>
    /// Handles the entity's events for this tick. Edit <paramref name="events"/> in place to change what later
    /// components see. Messages for other entities, or for this entity on a later tick, go into <paramref name="outbox"/>.
    /// </summary>
    void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox);
}

/// <summary>A predicate that takes its argument by reference, so that it can read a payload in place without a copy.</summary>
public delegate bool RefPredicate<T>(ref T item);

/// <summary>
/// A predicate that takes its argument by reference and one value of state. A static lambda gets the value it needs
/// through <paramref name="state"/> instead of a capture, so that it allocates nothing.
/// </summary>
public delegate bool RefPredicate<T, in TState>(ref T item, TState state);
