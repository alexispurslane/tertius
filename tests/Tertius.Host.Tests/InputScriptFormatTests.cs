using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tertius.Host.Scripting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The input events that the checked-in scripts post: every file in the folder scripts still loads, and each of its
/// <c>InputEvent</c> posts reads as the JSON says. The expected value comes from the raw JSON of the payload, never from
/// the type that reads it, so the test holds through a change of that type.
/// </summary>
public class InputScriptFormatTests
{
    // The kinds of input event as a script writes them, and the members that a payload of each kind has.
    private static readonly Dictionary<string, string> MemberOfKind = new()
    {
        ["keyDown"] = "code",
        ["keyUp"] = "code",
        ["mouseDown"] = "code",
        ["mouseUp"] = "code",
        ["mouseMove"] = "delta",
        ["scroll"] = "delta"
    };

    private static IEnumerable<string> ScriptFiles() =>
        Directory.GetFiles(Path.Combine(RepositoryRoot(), "scripts"), "*.json").Order(StringComparer.Ordinal);

    /// <summary>Every <c>InputEvent</c> post of every script in the folder, with the file that it is in and its step number.</summary>
    private static List<(string File, int Step, JsonElement Payload)> InputPosts() =>
    [
        .. ScriptFiles()
            .SelectMany(path => Script.Load(path).Steps
                .Select((step, index) => (Path.GetFileName(path), index, step))
                .Where(x => x.step is PostStep { Event: "InputEvent" })
                .Select(x => (x.Item1, x.index, ((PostStep)x.step.Value!).Payload!.Value)))
    ];

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>An input event as one line: the kind, then the code or the two numbers of the delta.</summary>
    private static string Describe(InputEvent e) => e switch
    {
        KeyChanged(var code, var isDown) => $"{(isDown ? "keyDown" : "keyUp")} {code}",
        MouseButtonChanged(var code, var isDown) => $"{(isDown ? "mouseDown" : "mouseUp")} {code}",
        MouseMoved(var delta) => $"mouseMove [{Number(delta.X)}, {Number(delta.Y)}]",
        Scrolled(var delta) => $"scroll [{Number(delta.X)}, {Number(delta.Y)}]",
        null => throw new InvalidOperationException("The test does not describe the empty input event.")
    };

    /// <summary>What the payload says, as the same line, read from the raw JSON.</summary>
    private static string Expected(JsonElement payload)
    {
        string kind = payload.GetProperty("kind").GetString()!;
        return MemberOfKind[kind] == "code"
            ? $"{kind} {payload.GetProperty("code").GetInt32()}"
            : $"{kind} [{Number(payload.GetProperty("delta")[0].GetSingle())}, {Number(payload.GetProperty("delta")[1].GetSingle())}]";
    }

    [Fact]
    public void EveryFileInTheScriptsFolderLoads()
    {
        string[] files = [.. ScriptFiles()];

        files.Should().NotBeEmpty();
        foreach (string path in files)
            Script.Load(path).Steps.Should().NotBeEmpty(because: path);
    }

    [Fact]
    public void TheScriptsPostOnlyKnownKindsOfInputWithAKindOfEachFormAmongThem()
    {
        List<(string File, int Step, JsonElement Payload)> posts = InputPosts();
        string[] kinds = [.. posts.Select(post => post.Payload.GetProperty("kind").GetString()!).Distinct()];

        kinds.Should().NotBeEmpty().And.BeSubsetOf(MemberOfKind.Keys);
        // A kind with a code and a kind with a delta, so that the tests below compare posts of both forms.
        kinds.Should().Contain(kind => MemberOfKind[kind] == "code").And.Contain(kind => MemberOfKind[kind] == "delta");
        posts.Select(post => post.File).Distinct().Count().Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void EveryInputPostOfAScriptHasTheKindAndTheOneMemberOfItsKind()
    {
        foreach ((string file, int step, JsonElement payload) in InputPosts())
        {
            string kind = payload.GetProperty("kind").GetString()!;
            payload.EnumerateObject().Select(property => property.Name).Should()
                .BeEquivalentTo(["kind", MemberOfKind[kind]], because: $"{file} step {step}");
        }
    }

    [Fact]
    public void EveryInputPostOfAScriptReadsAsItsJsonSays()
    {
        foreach ((string file, int step, JsonElement payload) in InputPosts())
            Describe(payload.Deserialize<InputEvent>(ScriptJson.Scripts)).Should()
                .Be(Expected(payload), because: $"{file} step {step}");
    }

    [Fact]
    public void ARecordingWritesEachInputPostWithTheKindAndTheOneMemberOfItsKind()
    {
        var recorder = new ScriptRecorder("orbit", new(800, 600), startCamera: null);
        recorder.Post(0, InputEvent.KeyDown(87));
        recorder.Post(0, InputEvent.KeyUp(87));
        recorder.Post(0, InputEvent.MouseDown(0));
        recorder.Post(0, InputEvent.MouseUp(0));
        recorder.Post(0, InputEvent.MouseMove(new(12.5f, -3)));
        recorder.Post(0, InputEvent.Scroll(new(0, 1)));

        Script written = Script.Parse(recorder.ToScript(endTick: 1).ToJson());

        written.Steps.Select(step => step.Value).OfType<PostStep>().Select(post => post.Payload!.Value.GetRawText())
            .Should().Equal(
                """{"kind":"keyDown","code":87}""",
                """{"kind":"keyUp","code":87}""",
                """{"kind":"mouseDown","code":0}""",
                """{"kind":"mouseUp","code":0}""",
                """{"kind":"mouseMove","delta":[12.5,-3]}""",
                """{"kind":"scroll","delta":[0,1]}""");
    }

    /// <summary>The vector that <paramref name="read"/> reads, or null when it refuses the JSON.</summary>
    private static Vector2? ReadOrNull(Func<Vector2> read)
    {
        try
        {
            return read();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [Theory]
    [InlineData(1.5f, -2f)]
    [InlineData(-0f, 0.1f)]
    [InlineData(float.NaN, float.NegativeInfinity)]
    public void AnInputEventWritesItsDeltaAsEveryOtherVectorIsWritten(float x, float y)
    {
        var delta = new Vector2(x, y);

        using JsonDocument written =
            JsonDocument.Parse(JsonSerializer.Serialize(InputEvent.Scroll(delta), ScriptJson.Recordings));

        written.RootElement.GetProperty("delta").GetRawText().Should()
            .Be(JsonSerializer.Serialize(delta, ScriptJson.Recordings));
    }

    [Theory]
    [InlineData("[1.5, -2]")]
    [InlineData("[3e38, -1e-45]")]
    [InlineData("[1e39, 0]")]
    [InlineData("[1, 2, 3]")]
    [InlineData("[1]")]
    [InlineData("""["1", 2]""")]
    [InlineData("{}")]
    public void AnInputEventReadsItsDeltaAsEveryOtherVectorIsRead(string array)
    {
        Vector2? vector = ReadOrNull(() => JsonSerializer.Deserialize<Vector2>(array, ScriptJson.Scripts));
        Vector2? delta = ReadOrNull(() =>
            JsonSerializer.Deserialize<InputEvent>($$"""{"kind":"scroll","delta":{{array}}}""", ScriptJson.Scripts)
                switch
                {
                    Scrolled(var read) => read,
                    _ => throw new InvalidOperationException("A scroll read as another case.")
                });

        delta.Should().Be(vector);
    }
}
