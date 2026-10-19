using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Moves a kinematic body along a straight line at a constant <see cref="Velocity"/> in its frame, with no turn. A kinematic
/// body moves only by its motion intents and teleports, and holds still in a step without one, so a spawn velocity no longer
/// drifts it (docs/plans/rails-components.md, "The flow per kind of entity"). In every tick the component writes the end pose
/// one step further along the line from where the body starts the next step (see <see cref="Self.PoseAtNextStepStartIn"/>).
/// The component first runs in the tick after the spawn, so a spawn through <see cref="Drifting"/> also carries the end pose
/// of the body's first step as its first motion intent, and the body moves from its first step on, as a spawn velocity moved
/// it.
/// </summary>
public sealed class KinematicDrift(Double3 velocity) : IComponent
{
    /// <summary>The velocity of the drift relative to the frame that the body is in, in its axes, in m/s.</summary>
    public Double3 Velocity => velocity;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        DPose start = self.PoseAtNextStepStartIn(self.Frame);
        self.MoveKinematic(new DPose(start.Position + velocity * self.DeltaTime, start.Rotation));
    }

    /// <summary>
    /// A copy of <paramref name="desc"/>, for a spawn outside a tick in <paramref name="world"/>, whose kinematic body drifts
    /// at <paramref name="velocity"/> from its first step on: a <see cref="KinematicDrift"/> comes before the other
    /// components, the first motion intent is the end pose one step along the line from the spawn pose, and the spawn
    /// velocities of <paramref name="desc"/> go, because a kinematic body that owns no frame takes none.
    /// </summary>
    public static SpawnDesc Drifting(SpawnDesc desc, World world, Double3 velocity) => new()
    {
        Frame = desc.Frame,
        Pose = desc.Pose,
        FirstMove = new MotionIntent(new EndPose(new DPose(desc.Pose.Position + velocity * world.DeltaTime, desc.Pose.Rotation))),
        Body = desc.Body,
        Render = desc.Render,
        Components = [new KinematicDrift(velocity), .. desc.Components ?? []],
        AsFrame = desc.AsFrame,
        AttachTo = desc.AttachTo,
        AttachOffset = desc.AttachOffset,
        Subscriptions = desc.Subscriptions,
    };
}
