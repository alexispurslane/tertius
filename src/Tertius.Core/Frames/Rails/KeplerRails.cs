namespace Tertius.Frames.Rails;

/// <summary>
/// The motion of a frame on a Kepler orbit: pure functions of a <see cref="KeplerOrbit"/>, the time and the G·M of the
/// parent. It holds no state. The G·M <c>mu</c> of every function is in m³/s², and it comes from the point gravity of the
/// parent frame (see <see cref="OrbitMu"/>).
/// </summary>
public static class KeplerRails
{
    /// <summary>
    /// The G·M that a Kepler orbit takes from <paramref name="parentGravity"/>, the gravity of its parent frame, in m³/s²:
    /// the G·M of a point field that can be orbited (see <see cref="GravityField.CanBeOrbited"/>), else 0. Zero leaves
    /// <see cref="StateAt"/> at zero.
    /// </summary>
    public static double OrbitMu(in GravityField parentGravity) =>
        parentGravity is PointGravity { CanBeOrbited: true } point ? point.Mu : 0;

    /// <summary>The mean angular speed around the parent, in radians per second, for a parent with the G·M <paramref name="mu"/>.</summary>
    public static double MeanMotion(in KeplerOrbit orbit, double mu) => orbit.SemiMajorAxis > 0 ? Math.Sqrt(mu / (orbit.SemiMajorAxis * orbit.SemiMajorAxis * orbit.SemiMajorAxis)) : 0;

    /// <summary>The time for one turn, in seconds, for a parent with the G·M <paramref name="mu"/>. Infinite for an orbit that does not turn.</summary>
    public static double Period(in KeplerOrbit orbit, double mu)
    {
        double meanMotion = MeanMotion(orbit, mu);
        return meanMotion > 0 ? 2 * Math.PI / meanMotion : double.PositiveInfinity;
    }

    /// <summary>
    /// Position and velocity relative to the parent, in parent axes, at <paramref name="time"/>, for a parent with the G·M
    /// <paramref name="mu"/>. Gives zero when the semi-major axis is 0 or below, or when <paramref name="mu"/> is
    /// 0 or below. This is the only check. An orbit that is not valid for another reason, such as an eccentricity of 1.5 or a
    /// semi-major axis of NaN, and a G·M of NaN or infinity, give NaN or an ordinary state. The World checks
    /// <see cref="KeplerOrbit.IsValid"/> and <see cref="GravityField.CanBeOrbited"/> before it gives a frame the Kepler driver, so a
    /// Kepler frame never meets these cases.
    /// </summary>
    public static (Double3 Position, Double3 Velocity) StateAt(in KeplerOrbit orbit, double time, double mu)
    {
        if (orbit.SemiMajorAxis <= 0 || mu <= 0)
            return (Double3.Zero, Double3.Zero);

        double e = orbit.Eccentricity;
        double n = MeanMotion(orbit, mu);
        double eccentric = EccentricAnomalyAt(orbit, time, mu, out _);
        double cosE = Math.Cos(eccentric), sinE = Math.Sin(eccentric);
        double a = orbit.SemiMajorAxis;
        double b = a * Math.Sqrt(1 - e * e);

        // Perifocal frame: P toward periapsis, Q 90 degrees ahead in the direction of motion.
        double px = a * (cosE - e), qy = b * sinE;
        double edot = n / (1 - e * cosE);
        double vpx = -a * sinE * edot, vqy = b * cosE * edot;

        DQuaternion orient = PerifocalToParent(orbit);
        // Perifocal axes in a Y-up frame: P = +X, Q = -Z, so that +Y is the orbit normal (counterclockwise seen from +Y).
        Double3 position = orient.Rotate(new Double3(px, 0, -qy));
        Double3 velocity = orient.Rotate(new Double3(vpx, 0, -vqy));
        return (position, velocity);
    }

    /// <summary>
    /// The frame's orientation relative to the parent at <paramref name="time"/>: for <see cref="KeplerAttitude.Spin"/>, the
    /// axial tilt, then the spin about local +Y; for <see cref="KeplerAttitude.FacesParent"/>, the belly toward the parent
    /// and the nose along the motion across the horizon. The G·M <paramref name="mu"/> of the parent counts only for
    /// <see cref="KeplerAttitude.FacesParent"/>, which turns with the frame's place on its orbit.
    /// </summary>
    public static DQuaternion RotationAt(in KeplerOrbit orbit, double time, double mu)
    {
        if (orbit.Attitude == KeplerAttitude.FacesParent)
            return FacingParentAt(orbit, time, mu);
        return SpinRails.RotationAt(Spin(orbit), time);
    }

    /// <summary>
    /// The frame's angular velocity in parent axes at <paramref name="time"/>, in radians per second. The G·M
    /// <paramref name="mu"/> of the parent counts only for <see cref="KeplerAttitude.FacesParent"/>.
    /// </summary>
    public static Double3 AngularVelocityAt(in KeplerOrbit orbit, double time, double mu)
    {
        if (orbit.Attitude == KeplerAttitude.FacesParent)
        {
            // A frame that faces its parent turns with the direction to it: at h / r² about the orbit normal.
            (Double3 position, Double3 velocity) = StateAt(orbit, time, mu);
            return Double3.Cross(position, velocity) / position.LengthSquared;
        }
        return SpinRails.AngularVelocity(Spin(orbit));
    }

    /// <summary>
    /// The pull of a point gravity of <paramref name="mu"/> m³/s² at <paramref name="position"/>, in m/s²: the acceleration
    /// of a Kepler orbit there, which takes its G·M from that gravity (see <see cref="OrbitMu"/>).
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
    private static double EccentricAnomalyAt(in KeplerOrbit orbit, double time, double mu, out double turns)
    {
        double e = orbit.Eccentricity;
        double mean = orbit.MeanAnomalyAtEpoch + MeanMotion(orbit, mu) * (time - orbit.Epoch);
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
    /// The attitude that faces the parent. At periapsis, the belly (-Y) points at the parent, along -P, and the nose
    /// (-Z) points along the motion, Q, which is perifocal -Z: a quarter turn about Z. From there, the frame turns
    /// about the orbit normal by the true anomaly, which keeps the belly on the parent and the nose on the horizon. The
    /// true anomaly counts whole orbits, so the quaternion never flips its sign from one tick to the next.
    /// </summary>
    private static DQuaternion FacingParentAt(in KeplerOrbit orbit, double time, double mu)
    {
        double e = orbit.Eccentricity;
        double eccentric = EccentricAnomalyAt(orbit, time, mu, out double turns);
        double trueAnomaly = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(eccentric / 2), Math.Sqrt(1 - e) * Math.Cos(eccentric / 2));
        DQuaternion atPeriapsis = DQuaternion.FromAxisAngle(Double3.UnitZ, -Math.PI / 2);
        return PerifocalToParent(orbit) * DQuaternion.FromAxisAngle(Double3.UnitY, trueAnomaly + turns) * atPeriapsis;
    }

    /// <summary>The spin of the attitude <see cref="KeplerAttitude.Spin"/>: the axial tilt, then the spin about +Y.</summary>
    private static FrameSpin Spin(in KeplerOrbit orbit) => new(orbit.AxialTilt, orbit.SpinRate, orbit.SpinPhaseAtEpoch, orbit.Epoch);

    private static DQuaternion PerifocalToParent(in KeplerOrbit orbit)
    {
        // Rotations about +Y (the reference normal) and +X (the line of nodes), in the order Ω, i, ω.
        DQuaternion node = DQuaternion.FromAxisAngle(Double3.UnitY, orbit.LongitudeOfAscendingNode);
        DQuaternion tilt = DQuaternion.FromAxisAngle(Double3.UnitX, orbit.Inclination);
        DQuaternion periapsis = DQuaternion.FromAxisAngle(Double3.UnitY, orbit.ArgumentOfPeriapsis);
        return node * tilt * periapsis;
    }
}
