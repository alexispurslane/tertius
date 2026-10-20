using Tertius.Demo;
using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>
/// A test component that adds a <see cref="Throttle"/> of 1 to its entity's events on every tick, for the entity's
/// <see cref="MainEngine"/>, which runs after it.
/// </summary>
internal sealed class FullThrottle : IComponent
{
    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => events.Add(new Throttle { Value = 1 });
}
