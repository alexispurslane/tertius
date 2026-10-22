using Tertius.Frames.Rails;

namespace Tertius.Demo;

/// <summary>
/// The guidance of the shuttle's transfer to the station, in a frame whose origin is the planet's center, the center of
/// its point gravity, and which does not turn. It steers from the measured states of the shuttle and the station alone,
/// so it corrects every error of the steps before (docs/plans/blue-danube-demo.md, "The transfer guidance").
/// </summary>
/// <remarks>
/// <para>The arrival point lies a distance along the station's axis from the station's place at the arrival time, which
/// <see cref="KeplerOrbit.Propagate"/> gives from the station's state. The station flies on rails on exactly such a path,
/// so the propagation equals its rails to the rounding. <see cref="KeplerOrbit.Lambert"/> gives the velocity that reaches
/// the arrival point at the arrival time, turning about the station's orbit normal. The shuttle gains the difference in
/// <see cref="TrimTime"/>, at most at <see cref="BurnAcceleration"/>, so the thrust of the burn falls in its last seconds.</para>
/// <para>The autopilot burns while the velocity to gain is longer than <see cref="SpeedTolerance"/>, and trims it below
/// that with the translation thrusters alone. In the last <see cref="CoastTime"/>, or within <see cref="CoastAngle"/> of
/// the arrival point, it solves nothing more (see <see cref="Coasts"/>).</para>
/// </remarks>
/// <param name="Mu">The planet's G·M, in m³/s².</param>
public readonly record struct TransferGuidance(double Mu)
{
    /// <summary>The time in which the push takes the velocity to gain, in seconds, as <see cref="AscentGuidance.TrimTime"/>.</summary>
    public const double TrimTime = AscentGuidance.TrimTime;

    /// <summary>
    /// The length of the velocity to gain, in m/s, above which the shuttle burns its main engine, and at or below which it
    /// trims with the translation thrusters: 0.1, the speed that the trim takes in <see cref="TrimTime"/> at half
    /// <see cref="FrameTree.ZeroGravityThreshold"/>. So the trim's push, the velocity to gain over <see cref="TrimTime"/>, is
    /// at most half the threshold, and the cabin stays clearly weightless through the trim. A strapped person floats there
    /// in the rigid form of a character, which does not turn its up. At a push of the threshold itself, a person who
    /// touches its seat walks again and turns its up to the push at once. The burn runs on below the threshold for about
    /// 3 s, at a throttle of about 0.001.
    /// </summary>
    public const double SpeedTolerance = FrameTree.ZeroGravityThreshold / 2 * TrimTime;

    /// <summary>
    /// The longest push of a burn, in m/s²: a throttle of about 0.10, so that the departure burn of about 60 m/s lasts about
    /// 32 s, long enough to film and far below the climb's force on the belt. The push stays at this cap for about 16 s,
    /// until about 12 m/s is left to gain, and <see cref="TrimTime"/> then takes the rest.
    /// </summary>
    public const double BurnAcceleration = 3;

    /// <summary>
    /// The time before the arrival time in which the autopilot solves nothing more, in seconds. A short transfer turns a
    /// small miss into a large velocity to gain, and the transfer angle nears the angle that Lambert's problem refuses.
    /// </summary>
    public const double CoastTime = 60;

    /// <summary>
    /// The transfer angle left to the arrival point within which of 0 the autopilot solves nothing more, in radians: 2
    /// degrees, twice <see cref="KeplerOrbit.LambertRefusedAngle"/>, so a degree of margin keeps every solve clear of the
    /// refusal. In the demo's orbits the shuttle turns about 4 degrees in the last <see cref="CoastTime"/>, so there the
    /// coast starts by the time, and this angle decides only in a slower orbit (see <see cref="Coasts"/>).
    /// </summary>
    public const double CoastAngle = 2 * KeplerOrbit.LambertRefusedAngle;

    /// <summary>
    /// The angle from <paramref name="from"/> to <paramref name="to"/> about <paramref name="normal"/>, counterclockwise seen
    /// from its tip, in radians from 0 to 2π. About the station's orbit normal, a point ahead along the orbit lies at a
    /// small angle, and a point behind at an angle near 2π. The normal need not be a unit vector.
    /// </summary>
    public static double TransferAngle(Double3 from, Double3 to, Double3 normal)
    {
        Double3 cross = Double3.Cross(from, to);
        double angle = Math.Atan2(Math.CopySign(cross.Length, Double3.Dot(cross, normal)), Double3.Dot(from, to));
        return angle < 0 ? angle + 2 * Math.PI : angle;
    }

    /// <summary>
    /// The unit normal of the station's orbit, along its angular momentum: the direction of
    /// <paramref name="stationPosition"/> × <paramref name="stationVelocity"/>. The orbit runs counterclockwise about it.
    /// </summary>
    public static Double3 OrbitNormal(Double3 stationPosition, Double3 stationVelocity) =>
        Double3.Cross(stationPosition, stationVelocity).Normalized();

    /// <summary>
    /// The arrival point after <paramref name="time"/> seconds, and the station's velocity then, in m/s: the station's place
    /// on its two-body path from <paramref name="stationPosition"/> with <paramref name="stationVelocity"/>, plus
    /// <paramref name="arrivalDistance"/> meters along its unit axis <paramref name="axis"/>. The axis keeps its direction:
    /// it lies along the orbit's normal, about which the station turns.
    /// </summary>
    public (Double3 Point, Double3 StationVelocity) Arrival(
        Double3 stationPosition, Double3 stationVelocity, Double3 axis, double arrivalDistance, double time)
    {
        (Double3 position, Double3 velocity) = KeplerOrbit.Propagate(stationPosition, stationVelocity, Mu, time);
        return (position + axis * arrivalDistance, velocity);
    }

    /// <summary>
    /// True when the autopilot coasts and solves nothing more: when <paramref name="timeLeft"/> is <see cref="CoastTime"/>
    /// or less, or when the transfer angle from <paramref name="shuttlePosition"/> to the arrival point (see
    /// <see cref="Arrival"/>) lies within <see cref="CoastAngle"/> of 0, measured either way about the station's orbit
    /// normal. The arguments are those of <see cref="Steer"/>. Lambert's problem refuses an angle within a degree of 0, and
    /// in a slow orbit the angle falls there before the last <see cref="CoastTime"/>. The angle left never nears 180
    /// degrees, because the transfer starts inside a window below 179 degrees and the angle only falls.
    /// </summary>
    public bool Coasts(Double3 shuttlePosition, Double3 stationPosition, Double3 stationVelocity, Double3 axis,
        double arrivalDistance, double timeLeft)
    {
        if (timeLeft <= CoastTime)
            return true;
        Double3 arrival = Arrival(stationPosition, stationVelocity, axis, arrivalDistance, timeLeft).Point;
        double angle = TransferAngle(shuttlePosition, arrival, OrbitNormal(stationPosition, stationVelocity));
        return angle is < CoastAngle or > 2 * Math.PI - CoastAngle;
    }

    /// <summary>
    /// The steering of a tick of the transfer, for a shuttle at <paramref name="shuttlePosition"/> that moves at
    /// <paramref name="shuttleVelocity"/>, to the arrival point <paramref name="arrivalDistance"/> meters along the unit
    /// axis <paramref name="axis"/> from the station, which is at <paramref name="stationPosition"/> and moves at
    /// <paramref name="stationVelocity"/> now, when <paramref name="timeLeft"/> seconds are left to the arrival time. The
    /// path turns about the station's orbit normal (see <see cref="OrbitNormal"/>). Lambert's problem refuses a time left
    /// of 0 or below and a transfer angle within a degree of 0 or 180 degrees, so the autopilot steers only while it does
    /// not coast (see <see cref="Coasts"/>).
    /// </summary>
    public TransferSteering Steer(Double3 shuttlePosition, Double3 shuttleVelocity, Double3 stationPosition,
        Double3 stationVelocity,
        Double3 axis, double arrivalDistance, double timeLeft)
    {
        (Double3 arrival, Double3 stationAtArrival) =
            Arrival(stationPosition, stationVelocity, axis, arrivalDistance, timeLeft);
        Double3 normal = OrbitNormal(stationPosition, stationVelocity);
        (Double3 needed, Double3 atArrival) = KeplerOrbit.Lambert(shuttlePosition, arrival, timeLeft, Mu, normal);
        Double3 toGain = needed - shuttleVelocity;
        Double3 push = toGain / TrimTime;
        double length = push.Length;
        if (length > BurnAcceleration)
            push *= BurnAcceleration / length;
        return new(toGain, push, -(atArrival - stationAtArrival).Normalized());
    }
}

/// <summary>
/// The steering of a tick of the transfer (see <see cref="TransferGuidance.Steer"/>), in the frame's axes.
/// </summary>
/// <param name="ToGain">The velocity to gain, in m/s: the velocity of the path to the arrival point less the shuttle's.</param>
/// <param name="Push">
/// The acceleration that the shuttle's thrust should give, in m/s²: the velocity to gain over
/// <see cref="TransferGuidance.TrimTime"/>, at most <see cref="TransferGuidance.BurnAcceleration"/> long.
/// </param>
/// <param name="BrakeDirection">
/// The unit direction of the brake: the opposite of the shuttle's velocity relative to the station at the arrival point, on
/// the path. In the trim, the nose turns toward it at the plan's <see cref="RendezvousPlan.WeightlessTurnRate"/>, and in
/// the coast against the velocity relative to the station, so the brake starts after only a small turn.
/// </param>
public readonly record struct TransferSteering(Double3 ToGain, Double3 Push, Double3 BrakeDirection)
{
    /// <summary>True while the velocity to gain is longer than <see cref="TransferGuidance.SpeedTolerance"/>: the main engine burns.</summary>
    public bool Burns => ToGain.Length > TransferGuidance.SpeedTolerance;
}
