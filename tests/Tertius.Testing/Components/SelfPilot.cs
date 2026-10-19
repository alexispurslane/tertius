using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>What a <see cref="SelfPilot"/> does with its entity during a tick. A delegate of its own, because <see cref="Self"/> is a ref struct.</summary>
public delegate void SelfCommand(Self self);

/// <summary>
/// Lets a test steer its entity through <see cref="Self"/>, for the writes that are no request to a body, such as
/// <see cref="Self.MoveKinematic"/> and <see cref="Self.MoveOnRails"/>. A command written during one tick acts in the
/// physics step of the next tick. <see cref="Pilot"/> does the same for the requests to a body.
/// </summary>
public sealed class SelfPilot : IComponent
{
    /// <summary>Runs on every tick until the test changes it. Null does nothing.</summary>
    public SelfCommand? EveryTick { get; set; }

    /// <summary>Runs on the next tick only. Null does nothing.</summary>
    public SelfCommand? Once { get; set; }

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        EveryTick?.Invoke(self);
        if (Once is { } once)
        {
            Once = null;
            once(self);
        }
    }
}
