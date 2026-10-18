using System.Numerics;

namespace Tertius.Frames.Rails;

/// <summary>How a Kepler frame turns as it orbits.</summary>
public enum KeplerAttitude : byte
{
    /// <summary>The axial tilt, then a steady spin about the frame's own +Y, as a planet turns.</summary>
    Spin,

    /// <summary>
    /// Faces the parent, as a station in low orbit does: the belly (-Y) points at the parent, and the nose (-Z) points
    /// along the motion across the horizon. The tilt and the spin are not used.
    /// </summary>
    FacesParent,
}

/// <summary>
/// Kepler elements of a frame around its parent, plus the frame's attitude. Angles are radians, distances meters,
/// times seconds. The reference plane is the parent's XZ plane, with +Y as the orbit normal at zero inclination.
/// <para>
/// The orbit has no G·M of its own. A member that needs the G·M of the parent, in m³/s², takes it as a parameter, called
/// <c>mu</c>. The World and physics take it from the point gravity of the parent frame (see <see cref="FrameTree.KeplerMu"/>
/// and <see cref="MuFrom"/>), so a Kepler frame and the loose bodies in its parent fall on the same G·M. The members are
/// pure functions. The instance members are functions of the orbit, the time and the G·M. The static members need no
/// orbit elements: <see cref="Chord"/> and <see cref="Acceleration"/> are functions of a state and a G·M,
/// <see cref="MuFrom"/> reads the G·M from a gravity field, and <see cref="Circular"/> makes an orbit.
/// </para>
/// </summary>
public record struct KeplerOrbit(
    double SemiMajorAxis,
    double Eccentricity,
    double Inclination,
    double LongitudeOfAscendingNode,
    double ArgumentOfPeriapsis,
    double MeanAnomalyAtEpoch,
    double Epoch,
    DQuaternion AxialTilt,
    double SpinRate,
    double SpinPhaseAtEpoch,
    KeplerAttitude Attitude = KeplerAttitude.Spin)
{
    /// <summary>A circular orbit in the reference plane.</summary>
    public static KeplerOrbit Circular(double radius, double phase = 0, double spinRate = 0) =>
        new(radius, 0, 0, 0, 0, phase, 0, DQuaternion.Identity, spinRate, 0);

    /// <summary>
    /// True for an orbit whose own elements the Kepler driver can follow: a closed orbit, with an eccentricity from 0 up to
    /// but not including 1 and a positive semi-major axis, and every element, the tilt and the spin finite. The default
    /// orbit is not valid: it would leave the frame at its parent's origin. The orbit holds no G·M, so this does not check
    /// the parent: the World does (see <see cref="GravityField.CanBeOrbited"/>).
    /// </summary>
    public readonly bool IsValid =>
        SemiMajorAxis > 0 && double.IsFinite(SemiMajorAxis) &&
        Eccentricity >= 0 && Eccentricity < 1 &&
        double.IsFinite(Inclination) && double.IsFinite(LongitudeOfAscendingNode) && double.IsFinite(ArgumentOfPeriapsis) &&
        double.IsFinite(MeanAnomalyAtEpoch) && double.IsFinite(Epoch) && double.IsFinite(SpinRate) && double.IsFinite(SpinPhaseAtEpoch) &&
        double.IsFinite(AxialTilt.X) && double.IsFinite(AxialTilt.Y) && double.IsFinite(AxialTilt.Z) && double.IsFinite(AxialTilt.W) &&
        Attitude is KeplerAttitude.Spin or KeplerAttitude.FacesParent;

    /// <summary>
    /// The G·M that a Kepler orbit takes from <paramref name="parentGravity"/>, the gravity of its parent frame, in m³/s²:
    /// the G·M of a point field that can be orbited (see <see cref="GravityField.CanBeOrbited"/>), else 0. Zero leaves
    /// <see cref="StateAt"/> at zero.
    /// </summary>
    public static double MuFrom(in GravityField parentGravity) =>
        parentGravity is PointGravity { CanBeOrbited: true } point ? point.Mu : 0;

    /// <summary>The mean angular speed around the parent, in radians per second, for a parent with the G·M <paramref name="mu"/>.</summary>
    public readonly double MeanMotion(double mu) => SemiMajorAxis > 0 ? Math.Sqrt(mu / (SemiMajorAxis * SemiMajorAxis * SemiMajorAxis)) : 0;

    /// <summary>The time for one turn, in seconds, for a parent with the G·M <paramref name="mu"/>. Infinite for an orbit that does not turn.</summary>
    public readonly double Period(double mu)
    {
        double meanMotion = MeanMotion(mu);
        return meanMotion > 0 ? 2 * Math.PI / meanMotion : double.PositiveInfinity;
    }

    /// <summary>
    /// Position and velocity relative to the parent, in parent axes, at <paramref name="time"/>, for a parent with the G·M
    /// <paramref name="mu"/>. Gives zero when the semi-major axis is 0 or below, or when <paramref name="mu"/> is
    /// 0 or below. This is the only check. An orbit that is not valid for another reason, such as an eccentricity of 1.5 or a
    /// semi-major axis of NaN, and a G·M of NaN or infinity, give NaN or an ordinary state. The World checks
    /// <see cref="IsValid"/> and <see cref="GravityField.CanBeOrbited"/> before it gives a frame the Kepler driver, so a
    /// Kepler frame never meets these cases.
    /// </summary>
    public readonly (Double3 Position, Double3 Velocity) StateAt(double time, double mu) =>
        Orbits(mu) ? StateAtAnomaly(EccentricAnomalyAt(time, mu, out _), mu) : (Double3.Zero, Double3.Zero);

    /// <summary>
    /// The frame's orientation relative to the parent at <paramref name="time"/>: for <see cref="KeplerAttitude.Spin"/>, the
    /// axial tilt, then the spin about local +Y; for <see cref="KeplerAttitude.FacesParent"/>, the belly toward the parent
    /// and the nose along the motion across the horizon. The G·M <paramref name="mu"/> of the parent counts only for
    /// <see cref="KeplerAttitude.FacesParent"/>, which turns with the frame's place on its orbit.
    /// </summary>
    public readonly DQuaternion RotationAt(double time, double mu)
    {
        if (Attitude == KeplerAttitude.FacesParent)
            return FacingParentAtAnomaly(EccentricAnomalyAt(time, mu, out double turns), turns);
        return Spin.RotationAt(time);
    }

    /// <summary>
    /// The frame's angular velocity in parent axes at <paramref name="time"/>, in radians per second. The G·M
    /// <paramref name="mu"/> of the parent counts only for <see cref="KeplerAttitude.FacesParent"/>. A frame that faces a
    /// parent without G·M rests at the parent's origin and does not turn.
    /// </summary>
    public readonly Double3 AngularVelocityAt(double time, double mu)
    {
        if (Attitude == KeplerAttitude.FacesParent)
        {
            (Double3 position, Double3 velocity) = StateAt(time, mu);
            return FacingAngularVelocity(position, velocity);
        }
        return Spin.AngularVelocity;
    }

    /// <summary>
    /// The frame's angular acceleration in parent axes at <paramref name="time"/>, in radians per second squared: zero for
    /// <see cref="KeplerAttitude.Spin"/>, which turns steadily. A frame that faces its parent turns at ω = h / r², where
    /// h = r × v is constant on a two-body orbit, so only r² changes: dω/dt = h · d(1/r²)/dt = -2 h (r · v) / r⁴. That is
    /// zero at the apsides and on a circular orbit. A frame that faces a parent without G·M does not turn.
    /// </summary>
    public readonly Double3 AngularAccelerationAt(double time, double mu)
    {
        if (Attitude != KeplerAttitude.FacesParent)
            return Double3.Zero;
        (Double3 position, Double3 velocity) = StateAt(time, mu);
        double r2 = position.LengthSquared;
        return r2 == 0 ? Double3.Zero : Double3.Cross(position, velocity) * (-2 * Double3.Dot(position, velocity) / (r2 * r2));
    }

    /// <summary>
    /// The frame's motion over the physics step of <paramref name="step"/> seconds that ends at <paramref name="stepEnd"/>,
    /// for a parent with the G·M <paramref name="mu"/>. It solves Kepler's equation once, at <paramref name="stepEnd"/>, for
    /// the pose, the instant velocity and the angular velocity, which equal <see cref="StateAt"/>, <see cref="RotationAt"/>
    /// and <see cref="AngularVelocityAt"/> there to the last bit. The chord velocity is <see cref="Chord"/> of that state.
    /// The acceleration is the pull (<see cref="Acceleration"/>) where the step starts, the end place less the chord times
    /// the step, and the angular acceleration is <see cref="AngularAccelerationAt"/> at the start of the step. The proper
    /// acceleration is zero, because an orbit falls freely. Around a parent without G·M, the frame rests at the parent's
    /// origin with no chord and no acceleration.
    /// </summary>
    public readonly MotionSample Sample(double mu, double stepEnd, double step)
    {
        double eccentric = EccentricAnomalyAt(stepEnd, mu, out double turns);
        bool orbits = Orbits(mu);
        (Double3 position, Double3 velocity) = orbits ? StateAtAnomaly(eccentric, mu) : (Double3.Zero, Double3.Zero);
        bool faces = Attitude == KeplerAttitude.FacesParent;
        DQuaternion rotation = faces ? FacingParentAtAnomaly(eccentric, turns) : Spin.RotationAt(stepEnd);
        Double3 angularVelocity = faces ? FacingAngularVelocity(position, velocity) : Spin.AngularVelocity;
        // At the parent's origin, the chord and the pull would divide zero by zero.
        Double3 chord = orbits ? Chord(position, velocity, mu, step) : Double3.Zero;
        Double3 acceleration = orbits ? Acceleration(position - chord * step, mu) : Double3.Zero;
        return new MotionSample(new DPose(position, rotation), chord, velocity, acceleration, angularVelocity,
            AngularAccelerationAt(stepEnd - step, mu), Vector3.Zero);
    }

    /// <summary>
    /// The pull of a point gravity of <paramref name="mu"/> m³/s² at <paramref name="position"/>, in m/s²: the acceleration
    /// of a Kepler orbit there, which takes its G·M from that gravity (see <see cref="MuFrom"/>).
    /// </summary>
    public static Double3 Acceleration(Double3 position, double mu)
    {
        double r2 = position.LengthSquared;
        return position * (-mu / (r2 * Math.Sqrt(r2)));
    }

    /// <summary>
    /// The chord velocity of a Kepler orbit of <paramref name="mu"/> m³/s² over the step of <paramref name="step"/> seconds
    /// that ends where the orbit is at <paramref name="position"/> with <paramref name="velocity"/>: the change of place
    /// over that step divided by its length. It is the velocity that Jolt gives a body that moves along the orbit, and a
    /// frame that starts a step at it, with the orbit's acceleration (see <see cref="Acceleration"/>), ends the step on
    /// the orbit in the scheme that Jolt uses for bodies. The Taylor series of the two-body motion to the jerk leaves out
    /// terms of the fourth order in the step, under 1e-13 m over a step of 1/60 s in low orbit. It comes from the state, not
    /// from the difference of two places, so it keeps every digit of a velocity far from the parent, where a place rounds.
    /// </summary>
    public static Double3 Chord(Double3 position, Double3 velocity, double mu, double step)
    {
        double r2 = position.LengthSquared;
        double pull = -mu / (r2 * Math.Sqrt(r2));
        Double3 acceleration = position * pull;
        Double3 jerk = (velocity - position * (3 * Double3.Dot(position, velocity) / r2)) * pull;
        return velocity - acceleration * (step / 2) + jerk * (step * step / 6);
    }

    /// <summary>
    /// The eccentric anomaly at <paramref name="time"/>, from -π to π, and in <paramref name="turns"/> the whole orbits
    /// before it, in radians: a multiple of 2π. Their sum grows without a jump as the frame goes round.
    /// </summary>
    private readonly double EccentricAnomalyAt(double time, double mu, out double turns)
    {
        double e = Eccentricity;
        double mean = MeanAnomalyAtEpoch + MeanMotion(mu) * (time - Epoch);
        double m = Math.IEEERemainder(mean, 2 * Math.PI);
        turns = mean - m;

        // Solve Kepler's equation M = E - e sin E with Newton iterations. For a high eccentricity, the start is ±π on the
        // side of M: a start on the other side diverges for some M.
        double eccentric = e < 0.8 ? m : Math.CopySign(Math.PI, m);
        for (int i = 0; i < 30; i++)
        {
            double f = eccentric - e * Math.Sin(eccentric) - m;
            double step = f / (1 - e * Math.Cos(eccentric));
            eccentric -= step;
            if (Math.Abs(step) < 1e-15)
                break;
        }
        return eccentric;
    }

    /// <summary>
    /// True when the orbit moves the frame: a semi-major axis above 0 round a G·M above 0. Otherwise the frame rests at the
    /// parent's origin (see <see cref="StateAt"/>). A NaN passes, so that it gives NaN.
    /// </summary>
    private readonly bool Orbits(double mu) => !(SemiMajorAxis <= 0 || mu <= 0);

    /// <summary>
    /// Position and velocity relative to the parent, in parent axes, at the eccentric anomaly <paramref name="eccentric"/>
    /// (see <see cref="EccentricAnomalyAt"/>), for a parent with the G·M <paramref name="mu"/>.
    /// </summary>
    private readonly (Double3 Position, Double3 Velocity) StateAtAnomaly(double eccentric, double mu)
    {
        double e = Eccentricity;
        double n = MeanMotion(mu);
        double cosE = Math.Cos(eccentric), sinE = Math.Sin(eccentric);
        double a = SemiMajorAxis;
        double b = a * Math.Sqrt(1 - e * e);

        // Perifocal frame: P toward periapsis, Q 90 degrees ahead in the direction of motion.
        double px = a * (cosE - e), qy = b * sinE;
        double edot = n / (1 - e * cosE);
        double vpx = -a * sinE * edot, vqy = b * cosE * edot;

        DQuaternion orient = PerifocalToParent();
        // Perifocal axes in a Y-up frame: P = +X, Q = -Z, so that +Y is the orbit normal (counterclockwise seen from +Y).
        Double3 position = orient.Rotate(new Double3(px, 0, -qy));
        Double3 velocity = orient.Rotate(new Double3(vpx, 0, -vqy));
        return (position, velocity);
    }

    /// <summary>
    /// The attitude that faces the parent, at the eccentric anomaly <paramref name="eccentric"/> after the whole orbits
    /// <paramref name="turns"/> (see <see cref="EccentricAnomalyAt"/>). At periapsis, the belly (-Y) points at the parent,
    /// along -P, and the nose (-Z) points along the motion, Q, which is perifocal -Z: a quarter turn about Z. From there, the
    /// frame turns about the orbit normal by the true anomaly, which keeps the belly on the parent and the nose on the
    /// horizon. The true anomaly counts whole orbits, so the quaternion never flips its sign from one tick to the next.
    /// </summary>
    private readonly DQuaternion FacingParentAtAnomaly(double eccentric, double turns)
    {
        double e = Eccentricity;
        double trueAnomaly = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(eccentric / 2), Math.Sqrt(1 - e) * Math.Cos(eccentric / 2));
        DQuaternion atPeriapsis = DQuaternion.FromAxisAngle(Double3.UnitZ, -Math.PI / 2);
        return PerifocalToParent() * DQuaternion.FromAxisAngle(Double3.UnitY, trueAnomaly + turns) * atPeriapsis;
    }

    /// <summary>
    /// The angular velocity of a frame that faces its parent from <paramref name="position"/> with
    /// <paramref name="velocity"/>: it turns with the direction to the parent, at h / r² about the orbit normal. At the
    /// parent's origin, where an orbit around no G·M rests, it does not turn.
    /// </summary>
    private static Double3 FacingAngularVelocity(Double3 position, Double3 velocity)
    {
        double r2 = position.LengthSquared;
        return r2 == 0 ? Double3.Zero : Double3.Cross(position, velocity) / r2;
    }

    /// <summary>The spin of the attitude <see cref="KeplerAttitude.Spin"/>: the axial tilt, then the spin about +Y.</summary>
    private readonly FrameSpin Spin => new(AxialTilt, SpinRate, SpinPhaseAtEpoch, Epoch);

    private readonly DQuaternion PerifocalToParent()
    {
        // Rotations about +Y (the reference normal) and +X (the line of nodes), in the order Ω, i, ω.
        DQuaternion node = DQuaternion.FromAxisAngle(Double3.UnitY, LongitudeOfAscendingNode);
        DQuaternion tilt = DQuaternion.FromAxisAngle(Double3.UnitX, Inclination);
        DQuaternion periapsis = DQuaternion.FromAxisAngle(Double3.UnitY, ArgumentOfPeriapsis);
        return node * tilt * periapsis;
    }
}
