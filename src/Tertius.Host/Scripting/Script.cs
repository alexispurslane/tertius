using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
            throw new ScriptException($"The script is not valid JSON of a script: {WithPosition(ex)}", ex);
        }
        if (script is null)
            throw new ScriptException("The script is null.");
        script.CheckSteps();
        return script;
    }

    // System.Text.Json writes the path and the position into the message of its own errors. For an error of a union converter
    // it sets only the properties, so the message gets them here, in the same form.
    private static string WithPosition(JsonException ex) =>
        ex is UnionJsonException
            ? $"{ex.Message} Path: {ex.Path} | LineNumber: {ex.LineNumber} | BytePositionInLine: {ex.BytePositionInLine}."
            : ex.Message;

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

/// <summary>
/// One step of a script: a <see cref="RunStep"/>, a <see cref="PostStep"/>, a <see cref="DumpStep"/>, a
/// <see cref="CaptureStep"/> or a <see cref="CameraStep"/>. Its <c>do</c> key names the case in JSON
/// (<see cref="ScriptStepJsonConverter"/>).
/// This is a custom C# 15 union. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler pattern-match
/// it, and a <c>switch</c> over it names every case. The cases are classes, so <see cref="Value"/> does not box.
/// The default value holds no case: it is the JSON <c>null</c>, and <see cref="Script.Parse"/> and the runner refuse it.
/// </summary>
[Union]
[JsonConverter(typeof(ScriptStepJsonConverter))]
public readonly struct ScriptStep : IEquatable<ScriptStep>
{
    // The held case, or null for the default value.
    private readonly object? step;

    public ScriptStep(RunStep run) { ArgumentNullException.ThrowIfNull(run); step = run; }
    public ScriptStep(PostStep post) { ArgumentNullException.ThrowIfNull(post); step = post; }
    public ScriptStep(DumpStep dump) { ArgumentNullException.ThrowIfNull(dump); step = dump; }
    public ScriptStep(CaptureStep capture) { ArgumentNullException.ThrowIfNull(capture); step = capture; }
    public ScriptStep(CameraStep camera) { ArgumentNullException.ThrowIfNull(camera); step = camera; }

    /// <summary>False for the default value, which holds no case.</summary>
    public bool HasValue => step != null;

    public bool TryGetValue([NotNullWhen(true)] out RunStep? value) => (value = step as RunStep) != null;
    public bool TryGetValue([NotNullWhen(true)] out PostStep? value) => (value = step as PostStep) != null;
    public bool TryGetValue([NotNullWhen(true)] out DumpStep? value) => (value = step as DumpStep) != null;
    public bool TryGetValue([NotNullWhen(true)] out CaptureStep? value) => (value = step as CaptureStep) != null;
    public bool TryGetValue([NotNullWhen(true)] out CameraStep? value) => (value = step as CameraStep) != null;

    /// <summary>The held case, or null for the default value.</summary>
    public object? Value => step;

    // The held case decides equality, by the value equality of its record. Two cases of different types are never equal.
    public bool Equals(ScriptStep other) => Equals(step, other.step);
    public override bool Equals(object? obj) => obj is ScriptStep other && Equals(other);
    public override int GetHashCode() => step?.GetHashCode() ?? 0;
    public static bool operator ==(ScriptStep a, ScriptStep b) => a.Equals(b);
    public static bool operator !=(ScriptStep a, ScriptStep b) => !a.Equals(b);

    public override string ToString() => step?.ToString() ?? "ScriptStep(none)";
}

/// <summary>Runs <paramref name="Ticks"/> ticks, at least 1.</summary>
public sealed record RunStep(int Ticks);

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
public sealed record PostStep(MessageTarget To, string Event, JsonElement? Payload = null);

/// <summary>Takes a state dump named <paramref name="Name"/>, written to <c>&lt;out&gt;/&lt;name&gt;.json</c>.</summary>
public sealed record DumpStep(string Name);

/// <summary>Renders the current state and writes <c>&lt;out&gt;/&lt;name&gt;.png</c>, at the size of the script's window.</summary>
public sealed record CaptureStep(string Name);

/// <summary>Makes the live entity with the scene id <paramref name="Entity"/> the camera of the world.</summary>
public sealed record CameraStep(string Entity);

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

/// <summary>
/// The recipient of a posted or recorded event, in the form that scripts and dumps write: a <see cref="ToEntity"/>, a
/// <see cref="ToTopic"/>, a <see cref="ToFrame"/> or a <see cref="ToEveryone"/>. Its <c>kind</c> key names the case in
/// JSON (<see cref="MessageTargetJsonConverter"/>).
/// This is a custom C# 15 union, like the engine's <c>Target</c>, and like <see cref="ScriptStep"/> its cases are classes.
/// The default value holds no case, and JSON refuses it.
/// </summary>
[Union]
[JsonConverter(typeof(MessageTargetJsonConverter))]
public readonly struct MessageTarget : IEquatable<MessageTarget>
{
    // The held case, or null for the default value.
    private readonly object? target;

    public MessageTarget(ToEntity entity) { ArgumentNullException.ThrowIfNull(entity); target = entity; }
    public MessageTarget(ToTopic topic) { ArgumentNullException.ThrowIfNull(topic); target = topic; }
    public MessageTarget(ToFrame frame) { ArgumentNullException.ThrowIfNull(frame); target = frame; }
    public MessageTarget(ToEveryone everyone) { ArgumentNullException.ThrowIfNull(everyone); target = everyone; }

    /// <summary>False for the default value, which holds no case.</summary>
    public bool HasValue => target != null;

    public bool TryGetValue([NotNullWhen(true)] out ToEntity? value) => (value = target as ToEntity) != null;
    public bool TryGetValue([NotNullWhen(true)] out ToTopic? value) => (value = target as ToTopic) != null;
    public bool TryGetValue([NotNullWhen(true)] out ToFrame? value) => (value = target as ToFrame) != null;
    public bool TryGetValue([NotNullWhen(true)] out ToEveryone? value) => (value = target as ToEveryone) != null;

    /// <summary>The held case, or null for the default value.</summary>
    public object? Value => target;

    // The held case decides equality, by the value equality of its record. Two cases of different types are never equal.
    public bool Equals(MessageTarget other) => Equals(target, other.target);
    public override bool Equals(object? obj) => obj is MessageTarget other && Equals(other);
    public override int GetHashCode() => target?.GetHashCode() ?? 0;
    public static bool operator ==(MessageTarget a, MessageTarget b) => a.Equals(b);
    public static bool operator !=(MessageTarget a, MessageTarget b) => !a.Equals(b);

    public override string ToString() => target?.ToString() ?? "MessageTarget(none)";
}

/// <summary>The entity with the scene id <paramref name="Id"/>.</summary>
public sealed record ToEntity(string Id);

/// <summary>Every subscriber of the named topic, such as <c>input</c>.</summary>
public sealed record ToTopic(string Name);

/// <summary>Every entity in the frame that the entity with the scene id <paramref name="Id"/> owns.</summary>
public sealed record ToFrame(string Id);

/// <summary>Every live entity.</summary>
public sealed record ToEveryone;

/// <summary>A script that cannot run: bad JSON, a bad step, or a name that the scene or the loaded code does not know.</summary>
public sealed class ScriptException(string message, Exception? inner = null) : Exception(message, inner);
