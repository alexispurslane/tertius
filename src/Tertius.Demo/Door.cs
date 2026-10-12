using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>Tells a <see cref="Door"/> to slide open.</summary>
public struct OpenDoor : IEventPayload;

/// <summary>Tells a <see cref="Door"/> to slide shut.</summary>
public struct CloseDoor : IEventPayload;

/// <summary>
/// Slides a door between its closed pose, the pose that it has when it handles its first tick, and its open pose,
/// <paramref name="slide"/> further on. <see cref="OpenDoor"/> and <see cref="CloseDoor"/> start a slide. The door
/// moves at a steady speed that takes <paramref name="openSeconds"/> from one end to the other, and it turns back at
/// once from wherever it is when the other order comes. Its entity needs a kinematic body, and must spawn shut.
/// </summary>
/// <param name="slide">The way from the closed pose to the open pose, in meters, in the door's own axes.</param>
/// <param name="openSeconds">The time that the door takes to open or to close all the way, in seconds.</param>
public sealed class Door(Vector3 slide, float openSeconds) : IComponent
{
    private DPose closed;
    private bool started;
    // How far open the door is, from 0 (shut) to 1 (open), and whether it moves toward open.
    private float openness;
    private bool opening;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<OpenDoor>().And<CloseDoor>().And<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (!started)
        {
            closed = self.Pose;
            started = true;
        }
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Is<OpenDoor>())
                opening = true;
            else if (events[i].Is<CloseDoor>())
                opening = false;
        }

        float target = opening ? 1 : 0;
        if (openness == target)
            return;
        float step = self.DeltaTime / openSeconds;
        openness = opening ? MathF.Min(openness + step, 1) : MathF.Max(openness - step, 0);
        // A kinematic target moves the body there over the next step, and stops it there.
        self.Body.MoveKinematic(new DPose(closed.Position + closed.Rotation.Rotate((Double3)(slide * openness)), closed.Rotation));
    }
}
