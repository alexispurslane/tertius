using System.Globalization;

namespace Tertius.Demo;

/// <summary>
/// The text of the readouts that the demo edit draws on each frame (docs/plans/blue-danube-demo.md, "The readouts on the
/// frame"). Every number shows in the invariant culture, with a comma between thousands.
/// </summary>
public static class ReadoutText
{
    /// <summary>Standard gravity, in m/s²: one g.</summary>
    public const double StandardGravity = 9.80665;

    /// <summary>Below this length, in meters, an altitude and a distance show in meters, and from it on in kilometers.</summary>
    private const double MetersBelow = 10e3;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>A speed of <paramref name="speed"/> m/s, with one decimal.</summary>
    public static string Speed(double speed) => string.Format(Invariant, "{0:N1} m/s", speed);

    /// <summary>
    /// The altitude of <paramref name="position"/>, a place in <c>earth</c>, in meters from Earth's center: its distance from Earth's center
    /// less <see cref="DemoScenes.EarthRadius"/>. In meters with one decimal below 10 km, and in kilometers with two above.
    /// </summary>
    public static string Altitude(Double3 position)
    {
        double altitude = position.Length - DemoScenes.EarthRadius;
        return altitude < MetersBelow
            ? string.Format(Invariant, "{0:N1} m", altitude)
            : string.Format(Invariant, "{0:N2} km", altitude / 1e3);
    }

    /// <summary>A distance of <paramref name="distance"/> meters: in meters with one decimal below 10 km, and in kilometers with one above.</summary>
    public static string Distance(double distance) =>
        distance < MetersBelow
            ? string.Format(Invariant, "{0:N1} m", distance)
            : string.Format(Invariant, "{0:N1} km", distance / 1e3);

    /// <summary>A felt gravity of <paramref name="felt"/> m/s², in g with two decimals.</summary>
    public static string Gravity(double felt) => string.Format(Invariant, "{0:F2} g", felt / StandardGravity);

    /// <summary>
    /// The sim clock at <paramref name="tick"/>, at the engine's 60 ticks per second from tick 0: <c>T+ h:mm:ss.s</c>, with
    /// the tenths of a second rounded down.
    /// </summary>
    public static string Clock(long tick)
    {
        long tenths = tick / 6;
        return string.Format(Invariant, "T+ {0}:{1:00}:{2:00}.{3}", tenths / 36_000, tenths / 600 % 60,
            tenths / 10 % 60, tenths % 10);
    }

    /// <summary>
    /// The rate of a film of every <paramref name="every"/>th tick in a video at 60 frames per second: real time for every
    /// tick, and a time-lapse of <paramref name="every"/> times for more.
    /// </summary>
    public static string Rate(int every) =>
        every == 1 ? "REAL TIME" : string.Format(Invariant, "TIME-LAPSE ×{0}", every);

    /// <summary>
    /// The four readouts of <paramref name="row"/>, one per line, each a label and its value, all of one width: the speed,
    /// the altitude, the distance to the station and the felt gravity.
    /// </summary>
    public static string[] Lines(ReadoutRow row) =>
    [
        Line("SPEED", Speed(row.Speed)),
        Line("ALTITUDE", Altitude(row.Position)),
        Line("TO STATION", Distance(row.ToStation)),
        Line("FELT GRAVITY", Gravity(row.Felt))
    ];

    private static string Line(string label, string value) => label.PadRight(13) + value.PadLeft(12);
}

/// <summary>
/// The lines of a check panel at <paramref name="row"/>, for a span of films whose first frame is <paramref name="first"/>
/// (docs/plans/blue-danube-demo.md, "The textbook checks"). All lines have one width.
/// </summary>
public delegate string[] CheckPanel(ReadoutRow first, ReadoutRow row);

/// <summary>The check panels of the demo edit: values of the run beside the values that a textbook formula predicts.</summary>
public static class CheckPanels
{
    /// <summary>Below this time since the span's first frame, in seconds, the run's period shows as measuring.</summary>
    private const double MeasuringFor = 10;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// The parking orbit: at r, the distance of the row's person from Earth's center, the circular speed √(GM / r) beside the
    /// row's speed, and the period 2π √(r³ / GM) of a circle beside the run's. GM is <see cref="DemoScenes.EarthMu"/>. The
    /// run's period is 2π t / θ, with t the sim time since <paramref name="first"/> and θ the angle round Earth's center
    /// between the two places, which holds while the span turns less than half an orbit. For the first 10 s of the span
    /// it shows <c>measuring</c>.
    /// </summary>
    public static string[] ParkingOrbit(ReadoutRow first, ReadoutRow row)
    {
        double r = row.Position.Length, mu = DemoScenes.EarthMu;
        double circularSpeed = Math.Sqrt(mu / r), circlePeriod = 2 * Math.PI * Math.Sqrt(r * r * r / mu);
        double t = (row.Tick - first.Tick) / 60.0;
        string run = "measuring";
        if (t >= MeasuringFor)
        {
            double cos = Double3.Dot(first.Position.Normalized(), row.Position.Normalized());
            run = string.Format(Invariant, "{0:F1} min", 2 * Math.PI * t / Math.Acos(Math.Clamp(cos, -1, 1)) / 60);
        }

        string[] lines =
        [
            string.Format(Invariant, "TEXTBOOK CHECK   parking orbit, {0:F1} km up",
                (r - DemoScenes.EarthRadius) / 1e3),
            string.Format(provider: Invariant, format: "circular speed √(GM/r)   {0,11}   run {1}",
                arg0: ReadoutText.Speed(circularSpeed),
                arg1: ReadoutText.Speed(row.Speed)),
            string.Format(provider: Invariant, format: "one orbit 2π√(r³/GM)     {0,11}   run {1}",
                arg0: string.Format(Invariant, "{0:F1} min", circlePeriod / 60), arg1: run)
        ];
        int width = lines.Max(line => line.Length);
        return [.. lines.Select(line => line.PadRight(width))];
    }

    /// <summary>
    /// Spin gravity in the wheel: at r, the row's distance from the station's axis, the felt gravity ω² r of a place at rest
    /// in the station's turning axes, beside the row's felt gravity. ω is the station's spin rate,
    /// <see cref="DemoScenes.SpinStation.Rate"/>, a constant of the scene as G·M is for the orbit. The panel reads no first
    /// row: it holds at each frame.
    /// </summary>
    public static string[] SpinGravity(ReadoutRow _, ReadoutRow row)
    {
        double omega = DemoScenes.SpinStation.Rate, r = row.Axis;
        string[] lines =
        [
            "TEXTBOOK CHECK   spin gravity in the wheel",
            string.Format(provider: Invariant, format: "ω {0:F5} rad/s, r {1:F2} m from the axis", arg0: omega,
                arg1: r),
            string.Format(provider: Invariant, format: "felt gravity ω²r   {0}   run {1}",
                arg0: Acceleration(omega * omega * r),
                arg1: Acceleration(row.Felt))
        ];
        int width = lines.Max(line => line.Length);
        return [.. lines.Select(line => line.PadRight(width))];
    }

    /// <summary>
    /// The coda's throws in the wheel: for each throw, its release's speed and angle above the floor, and where it lands,
    /// along the floor from under its release, in the direction of the throw: the first throw spinward, the second
    /// antispinward. The straight line is the landing of the release's straight line in inertial space
    /// (<see cref="ThrowRelease.Landing"/>), the run is the landing that the box's <see cref="FlightRecorder"/> measured,
    /// and the flat field is the parabola over a flat floor in ω² R, the floor's 1 g (<see cref="ThrowRelease.FlatLanding"/>),
    /// with ω <see cref="DemoScenes.SpinStation.Rate"/> and R the floor's radius. A throw before its release shows
    /// <c>waiting</c>, and a throw in the air shows <c>in the air</c> for its run. The panel reads no first row: every
    /// number comes from the row's own record of the throws, in the station's axes. The panel calls the flat field
    /// <c>uniform 1 g</c>, and writes no "fi" and no "fl": libass draws each such pair as one ligature of Menlo's, which
    /// would draw the line narrower than its count of characters and push it out of line with the others.
    /// </summary>
    public static string[] Throws(ReadoutRow _, ReadoutRow row)
    {
        double omega = DemoScenes.SpinStation.Rate;
        string[] lines =
        [
            string.Format(Invariant, "TEXTBOOK CHECK   a box thrown in the wheel, ω {0:F5} rad/s", omega),
            ThrowColumns(name: "lands", release: "release", line: "straight line", run: "run", flat: "uniform 1 g"),
            ThrowLine("spinward", 1, row.FirstThrow),
            ThrowLine("antispinward", -1, row.SecondThrow)
        ];
        int width = lines.Max(line => line.Length);
        return [.. lines.Select(line => line.PadRight(width))];
    }

    /// <summary>
    /// The line of one throw, named <paramref name="name"/>, with each landing measured along the floor in the throw's
    /// direction: spinward for a <paramref name="direction"/> of 1 and antispinward for -1.
    /// </summary>
    private static string ThrowLine(string name, int direction, ThrowReadout? readout)
    {
        if (readout is not { } thrown)
            return string.Format(Invariant, "{0,-12} waiting", name);
        WheelFloor floor = DemoScenes.DemoStation.WheelFloor;
        double omega = DemoScenes.SpinStation.Rate, half = DemoScenes.WheelCoda.BoxHalf;
        ThrowRelease release = thrown.Release;
        double speed = release.Velocity.Length;
        double angle = Math.Asin(Double3.Dot(release.Velocity, floor.Normal(release.Place)) / -speed) * 180 / Math.PI;
        double Along(Double3 place) => direction * floor.Spinward(release.Place, place);
        string line = Meters(Along(release.Landing(omega, floor, half).Place));
        string run = thrown.Landing is { } landing ? Meters(Along(landing.Place)) : "in the air";
        string flat = Meters(direction * release.FlatLanding(omega * omega * floor.Radius, floor, half).Spinward);
        return ThrowColumns(name: name,
            release: string.Format(provider: Invariant, format: "{0:F2} m/s {1:F1}°", arg0: speed, arg1: angle),
            line: line, run: run, flat: flat);
    }

    /// <summary>
    /// One line of the throws panel's columns: the name, the release, and the three landings, each right-aligned. Each
    /// column is wide enough that its header stands at least three spaces from the header before it, also "uniform 1 g"
    /// after "run".
    /// </summary>
    private static string ThrowColumns(string name, string release, string line, string run, string flat) =>
        string.Format(Invariant, "{0,-12} {1,-14} {2,14} {3,10} {4,14}", name, release, line, run, flat);

    private static string Meters(double meters) => string.Format(Invariant, "{0:F2} m", meters);


    private static string Acceleration(double acceleration) => string.Format(Invariant, "{0:F2} m/s²", acceleration);
}
