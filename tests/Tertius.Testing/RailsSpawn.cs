using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Spawns of frames on rails, outside a tick. A rails component (<see cref="KeplerMotion"/> or <see cref="SpinMotion"/>)
/// writes its first sample in the first tick after the spawn, which acts in the second step. Without a first motion intent,
/// the new frame holds still at the pose of its spawn description for its first step, and the first sample of its
/// component puts it on its rails (docs/plans/rails-components.md, "Other rules" in "The rails components"). So a spawn
/// that starts at the rails pose and velocities of the world's time is one step of its motion behind its rails until then,
/// and never far from them. With <c>firstMove</c>, the description carries the sample of the first step as its first
/// motion intent (see <see cref="SpawnDesc.FirstMove"/>), so the frame is on its rails from its first step.
/// </summary>
public static class RailsSpawn
{
    /// <summary>
    /// A copy of <paramref name="desc"/> whose frame goes round the point gravity of its parent, <see cref="SpawnDesc.Frame"/>
    /// ,
    /// on <paramref name="orbit"/>: a <see cref="KeplerMotion"/> on that orbit comes before the other components, and the
    /// pose, the velocity and the angular velocity are the orbit's at <see cref="World.Time"/>, with the G·M of the parent's
    /// point gravity (see <see cref="KeplerOrbit.MuFrom"/>). An entity that takes no spawn velocity, such as one without a body or a plain kinematic body, takes the pose and no velocities (see
    /// <see cref="With"/>). A parent without a point gravity gives the place of an orbit of G·M 0.
    /// With <paramref name="firstMove"/>, its first motion intent is the orbit's sample of the step of the next tick.
    /// </summary>
    public static SpawnDesc OnKeplerOrbit(this SpawnDesc desc, World world, KeplerOrbit orbit, bool firstMove = false)
    {
        double mu = KeplerOrbit.MuFrom(world.Frames.Gravity(desc.Frame));
        double time = world.Time;
        (Double3 position, Double3 velocity) = orbit.StateAt(time, mu);
        MotionIntent first = firstMove
            ? new MotionIntent(orbit.Sample(mu, FirstStepEnd(world), world.DeltaTime))
            : default;
        return With(
            desc: desc,
            pose: new(position, orbit.RotationAt(time, mu)),
            velocity: velocity,
            angularVelocity: orbit.AngularVelocityAt(time, mu).ToVector3(),
            firstMove: first,
            rails: new KeplerMotion { Orbit = orbit }
        );
    }

    /// <summary>
    /// A copy of <paramref name="desc"/> whose frame turns in place by <paramref name="spin"/>: a <see cref="SpinMotion"/>
    /// with
    /// that spin comes before the other components, the pose keeps the place of
    /// <paramref name="desc"/> and takes the spin's rotation at <see cref="World.Time"/>, the velocity is zero, and the angular
    /// velocity is the spin's, for an entity that takes a spawn velocity (see <see cref="With"/>); any other takes the pose and no velocities. With
    /// <paramref name="firstMove"/>, its first motion intent is the spin's sample of the step
    /// of the next tick, at the place of <paramref name="desc"/>.
    /// </summary>
    public static SpawnDesc Spinning(this SpawnDesc desc, World world, FrameSpin spin, bool firstMove = false) =>
        With(
            desc: desc,
            pose: new(desc.Pose.Position, spin.RotationAt(world.Time)),
            velocity: Double3.Zero,
            angularVelocity: spin.AngularVelocity.ToVector3(),
            firstMove: firstMove ? new MotionIntent(spin.Sample(desc.Pose.Position, FirstStepEnd(world))) : default,
            rails: new SpinMotion { Spin = spin }
        );

    /// <summary>The end of the first step of an entity spawned now, outside a tick: the end of the step of the next tick.</summary>
    private static double FirstStepEnd(World world) => world.TimeAt(world.TickNumber + 1);

    /// <summary>
    /// The copy. Only an entity whose spawn takes a velocity gets the rails velocities (docs/plans/rails-components.md,
    /// "Which entity takes a spawn velocity"): a kinematic hull, which keeps them as its frame's velocities, and a body that
    /// physics integrates, such as a hull that a test makes dynamic. A spawn refuses a velocity on every entity that only its
    /// motion intents and teleports move, and a rails component moves a plain kinematic body and an entity without a body by
    /// its samples alone, so those take the pose and no velocities.
    /// </summary>
    private static SpawnDesc With(
        SpawnDesc desc,
        DPose pose,
        Double3 velocity,
        Vector3 angularVelocity,
        MotionIntent firstMove,
        IComponent rails
    ) =>
        new()
        {
            Frame = desc.Frame,
            Pose = pose,
            LinearVelocity = TakesSpawnVelocity(desc) ? velocity : Double3.Zero,
            AngularVelocity = TakesSpawnVelocity(desc) ? angularVelocity : Vector3.Zero,
            FirstMove = firstMove,
            Body = desc.Body,
            Render = desc.Render,
            Components = [rails, .. desc.Components],
            AsFrame = desc.AsFrame,
            AttachTo = desc.AttachTo,
            AttachOffset = desc.AttachOffset,
            Subscriptions = desc.Subscriptions,
        };

    /// <summary>
    /// True when the spawn of <paramref name="desc"/> takes a velocity: a kinematic hull, the body of a frame that is not a
    /// root, or a dynamic body or a character that is not attached.
    /// </summary>
    private static bool TakesSpawnVelocity(SpawnDesc desc) =>
        desc.AttachTo.IsNull
        && desc.Body?.Motion switch
        {
            BodyMotion.Kinematic => desc.AsFrame is not null && !desc.Frame.IsNull,
            BodyMotion.Dynamic or BodyMotion.Character => true,
            _ => false,
        };
}
