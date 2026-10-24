using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The rotation that a hull's <see cref="AttitudeAutopilot"/> turns the body toward in one tick, which a component that
/// flies the hull adds to its entity's events (see <see cref="EventList.Add{T}"/>), for the attitude autopilot of the same
/// entity, which runs after it.
/// </summary>
public struct AttitudeTarget : IEventPayload
{
    /// <summary>The rotation to turn to, in the axes of the entity's frame.</summary>
    public required DQuaternion Rotation;

    /// <summary>
    /// The target's own angular velocity relative to the entity's frame, in that frame's axes, in radians per second. The
    /// autopilot adds it to the rate that it asks for, so a target that turns faster than
    /// <see cref="AttitudeAutopilot.MaxTurnRate"/>, as one that turns with the demo station does, needs no correction for its
    /// turn. Zero, the default, for a target that holds still in the frame. A feed-forward that is not finite makes the
    /// autopilot throw <see cref="ArgumentOutOfRangeException"/>, which the world logs as an error, and give no torque in
    /// that tick.
    /// </summary>
    public Vector3 FeedForward;

    /// <summary>
    /// The fastest correction toward <see cref="Rotation"/> that the autopilot may ask for, beside the
    /// <see cref="FeedForward"/>, in radians per second, at most <see cref="AttitudeAutopilot.MaxTurnRate"/>. A layer that
    /// flies a weightless cabin with a strapped crew turns slowly, so that the turn gives the crew no felt gravity (see
    /// <see cref="RendezvousPlan.WeightlessTurnRate"/>). A rate below 0, or NaN, makes the autopilot throw
    /// <see cref="ArgumentOutOfRangeException"/>, which the world logs as an error, and give no torque in that tick. It is a
    /// float, so that the target fits the 48 bytes of an event payload.
    /// </summary>
    public required float MaxTurnRate;
}

/// <summary>
/// Turns its entity's body toward the <see cref="AttitudeTarget"/> of each tick that brings one: it asks the hull's turning
/// jets for a torque, as the attitude control of a ship does, and adds it to its entity's events as a <see cref="Torque"/>
/// for the <see cref="ShuttleJetControl"/>, which runs after it and fires the jets' couples. It pushes no body itself. It
/// asks for the target's <see cref="AttitudeTarget.FeedForward"/>, the target's own
/// turn, plus a correction along the shortest turn to the target, <see cref="Gain"/> per radian of the turn that is left,
/// and at most the target's <see cref="AttitudeTarget.MaxTurnRate"/> and <see cref="MaxTurnRate"/>. The cap takes only
/// the correction, so a target that turns faster than the cap is followed. Then it gives the torque that brings the body's
/// angular velocity to that rate in <see cref="RateTime"/>, from the body's moments of inertia about its principal axes: it
/// turns the angular acceleration that it asks for into the principal axes, multiplies it by the moments there, and turns
/// the torque back. A body whose principal axes tilt from its own couples a turn about one of its axes with a turn about
/// another, so only the moments in its own axes would turn it about an axis that it did not ask for. A tick without a target
/// gives no torque, so the autopilot keeps nothing from an earlier tick. When a tick brings several targets, the last one
/// counts. It runs at priority 10, after every layer of the flight program, so it follows the last layer that speaks. Its
/// entity needs a dynamic body whose principal axes are <see cref="PrincipalInertia.Axes"/> of its inertia, in its own axes.
/// </summary>
/// <remarks>
/// The autopilot reads the body's rotation and angular velocity at the end of the last finished step. The jets get their
/// throttles by message, so they push in the next tick, and physics applies the push in the step of the tick after that.
/// So the autopilot acts on what it read three steps late. <see cref="RateTime"/> is 20 steps, so that lag costs the loop
/// little (docs/plans/blue-danube-demo.md, "The jets as parts").
/// </remarks>
/// <param name="inertia">The moments of inertia of the body about its principal axes, in kg·m², and those axes in its own axes.</param>
public sealed class AttitudeAutopilot(PrincipalInertia inertia) : IComponent
{
    /// <summary>The turn rate that the autopilot asks for per radian of the turn that is left, in 1/s.</summary>
    public const double Gain = 0.8;

    /// <summary>The fastest turn that the autopilot asks for, in radians per second: about 8.6 degrees per second.</summary>
    public const float MaxTurnRate = 0.15f;

    /// <summary>The time in which the torque brings the body's angular velocity to the rate asked for, in seconds.</summary>
    public const double RateTime = 1.0 / 3;

    private readonly Double3 moments = inertia.Moments;

    /// <summary>The rotation that turns the body's own axes into its principal axes.</summary>
    private readonly DQuaternion axes = DQuaternion.FromQuaternion(inertia.Axes).Normalized();

    // After every layer of the flight program, priorities 0 to 2, which add the target in the same tick.
    public int Priority => 10;

    public EventMask Wants => EventMask.Of<AttitudeTarget>();

    /// <summary>
    /// The most torque about each of the body's own axes that the autopilot asks for while the body turns within its cap,
    /// in N·m, for a body of <paramref name="inertia"/>. The rate that it asks for lies within <see cref="MaxTurnRate"/> of
    /// the target's feed-forward, and so does the body's angular velocity once the body follows, so the two differ by at
    /// most twice the cap, and the autopilot asks for an angular acceleration of at most twice the cap over
    /// <see cref="RateTime"/>, in any direction. The torque about one axis is the dot product of that acceleration with the
    /// inertia tensor's row for the axis, so its most is the length of the row times that acceleration. A feed-forward
    /// that jumps, or a body that spins faster than the target, asks for more. The shuttle's turning jets give this torque
    /// at full throttle (see <see cref="ShuttleJets"/>).
    /// </summary>
    public static Double3 MostTorque(PrincipalInertia inertia)
    {
        double acceleration = 2 * MaxTurnRate / RateTime;
        DQuaternion axes = DQuaternion.FromQuaternion(inertia.Axes).Normalized();
        Double3 moments = inertia.Moments;

        // The tensor is symmetric, so its row for an axis is its column, the torque of a unit acceleration about the axis.
        double Row(Double3 axis) => axes.Rotate(axes.Conjugate().Rotate(axis) * moments).Length * acceleration;
        return new(Row(Double3.UnitX), Row(Double3.UnitY), Row(Double3.UnitZ));
    }

    /// <summary>
    /// The rotation whose nose, its -Z, points along <paramref name="nose"/>, and whose roof, its +Y, points as near to
    /// <paramref name="roof"/> as it can across the nose. Neither needs unit length. Throws
    /// <see cref="ArgumentException"/> when either is zero or the two are parallel.
    /// </summary>
    public static DQuaternion Facing(Double3 nose, Double3 roof)
    {
        if (nose.Length == 0 || roof.Length == 0)
            throw new ArgumentException($"A nose {nose} and a roof {roof} need a direction each.");
        // The rotation's own axes, in the axes that it turns into: +Z aft, +Y the roof across the nose, and +X = Y × Z.
        Double3 z = -nose.Normalized();
        Double3 across = roof - z * Double3.Dot(roof, z);
        if (across.Length < 1e-9 * roof.Length)
            throw new ArgumentException(
                $"The roof {roof} lies along the nose {nose}, so it gives no turn about the nose."
            );
        Double3 y = across.Normalized();
        var x = Double3.Cross(y, z);
        return FromAxes(x, y, z);
    }

    /// <summary>
    /// The rotation whose nose points along <paramref name="nose"/>, with the roof away from the planet across the nose:
    /// along the position of <paramref name="pose"/>, in a frame whose origin is the planet's center. When the nose points
    /// straight away from the planet or at it, the roof keeps its direction in <paramref name="pose"/>. Throws
    /// <see cref="ArgumentException"/> when the nose is zero.
    /// </summary>
    public static DQuaternion Facing(Double3 nose, in DPose pose)
    {
        Double3 up = pose.Position.Normalized(),
            along = nose.Normalized();
        Double3 roof = up - along * Double3.Dot(up, along);
        return Facing(nose, roof.Length > 1e-6 ? roof : pose.Rotation.Rotate(Double3.UnitY));
    }

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        // The autopilot wants only AttitudeTarget, so the events hold at least one, and the last one counts.
        AttitudeTarget target = default;
        foreach (ref AttitudeTarget each in events.Of<AttitudeTarget>())
            target = each;
        if (target.MaxTurnRate is not >= 0)
            throw new ArgumentOutOfRangeException(
                nameof(AttitudeTarget.MaxTurnRate),
                target.MaxTurnRate,
                "A turn rate lies from 0 up."
            );
        if (
            !float.IsFinite(target.FeedForward.X)
            || !float.IsFinite(target.FeedForward.Y)
            || !float.IsFinite(target.FeedForward.Z)
        )
            throw new ArgumentOutOfRangeException(
                nameof(AttitudeTarget.FeedForward),
                target.FeedForward,
                "A feed-forward is finite."
            );
        DPose pose = self.PoseIn(self.Frame);
        // The turn that is left, as a rotation vector in the frame's axes, and the correction toward it that the autopilot
        // asks for. The cap takes only the correction: the feed-forward is the target's own turn, which needs none.
        Double3 correction = pose.Rotation.AngularVelocityTo(target.Rotation, 1) * Gain;
        double maxRate = Math.Min(MaxTurnRate, target.MaxTurnRate);
        if (correction.Length > maxRate)
            correction *= maxRate / correction.Length;
        Double3 rate = target.FeedForward + correction;
        Double3 angularAcceleration = (rate - self.AngularVelocityIn(self.Frame)) / RateTime;
        // The principal axes in the frame's axes: the torque about each is its moment times the acceleration about it.
        DQuaternion principal = pose.Rotation * axes;
        Double3 inPrincipal = principal.Conjugate().Rotate(angularAcceleration);
        events.Add(new Torque { Value = principal.Rotate(inPrincipal * moments) });
    }

    /// <summary>The rotation that turns the unit axes X, Y and Z into the orthonormal, right-handed <paramref name="x"/>,
    /// <paramref name="y"/> and <paramref name="z"/>.</summary>
    private static DQuaternion FromAxes(Double3 x, Double3 y, Double3 z)
    {
        // The rotation matrix has the axes as its columns. Its quaternion comes from the largest of the four diagonal sums,
        // so that no square root is near zero.
        double trace = x.X + y.Y + z.Z;
        if (trace > 0)
        {
            double s = 2 * Math.Sqrt(trace + 1);
            return new DQuaternion(X: (y.Z - z.Y) / s, Y: (z.X - x.Z) / s, Z: (x.Y - y.X) / s, W: s / 4).Normalized();
        }

        if (x.X > y.Y && x.X > z.Z)
        {
            double s = 2 * Math.Sqrt(1 + x.X - y.Y - z.Z);
            return new DQuaternion(X: s / 4, Y: (y.X + x.Y) / s, Z: (z.X + x.Z) / s, W: (y.Z - z.Y) / s).Normalized();
        }

        if (y.Y > z.Z)
        {
            double s = 2 * Math.Sqrt(1 + y.Y - x.X - z.Z);
            return new DQuaternion(X: (y.X + x.Y) / s, Y: s / 4, Z: (z.Y + y.Z) / s, W: (z.X - x.Z) / s).Normalized();
        }

        double t = 2 * Math.Sqrt(1 + z.Z - x.X - y.Y);
        return new DQuaternion(X: (z.X + x.Z) / t, Y: (z.Y + y.Z) / t, Z: t / 4, W: (x.Y - y.X) / t).Normalized();
    }
}
