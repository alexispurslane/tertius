using System.Collections.Concurrent;
using Tertius.Host.Scripting;
using Tertius.Simulation;

namespace Tertius.Host.Windowing;

/// <summary>Where free play writes its record, and what the record says about the session.</summary>
/// <param name="Path">The script file to write.</param>
/// <param name="Scene">The name of the scene that the session runs, which the script names.</param>
/// <param name="Window">The size of the session's window, which the script carries as its <c>window</c> key.</param>
public sealed record RecordTo(string Path, string Scene, ScriptWindow Window);

/// <summary>
/// Free play on the simulation thread: ticks the world until the playback stops, and hands the world what the window's
/// input asked for. See docs/plans/host-and-scripted-runs.md, "The window" and "Recording free play".
/// </summary>
/// <remarks>
/// The main thread must not post an input event to the world, because the tick that the post joins would then depend on
/// the timing of two threads. It queues the event here instead. The simulation thread posts the queued events and applies
/// the view switches between two ticks, before the tick that starts from <c>World.TickNumber</c> completed ones. So each
/// event joins the world at a tick that is known, and a replay that posts it before that same tick gets the same run. With
/// a record, the simulation thread notes each event and each view switch at that tick.
/// </remarks>
public sealed class FreePlay
{
    private readonly World world;
    private readonly SceneInstance scene;
    private readonly ViewSwitch views;
    private readonly ConcurrentQueue<InputEvent> queued = new();
    private readonly RecordTo? record;
    private readonly ScriptRecorder? recorder;
    private readonly Action<Entity>? noteSwitch;

    /// <summary>Starts a session. With <paramref name="record"/>, the camera of <paramref name="world"/> now is the camera at the start.</summary>
    public FreePlay(World world, SceneInstance scene, RecordTo? record = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(scene);
        this.world = world;
        this.scene = scene;
        views = new(scene);
        if (record is null)
            return;
        this.record = record;
        // A camera that the scene did not name, or no camera, cannot be a camera step.
        var started = new ScriptRecorder(record.Scene, record.Window, SceneIdOf(world.Camera));
        recorder = started;
        noteSwitch = camera =>
        {
            if (SceneIdOf(camera) is { } id)
                started.SwitchCamera(world.TickNumber, id);
        };
    }

    /// <summary>
    /// Why the record could not be written, or null if it was or none was asked for. Set when <see cref="Run"/> ends. The
    /// run itself does not fail for it.
    /// </summary>
    public Exception? RecordFailure { get; private set; }

    /// <summary>True once <see cref="Run"/> has written the record.</summary>
    public bool RecordWritten { get; private set; }

    /// <summary>Queues an input event for the game's <c>input</c> topic. The simulation thread posts it before its next tick. Safe on any thread.</summary>
    public void Post(InputEvent e) => queued.Enqueue(e);

    /// <summary>Counts a press of the view key. The simulation thread makes the scene's next view the camera before its next tick. Safe on any thread.</summary>
    public void NextView() => views.Press();

    /// <summary>
    /// Ticks the world until <paramref name="playback"/> says to stop, which it does when the window closes. Before each
    /// tick it applies the view switches and posts the input events that came since the last tick. If a record was asked
    /// for, it writes the record when the loop ends, whether the playback stopped it or an exception escaped. A problem with
    /// the file is in <see cref="RecordFailure"/>. Call it on the simulation thread.
    /// </summary>
    public void Run(IPlayback playback)
    {
        ArgumentNullException.ThrowIfNull(playback);
        try
        {
            while (playback.WaitForTick())
            {
                Deliver();
                world.Tick();
            }
        }
        finally
        {
            SaveRecord();
        }
    }

    private void Deliver()
    {
        views.Apply(world, noteSwitch);
        while (queued.TryDequeue(out InputEvent e))
        {
            recorder?.Post(world.TickNumber, e);
            world.Post(Event.Create(target: Topics.Input, sender: Entity.Null, payload: e));
        }
    }

    // The record covers the ticks that completed. A tick that threw is not in it.
    private void SaveRecord()
    {
        if (record is null || recorder is null)
            return;
        try
        {
            // A record in the root folder has no folder to make.
            if (Path.GetDirectoryName(Path.GetFullPath(record.Path)) is { } folder)
                Directory.CreateDirectory(folder);
            File.WriteAllText(record.Path, recorder.ToScript(world.TickNumber).ToJson());
            RecordWritten = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RecordFailure = ex;
        }
    }

    /// <summary>The scene id of the entity, or null if the scene has none for it, as for a camera that the scene did not name.</summary>
    private string? SceneIdOf(Entity entity)
    {
        string id = scene.IdOf(entity);
        return SceneInstance.IsSceneId(id) ? id : null;
    }
}
