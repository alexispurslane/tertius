using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>A test component that unparks its hull on its first tick, to dynamic.</summary>
internal sealed class Unpark : IComponent
{
    private bool done;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (!done)
            self.Body.SetMotion(BodyMotion.Dynamic);
        done = true;
    }
}
