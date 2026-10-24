using System.Globalization;
using System.Text;
using Tertius.Host;
using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// What the engine said at one frame of a script, for the readouts of the demo edit (docs/plans/blue-danube-demo.md, "The
/// readout log"). Every quantity is about the scene's <c>person</c>, in the frame <c>earth</c>, which has its origin at
/// Earth's center and does not turn: the frame of the guidance and of the parking orbit's textbook check. A speed in the
/// turning <c>earth-surface</c> misses the ground's turn, up to 480 m/s on an orbit 200 km up.
/// </summary>
/// <param name="Frame">The name of the capture, such as <c>climb-low-000012</c>.</param>
/// <param name="Tick">The world's tick number at the capture.</param>
/// <param name="Position">The person's position in <c>earth</c>, in meters from Earth's center.</param>
/// <param name="Speed">The length of the person's velocity in <c>earth</c>, in m/s.</param>
/// <param name="ToStation">The distance from the person to the center of the scene's <c>station</c>, in meters.</param>
/// <param name="Axis">
/// The distance from the person to the station's axis, in meters: to the line through the station's center along the +Y of
/// the station's own frame, the axis of its turn. It is the radius of the spin gravity's textbook check, ω² r.
/// </param>
/// <param name="Felt">The length of the felt gravity at the person's place (see <see cref="FrameTree.FeltGravity"/>), in m/s².</param>
/// <param name="FirstThrow">The coda's first throw, from the first capture after its release on, or null.</param>
/// <param name="SecondThrow">The coda's second throw, from the first capture after its release on, or null.</param>
public readonly record struct ReadoutRow(
    string Frame,
    long Tick,
    Double3 Position,
    double Speed,
    double ToStation,
    double Axis,
    double Felt,
    ThrowReadout? FirstThrow = null,
    ThrowReadout? SecondThrow = null);

/// <summary>
/// One throw of the coda, as the <see cref="FlightRecorder"/> of its box records it, in the station's own frame: its origin
/// at the station's center, its axes those of the station, which turn with it (docs/plans/blue-danube-demo.md, "The coda").
/// </summary>
/// <param name="Release">The box's place and instant velocity in its first tick after the throw.</param>
/// <param name="Landing">Where and when the box came down to the floor, or null before the landing.</param>
public readonly record struct ThrowReadout(ThrowRelease Release, ThrowLanding? Landing);

/// <summary>
/// The readout log of a script: one <see cref="ReadoutRow"/> for each capture, in the order of the captures, so row k
/// describes frame k of the script's video. It is a playback that draws nothing. A component changes only its own entity,
/// and the log watches the whole world from outside, at the moments that the runner captures.
/// </summary>
public sealed class ReadoutLog : IPlayback
{
    /// <summary>The header line of the log's CSV.</summary>
    public const string Header = "frame,tick,x,y,z,speed,station,axis,felt," + ThrowColumns1 + "," + ThrowColumns2;

    private const string ThrowColumns1 =
        "throw1_x,throw1_y,throw1_z,throw1_vx,throw1_vy,throw1_vz,throw1_time,throw1_land_x,throw1_land_y,throw1_land_z";

    private const string ThrowColumns2 =
        "throw2_x,throw2_y,throw2_z,throw2_vx,throw2_vy,throw2_vz,throw2_time,throw2_land_x,throw2_land_y,throw2_land_z";

    // The cells of a row: nine for the person, then ten for each of the two throws.
    private const int Cells = 29;

    private readonly Entity person, station;
    private readonly FrameHandle earth;
    private readonly List<ReadoutRow> rows = [];

    /// <summary>A log of the <c>person</c>, the <c>station</c> and the frame <c>earth</c> of <paramref name="scene"/>.</summary>
    public ReadoutLog(World world, SceneInstance scene)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(scene);
        person = scene["person"];
        station = scene["station"];
        earth = world.GetOwnedFrame(scene["earth"]);
    }

    /// <summary>The rows of the captures so far, in their order.</summary>
    public IReadOnlyList<ReadoutRow> Rows => rows;

    public bool WaitForTick() => true;

    /// <summary>Adds the row of the capture <paramref name="name"/>, read from <paramref name="world"/> now.</summary>
    public bool Capture(World world, string name)
    {
        Double3 position = world.PoseIn(person, earth).Position;
        FrameHandle home = world.GetFrame(person);
        Double3 felt = world.Frames.FeltGravity(home, world.PoseIn(person, home).Position);
        // The station's axis is its +Y through its center. The part of the offset along the axis leaves the part across it.
        DPose stationPose = world.PoseIn(station, earth);
        Double3 offset = position - stationPose.Position, axis = stationPose.Rotation.Rotate(Double3.UnitY);
        rows.Add(new(Frame: name, Tick: world.TickNumber, Position: position,
            Speed: world.VelocityIn(person, earth).Length,
            ToStation: offset.Length, Axis: (offset - axis * Double3.Dot(offset, axis)).Length, Felt: felt.Length,
            FirstThrow: ThrowOf(world, 0),
            SecondThrow: ThrowOf(world, 1)));
        return true;
    }

    /// <summary>
    /// The throw <paramref name="index"/> of the person's <see cref="BoxPlay"/>, from its box's <see cref="FlightRecorder"/>,
    /// or null when the person has no play, has not thrown it, or its box did not tick since the throw.
    /// </summary>
    private ThrowReadout? ThrowOf(World world, int index)
    {
        if (world.GetComponent<BoxPlay>(person) is not { } play || index >= play.Thrown.Count)
            return null;
        var recorder = world.GetComponent<FlightRecorder>(play.Thrown[index]);
        return recorder?.Release is { } release ? new ThrowReadout(release, recorder.Landing) : null;
    }

    /// <summary>
    /// Runs <paramref name="script"/> in a new world, as the headless command builds it, with its scene from
    /// <paramref name="scenes"/> and no renderer, and returns the row of each capture. The engine is deterministic, so the
    /// rows describe the frames of the script's video. Throws <see cref="ScriptException"/> for a script that cannot run.
    /// </summary>
    public static IReadOnlyList<ReadoutRow> Record(Script script, IReadOnlyList<Scene> scenes)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(scenes);
        Scene scene = scenes.SingleOrDefault(s => s.Name == script.Scene) ??
                      throw new ScriptException($"No scene is named '{script.Scene}'.");
        using var world = new World(new() { DisposePhysics = true },
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced));
        SceneInstance instance = scene.Build(world, RenderResources.None);
        var log = new ReadoutLog(world, instance);
        ScriptRunner.Run(script: script, world: world, scene: instance, outputFolder: null, playback: log);
        return log.Rows;
    }

    /// <summary>The CSV of <paramref name="rows"/>: <see cref="Header"/>, then one line for each row, each number in the round-trip format.</summary>
    public static string ToCsv(IReadOnlyList<ReadoutRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        StringBuilder text = new StringBuilder(Header).Append('\n');
        foreach (ReadoutRow row in rows)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{row.Frame},{row.Tick},{row.Position.X:R},{row.Position.Y:R},{row.Position.Z:R},{row.Speed:R},{row.ToStation:R},{row.Axis:R},{row.Felt:R}");
            AppendThrow(text, row.FirstThrow);
            AppendThrow(text, row.SecondThrow);
            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The ten cells of a throw, each empty when it has no value yet.</summary>
    private static void AppendThrow(StringBuilder text, ThrowReadout? readout)
    {
        ThrowRelease? release = readout?.Release;
        ThrowLanding? landing = readout?.Landing;
        foreach (double? value in new[]
                 {
                     release?.Place.X, release?.Place.Y, release?.Place.Z, release?.Velocity.X, release?.Velocity.Y,
                     release?.Velocity.Z,
                     landing?.Time, landing?.Place.X, landing?.Place.Y, landing?.Place.Z
                 })
            text.Append(CultureInfo.InvariantCulture, $",{value:R}");
    }

    /// <summary>The rows of a CSV that <see cref="ToCsv"/> wrote. Throws <see cref="FormatException"/> for any other text.</summary>
    public static IReadOnlyList<ReadoutRow> FromCsv(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        string[] lines = csv.TrimEnd('\n').Split('\n');
        if (lines[0] != Header)
            throw new FormatException($"A readout log starts with the line '{Header}'.");
        var rows = new List<ReadoutRow>(lines.Length - 1);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] cells = lines[i].Split(',');
            if (cells.Length != Cells)
                throw new FormatException($"Line {i + 1} of the readout log has {cells.Length} cells, not {Cells}.");
            double Number(int cell) => double.Parse(cells[cell], NumberStyles.Float, CultureInfo.InvariantCulture);
            rows.Add(new(Frame: cells[0], Tick: long.Parse(cells[1], CultureInfo.InvariantCulture),
                Position: new(Number(2), Number(3), Number(4)), Speed: Number(5), ToStation: Number(6), Axis: Number(7),
                Felt: Number(8),
                FirstThrow: ReadThrow(cells, 9), SecondThrow: ReadThrow(cells, 19)));
        }

        return rows;
    }

    /// <summary>The throw in the ten cells from <paramref name="start"/>: null when its release is empty, and no landing when its landing is empty.</summary>
    private static ThrowReadout? ReadThrow(string[] cells, int start)
    {
        double Number(int cell) => double.Parse(cells[start + cell], NumberStyles.Float, CultureInfo.InvariantCulture);
        if (cells[start] == "")
            return null;
        var release = new ThrowRelease(new(Number(0), Number(1), Number(2)), new(Number(3), Number(4), Number(5)));
        ThrowLanding? landing = cells[start + 6] == ""
            ? null
            : new ThrowLanding(Number(6), new(Number(7), Number(8), Number(9)));
        return new ThrowReadout(release, landing);
    }
}
