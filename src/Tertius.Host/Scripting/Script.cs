using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tertius.Host.Scripting;

/// <summary>
/// A scripted run: the scene to build, then steps that run in file order. See docs/plans/host-and-scripted-runs.md,
/// "Script format".
/// </summary>
/// <param name="Scene">The name of the scene that the program builds before the first step.</param>
/// <param name="Steps">The steps, in the order that they run.</param>
public sealed record Script(string Scene, IReadOnlyList<ScriptStep> Steps)
{
    /// <summary>
    /// Reads a script from JSON text. Throws <see cref="ScriptException"/> for a script that is not valid: bad JSON, a
    /// missing, unknown or mistyped key, no steps, a run of less than one tick, or a dump name that is not a scene id or
    /// that an earlier dump has.
    /// </summary>
    public static Script Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Script? script;
        try
        {
            script = JsonSerializer.Deserialize<Script>(json, ScriptJson.Scripts);
        }
        catch (JsonException ex)
        {
            throw new ScriptException($"The script is not valid JSON of a script: {ex.Message}", ex);
        }
        // System.Text.Json cannot make an abstract step or target without its discriminator.
        catch (NotSupportedException ex)
        {
            throw new ScriptException($"Every step needs a \"do\" key, and every event target a \"kind\" key: {ex.Message}", ex);
        }
        if (script is null)
            throw new ScriptException("The script is null.");
        script.CheckSteps();
        return script;
    }

    /// <summary>
    /// Reads a script file. Throws <see cref="ScriptException"/> for a script that is not valid, and the exceptions of
    /// <see cref="File.ReadAllText(string)"/> for a file that cannot be read.
    /// </summary>
    public static Script Load(string path) => Parse(File.ReadAllText(path));

    private void CheckSteps()
    {
        if (Steps.Count == 0)
            throw new ScriptException("The script has no steps.");
        var dumpSteps = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Steps.Count; i++)
        {
            int number = i + 1;
            switch (Steps[i])
            {
                case RunStep { Ticks: < 1 } run:
                    throw new ScriptException($"step {number} (run): it runs {run.Ticks} ticks, and a run step runs at least 1.");
                case DumpStep dump when !SceneInstance.IsSceneId(dump.Name):
                    throw new ScriptException($"step {number} (dump): '{dump.Name}' is not a dump name. A dump name follows the grammar of a scene id: lowercase letters, digits, '_', '.' and '-', starting with a letter or a digit.");
                case DumpStep dump when dumpSteps.TryGetValue(dump.Name, out int earlier):
                    throw new ScriptException($"step {number} (dump): step {earlier} has the dump name '{dump.Name}' already.");
                case DumpStep dump:
                    dumpSteps.Add(dump.Name, number);
                    break;
            }
        }
    }
}

/// <summary>One step of a script. Its <c>do</c> key names the kind.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "do")]
[JsonDerivedType(typeof(RunStep), "run")]
[JsonDerivedType(typeof(PostStep), "post")]
[JsonDerivedType(typeof(DumpStep), "dump")]
public abstract record ScriptStep;

/// <summary>Runs <paramref name="Ticks"/> ticks, at least 1.</summary>
public sealed record RunStep(int Ticks) : ScriptStep;

/// <summary>
/// Posts an event with <c>World.Post</c>. It joins the routing at the end of the next tick, and its recipients handle it
/// during the tick after that.
/// </summary>
/// <param name="To">Who receives the event.</param>
/// <param name="Event">The C# name of the payload type, such as <c>InputEvent</c>.</param>
/// <param name="Payload">The payload's fields by their camelCase names. Null is the same as an empty object.</param>
public sealed record PostStep(MessageTarget To, string Event, JsonElement? Payload = null) : ScriptStep;

/// <summary>Takes a state dump named <paramref name="Name"/>, written to <c>&lt;out&gt;/&lt;name&gt;.json</c>.</summary>
public sealed record DumpStep(string Name) : ScriptStep;

/// <summary>The recipient of a posted or recorded event, in the form that scripts and dumps write. Its <c>kind</c> key names the case.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ToEntity), "entity")]
[JsonDerivedType(typeof(ToTopic), "topic")]
[JsonDerivedType(typeof(ToFrame), "frame")]
[JsonDerivedType(typeof(ToEveryone), "broadcast")]
public abstract record MessageTarget;

/// <summary>The entity with the scene id <paramref name="Id"/>.</summary>
public sealed record ToEntity(string Id) : MessageTarget;

/// <summary>Every subscriber of the named topic, such as <c>input</c>.</summary>
public sealed record ToTopic(string Name) : MessageTarget;

/// <summary>Every entity in the frame that the entity with the scene id <paramref name="Id"/> owns.</summary>
public sealed record ToFrame(string Id) : MessageTarget;

/// <summary>Every live entity.</summary>
public sealed record ToEveryone : MessageTarget;

/// <summary>A script that cannot run: bad JSON, a bad step, or a name that the scene or the loaded code does not know.</summary>
public sealed class ScriptException(string message, Exception? inner = null) : Exception(message, inner);
