using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tertius.Host.Scripting;

/// <summary>
/// A scripted run: the scene to build, then steps that run in file order. See docs/plans/host-and-scripted-runs.md,
/// "Script format".
/// </summary>
/// <param name="Scene">The name of the scene that the program builds before the first step.</param>
/// <param name="Steps">The steps, in the order that they run. A written script puts them after the window.</param>
/// <param name="Window">The size of the captured images. A script with no <c>window</c> key has <see cref="ScriptWindow.Default"/>.</param>
public sealed record Script(string Scene, [property: JsonPropertyOrder(1)] IReadOnlyList<ScriptStep> Steps, ScriptWindow? Window = null)
{
    /// <summary>The size of the captured images, in pixels.</summary>
    public ScriptWindow Window { get; init; } = Window ?? ScriptWindow.Default;

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

    /// <summary>
    /// The script as JSON text with one step on each line, ending in a new line. <see cref="Parse"/> reads it back. A
    /// script that was built in code and cannot run, such as one with a run of no ticks, writes all the same.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, ScriptJson.Recordings) + "\n";

    /// <summary>Throws <see cref="ScriptException"/> for a step that cannot run, whether the script was parsed or built in code.</summary>
    internal void CheckSteps()
    {
        if (Window.Width is < 1 or > ScriptWindow.MaxSide || Window.Height is < 1 or > ScriptWindow.MaxSide)
            throw new ScriptException($"The window is {Window.Width} by {Window.Height} pixels. Each side must be 1 to {ScriptWindow.MaxSide}.");
        if (Steps.Count == 0)
            throw new ScriptException("The script has no steps.");
        var dumpSteps = new Dictionary<string, int>(StringComparer.Ordinal);
        var captureSteps = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Steps.Count; i++)
        {
            int number = i + 1;
            switch (Steps[i])
            {
                case null:
                    throw new ScriptException($"step {number}: a step cannot be null.");
                case RunStep { Ticks: < 1 } run:
                    throw new ScriptException($"step {number} (run): it runs {run.Ticks} ticks, and a run step runs at least 1.");
                case DumpStep dump when !SceneInstance.IsSceneId(dump.Name):
                    throw new ScriptException($"step {number} (dump): '{dump.Name}' is not a dump name. A dump name follows the grammar of a scene id: lowercase letters, digits, '_', '.' and '-', starting with a letter or a digit.");
                case DumpStep dump when dumpSteps.TryGetValue(dump.Name, out int earlier):
                    throw new ScriptException($"step {number} (dump): step {earlier} has the dump name '{dump.Name}' already.");
                case DumpStep dump:
                    dumpSteps.Add(dump.Name, number);
                    break;
                case CaptureStep capture when !SceneInstance.IsSceneId(capture.Name):
                    throw new ScriptException($"step {number} (capture): '{capture.Name}' is not a capture name. A capture name follows the grammar of a scene id: lowercase letters, digits, '_', '.' and '-', starting with a letter or a digit.");
                case CaptureStep capture when captureSteps.TryGetValue(capture.Name, out int earlier):
                    throw new ScriptException($"step {number} (capture): step {earlier} has the capture name '{capture.Name}' already.");
                case CaptureStep capture:
                    captureSteps.Add(capture.Name, number);
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
[JsonDerivedType(typeof(CaptureStep), "capture")]
[JsonDerivedType(typeof(CameraStep), "camera")]
public abstract record ScriptStep
{
    // The kinds of step are a closed set: only this assembly can add one, together with its arm in the runner.
    private protected ScriptStep()
    {
    }
}

/// <summary>Runs <paramref name="Ticks"/> ticks, at least 1.</summary>
public sealed record RunStep(int Ticks) : ScriptStep;

/// <summary>
/// Posts an event with <c>World.Post</c>. It joins the routing at the end of the next tick, and its recipients handle it
/// during the tick after that.
/// </summary>
/// <param name="To">Who receives the event.</param>
/// <param name="Event">The C# name of the payload type, such as <c>InputEvent</c>.</param>
/// <param name="Payload">
/// The payload's fields by their camelCase names. Null is the same as an empty object. A payload type with its own JSON
/// converter takes the form of that converter instead: an <see cref="InputEvent"/> is <c>{"kind":"keyDown","code":87}</c>
/// or <c>{"kind":"mouseMove","delta":[3,-4]}</c> (see <c>InputEventJsonConverter</c>), and a null payload is refused for it,
/// because an input event needs a kind.
/// </param>
public sealed record PostStep(MessageTarget To, string Event, JsonElement? Payload = null) : ScriptStep;

/// <summary>Takes a state dump named <paramref name="Name"/>, written to <c>&lt;out&gt;/&lt;name&gt;.json</c>.</summary>
public sealed record DumpStep(string Name) : ScriptStep;

/// <summary>Renders the current state and writes <c>&lt;out&gt;/&lt;name&gt;.png</c>, at the size of the script's window.</summary>
public sealed record CaptureStep(string Name) : ScriptStep;

/// <summary>Makes the live entity with the scene id <paramref name="Entity"/> the camera of the world.</summary>
public sealed record CameraStep(string Entity) : ScriptStep;

/// <summary>The size of a script's captured images, and later of its window, in pixels.</summary>
public sealed record ScriptWindow(int Width, int Height)
{
    /// <summary>1280 by 720, the size of a script with no <c>window</c> key.</summary>
    public static ScriptWindow Default { get; } = new(1280, 720);

    /// <summary>
    /// The longest side that a script can ask for, in pixels: the image limit of common desktop GPUs. Vulkan guarantees
    /// only 4096, so on a device with a lower limit, the renderer fails to make a larger image.
    /// </summary>
    public const int MaxSide = 16384;
}

/// <summary>The recipient of a posted or recorded event, in the form that scripts and dumps write. Its <c>kind</c> key names the case.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ToEntity), "entity")]
[JsonDerivedType(typeof(ToTopic), "topic")]
[JsonDerivedType(typeof(ToFrame), "frame")]
[JsonDerivedType(typeof(ToEveryone), "broadcast")]
public abstract record MessageTarget
{
    // The kinds of target are a closed set, like the cases of the engine's Target union.
    private protected MessageTarget()
    {
    }
}

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
