using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tertius.Host.Scripting;

/// <summary>
/// A scripted run: the scene to build, then steps that run in file order. See docs/plans/host-and-scripted-runs.md,
/// "Script format".
/// </summary>
/// <param name="Scene">The name of the scene that the program builds before the first step.</param>
/// <param name="Steps">The steps, in the order that they run. A written script puts them after the window.</param>
/// <param name="Window">The size of the captured images. A script with no <c>window</c> key has
/// <see cref="ScriptWindow.Default"/>.</param>
public sealed record Script(
    string Scene,
    [property: JsonPropertyOrder(1)] IReadOnlyList<ScriptStep> Steps,
    ScriptWindow? Window = null)
{
    /// <summary>The size of the captured images, in pixels.</summary>
    public ScriptWindow Window { get; init; } = Window ?? ScriptWindow.Default;

    /// <summary>
    /// Reads a script from JSON text. Throws <see cref="ScriptException"/> for a script that is not valid: bad JSON, a
    /// missing, unknown or mistyped key, no steps, a run of less than one tick, a film with a number of frames or of ticks
    /// before each frame out of its range, a dump, capture or film name that is not a scene id, or a name that an earlier
    /// step has: two dumps, two captures or two films with one name, or a capture with the name of a frame of a film.
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
            throw new ScriptException(
                $"The window is {Window.Width} by {Window.Height} pixels. Each side must be 1 to {ScriptWindow.MaxSide}.");
        if (Steps.Count == 0)
            throw new ScriptException("The script has no steps.");
        var dumpSteps = new Dictionary<string, int>(StringComparer.Ordinal);
        // The capture names and the films, each with its step number. The frames of a film count as captures, but the
        // check makes no list of them: a frame name is its film's name with seven characters more, so a film's frames
        // clash with another film's frames only when the two films have one name.
        var captureSteps = new Dictionary<string, int>(StringComparer.Ordinal);
        var filmSteps = new Dictionary<string, (FilmStep Film, int Number)>(StringComparer.Ordinal);
        for (int i = 0; i < Steps.Count; i++)
        {
            int number = i + 1;
            switch (Steps[i])
            {
                case null:
                    throw new ScriptException($"step {number}: a step cannot be null.");
                // JSON cannot read such a post, but code can build one, because the target is a struct.
                case PostStep { To: null }:
                    throw new ScriptException($"step {number} (post): an event target cannot be null.");
                case RunStep { Ticks: < 1 } run:
                    throw new ScriptException(
                        $"step {number} (run): it runs {run.Ticks} ticks, and a run step runs at least 1.");
                case DumpStep dump when !SceneInstance.IsSceneId(dump.Name):
                    throw new ScriptException(
                        $"step {number} (dump): '{dump.Name}' is not a dump name. A dump name follows the grammar of a scene id: lowercase letters, digits, '_', '.' and '-', starting with a letter or a digit.");
                case DumpStep dump when dumpSteps.TryGetValue(dump.Name, out int earlier):
                    throw new ScriptException(
                        $"step {number} (dump): step {earlier} has the dump name '{dump.Name}' already.");
                case DumpStep dump:
                    dumpSteps.Add(dump.Name, number);
                    break;
                case CaptureStep capture when !SceneInstance.IsSceneId(capture.Name):
                    throw new ScriptException(
                        $"step {number} (capture): '{capture.Name}' is not a capture name. A capture name follows the grammar of a scene id: lowercase letters, digits, '_', '.' and '-', starting with a letter or a digit.");
                case CaptureStep capture when captureSteps.TryGetValue(capture.Name, out int earlier):
                    throw new ScriptException(
                        $"step {number} (capture): step {earlier} has the capture name '{capture.Name}' already.");
                case CaptureStep capture when FilmWithFrameNamed(capture.Name, filmSteps) is int film:
                    throw new ScriptException(
                        $"step {number} (capture): the capture name '{capture.Name}' is the name of a frame of the film of step {film}.");
                case CaptureStep capture:
                    captureSteps.Add(capture.Name, number);
                    break;
                case FilmStep { Frames: < 1 or > FilmStep.MaxFrames } film:
                    throw new ScriptException(
                        $"step {number} (film): it has {film.Frames} frames, and a film has 1 to {FilmStep.MaxFrames:N0}.");
                case FilmStep { Every: < 1 } film:
                    throw new ScriptException(
                        $"step {number} (film): it runs every {film.Every} ticks, and a film runs at least 1 tick before each frame.");
                case FilmStep film
                    when !SceneInstance.IsSceneId(film.Name) || film.Name.Length > FilmStep.MaxNameLength:
                    throw new ScriptException(
                        $"step {number} (film): '{film.Name}' is not a film name. A film name follows the grammar of a scene id: lowercase letters, digits, '_', '.' and '-', starting with a letter or a digit. It has at most {FilmStep.MaxNameLength} characters, so that the names of its frames, such as '{film.Name}-000000', are capture names.");
                case FilmStep film when filmSteps.TryGetValue(film.Name, out (FilmStep Film, int Number) earlier):
                    throw new ScriptException(
                        $"step {number} (film): step {earlier.Number} films '{film.Name}' already, so the frames of the two films would have the same names.");
                case FilmStep film when CaptureNamedAfterAFrameOf(film, captureSteps) is (string capture, int earlier):
                    throw new ScriptException(
                        $"step {number} (film): step {earlier} has the capture name '{capture}', which is the name of a frame of this film.");
                case FilmStep film:
                    filmSteps.Add(film.Name, (film, number));
                    break;
                case PostStep or RunStep or CameraStep or OutlinesStep:
                    // A valid post or run step, and every camera and outlines step: nothing to check across steps.
                    break;
                default:
                    throw new UnreachableException($"Step {number} has a case that the check does not know.");
            }
        }
    }

    /// <summary>The step number of the film that has a frame named <paramref name="captureName"/>, or null for none.</summary>
    private static int? FilmWithFrameNamed(string captureName,
        Dictionary<string, (FilmStep Film, int Number)> filmSteps) =>
        captureName.Length > 7 && filmSteps.TryGetValue(captureName[..^7], out (FilmStep Film, int Number) found) &&
        found.Film.HasFrameNamed(captureName)
            ? found.Number
            : null;

    /// <summary>A capture whose name is the name of a frame of <paramref name="film"/>, with its step number, or null for none.</summary>
    private static (string Name, int Number)? CaptureNamedAfterAFrameOf(FilmStep film,
        Dictionary<string, int> captureSteps)
    {
        foreach ((string name, int number) in captureSteps)
            if (film.HasFrameNamed(name))
                return (name, number);
        return null;
    }
}

/// <summary>
/// One step of a script: a <see cref="RunStep"/>, a <see cref="PostStep"/>, a <see cref="DumpStep"/>, a
/// <see cref="CaptureStep"/>, a <see cref="CameraStep"/>, a <see cref="FilmStep"/> or an <see cref="OutlinesStep"/>. Its
/// <c>do</c> key names the case in JSON (<see cref="ScriptStepJsonConverter"/>).
/// This is a built-in C# 15 union: a <c>switch</c> over it names every case, and an <c>is</c> pattern tests one. The cases
/// are classes, so it is a struct around one reference, and <c>Value</c> does not box.
/// The default value holds no case and matches the <c>null</c> pattern. It is the JSON <c>null</c>, and
/// <see cref="Script.Parse"/> and the runner refuse it. A null case makes the default value too.
/// </summary>
[JsonConverter(typeof(ScriptStepJsonConverter))]
public union ScriptStep(RunStep, PostStep, DumpStep, CaptureStep, CameraStep, FilmStep, OutlinesStep)
    : IEquatable<ScriptStep>
{
    // The held case decides equality, by the value equality of its record. Two cases of different types are never equal.
    // The built-in union has only the equality of ValueType, which reads the field by reflection, and no == operator.
    public readonly bool Equals(ScriptStep other) => Equals(Value, other.Value);
    public override readonly bool Equals(object? obj) => obj is ScriptStep other && Equals(other);
    public override readonly int GetHashCode() => Value?.GetHashCode() ?? 0;
    public static bool operator ==(ScriptStep a, ScriptStep b) => a.Equals(b);
    public static bool operator !=(ScriptStep a, ScriptStep b) => !a.Equals(b);

    // The built-in union's ToString gives only the name of the union.
    public override readonly string ToString() => Value?.ToString() ?? "ScriptStep(none)";
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

/// <summary>
/// Renders the current state at the size of the script's window, into the run's capture target: the file
/// <c>&lt;out&gt;/&lt;name&gt;.png</c>, or the next frame of the video of <c>--video</c>.
/// </summary>
public sealed record CaptureStep(string Name);

/// <summary>Makes the live entity with the scene id <paramref name="Entity"/> the camera of the world.</summary>
public sealed record CameraStep(string Entity);

/// <summary>
/// Films the world: runs <paramref name="Every"/> ticks and captures, <paramref name="Frames"/> times. It is the same as
/// <paramref name="Frames"/> repetitions of a <see cref="RunStep"/> of <paramref name="Every"/> ticks followed by a
/// <see cref="CaptureStep"/> named <see cref="FrameName"/>, so it runs <paramref name="Frames"/> ×
/// <paramref name="Every"/>
/// ticks, and its first capture comes after its first <paramref name="Every"/> ticks. See docs/plans/blue-danube-demo.md,
/// change 2.
/// </summary>
/// <param name="Name">The name of the film. Its frames are named after it, so it follows the grammar of a scene id and has at most
/// <see cref="MaxNameLength"/> characters.</param>
/// <param name="Frames">How many images it captures, 1 to <see cref="MaxFrames"/>.</param>
/// <param name="Every">How many ticks it runs before each image, at least 1.</param>
public sealed record FilmStep(string Name, int Frames, int Every)
{
    /// <summary>The most frames that one film captures.</summary>
    public const int MaxFrames = 1_000_000;

    /// <summary>
    /// The longest name of a film. A frame name has seven characters more, and it must be a scene id of at most 64
    /// characters, as a capture name is.
    /// </summary>
    public const int MaxNameLength = 57;

    /// <summary>
    /// The capture name of frame <paramref name="number"/>, counted from 0: the film's name, a dash and the number in six
    /// digits, such as <c>climb-000001</c>. That fits ffmpeg's <c>%06d</c> pattern.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="number"/> is negative, or not below <see cref="Frames"/>, so it names no frame of the film.
    /// </exception>
    public string FrameName(int number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(number, Frames);
        return $"{Name}-{number:D6}";
    }

    /// <summary>True if <paramref name="captureName"/> is the <see cref="FrameName"/> of one of the film's frames.</summary>
    public bool HasFrameNamed(string captureName) =>
        captureName.Length == Name.Length + 7
        && captureName.StartsWith(Name, StringComparison.Ordinal)
        && captureName[Name.Length] == '-'
        && int.TryParse(s: captureName.AsSpan(Name.Length + 1), style: NumberStyles.None,
            provider: CultureInfo.InvariantCulture,
            result: out int number)
        && number < Frames;
}

/// <summary>
/// Sets the view setting <c>World.ShowFrameOutlines</c> to <paramref name="Show"/>: true shows the outline of each frame
/// that has a parent, and false hides the outlines. As after a camera step, the first render snapshot that is built after the
/// step shows the change, so the next capture does.
/// </summary>
public sealed record OutlinesStep(bool Show);

/// <summary>The size of a script's captured images, and later of its window, in pixels.</summary>
public sealed record ScriptWindow(int Width, int Height)
{
    /// <summary>
    /// The longest side that a script can ask for, in pixels: the image limit of common desktop GPUs. Vulkan guarantees
    /// only 4096, so on a device with a lower limit, the renderer fails to make a larger image.
    /// </summary>
    public const int MaxSide = 16384;

    /// <summary>1280 by 720, the size of a script with no <c>window</c> key.</summary>
    public static ScriptWindow Default { get; } = new(1280, 720);
}

/// <summary>
/// The recipient of a posted or recorded event, in the form that scripts and dumps write: a <see cref="ToEntity"/>, a
/// <see cref="ToTopic"/>, a <see cref="ToFrame"/> or a <see cref="ToEveryone"/>. Its <c>kind</c> key names the case in
/// JSON (<see cref="MessageTargetJsonConverter"/>).
/// This is a built-in C# 15 union, like <see cref="ScriptStep"/>, and like it its cases are classes. The default value
/// holds no case and matches the <c>null</c> pattern, and a null case makes it too. JSON refuses it, and
/// <see cref="Script.CheckSteps"/> refuses a post step built in code with it.
/// </summary>
[JsonConverter(typeof(MessageTargetJsonConverter))]
public union MessageTarget(ToEntity, ToTopic, ToFrame, ToEveryone) : IEquatable<MessageTarget>
{
    // The held case decides equality, by the value equality of its record. Two cases of different types are never equal.
    // The built-in union has only the equality of ValueType, which reads the field by reflection, and no == operator.
    public readonly bool Equals(MessageTarget other) => Equals(Value, other.Value);
    public override readonly bool Equals(object? obj) => obj is MessageTarget other && Equals(other);
    public override readonly int GetHashCode() => Value?.GetHashCode() ?? 0;
    public static bool operator ==(MessageTarget a, MessageTarget b) => a.Equals(b);
    public static bool operator !=(MessageTarget a, MessageTarget b) => !a.Equals(b);

    // The built-in union's ToString gives only the name of the union.
    public override readonly string ToString() => Value?.ToString() ?? "MessageTarget(none)";
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
