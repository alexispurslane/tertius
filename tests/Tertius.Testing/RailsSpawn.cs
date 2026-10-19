using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Spawns of frames on rails. A rails component (<see cref="KeplerMotion"/> or <see cref="SpinMotion"/>) moves a frame from
/// the second sync point after its spawn on: the new frame holds still at the pose of its spawn description for its first
/// step, and the first sample of its component puts it on its rails (docs/plans/rails-components.md, "Other rules" in "The
/// rails components"). So a spawn that starts at the rails pose and velocities of the world's time is one step of its motion
/// behind its rails until then, and never far from them.
/// </summary>
public static class RailsSpawn
{
    /// <summary>
    /// A copy of <paramref name="desc"/> whose frame goes round the point gravity of its parent, <see cref="SpawnDesc.Frame"/>,
    /// on <paramref name="orbit"/>: a <see cref="KeplerMotion"/> on that orbit comes before the other components, and the
    /// pose, the velocity and the angular velocity are the orbit's at <see cref="World.Time"/>, with the G·M of the parent's
    /// point gravity (see <see cref="KeplerOrbit.MuFrom"/>). A parent without one gives the place of an orbit of G·M 0.
    /// </summary>
    public static SpawnDesc OnKeplerOrbit(this SpawnDesc desc, World world, KeplerOrbit orbit)
    {
        double mu = KeplerOrbit.MuFrom(world.Frames.Gravity(desc.Frame));
        double time = world.Time;
        (Double3 position, Double3 velocity) = orbit.StateAt(time, mu);
        return With(desc, new DPose(position, orbit.RotationAt(time, mu)), velocity, orbit.AngularVelocityAt(time, mu).ToVector3(),
            new KeplerMotion { Orbit = orbit });
    }

    /// <summary>
    /// A copy of <paramref name="desc"/> whose frame turns in place by <paramref name="spin"/>: a <see cref="SpinMotion"/> with
    /// that spin comes before the other components, the pose keeps the place of
    /// <paramref name="desc"/> and takes the spin's rotation at <see cref="World.Time"/>, the velocity is zero, and the angular
    /// velocity is the spin's.
    /// </summary>
    public static SpawnDesc Spinning(this SpawnDesc desc, World world, FrameSpin spin) =>
        With(desc, new DPose(desc.Pose.Position, spin.RotationAt(world.Time)), Double3.Zero, spin.AngularVelocity.ToVector3(),
            new SpinMotion { Spin = spin });

    private static SpawnDesc With(SpawnDesc desc, DPose pose, Double3 velocity, Vector3 angularVelocity, IComponent rails) => new()
    {
        Frame = desc.Frame,
        Pose = pose,
        LinearVelocity = velocity,
        AngularVelocity = angularVelocity,
        Body = desc.Body,
        Render = desc.Render,
        Components = [rails, .. desc.Components],
        AsFrame = desc.AsFrame,
        AttachTo = desc.AttachTo,
        AttachOffset = desc.AttachOffset,
        Subscriptions = desc.Subscriptions,
    };
}
