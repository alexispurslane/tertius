namespace Tertius.Simulation.Tests;

/// <summary>Moves its bodiless entity by a fixed step every tick.</summary>
/// <param name="step">The displacement per tick, in frame axes.</param>
public sealed class Mover(Double3 step) : IComponent
{
    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        ref DPose pose = ref self.Transform;
        pose = pose with { Position = self.Pose.Position + step };
    }
}
