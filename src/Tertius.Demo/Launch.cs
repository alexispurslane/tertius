using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>Tells a <see cref="Launchable"/> body to fly off at <see cref="Velocity"/>.</summary>
public struct Launch : IEventPayload
{
    /// <summary>The body's new velocity, in m/s, relative to its frame, in the frame's axes.</summary>
    public Double3 Velocity;
}

/// <summary>
/// A body that an order throws, as a mass driver throws its load: a <see cref="Launch"/> sets the body's velocity
/// relative to its frame before the next step. When one tick brings several launches, the last one counts. A script
/// throws with it at a speed that no person reaches, as the station scene throws its crate at 1,200 m/s (see
/// <see cref="DemoScenes.Station"/>). Its entity needs a dynamic body.
/// </summary>
public sealed class Launchable : IComponent
{
    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Launch>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].Is<Launch>())
                self.Body.SetVelocity(events[i].As<Launch>().Velocity);
    }
}
