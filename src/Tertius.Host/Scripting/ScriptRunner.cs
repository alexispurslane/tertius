using System.Text.Json;
using Tertius.Simulation;

namespace Tertius.Host.Scripting;

/// <summary>Runs a script on a world in lock-step: ticks run only when a step asks for them.</summary>
public static class ScriptRunner
{
    /// <summary>
    /// Runs every step of <paramref name="script"/> on <paramref name="world"/>, whose scene ids are
    /// <paramref name="scene"/>. Checks every step before the first tick, and throws <see cref="ScriptException"/>
    /// without changing the world for a step that cannot run. With <paramref name="outputFolder"/>, writes each dump
    /// there as <c>&lt;name&gt;.json</c> when it is taken. <paramref name="playback"/> paces the ticks and draws each capture
    /// into <c>&lt;name&gt;.png</c> in the output folder, so a script with captures needs both. With no playback, the ticks
    /// run back to back. If the playback stops the run, the steps after that point do not run, and the result says so.
    /// </summary>
    public static RunResult Run(Script script, World world, SceneInstance scene, string? outputFolder = null, IPlayback? playback = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(scene);
        EventPayloads payloads = EventPayloads.FromLoadedAssemblies();
        ReadyStep[] steps = MakeReady(script, world, scene, outputFolder, playback, payloads);
        JsonSerializerOptions dumpJson = ScriptJson.Dumps(scene);

        if (outputFolder != null)
            Directory.CreateDirectory(outputFolder);
        var recorder = new DumpRecorder(world, scene, payloads);
        var dumps = new List<StateDump>();
        var captures = new List<string>();
        foreach (ReadyStep step in steps)
        {
            switch (step)
            {
                case ReadyRun run:
                    for (int tick = 0; tick < run.Ticks; tick++)
                    {
                        if (playback?.WaitForTick() == false)
                            return new RunResult(dumps, captures, stopped: true);
                        world.Tick();
                        recorder.RecordRoutedEvents();
                    }
                    break;
                case ReadyPost post:
                    world.Post(post.Event);
                    break;
                case ReadyCamera(Entity entity, string where):
                    world.Camera = world.IsAlive(entity) ? entity : throw new ScriptException($"{where}: at tick {world.TickNumber}, the entity '{scene.IdOf(entity)}' is not alive.");
                    break;
                case ReadyDump(string name):
                    StateDump dump = recorder.Take(name);
                    dumps.Add(dump);
                    if (outputFolder != null)
                        File.WriteAllText(Path.Combine(outputFolder, name + ".json"), JsonSerializer.Serialize(dump, dumpJson) + "\n");
                    break;
                case ReadyCapture(string path, string where):
                    // The check before the first tick saw a live camera, but a component may have destroyed it since.
                    if (!world.IsAlive(world.Camera))
                        throw new ScriptException($"{where}: at tick {world.TickNumber}, the camera '{scene.IdOf(world.Camera)}' is not alive.");
                    playback!.Capture(world, path);
                    captures.Add(path);
                    break;
                default:
                    throw new InvalidOperationException($"The runner has no arm for the step {step}.");
            }
        }
        return new RunResult(dumps, captures);
    }

    /// <summary>A step after its checks, with every name in it resolved. Running it cannot fail for a reason of the script.</summary>
    private abstract record ReadyStep;

    private sealed record ReadyRun(int Ticks) : ReadyStep;

    private sealed record ReadyPost(Event Event) : ReadyStep;

    private sealed record ReadyCamera(Entity Entity, string Where) : ReadyStep;

    private sealed record ReadyDump(string Name) : ReadyStep;

    private sealed record ReadyCapture(string Path, string Where) : ReadyStep;

    /// <summary>
    /// Checks every step and resolves its names before the first tick, so that a bad step changes nothing. Throws
    /// <see cref="ScriptException"/> for the first step that cannot run.
    /// </summary>
    private static ReadyStep[] MakeReady(Script script, World world, SceneInstance scene, string? outputFolder, IPlayback? playback, EventPayloads payloads)
    {
        script.CheckSteps();
        JsonSerializerOptions json = ScriptJson.Payloads(world, scene);
        var ready = new ReadyStep[script.Steps.Count];
        // The camera that each capture will use, as the camera steps before it set it.
        Entity camera = world.Camera;
        for (int i = 0; i < script.Steps.Count; i++)
        {
            ScriptStep step = script.Steps[i];
            string where = $"step {i + 1}";
            switch (step)
            {
                case RunStep run:
                    ready[i] = new ReadyRun(run.Ticks);
                    break;
                case PostStep post:
                    ready[i] = new ReadyPost(MakeEvent(post, world, scene, payloads, json, $"{where} (post)"));
                    break;
                case CameraStep cameraStep:
                    camera = LiveEntity(cameraStep.Entity, world, scene, $"{where} (camera)");
                    ready[i] = new ReadyCamera(camera, $"{where} (camera)");
                    break;
                case DumpStep dump:
                    ready[i] = new ReadyDump(dump.Name);
                    break;
                case CaptureStep { Name: var name } when !world.IsAlive(camera):
                    throw new ScriptException($"{where} (capture): a capture of '{name}' needs a live camera. Let the scene set one, or put a camera step before the capture.");
                case CaptureStep { Name: var name } when playback is null:
                    throw new ScriptException($"{where} (capture): a capture of '{name}' needs a renderer, and the run has none.");
                case CaptureStep { Name: var name } when outputFolder is null:
                    throw new ScriptException($"{where} (capture): a capture of '{name}' needs an output folder, and the run has none.");
                case CaptureStep capture:
                    ready[i] = new ReadyCapture(Path.Combine(outputFolder, capture.Name + ".png"), $"{where} (capture)");
                    break;
                default:
                    throw new InvalidOperationException($"The runner has no arm for the step {step}.");
            }
        }
        return ready;
    }

    /// <summary>The event of a post step.</summary>
    private static Event MakeEvent(PostStep post, World world, SceneInstance scene, EventPayloads payloads, JsonSerializerOptions json, string where)
    {
        EventPayloads.PayloadType type = payloads.Find(post.Event, out string? problem) ?? throw new ScriptException($"{where}: {problem}");
        Target target = TargetOf(post.To, world, scene, where);
        object? payload;
        try
        {
            payload = post.Payload is { } given ? given.Deserialize(type.Type, json) : JsonSerializer.Deserialize("{}", type.Type, json);
        }
        catch (JsonException ex)
        {
            throw new ScriptException($"{where}: the payload is not a {post.Event}, at {ex.Path}: {ex.Message}", ex);
        }
        // System.Text.Json reads no field of a type such as nint.
        catch (NotSupportedException ex)
        {
            throw new ScriptException($"{where}: a script cannot write a {post.Event}: {ex.Message}", ex);
        }
        return type.Create(target, payload ?? throw new ScriptException($"{where}: the payload is null."));
    }

    private static Target TargetOf(MessageTarget to, World world, SceneInstance scene, string where) => to switch
    {
        ToEntity(string id) => LiveEntity(id, world, scene, where),
        ToTopic(string name) => Topics.Get(name),
        ToFrame(string id) => world.GetOwnedFrame(LiveEntity(id, world, scene, where)) is { IsNull: false } frame
            ? frame.Topic
            : throw new ScriptException($"{where}: the entity '{id}' owns no frame."),
        ToEveryone => new Broadcast(),
        _ => throw new InvalidOperationException($"The runner has no arm for the target {to}."),
    };

    private static Entity LiveEntity(string id, World world, SceneInstance scene, string where)
    {
        if (!scene.TryGetEntity(id, out Entity entity))
            throw new ScriptException($"{where}: the scene has no entity with the id '{id}'.");
        return world.IsAlive(entity) ? entity : throw new ScriptException($"{where}: the entity '{id}' is not alive.");
    }
}

/// <summary>What a scripted run made: its state dumps and the paths of its captured images, each in the order taken.</summary>
public sealed class RunResult(IReadOnlyList<StateDump> dumps, IReadOnlyList<string> captures, bool stopped = false)
{
    /// <summary>True if the playback stopped the run before its last step, for example because the user closed the window.</summary>
    public bool Stopped => stopped;

    public IReadOnlyList<StateDump> Dumps => dumps;

    /// <summary>The path of each captured image, in the order taken.</summary>
    public IReadOnlyList<string> Captures => captures;

    /// <summary>The dump with the name. Throws <see cref="KeyNotFoundException"/> if the run took none.</summary>
    public StateDump Dump(string name) =>
        dumps.FirstOrDefault(dump => dump.Name == name) ?? throw new KeyNotFoundException($"The run took no dump named '{name}'.");
}
