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
    /// The fastest turn that the autopilot may ask for to reach <see cref="Rotation"/>, in radians per second, at most
    /// <see cref="AttitudeAutopilot.MaxTurnRate"/>. A layer that flies a weightless cabin with a strapped crew turns slowly,
    /// so that the turn gives the crew no felt gravity (see <see cref="RendezvousPlan.WeightlessTurnRate"/>).
    /// </summary>
    public required double MaxTurnRate;
}

/// <summary>
/// Turns its entity's body toward the <see cref="AttitudeTarget"/> of each tick that brings one, with a torque, as the
/// attitude thrusters of a ship do. It asks for a turn rate along the shortest turn to the target, <see cref="Gain"/> per
/// radian of the turn that is left, and at most the target's <see cref="AttitudeTarget.MaxTurnRate"/> and
/// <see cref="MaxTurnRate"/>. Then it gives the torque that brings the body's
/// angular velocity to that rate in <see cref="RateTime"/>, from the body's moments of inertia. A tick without a target
/// gives no torque, so the autopilot keeps nothing from an earlier tick. When a tick brings several targets, the last one
/// counts. It runs at priority 10, after every layer of the flight program, so it follows the last layer that speaks. Its entity needs a dynamic body, whose principal axes are its own axes.
/// </summary>
/// <remarks>
/// The autopilot reads the body's rotation and angular velocity at the end of the last finished step, and its torque
/// acts in the step of the next tick. So it acts on what it read two steps late. <see cref="RateTime"/> is 20 steps, so
/// that lag costs the loop little.
/// </remarks>
/// <param name="inertia">The moments of inertia of the body about its own X, Y and Z axes, in kg·m².</param>
public sealed class AttitudeAutopilot(Vector3 inertia) : IComponent
{
    /// <summary>The turn rate that the autopilot asks for per radian of the turn that is left, in 1/s.</summary>
    public const double Gain = 0.8;

    /// <summary>The fastest turn that the autopilot asks for, in radians per second: about 8.6 degrees per second.</summary>
    public const double MaxTurnRate = 0.15;

    /// <summary>The time in which the torque brings the body's angular velocity to the rate asked for, in seconds.</summary>
    public const double RateTime = 1.0 / 3;

    private readonly Vector3 inertia = inertia;

    // After every layer of the flight program, priorities 0 to 2, which add the target in the same tick.
    public int Priority => 10;

    public EventMask Wants => EventMask.Of<AttitudeTarget>();

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
            throw new ArgumentException($"The roof {roof} lies along the nose {nose}, so it gives no turn about the nose.");
        Double3 y = across.Normalized();
        Double3 x = Double3.Cross(y, z);
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
        Double3 up = pose.Position.Normalized(), along = nose.Normalized();
        Double3 roof = up - along * Double3.Dot(up, along);
        return Facing(nose, roof.Length > 1e-6 ? roof : pose.Rotation.Rotate(Double3.UnitY));
    }

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        // The autopilot wants only AttitudeTarget, so the events hold at least one, and the last one counts.
        AttitudeTarget target = default;
        foreach (ref AttitudeTarget each in events.Of<AttitudeTarget>())
            target = each;
        DPose pose = self.PoseIn(self.Frame);
        // The turn that is left, as a rotation vector in the frame's axes, and the turn rate that the autopilot asks for.
        Double3 rate = pose.Rotation.AngularVelocityTo(target.Rotation, 1) * Gain;
        double maxRate = Math.Min(MaxTurnRate, target.MaxTurnRate);
        if (rate.Length > maxRate)
            rate *= maxRate / rate.Length;
        Double3 angularAcceleration = (rate - (Double3)self.AngularVelocityIn(self.Frame)) / RateTime;
        Double3 inBody = pose.Rotation.Conjugate().Rotate(angularAcceleration);
        self.Body.AddTorque(pose.Rotation.Rotate(inBody * (Double3)inertia).ToVector3());
    }

    /// <summary>The rotation that turns the unit axes X, Y and Z into the orthonormal, right-handed <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/>.</summary>
    private static DQuaternion FromAxes(Double3 x, Double3 y, Double3 z)
    {
        // The rotation matrix has the axes as its columns. Its quaternion comes from the largest of the four diagonal sums,
        // so that no square root is near zero.
        double trace = x.X + y.Y + z.Z;
        if (trace > 0)
        {
            double s = 2 * Math.Sqrt(trace + 1);
            return new DQuaternion((y.Z - z.Y) / s, (z.X - x.Z) / s, (x.Y - y.X) / s, s / 4).Normalized();
        }
        if (x.X > y.Y && x.X > z.Z)
        {
            double s = 2 * Math.Sqrt(1 + x.X - y.Y - z.Z);
            return new DQuaternion(s / 4, (y.X + x.Y) / s, (z.X + x.Z) / s, (y.Z - z.Y) / s).Normalized();
        }
        if (y.Y > z.Z)
        {
            double s = 2 * Math.Sqrt(1 + y.Y - x.X - z.Z);
            return new DQuaternion((y.X + x.Y) / s, s / 4, (z.Y + y.Z) / s, (z.X - x.Z) / s).Normalized();
        }
        double t = 2 * Math.Sqrt(1 + z.Z - x.X - y.Y);
        return new DQuaternion((z.X + x.Z) / t, (z.Y + y.Z) / t, t / 4, (x.Y - y.X) / t).Normalized();
    }
}
