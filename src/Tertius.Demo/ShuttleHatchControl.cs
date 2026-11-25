using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// Toggles a boarding fixture's visibility based on whether its shuttle hull is flying (<see cref="BodyMotion.Dynamic"/>)
/// or parked (<see cref="BodyMotion.Static"/>). The hatch door closes in flight and opens when parked; the boarding bridge
/// deploys when parked and retracts in flight.
/// </summary>
public sealed class ShuttleHatchControl(FrameHandle cabin, bool showWhenDynamic) : IComponent
{
    private readonly FrameHandle cabin = cabin;
    private readonly bool showWhenDynamic = showWhenDynamic;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        bool dynamic = self.Snapshot.Frames.HullMotion(cabin) == BodyMotion.Dynamic;
        bool visible = dynamic == showWhenDynamic;
        ref RenderData render = ref self.Render;
        if (visible)
            render.Flags |= RenderFlags.Visible | RenderFlags.CastsShadow;
        else
            render.Flags &= ~(RenderFlags.Visible | RenderFlags.CastsShadow);
    }
}
