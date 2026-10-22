using System.Text.Json;

namespace Tertius.Host.Scripting;

/// <summary>
/// Builds the script that replays a session of free play: the input that the game got and the views that were switched,
/// each at the tick it happened, with the ticks between them as run steps. The script starts at the camera of the world,
/// and ends with a dump named <see cref="EndDump"/>. See docs/plans/host-and-scripted-runs.md, "Recording free play".
/// Not thread-safe. The simulation thread that ticks the world calls it.
/// </summary>
/// <remarks>
/// A tick is the number of ticks that the world has completed, <c>World.TickNumber</c>. A step that the recorder takes at
/// tick <c>n</c> happens after exactly <c>n</c> ticks, before the tick that starts from <c>n</c> completed ones. So a
/// replay that runs the steps in file order, from a new world, does each step at the same tick as the session did.
/// </remarks>
public sealed class ScriptRecorder
{
    /// <summary>The name of the dump that ends every recorded script.</summary>
    public const string EndDump = "end";

    private readonly string scene;
    private readonly ScriptWindow window;
    private readonly List<ScriptStep> steps = [];

    // The ticks that the steps so far run, which is the tick of the latest step.
    private long ticksRun;

    /// <param name="scene">The name of the scene that the session ran.</param>
    /// <param name="window">The size of the session's window.</param>
    /// <param name="startCamera">The scene id of the world's camera at the start, or null if it has none.</param>
    public ScriptRecorder(string scene, ScriptWindow window, string? startCamera)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(window);
        this.scene = scene;
        this.window = window;
        if (startCamera != null)
            steps.Add(new CameraStep(startCamera));
    }

    /// <summary>
    /// Records an input event that the game got after <paramref name="tick"/> ticks, as a post to the <c>input</c> topic.
    /// Throws <see cref="ArgumentOutOfRangeException"/> for a tick before the tick of the latest step.
    /// </summary>
    public void Post(long tick, InputEvent e)
    {
        RunUntil(tick);
        steps.Add(new PostStep(new ToTopic(Topics.NameOf(Topics.Input)), nameof(InputEvent),
            JsonSerializer.SerializeToElement(e, ScriptJson.Recordings)));
    }

    /// <summary>
    /// Records that the world's camera became the entity <paramref name="entity"/> after <paramref name="tick"/> ticks.
    /// Throws <see cref="ArgumentOutOfRangeException"/> for a tick before the tick of the latest step.
    /// </summary>
    public void SwitchCamera(long tick, string entity)
    {
        RunUntil(tick);
        steps.Add(new CameraStep(entity));
    }

    /// <summary>
    /// The script of the session so far, which ends after <paramref name="endTick"/> ticks with a dump. It changes
    /// nothing, so the recorder can go on. Throws <see cref="ArgumentOutOfRangeException"/> for a tick before the tick of
    /// the latest step.
    /// </summary>
    public Script ToScript(long endTick)
    {
        var all = new List<ScriptStep>(steps);
        AddRuns(all, endTick - ticksRun, endTick);
        all.Add(new DumpStep(EndDump));
        return new(scene, all, window);
    }

    private void RunUntil(long tick)
    {
        AddRuns(steps, tick - ticksRun, tick);
        ticksRun = tick;
    }

    // Adds run steps for a gap of ticks before the step at tick. A run step holds an int, so a gap of more than int.MaxValue
    // ticks (more than 414 days at 60 ticks a second) is split.
    private static void AddRuns(List<ScriptStep> to, long gap, long tick)
    {
        if (gap < 0)
            throw new ArgumentOutOfRangeException(nameof(tick), tick,
                "A step cannot come at a tick before the tick of the step before it.");
        for (; gap > 0; gap -= int.MaxValue)
            to.Add(new RunStep((int)Math.Min(gap, int.MaxValue)));
    }
}
