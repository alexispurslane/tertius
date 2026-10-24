using System.Globalization;
using System.Text;
using Tertius.Simulation;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// One scene of a motion drift measurement (ticket ter-kugq). The scene sits in one root frame.
/// Its reference point starts at a given place, and every body in it gets the same extra velocity, the common velocity,
/// on top of its own motion. A Galilean shift must not change physics, so the scene's exact answer is known for every
/// common velocity, and the measurement records how far the World on the real Jolt backend ends up from it.
/// </summary>
internal abstract class DriftScene
{
    /// <summary>The name of the measurement, which is also the name of its CSV file.</summary>
    public abstract string Measurement { get; }

    /// <summary>The direction of the common velocity in the easy case: a coordinate axis perpendicular to <see cref="Motion"/>
    /// .</summary>
    public abstract Double3 AxisDirection { get; }

    /// <summary>The unit vector of the scene's own motion: the errors are split into the part along it and the part across it.</summary>
    public abstract Double3 Motion { get; }

    /// <summary>The root frame that the scene runs in.</summary>
    public abstract FrameSpec Space { get; }

    /// <summary>Spawns the scene with its reference point at <paramref name="start"/> and the common velocity
    /// <paramref name="common"/>.</summary>
    public abstract Entity[] Spawn(World world, FrameHandle space, Double3 start, Double3 common);

    /// <summary>The measured position and velocity, in the root frame: of one body, or the relative ones of a pair.</summary>
    public abstract (Double3 Position, Double3 Velocity) Measure(World world, FrameHandle space, Entity[] bodies);

    /// <summary>The exact position and velocity after <paramref name="ticks"/> ticks of <paramref name="dt"/> seconds each.</summary>
    public abstract (Double3 Position, Double3 Velocity) Expected(int ticks, double dt, Double3 start, Double3 common);

    /// <summary>
    /// The size of one float rounding of the measured velocity at spawn, in m/s: the distance between the velocity that
    /// the scene asks for, in doubles, and the same velocity narrowed to a float in each component, as Jolt stores it.
    /// </summary>
    public abstract double StartVelocityRounding(Double3 common);

    /// <summary>The velocity narrowed to a float in each component, as physics narrows a velocity when Jolt gets it.</summary>
    protected static Double3 Narrowed(Double3 velocity) => velocity.ToVector3();
}

/// <summary>
/// The method that every motion drift measurement shares (ticket ter-kugq). For each common speed in <see cref="Speeds"/>
/// and each of two directions (the scene's <see cref="DriftScene.AxisDirection"/> and the diagonal (1, 1, 1)/√3), it runs
/// two scenes for 60 s at 60 Hz, each in a new World on a new real Jolt backend, single-threaded, with no numeric frames:
/// <list type="bullet">
/// <item>A speed run: the scene's reference point starts at −V·(60 s)/2 along the direction, so that its path passes the
/// origin at mid-run, and every body has the common velocity V along the direction.</item>
/// <item>A distance run: no common velocity, and the reference point starts at the largest distance from the origin that
/// any body of the speed run reached, along the same direction.</item>
/// </list>
/// After 300, 1200 and 3600 ticks (5, 20 and 60 s), it records the error of the measured position and velocity against
/// the scene's exact answer. It writes one CSV per measurement and judges no number.
/// </summary>
internal static class MotionDrift
{
    /// <summary>How many ticks a run lasts.</summary>
    public const int RunTicks = 3600;

    /// <summary>How long a run lasts, in seconds, for the start of a speed run at minus half its path.</summary>
    public const double RunSeconds = 60;

    /// <summary>
    /// Jolt's speed limit for a body in these runs, in m/s. The engine's default of 30 km/s would clamp the fastest common
    /// speeds, so the measurement raises it above every speed that a run reaches.
    /// </summary>
    public const float SpeedLimit = 1e6f;

    /// <summary>The radius of the root frame's bubble, in meters, far beyond the largest distance that a run reaches.</summary>
    public const float SpaceRadius = 1e9f;

    /// <summary>The environment variable that names the folder for the CSV files.</summary>
    public const string OutputVariable = "TERTIUS_PRECISION_OUT";

    /// <summary>The common speeds, in m/s.</summary>
    public static readonly double[] Speeds =
        [0, 1, 10, 50, 100, 200, 500, 1000, 2000, 5000, 7672, 10000, 20000, 50000, 100000, 300000];

    /// <summary>The ticks after which a run records its errors: 5, 20 and 60 s at 60 Hz.</summary>
    public static readonly int[] SampleTicks = [300, 1200, 3600];

    /// <summary>The direction of the hard case, where every component of every velocity is large.</summary>
    public static readonly Double3 Diagonal = new Double3(1, 1, 1) / Math.Sqrt(3);

    private static readonly string[] Header =
    [
        "run", "direction", "direction_x (unitless)", "direction_y (unitless)", "direction_z (unitless)",
        "common_speed (m/s)", "matching_speed (m/s)", "start_along_direction (m)", "largest_distance (m)", "time (s)",
        "ticks (count)",
        "position_error_x (m)", "position_error_y (m)", "position_error_z (m)",
        "position_error_along (m)", "position_error_across (m)", "position_error_length (m)",
        "velocity_error_x (m/s)", "velocity_error_y (m/s)", "velocity_error_z (m/s)",
        "velocity_error_along (m/s)", "velocity_error_across (m/s)", "velocity_error_length (m/s)",
        "start_velocity_rounding (m/s)", "world_errors (count)"
    ];

    /// <summary>The errors of one run at one sample tick.</summary>
    private readonly record struct Sample(
        int Ticks,
        double Time,
        Double3 PositionError,
        Double3 VelocityError,
        int WorldErrors);

    /// <summary>The samples of one run and the largest distance from the origin that any of its bodies reached.</summary>
    private sealed record RunResult(List<Sample> Samples, double LargestDistance);

    /// <summary>
    /// A world with one gameplay thread and no numeric frames, on a new real backend with one Jolt worker thread and the
    /// raised speed limit. It checks all of its invariants at the end of every tick, which changes no physics.
    /// </summary>
    public static World NewWorld() => new(
        new()
        {
            MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true, NumericFrames = WithoutNumericFrames
        },
        new JoltPhysicsBackend(new() { WorkerThreads = 1, MaxLinearVelocity = SpeedLimit }));

    /// <summary>Runs every speed and direction of <paramref name="scene"/> and writes its CSV. Returns the path of the file.</summary>
    public static string Record(DriftScene scene)
    {
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', Header));
        foreach ((string name, Double3 direction) in new[] { ("axis", scene.AxisDirection), ("diagonal", Diagonal) })
        foreach (double speed in Speeds)
        {
            Double3 speedStart = direction * (-speed * RunSeconds / 2);
            RunResult moving = Run(scene, speedStart, direction * speed);
            Append(csv, scene, "speed", name, direction, speed, speed, -speed * RunSeconds / 2, moving);

            Double3 distanceStart = direction * moving.LargestDistance;
            RunResult still = Run(scene, distanceStart, Double3.Zero);
            Append(csv, scene, "distance", name, direction, 0, speed, moving.LargestDistance, still);
        }

        string folder = Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } named
            ? named
            : Path.Combine(Path.GetTempPath(), "tertius-precision");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, scene.Measurement + ".csv");
        File.WriteAllText(path, csv.ToString());
        return path;
    }

    private static RunResult Run(DriftScene scene, Double3 start, Double3 common)
    {
        using World world = NewWorld();
        FrameHandle space = world.CreateRootFrame(scene.Space);
        Entity[] bodies = scene.Spawn(world, space, start, common);
        double dt = world.DeltaTime;
        double largest = LargestDistance(world, space, bodies);
        var samples = new List<Sample>();
        for (int tick = 1; tick <= RunTicks; tick++)
        {
            world.Tick();
            largest = Math.Max(largest, LargestDistance(world, space, bodies));
            if (Array.IndexOf(SampleTicks, tick) < 0)
                continue;
            (Double3 position, Double3 velocity) = scene.Measure(world, space, bodies);
            (Double3 expectedPosition, Double3 expectedVelocity) = scene.Expected(tick, dt, start, common);
            samples.Add(new(tick, tick * dt, position - expectedPosition, velocity - expectedVelocity,
                world.Errors.Count));
        }

        return new(samples, largest);
    }

    private static double LargestDistance(World world, FrameHandle space, Entity[] bodies) =>
        bodies.Max(body => world.PoseIn(body, space).Position.Length);

    private static void Append(StringBuilder csv, DriftScene scene, string run, string direction, Double3 d,
        double speed,
        double matchingSpeed, double startAlong, RunResult result)
    {
        double rounding = scene.StartVelocityRounding(d * speed);
        foreach (Sample sample in result.Samples)
        {
            var cells = new List<string>
            {
                run, direction, Number(d.X), Number(d.Y), Number(d.Z), Number(speed), Number(matchingSpeed),
                Number(startAlong), Number(result.LargestDistance), Number(sample.Time),
                sample.Ticks.ToString(CultureInfo.InvariantCulture)
            };
            AddSplit(cells, sample.PositionError, scene.Motion);
            AddSplit(cells, sample.VelocityError, scene.Motion);
            cells.Add(Number(rounding));
            cells.Add(sample.WorldErrors.ToString(CultureInfo.InvariantCulture));
            csv.AppendLine(string.Join(',', cells));
        }
    }

    /// <summary>
    /// Adds the components of <paramref name="error"/>, its signed part along <paramref name="motion"/>, the length of
    /// its part across it, and its length.
    /// </summary>
    private static void AddSplit(List<string> cells, Double3 error, Double3 motion)
    {
        double along = Double3.Dot(error, motion);
        double across = (error - motion * along).Length;
        cells.AddRange([
            Number(error.X), Number(error.Y), Number(error.Z), Number(along), Number(across), Number(error.Length)
        ]);
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
