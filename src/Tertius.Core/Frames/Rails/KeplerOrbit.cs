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
    FacesParent
}

/// <summary>
/// Kepler elements of a frame around its parent, plus the frame's attitude. Angles are radians, distances meters,
/// times seconds. The reference plane is the parent's XZ plane, with +Y as the orbit normal at zero inclination.
/// <para>
/// The orbit has no G·M of its own. A member that needs the G·M of the parent, in m³/s², takes it as a parameter, called
/// <c>mu</c>. A <c>KeplerMotion</c> takes it from the point gravity of its frame's parent in every tick (see
/// <see cref="MuFrom"/>), so a Kepler frame and the loose bodies in its parent fall on the same G·M. The members are
/// pure functions. The instance members are functions of the orbit, the time and the G·M. The static members need no
/// orbit elements: <see cref="Chord"/> and <see cref="Acceleration"/> are functions of a state and a G·M,
/// <see cref="Propagate"/> and <see cref="Lambert"/> solve two-body problems from states and a G·M,
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
    /// <summary>
    /// The angle within which <see cref="Lambert"/> refuses a transfer angle near 0, π or 2π, in radians: 1 degree. At 0
    /// and π the two points and the center lie on one line, which leaves the plane of the path free. Near π, a point 1.5 km
    /// to the side of an orbit tilts the plane of the transfer steeply, and the cost has no bound at π.
    /// </summary>
    public const double LambertRefusedAngle = Math.PI / 180;

    /// <summary>
    /// The relative change of the universal variable below which <see cref="Propagate"/> stops Newton's method. Newton's
    /// method doubles the digits in each step, so the last step leaves an error far below this, and the rounding of the
    /// state decides the result. At 1e-13, a step on a low orbit moves the place by 2e-6 m.
    /// </summary>
    private const double UniversalAnomalyTolerance = 1e-13;

    /// <summary>
    /// The most steps of Newton's method in <see cref="Propagate"/>, a bound above the need. From its start, a circle takes
    /// 1 step, an oval about 4, an escape over 2 days about 17, and a hyperbola over 1e16 s up to about 63. A hyperbola
    /// over more than about 1e28 s takes more, and the propagation refuses it.
    /// </summary>
    private const int PropagationIterations = 100;

    /// <summary>
    /// Below this size of z, <see cref="Stumpff"/> sums the series, which leaves out terms under 5e-14 of the values there.
    /// The closed forms lose digits to the difference of two values near 1 for a small z.
    /// </summary>
    private const double StumpffSeriesLimit = 0.1;

    /// <summary>
    /// The least z that <see cref="Lambert"/> searches: -4π², a hyperbola whose anomaly turns by 2π. A path that needs a
    /// lower z is far faster than the escape speed, and the search then refuses it.
    /// </summary>
    private const double LambertLowestZ = -4 * Math.PI * Math.PI;

    /// <summary>The largest z that <see cref="Lambert"/> searches: 4π², where the time of one revolution has no bound.</summary>
    private const double LambertHighestZ = 4 * Math.PI * Math.PI;

    /// <summary>
    /// The width of the range of z at which the bisection of <see cref="Lambert"/> stops: a few roundings of z near 10.
    /// The time of the demo's transfer then lies within 1e-11 s of the time of flight. The bisection takes 53 halvings.
    /// </summary>
    private const double LambertZTolerance = 1e-14;

    /// <summary>The most halvings of the bisection of <see cref="Lambert"/>: a bound, twice the 53 that the tolerance takes.</summary>
    private const int LambertBisections = 100;

    /// <summary>
    /// How far the time of the solution of <see cref="Lambert"/> may lie from the time of flight, as a part of it. A solution
    /// inside the range of z lies within 1e-12 of it on the demo's paths, and within 4e-10 on a hyperbola at 6,000 km/s. A
    /// time that needs a z beyond the range ends the search at an end of the range, and misses by far more.
    /// </summary>
    private const double LambertTimeTolerance = 1e-6;

    /// <summary>
    /// True for an orbit whose own elements a <c>KeplerMotion</c> can follow: a closed orbit, with an eccentricity from 0 up
    /// to but not including 1 and a positive semi-major axis, and every element, the tilt and the spin finite. The default
    /// orbit is not valid: it would leave the frame at its parent's origin. The orbit holds no G·M, so this does not check
    /// the parent: the component does, in every tick (see <see cref="MuFrom"/>).
    /// </summary>
    public readonly bool IsValid =>
        SemiMajorAxis > 0 && double.IsFinite(SemiMajorAxis) &&
        Eccentricity >= 0 && Eccentricity < 1 &&
        double.IsFinite(Inclination) && double.IsFinite(LongitudeOfAscendingNode) &&
        double.IsFinite(ArgumentOfPeriapsis) &&
        double.IsFinite(MeanAnomalyAtEpoch) && double.IsFinite(Epoch) && double.IsFinite(SpinRate) &&
        double.IsFinite(SpinPhaseAtEpoch) &&
        double.IsFinite(AxialTilt.X) && double.IsFinite(AxialTilt.Y) && double.IsFinite(AxialTilt.Z) &&
        double.IsFinite(AxialTilt.W) &&
        Attitude is KeplerAttitude.Spin or KeplerAttitude.FacesParent;

    /// <summary>The spin of the attitude <see cref="KeplerAttitude.Spin"/>: the axial tilt, then the spin about +Y.</summary>
    private readonly FrameSpin Spin => new(AxialTilt, SpinRate, SpinPhaseAtEpoch, Epoch);

    /// <summary>A circular orbit in the reference plane.</summary>
    public static KeplerOrbit Circular(double radius, double phase = 0, double spinRate = 0) =>
        new(radius, 0, 0, 0, 0, phase, 0, DQuaternion.Identity, spinRate, 0);

    /// <summary>
    /// The G·M that a Kepler orbit takes from <paramref name="parentGravity"/>, the gravity of its parent frame, in m³/s²:
    /// the G·M of a point field that can be orbited (see <see cref="GravityField.CanBeOrbited"/>), else 0. Zero leaves
    /// <see cref="StateAt"/> at zero.
    /// </summary>
    public static double MuFrom(in GravityField parentGravity) =>
        parentGravity is PointGravity { CanBeOrbited: true } point ? point.Mu : 0;

    /// <summary>The mean angular speed around the parent, in radians per second, for a parent with the G·M
    /// <paramref name="mu"/>.</summary>
    public readonly double MeanMotion(double mu) =>
        SemiMajorAxis > 0 ? Math.Sqrt(mu / (SemiMajorAxis * SemiMajorAxis * SemiMajorAxis)) : 0;

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
    /// semi-major axis of NaN, and a G·M of NaN or infinity, give NaN or an ordinary state. A <c>KeplerMotion</c> takes only
    /// an orbit that <see cref="IsValid"/>, and samples it only for a G·M above 0 from <see cref="MuFrom"/>, so a Kepler
    /// frame never meets these cases.
    /// </summary>
    public readonly (Double3 Position, Double3 Velocity) StateAt(double time, double mu) =>
        Orbits(mu) ? StateAtAnomaly(EccentricAnomalyAt(time, mu, out _), mu) : (Double3.Zero, Double3.Zero);

    /// <summary>
    /// The frame's orientation relative to the parent at <paramref name="time"/>: for <see cref="KeplerAttitude.Spin"/>, the
    /// axial tilt, then the spin about local +Y; for <see cref="KeplerAttitude.FacesParent"/>, the belly toward the parent
    /// and the nose along the motion across the horizon. The G·M <paramref name="mu"/> of the parent counts only for
    /// <see cref="KeplerAttitude.FacesParent"/>, which turns with the frame's place on its orbit.
    /// </summary>
    public readonly DQuaternion RotationAt(double time, double mu) => Attitude switch
    {
        KeplerAttitude.Spin => Spin.RotationAt(time),
        KeplerAttitude.FacesParent => FacingParentAtAnomaly(EccentricAnomalyAt(time, mu, out double turns), turns)
    };

    /// <summary>
    /// The frame's angular velocity in parent axes at <paramref name="time"/>, in radians per second. The G·M
    /// <paramref name="mu"/> of the parent counts only for <see cref="KeplerAttitude.FacesParent"/>. A frame that faces a
    /// parent without G·M rests at the parent's origin and does not turn.
    /// </summary>
    public readonly Double3 AngularVelocityAt(double time, double mu) => Attitude switch
    {
        KeplerAttitude.Spin => Spin.AngularVelocity,
        KeplerAttitude.FacesParent => FacingAngularVelocityAt(time, mu)
    };

    /// <summary>The angular velocity of <see cref="AngularVelocityAt"/> for <see cref="KeplerAttitude.FacesParent"/>.</summary>
    private readonly Double3 FacingAngularVelocityAt(double time, double mu)
    {
        (Double3 position, Double3 velocity) = StateAt(time, mu);
        return FacingAngularVelocity(position, velocity);
    }

    /// <summary>
    /// The frame's angular acceleration in parent axes at <paramref name="time"/>, in radians per second squared: zero for
    /// <see cref="KeplerAttitude.Spin"/>, which turns steadily. A frame that faces its parent turns at ω = h / r², where
    /// h = r × v is constant on a two-body orbit, so only r² changes: dω/dt = h · d(1/r²)/dt = -2 h (r · v) / r⁴. That is
    /// zero at the apsides and on a circular orbit. A frame that faces a parent without G·M does not turn.
    /// </summary>
    public readonly Double3 AngularAccelerationAt(double time, double mu) => Attitude switch
    {
        KeplerAttitude.Spin => Double3.Zero,
        KeplerAttitude.FacesParent => FacingAngularAccelerationAt(time, mu)
    };

    /// <summary>The angular acceleration of <see cref="AngularAccelerationAt"/> for <see cref="KeplerAttitude.FacesParent"/>.</summary>
    private readonly Double3 FacingAngularAccelerationAt(double time, double mu)
    {
        (Double3 position, Double3 velocity) = StateAt(time, mu);
        double r2 = position.LengthSquared;
        return r2 == 0
            ? Double3.Zero
            : Double3.Cross(position, velocity) * (-2 * Double3.Dot(position, velocity) / (r2 * r2));
    }

    /// <summary>
    /// The frame's motion over the physics step of <paramref name="step"/> seconds that ends at <paramref name="stepEnd"/>,
    /// for a parent with the G·M <paramref name="mu"/>. It solves Kepler's equation once, at <paramref name="stepEnd"/>, for
    /// the pose, the instant velocity and the angular velocity, which equal <see cref="StateAt"/>, <see cref="RotationAt"/>
    /// and <see cref="AngularVelocityAt"/> there to the last bit. The chord velocity is <see cref="Chord"/> of that state.
    /// The acceleration is the pull (<see cref="Acceleration"/>) where the step starts, the end place less the chord times
    /// the step. The angular acceleration is the mean over the step, the meaning of every step record's angular acceleration
    /// (docs/plans/spin-gravity.md, "V3"): the change of the angular velocity over the step, divided by the step, with
    /// <see cref="AngularVelocityAt"/> solved at the start of the step too. It is zero for <see cref="KeplerAttitude.Spin"/>,
    /// which turns steadily. Around a parent without G·M, the frame rests at the parent's origin with no chord and no
    /// acceleration.
    /// </summary>
    public readonly MotionSample Sample(double mu, double stepEnd, double step)
    {
        double eccentric = EccentricAnomalyAt(stepEnd, mu, out double turns);
        bool orbits = Orbits(mu);
        (Double3 position, Double3 velocity) = orbits ? StateAtAnomaly(eccentric, mu) : (Double3.Zero, Double3.Zero);
        bool faces = Attitude switch
        {
            KeplerAttitude.Spin => false,
            KeplerAttitude.FacesParent => true
        };
        DQuaternion rotation = faces ? FacingParentAtAnomaly(eccentric, turns) : Spin.RotationAt(stepEnd);
        Double3 angularVelocity = faces ? FacingAngularVelocity(position, velocity) : Spin.AngularVelocity;
        // At the parent's origin, the chord and the pull would divide zero by zero.
        Double3 chord = orbits ? Chord(position, velocity, mu, step) : Double3.Zero;
        Double3 acceleration = orbits ? Acceleration(position - chord * step, mu) : Double3.Zero;
        return new(new(position, rotation), chord, velocity, acceleration, angularVelocity,
            (angularVelocity - AngularVelocityAt(stepEnd - step, mu)) / step);
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
    /// The state after <paramref name="time"/> seconds on the two-body path of a point gravity of <paramref name="mu"/>
    /// m³/s² at the origin, from <paramref name="position"/> with <paramref name="velocity"/>: the position in meters and
    /// the velocity in m/s, relative to the center of the gravity, in the axes of the inputs, as <see cref="StateAt"/>
    /// gives them. A negative time runs the path back, and a time of 0 keeps the state to the bit. The path can be a
    /// circle, an oval, a parabola or a hyperbola, and it needs no orbit elements.
    /// </summary>
    /// <remarks>
    /// The universal variable χ, in √m, measures the path in one form for every kind of conic (Curtis, "Orbital Mechanics
    /// for Engineering Students", section 3.7). Newton's method solves the universal Kepler equation for it. Its slope is
    /// the distance from the center, which is never 0, so each step is defined, and the equation grows with χ. So its value
    /// at each step bounds the solution on one side, and a step that leaves those bounds, or that shrinks by less than
    /// half, gives way to a halving of them. On a closed path, the start is √a times the mean anomaly that the time adds,
    /// which is exact on a circle. Otherwise it is the time at the starting distance from the center. On a hyperbola that
    /// start lies far beyond the solution after a long time, where Newton's method alone takes out only about one unit of
    /// the hyperbolic anomaly per step, and the halvings bring it near: an escape over 2 days takes about 17 steps. The
    /// Lagrange coefficients f, g, ḟ and ġ then give the state from χ and Stumpff's functions.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The G·M is not finite or not above 0, the position is not finite or is
    /// at the center, the velocity is not finite, or the time is not finite. Or Newton's method does not converge in
    /// <see cref="PropagationIterations"/> steps, which happens only on a hyperbola over more than about 1e28 s.</exception>
    public static (Double3 Position, Double3 Velocity) Propagate(Double3 position, Double3 velocity, double mu,
        double time)
    {
        CheckGravity(mu);
        CheckPlace(position, nameof(position));
        if (!IsFinite(velocity))
            throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "The velocity must be finite.");
        if (!double.IsFinite(time))
            throw new ArgumentOutOfRangeException(nameof(time), time, "The time must be finite.");

        double r0 = position.Length;
        double sqrtMu = Math.Sqrt(mu);
        // r0 · vr0 / √μ, with vr0 the speed away from the center, in √m.
        double radial = Double3.Dot(position, velocity) / sqrtMu;
        // The reciprocal of the semi-major axis, in 1/m: above 0 on a closed path, 0 on a parabola, below 0 on a hyperbola.
        double alpha = 2 / r0 - velocity.LengthSquared / mu;
        double chi = alpha > 0 ? sqrtMu * alpha * time : sqrtMu * time / r0;
        // The universal Kepler function grows with χ, as its slope is the distance from the center. It is below 0 at
        // χ = 0 for a time above 0, so the solution lies above 0, and the other way round. Each value of the function
        // narrows this range, and a step that leaves the range, or that shrinks by less than half, gives way to halving.
        double low = time > 0 ? 0 : double.NegativeInfinity, high = time > 0 ? double.PositiveInfinity : 0;
        double lastStep = double.PositiveInfinity;
        bool converged = false;
        for (int i = 0; i < PropagationIterations; i++)
        {
            double z = alpha * chi * chi;
            (double c, double s) = Stumpff(z);
            double kepler = radial * chi * chi * c + (1 - alpha * r0) * chi * chi * chi * s + r0 * chi - sqrtMu * time;
            double distance = radial * chi * (1 - z * s) + (1 - alpha * r0) * chi * chi * c + r0;
            double next;
            if (!(double.IsFinite(kepler) && double.IsFinite(distance)))
            {
                // cosh overflows only far beyond the solution, on the side of χ's sign.
                if (chi > 0)
                    high = chi;
                else
                    low = chi;
                next = (low + high) / 2;
            }
            else
            {
                double step = kepler / distance;
                if (Math.Abs(step) <= UniversalAnomalyTolerance * Math.Abs(chi))
                {
                    chi -= step;
                    converged = true;
                    break;
                }

                if (kepler < 0)
                    low = chi;
                else
                    high = chi;
                next = chi - step;
                bool bounded = double.IsFinite(low) && double.IsFinite(high);
                if (bounded && (!(next > low && next < high) || Math.Abs(step) > lastStep / 2))
                    next = (low + high) / 2;
            }

            lastStep = Math.Abs(next - chi);
            chi = next;
        }

        if (!converged)
            throw new ArgumentOutOfRangeException(nameof(time), time,
                "Newton's method found no universal variable for this time: a hyperbola over more than about 1e28 s.");

        double zEnd = alpha * chi * chi;
        (double cEnd, double sEnd) = Stumpff(zEnd);
        Double3 later = position * (1 - chi * chi / r0 * cEnd) + velocity * (time - chi * chi * chi * sEnd / sqrtMu);
        double r = later.Length;
        Double3 laterVelocity = position * (sqrtMu / (r * r0) * chi * (zEnd * sEnd - 1)) +
                                velocity * (1 - chi * chi / r * cEnd);
        return (later, laterVelocity);
    }

    /// <summary>
    /// The velocities at both ends of the two-body path of a point gravity of <paramref name="mu"/> m³/s² at the origin
    /// that runs from <paramref name="from"/> to <paramref name="to"/> in <paramref name="timeOfFlight"/> seconds, in m/s,
    /// in the axes of the inputs. The path turns about <paramref name="normal"/>, counterclockwise seen from its tip,
    /// through the angle from <paramref name="from"/> to <paramref name="to"/> about it, from 0 to 2π, in less than one
    /// revolution. So above π the path runs the long way round. The normal need not be a unit vector, and only its side of
    /// the plane of the two points counts. A normal in that plane counts as on the side of from × to.
    /// </summary>
    /// <remarks>
    /// The universal-variable method of Bate, Mueller and White (Curtis, algorithm 5.2). The time of flight grows with the
    /// variable z over a single revolution, from a hyperbola at z below 0 to the slowest oval at 4π², so bisection finds z.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The G·M is not finite or not above 0, a point is not finite or is at the
    /// center, the time of flight is not finite or not above 0, or the normal is not finite or is zero. Or the path that
    /// takes that time is faster than the search covers: a hyperbola whose anomaly turns by more than 2π, far faster than
    /// any ship flies.</exception>
    /// <exception cref="ArgumentException">The angle about the normal from <paramref name="from"/> to <paramref name="to"/>
    /// lies within <see cref="LambertRefusedAngle"/> of 0 or π, where the two points and the center do not define the path's
    /// plane, and a small move of a point turns it far.</exception>
    public static (Double3 Departure, Double3 Arrival) Lambert(Double3 from, Double3 to, double timeOfFlight, double mu,
        Double3 normal)
    {
        CheckGravity(mu);
        CheckPlace(from, nameof(from));
        CheckPlace(to, nameof(to));
        if (!(timeOfFlight > 0 && double.IsFinite(timeOfFlight)))
            throw new ArgumentOutOfRangeException(nameof(timeOfFlight), timeOfFlight,
                "The time of flight must be finite and above zero.");
        if (!IsFinite(normal) || normal.LengthSquared == 0)
            throw new ArgumentOutOfRangeException(nameof(normal), normal, "The normal must be finite and not zero.");

        double r1 = from.Length, r2 = to.Length;
        Double3 cross = Double3.Cross(from, to);
        double sin = cross.Length / (r1 * r2), cos = Double3.Dot(from, to) / (r1 * r2);
        if (Double3.Dot(cross, normal) < 0)
            sin = -sin;
        double angle = Math.Atan2(sin, cos);
        if (angle < 0)
            angle += 2 * Math.PI;
        if (angle < LambertRefusedAngle || angle > 2 * Math.PI - LambertRefusedAngle ||
            Math.Abs(angle - Math.PI) < LambertRefusedAngle)
            throw new ArgumentException(
                $"The transfer angle of {angle * 180 / Math.PI:F3} degrees lies within a degree of 0 or 180 degrees, where the path has no plane.",
                nameof(to));

        double a = sin * Math.Sqrt(r1 * r2 / (1 - cos));
        double sqrtMu = Math.Sqrt(mu);
        double low = LambertLowestZ, high = LambertHighestZ;
        for (int i = 0; i < LambertBisections && high - low > LambertZTolerance; i++)
        {
            double middle = (low + high) / 2;
            if (LambertTime(middle, r1, r2, a, sqrtMu, out _) < timeOfFlight)
                low = middle;
            else
                high = middle;
        }

        double z = (low + high) / 2;
        double time = LambertTime(z, r1, r2, a, sqrtMu, out double y);
        if (!(Math.Abs(time - timeOfFlight) <= LambertTimeTolerance * timeOfFlight))
            throw new ArgumentOutOfRangeException(nameof(timeOfFlight), timeOfFlight,
                "The path that takes this time between these points is faster than the search covers: a hyperbola whose anomaly turns by more than 2π.");

        double f = 1 - y / r1, g = a * Math.Sqrt(y / mu), gDot = 1 - y / r2;
        return ((to - from * f) / g, (to * gDot - from) / g);
    }

    /// <summary>
    /// The time of flight of the path of <see cref="Lambert"/> at the variable <paramref name="z"/>, in seconds, and in
    /// <paramref name="y"/> the auxiliary length of Curtis's equation 5.38, in meters. Where y is below 0, there is no path,
    /// and the time is 0, below every time of flight, as the time falls to 0 where y does.
    /// </summary>
    private static double LambertTime(double z, double r1, double r2, double a, double sqrtMu, out double y)
    {
        (double c, double s) = Stumpff(z);
        y = r1 + r2 + a * (z * s - 1) / Math.Sqrt(c);
        if (y < 0)
            return 0;
        double x = Math.Sqrt(y / c);
        return (x * x * x * s + a * Math.Sqrt(y)) / sqrtMu;
    }

    /// <summary>
    /// Stumpff's functions C(z) and S(z) of the universal variable: (1 - cos √z) / z and (√z - sin √z) / √z³ for z above
    /// 0, the same with cosh and sinh of √-z below 0, and 1/2 and 1/6 at 0.
    /// </summary>
    private static (double C, double S) Stumpff(double z)
    {
        if (Math.Abs(z) < StumpffSeriesLimit)
            return (1.0 / 2 - z * (1.0 / 24 - z * (1.0 / 720 - z * (1.0 / 40320 - z / 3628800))),
                1.0 / 6 - z * (1.0 / 120 - z * (1.0 / 5040 - z * (1.0 / 362880 - z / 39916800))));
        if (z > 0)
        {
            double x = Math.Sqrt(z), half = Math.Sin(x / 2);
            // 1 - cos x as 2 sin² (x / 2), which keeps its digits near x = 2π, the end of Lambert's search.
            return (2 * half * half / z, (x - Math.Sin(x)) / (z * x));
        }

        double h = Math.Sqrt(-z);
        return ((Math.Cosh(h) - 1) / -z, (Math.Sinh(h) - h) / (-z * h));
    }

    private static void CheckGravity(double mu)
    {
        if (!(mu > 0 && double.IsFinite(mu)))
            throw new ArgumentOutOfRangeException(nameof(mu), mu, "The G·M must be finite and above zero.");
    }

    private static void CheckPlace(Double3 place, string name)
    {
        if (!IsFinite(place) || place.LengthSquared == 0)
            throw new ArgumentOutOfRangeException(name, place,
                "The place must be finite and away from the center of the gravity.");
    }

    private static bool IsFinite(Double3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

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
        double trueAnomaly = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(eccentric / 2),
            Math.Sqrt(1 - e) * Math.Cos(eccentric / 2));
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

    private readonly DQuaternion PerifocalToParent()
    {
        // Rotations about +Y (the reference normal) and +X (the line of nodes), in the order Ω, i, ω.
        DQuaternion node = DQuaternion.FromAxisAngle(Double3.UnitY, LongitudeOfAscendingNode);
        DQuaternion tilt = DQuaternion.FromAxisAngle(Double3.UnitX, Inclination);
        DQuaternion periapsis = DQuaternion.FromAxisAngle(Double3.UnitY, ArgumentOfPeriapsis);
        return node * tilt * periapsis;
    }
}
