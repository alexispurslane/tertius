using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The puffs that one kind of jet blows (docs/plans/blue-danube-demo.md, "The plumes"): what its puffs share,
/// <see cref="Look"/>, how many it makes in each second at full throttle, <see cref="FullRate"/>, their speed relative to
/// the jet, <see cref="Speed"/>, in m/s, and the half-angle of their cone, <see cref="Spread"/>, in rad. A jet sets the
/// emitter of its own part in each tick from its throttle, <see cref="At"/>, so its plume shows exactly while it pushes.
/// Puffs leave the part's origin along its -Z, so the part's offset aims its -Z against its push (see <see cref="Aim"/>).
/// </summary>
public readonly record struct Plume(PuffLook Look, float FullRate, float Speed, float Spread)
{
    /// <summary>
    /// The emitter at <paramref name="throttle"/>, from 0, off, to 1, full: the rate is the square root of the throttle
    /// times <see cref="FullRate"/>, and the look, the speed and the spread stay the same, so the puffs of one jet share one
    /// look however its throttle moves. At 0, or below, it is the default emitter, which makes no puffs.
    /// </summary>
    /// <remarks>
    /// The square root shows small forces. The shuttle's jets are sized for the attitude autopilot's largest torque, so
    /// they mostly work at 0.1 to 3 percent of their thrust, and a rate in proportion to the throttle made almost no
    /// puffs in the docking. At a hundredth of the thrust, the square root makes a tenth of the full rate.
    /// </remarks>
    public PuffEmitter At(float throttle) =>
        throttle > 0
            ? new PuffEmitter(Rate: MathF.Sqrt(throttle) * FullRate, Speed: Speed, Spread: Spread, Look: Look)
            : default;

    /// <summary>
    /// The rotation whose -Z, the way that puffs leave an entity, points along <paramref name="exhaust"/>, which need not
    /// have unit length. Its +Y lies as near to +Y as it can across the exhaust, or to +Z when the exhaust runs within 60
    /// degrees of the Y axis. Throws <see cref="ArgumentException"/> for an exhaust of zero.
    /// </summary>
    public static DQuaternion Aim(Double3 exhaust)
    {
        if (!(exhaust.Length > 0))
            throw new ArgumentException($"An exhaust {exhaust} needs a direction.", nameof(exhaust));
        bool alongY = Math.Abs(exhaust.Y) > 0.5 * exhaust.Length;
        return AttitudeAutopilot.Facing(exhaust, alongY ? Double3.UnitZ : Double3.UnitY);
    }

    /// <summary>
    /// The rotation of the body that a jet's part pushes, in the axes of its frame, at the end of the last finished step:
    /// the rotation of the part's attachment parent, or the entity's own when it is not attached. A jet's push is fixed in
    /// the axes of that body, so the two jets of a couple, which read one rotation, push with exact negatives of one force,
    /// whatever their parts' own rotations. Throws <see cref="InvalidOperationException"/> when the parent is gone, which
    /// a live part's parent never is.
    /// </summary>
    public static DQuaternion PushedBodyRotation(Self self)
    {
        Entity parent = self.AttachedTo;
        return parent.IsNull
            ? self.PoseIn(self.Frame).Rotation
            : !self.Snapshot.TryGetPoseIn(parent, self.Frame, out DPose pose)
                ? throw new InvalidOperationException($"The part {self.Entity} has no live parent to push.")
                : pose.Rotation;
    }

    /// <summary>The offset of a part at <paramref name="place"/>, in meters in its parent's axes, whose puffs leave along
    /// <paramref name="exhaust"/>, in the same axes.</summary>
    public static Pose AimedOffset(Vector3 place, Double3 exhaust) => new(place, Aim(exhaust).ToQuaternion());
}
