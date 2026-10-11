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
    /// there as <c>&lt;name&gt;.json</c> when it is taken.
    /// </summary>
    public static RunResult Run(Script script, World world, SceneInstance scene, string? outputFolder = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(scene);
        JsonSerializerOptions json = ScriptJson.ForRun(world, scene);
        EventPayloads payloads = EventPayloads.FromLoadedAssemblies();
        Event[] posts = MakePostedEvents(script, world, scene, payloads, json);

        if (outputFolder != null)
            Directory.CreateDirectory(outputFolder);
        var recorder = new DumpRecorder(world, scene, payloads);
        var dumps = new List<StateDump>();
        for (int i = 0; i < script.Steps.Count; i++)
        {
            switch (script.Steps[i])
            {
                case RunStep run:
                    for (int tick = 0; tick < run.Ticks; tick++)
                    {
                        world.Tick();
                        recorder.RecordRoutedEvents();
                    }
                    break;
                case PostStep:
                    world.Post(posts[i]);
                    break;
                case DumpStep step:
                    StateDump dump = recorder.Take(step.Name);
                    dumps.Add(dump);
                    if (outputFolder != null)
                        File.WriteAllText(Path.Combine(outputFolder, step.Name + ".json"), JsonSerializer.Serialize(dump, json) + "\n");
                    break;
            }
        }
        return new RunResult(dumps);
    }

    /// <summary>The event of each post step, by step index, made before the first tick so that a bad step changes nothing.</summary>
    private static Event[] MakePostedEvents(Script script, World world, SceneInstance scene, EventPayloads payloads, JsonSerializerOptions json)
    {
        var posts = new Event[script.Steps.Count];
        for (int i = 0; i < script.Steps.Count; i++)
        {
            if (script.Steps[i] is not PostStep post)
                continue;
            string where = $"step {i + 1} (post)";
            EventPayloads.PayloadType type = payloads.Find(post.Event, out string? problem) ?? throw new ScriptException($"{where}: {problem}");
            Target target = TargetOf(post.To, world, scene, where);
            object? payload;
            try
            {
                payload = post.Payload is { } given ? given.Deserialize(type.Type, json) : JsonSerializer.Deserialize("{}", type.Type, json);
            }
            catch (JsonException ex)
            {
                throw new ScriptException($"{where}: the payload is not a {post.Event}: {ex.Message}", ex);
            }
            posts[i] = type.Create(target, payload ?? throw new ScriptException($"{where}: the payload is null."));
        }
        return posts;
    }

    private static Target TargetOf(MessageTarget to, World world, SceneInstance scene, string where) => to switch
    {
        ToEntity(string id) => EntityWithId(id, scene, where),
        ToTopic(string name) => Topics.Get(name),
        ToFrame(string id) => world.GetOwnedFrame(EntityWithId(id, scene, where)) is { IsNull: false } frame
            ? frame.Topic
            : throw new ScriptException($"{where}: the entity '{id}' owns no frame."),
        ToEveryone => new Broadcast(),
        _ => throw new ScriptException($"{where}: the target has no kind."),
    };

    private static Entity EntityWithId(string id, SceneInstance scene, string where) =>
        scene.TryGetEntity(id, out Entity entity) ? entity : throw new ScriptException($"{where}: the scene has no entity with the id '{id}'.");
}

/// <summary>What a scripted run made: its state dumps, in the order taken.</summary>
public sealed class RunResult(IReadOnlyList<StateDump> dumps)
{
    public IReadOnlyList<StateDump> Dumps => dumps;

    /// <summary>The dump with the name. Throws <see cref="KeyNotFoundException"/> if the run took none.</summary>
    public StateDump Dump(string name) =>
        dumps.FirstOrDefault(dump => dump.Name == name) ?? throw new KeyNotFoundException($"The run took no dump named '{name}'.");
}
