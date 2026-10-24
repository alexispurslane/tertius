using System.Globalization;
using System.Numerics;
using System.Text;
using Tertius.Simulation;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// What the four contact measurements of ticket ter-kugq share. Each measurement runs one scene at a set of common speeds
/// V: every body gets the same extra velocity V along a direction d. A Galilean shift must not change physics, so each
/// error is measured against the same scene at V = 0, shifted by V t, or against an analytic answer. Numeric frames are
/// off, so Jolt holds every body in the system of one root semantic frame, and every float
/// velocity in Jolt holds the whole common velocity. A distance control runs the scene at V = 0, placed at the largest
/// distance from the origin that the speed run reached, to separate the part of an error that comes from distance alone.
/// </summary>
internal static class ContactRuns
{
    /// <summary>
    /// The speed limit of a Jolt body in these runs, in m/s. The engine's default of 30 km/s would clamp the bodies of the
    /// faster runs, so the runs raise it above the fastest common speed.
    /// </summary>
    public const float SpeedLimit = 1e6f;

    /// <summary>The radius of the space's bubble, in meters, far beyond the largest distance of any run (9,300 km).</summary>
    private const float SpaceRadius = 1e9f;

    /// <summary>The common speeds V, in m/s. 7,672 m/s is the speed of a low Earth orbit.</summary>
    public static readonly double[] Speeds =
    [
        0,
        1,
        10,
        50,
        100,
        200,
        500,
        1000,
        2000,
        5000,
        7672,
        10000,
        20000,
        50000,
        100000,
        300000,
    ];

    public static readonly Direction AlongX = new("x", Double3.UnitX);

    public static readonly Direction AlongZ = new("z", Double3.UnitZ);

    /// <summary>d = (1, 1, 1) / sqrt(3), where every component of every velocity holds the common speed.</summary>
    public static readonly Direction Diagonal = new("diagonal", new Double3(1, 1, 1) / Math.Sqrt(3));

    /// <summary>
    /// The folder that the CSV files go to: the environment variable TERTIUS_PRECISION_DIR, or the folder
    /// tertius-precision in the temporary folder.
    /// </summary>
    public static string OutputFolder =>
        Environment.GetEnvironmentVariable("TERTIUS_PRECISION_DIR") is { Length: > 0 } folder
            ? folder
            : Path.Combine(Path.GetTempPath(), "tertius-precision");

    /// <summary>A direction of the common velocity, with the name that the CSV files give it.</summary>
    /// <param name="Name">"x", "z" or "diagonal".</param>
    /// <param name="Unit">The unit vector d in the axes of the space.</param>
    public readonly record struct Direction(string Name, Double3 Unit);

    /// <summary>
    /// A world without numeric frames, on a new real backend with one Jolt worker thread and the speed limit
    /// <see cref="SpeedLimit"/>, with one gameplay thread and no debug checks. The backend's warnings go to
    /// <paramref name="warnings"/>.
    /// </summary>
    public static World NewWorld(out JoltPhysicsBackend backend, List<string> warnings)
    {
        backend = new(
            new()
            {
                WorkerThreads = 1,
                MaxLinearVelocity = SpeedLimit,
                Log = message =>
                {
                    lock (warnings)
                    {
                        warnings.Add(message);
                    }
                },
            }
        );
        var options = new WorldOptions
        {
            MaxDegreeOfParallelism = 1,
            DisposePhysics = true,
            NumericFrames = WithoutNumericFrames,
        };
        return new(options, backend);
    }

    /// <summary>The root frame that every scene runs in: a uniform field of <paramref name="gravity"/> m/s², or none when it is zero.</summary>
    public static FrameHandle Space(World world, Vector3 gravity) =>
        world.CreateRootFrame(
            new(FrameBounds.Sphere(SpaceRadius), gravity == Vector3.Zero ? default : GravityField.Uniform(gravity))
        );

    public static DPose At(Double3 position) => new(position, DQuaternion.Identity);

    /// <summary>The rotation that turns +X onto the unit vector <paramref name="d"/>, the shortest one.</summary>
    public static DQuaternion TurnXOnto(Double3 d)
    {
        var axis = Double3.Cross(Double3.UnitX, d);
        double sine = axis.Length;
        return sine < 1e-12
            ? DQuaternion.Identity
            : DQuaternion.FromAxisAngle(axis / sine, Math.Atan2(sine, Double3.Dot(Double3.UnitX, d)));
    }

    /// <summary>The angle of <paramref name="rotation"/> from no rotation, in radians, from 0 to pi.</summary>
    public static double AngleOf(DQuaternion rotation)
    {
        DQuaternion unit = rotation.Normalized();
        return 2 * Math.Acos(Math.Min(1, Math.Abs(unit.W)));
    }

    /// <summary>The signed part of <paramref name="v"/> along the unit vector <paramref name="axis"/>, and the length of the rest.</summary>
    public static (double Along, double Across) Split(Double3 v, Double3 axis)
    {
        double along = Double3.Dot(v, axis);
        return (along, (v - axis * along).Length);
    }

    /// <summary>
    /// A table that becomes one CSV file: a header row that names each column and its unit, then one row per record.
    /// Doubles keep every digit that tells them apart (the shortest round-trip form), and booleans are true or false.
    /// </summary>
    public sealed class Csv
    {
        private readonly StringBuilder text = new();
        private readonly int columns;

        public Csv(params string[] header)
        {
            columns = header.Length;
            text.AppendLine(string.Join(',', header));
        }

        /// <summary>The number of rows after the header.</summary>
        public int Rows { get; private set; }

        public void Add(params object[] values)
        {
            if (values.Length != columns)
                throw new ArgumentException(
                    $"A row has {values.Length} values, and the header {columns} columns.",
                    nameof(values)
                );
            text.AppendLine(string.Join(',', values.Select(Format)));
            Rows++;
        }

        /// <summary>Writes the table to <c>name.csv</c> in <see cref="OutputFolder"/> and returns the file's path.</summary>
        public string Save(string name)
        {
            Directory.CreateDirectory(OutputFolder);
            string path = Path.Combine(OutputFolder, name + ".csv");
            File.WriteAllText(path, text.ToString());
            return path;
        }

        private static string Format(object value) =>
            value switch
            {
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                float f => f.ToString("R", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                string s when s.Contains(',') || s.Contains('"') => '"' + s.Replace("\"", "\"\"") + '"',
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "",
            };
    }
}
