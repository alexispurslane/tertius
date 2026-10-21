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
/// <param name="Felt">The length of the felt gravity at the person's place (see <see cref="FrameTree.FeltGravity"/>), in m/s².</param>
public readonly record struct ReadoutRow(string Frame, long Tick, Double3 Position, double Speed, double ToStation, double Felt);

/// <summary>
/// The readout log of a script: one <see cref="ReadoutRow"/> for each capture, in the order of the captures, so row k
/// describes frame k of the script's video. It is a playback that draws nothing. A component changes only its own entity,
/// and the log watches the whole world from outside, at the moments that the runner captures.
/// </summary>
public sealed class ReadoutLog : IPlayback
{
    /// <summary>The header line of the log's CSV.</summary>
    public const string Header = "frame,tick,x,y,z,speed,station,felt";

    private readonly Entity person, station;
    private readonly FrameHandle earth;
    private readonly List<ReadoutRow> rows = new();

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
        rows.Add(new ReadoutRow(name, world.TickNumber, position, world.VelocityIn(person, earth).Length,
            Double3.Distance(position, world.PoseIn(station, earth).Position), felt.Length));
        return true;
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
        Scene scene = scenes.SingleOrDefault(s => s.Name == script.Scene) ?? throw new ScriptException($"No scene is named '{script.Scene}'.");
        using var world = new World(new WorldOptions { DisposePhysics = true }, new JoltPhysicsBackend());
        SceneInstance instance = scene.Build(world, RenderResources.None);
        var log = new ReadoutLog(world, instance);
        ScriptRunner.Run(script, world, instance, outputFolder: null, log);
        return log.Rows;
    }

    /// <summary>The CSV of <paramref name="rows"/>: <see cref="Header"/>, then one line for each row, each number in the round-trip format.</summary>
    public static string ToCsv(IReadOnlyList<ReadoutRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var text = new StringBuilder(Header).Append('\n');
        foreach (ReadoutRow row in rows)
            text.Append(CultureInfo.InvariantCulture, $"{row.Frame},{row.Tick},{row.Position.X:R},{row.Position.Y:R},{row.Position.Z:R},{row.Speed:R},{row.ToStation:R},{row.Felt:R}\n");
        return text.ToString();
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
            if (cells.Length != 8)
                throw new FormatException($"Line {i + 1} of the readout log has {cells.Length} cells, not 8.");
            double Number(int cell) => double.Parse(cells[cell], NumberStyles.Float, CultureInfo.InvariantCulture);
            rows.Add(new ReadoutRow(cells[0], long.Parse(cells[1], CultureInfo.InvariantCulture), new Double3(Number(2), Number(3), Number(4)), Number(5), Number(6), Number(7)));
        }
        return rows;
    }
}
