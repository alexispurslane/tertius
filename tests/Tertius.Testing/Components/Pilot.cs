using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>What a <see cref="Pilot"/> does to its entity's body during a tick.</summary>
public delegate void BodyCommand(BodyHandle body);

/// <summary>
/// Lets a test steer its entity's body. A command written during one tick acts in the physics step of the next tick.
/// </summary>
public sealed class Pilot : IComponent
{
    /// <summary>Runs on every tick until the test changes it. Null does nothing.</summary>
    public BodyCommand? EveryTick { get; set; }

    /// <summary>Runs on the next tick only. Null does nothing.</summary>
    public BodyCommand? Once { get; set; }

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        EveryTick?.Invoke(self.Body);
        if (Once is { } once)
        {
            Once = null;
            once(self.Body);
        }
    }
}
