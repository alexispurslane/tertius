namespace Tertius.Simulation.Tests;

/// <summary>
/// Removes every <see cref="Destroy"/> from the event list, so that the world does not destroy the entity: a Destroy
/// that is still in the list when the last component has run destroys it. It sees the Destroy messages that other
/// entities sent and, with the default priority of 10, also the one that <see cref="Health"/> adds at priority 0. A
/// lower priority hides the Destroy message from the components after it, but cannot remove one that a later
/// component adds.
/// </summary>
/// <param name="priority">Its place in the entity's pipeline. Lower runs first.</param>
public sealed class Invulnerable(int priority = 10) : IComponent
{
    public int Priority => priority;
    public EventMask Wants => EventMask.Of<Destroy>();

    /// <summary>How many Destroy events it has removed.</summary>
    public int Blocked { get; private set; }

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        Blocked += events.RemoveAll(static (ref Destroy _) => true);
}
