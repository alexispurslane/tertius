using System.Numerics;
using System.Text.Json;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>ScriptRecorder: the steps of a script that replays a session, and the JSON that a recorded script is written as.</summary>
public class ScriptRecorderTests
{
    private static readonly ScriptWindow Size = new(1280, 720);

    private static InputEvent Key(InputKind kind, int code) => new() { Kind = kind, Code = code };

    private static InputEvent PayloadOf(PostStep post) => post.Payload!.Value.Deserialize<InputEvent>(ScriptJson.Scripts);

    /// <summary>A step as one short line, so that a test states the steps it expects as text.</summary>
    internal static string Describe(ScriptStep step) => step switch
    {
        RunStep run => $"run {run.Ticks}",
        CameraStep camera => $"camera {camera.Entity}",
        DumpStep dump => $"dump {dump.Name}",
        PostStep { To: ToTopic topic } post => $"post {topic.Name} {post.Event} {KindName(PayloadOf(post).Kind)} {PayloadOf(post).Code}",
        _ => throw new InvalidOperationException($"The test does not describe the step {step}."),
    };

    // The name of an input kind as a script writes it: KeyDown is keyDown.
    private static string KindName(InputKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];

    internal static string[] Describe(Script script) => script.Steps.Select(Describe).ToArray();

    [Fact]
    public void ARecordOfNoInputStartsAtTheCameraRunsForItsTicksAndEndsWithADump()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");

        Script script = recorder.ToScript(endTick: 120);

        script.Scene.Should().Be("orbit");
        script.Window.Should().Be(Size);
        Describe(script).Should().Equal("camera chase", "run 120", "dump end");
    }

    [Fact]
    public void AnInputEventIsPostedToTheInputTopicAfterTheTicksThatCameBeforeIt()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");

        recorder.Post(0, Key(InputKind.KeyDown, 87));
        recorder.Post(5, Key(InputKind.KeyUp, 87));
        Script script = recorder.ToScript(endTick: 8);

        Describe(script).Should().Equal(
            "camera chase",
            "post input InputEvent keyDown 87",
            "run 5",
            "post input InputEvent keyUp 87",
            "run 3",
            "dump end");
    }

    [Fact]
    public void StepsOfOneTickKeepTheirOrderWithNoRunBetweenThem()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");

        recorder.Post(4, Key(InputKind.KeyDown, 87));
        recorder.SwitchCamera(4, "far");
        recorder.Post(4, Key(InputKind.MouseDown, 0));
        Script script = recorder.ToScript(endTick: 4);

        Describe(script).Should().Equal(
            "camera chase",
            "run 4",
            "post input InputEvent keyDown 87",
            "camera far",
            "post input InputEvent mouseDown 0",
            "dump end");
    }

    [Fact]
    public void AnEventAtTheEndTickAddsNoEmptyRun()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");

        recorder.Post(5, Key(InputKind.KeyDown, 87));
        Script script = recorder.ToScript(endTick: 5);

        Describe(script).Should().Equal("camera chase", "run 5", "post input InputEvent keyDown 87", "dump end");
    }

    [Fact]
    public void ACameraSwitchIsACameraStepAfterTheTicksThatCameBeforeIt()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");

        recorder.SwitchCamera(30, "far");
        Script script = recorder.ToScript(endTick: 31);

        Describe(script).Should().Equal("camera chase", "run 30", "camera far", "run 1", "dump end");
    }

    [Fact]
    public void WithoutACameraAtTheStartTheRecordHasNoCameraStepThere()
    {
        var recorder = new ScriptRecorder("orbit", Size, startCamera: null);

        Describe(recorder.ToScript(endTick: 2)).Should().Equal("run 2", "dump end");
    }

    [Fact]
    public void ARecordOfNoTicksAndNoInputIsOnlyTheStartAndTheDump()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");

        Describe(recorder.ToScript(endTick: 0)).Should().Equal("camera chase", "dump end");
    }

    [Fact]
    public void ThePayloadReadsBackAsTheEventThatWasPostedToTheLastBit()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");
        var moved = new InputEvent { Kind = InputKind.MouseMove, Delta = new Vector2(0.1f, -1f / 3f) };

        recorder.Post(2, moved);

        var post = (PostStep)recorder.ToScript(2).Steps.Single(step => step is PostStep);
        InputEvent read = PayloadOf(post);
        read.Kind.Should().Be(InputKind.MouseMove);
        read.Code.Should().Be(0);
        BitConverter.SingleToInt32Bits(read.Delta.X).Should().Be(BitConverter.SingleToInt32Bits(0.1f));
        BitConverter.SingleToInt32Bits(read.Delta.Y).Should().Be(BitConverter.SingleToInt32Bits(-1f / 3f));
    }

    [Fact]
    public void ATickBeforeAnEarlierStepIsRefused()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");
        recorder.Post(5, Key(InputKind.KeyDown, 87));

        Action post = () => recorder.Post(3, Key(InputKind.KeyUp, 87));
        Action camera = () => recorder.SwitchCamera(4, "far");
        Action end = () => recorder.ToScript(4);

        post.Should().Throw<ArgumentOutOfRangeException>();
        camera.Should().Throw<ArgumentOutOfRangeException>();
        end.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AGapLongerThanARunStepCanHoldIsSplitIntoRunSteps()
    {
        var recorder = new ScriptRecorder("orbit", Size, startCamera: null);

        Script script = recorder.ToScript(endTick: 3_000_000_000);

        Describe(script).Should().Equal("run 2147483647", "run 852516353", "dump end");
    }

    [Fact]
    public void AskingForTheScriptTwiceGivesTheSameScriptAndKeepsRecording()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");
        recorder.Post(1, Key(InputKind.KeyDown, 87));

        string[] first = Describe(recorder.ToScript(3));
        string[] again = Describe(recorder.ToScript(3));
        recorder.Post(4, Key(InputKind.KeyUp, 87));

        again.Should().Equal(first);
        Describe(recorder.ToScript(4)).Should().Equal(
            "camera chase", "run 1", "post input InputEvent keyDown 87", "run 3", "post input InputEvent keyUp 87", "dump end");
    }

    [Fact]
    public void TheJsonOfARecordParsesBackAsTheSameScript()
    {
        var recorder = new ScriptRecorder("orbit", new ScriptWindow(800, 600), "chase");
        recorder.Post(2, Key(InputKind.KeyDown, 87));
        recorder.Post(2, new InputEvent { Kind = InputKind.MouseMove, Delta = new Vector2(12.5f, -3f) });
        recorder.SwitchCamera(9, "far");
        recorder.Post(9, new InputEvent { Kind = InputKind.Scroll, Delta = new Vector2(0, 1) });
        Script script = recorder.ToScript(20);

        Script parsed = Script.Parse(script.ToJson());

        parsed.Scene.Should().Be("orbit");
        parsed.Window.Should().Be(new ScriptWindow(800, 600));
        parsed.Steps.Should().HaveCount(script.Steps.Count);
        parsed.Steps.Select(step => step.GetType()).Should().Equal(script.Steps.Select(step => step.GetType()));
        var moved = (PostStep)parsed.Steps[3];
        PayloadOf(moved).Kind.Should().Be(InputKind.MouseMove);
        PayloadOf(moved).Delta.Should().Be(new Vector2(12.5f, -3f));
        var scrolled = (PostStep)parsed.Steps[6];
        PayloadOf(scrolled).Kind.Should().Be(InputKind.Scroll);
        PayloadOf(scrolled).Delta.Should().Be(new Vector2(0, 1));
        ((CameraStep)parsed.Steps[0]).Entity.Should().Be("chase");
        ((CameraStep)parsed.Steps[5]).Entity.Should().Be("far");
        ((DumpStep)parsed.Steps[^1]).Name.Should().Be("end");
    }

    [Fact]
    public void TheJsonHasOneStepOnEachLine()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");
        for (int tick = 0; tick < 10; tick++)
            recorder.Post(tick, Key(InputKind.KeyDown, 65 + tick));
        Script script = recorder.ToScript(10);

        string json = script.ToJson();

        string[] stepLines = json.Split('\n').Where(line => line.Contains("\"do\"")).ToArray();
        stepLines.Should().HaveCount(script.Steps.Count);
        stepLines.Should().OnlyContain(line => line.Split("\"do\"").Length == 2, "no line holds two steps");
        json.Should().EndWith("\n");
    }

    [Fact]
    public void TheJsonStartsWithTheSceneAndTheWindowSoAReaderSeesThemBeforeTheSteps()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");

        using JsonDocument json = JsonDocument.Parse(recorder.ToScript(1).ToJson());

        json.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("scene", "window", "steps");
    }

    [Fact]
    public void TheJsonOfARecordRunsAsAnOrdinaryScriptOnItsScene()
    {
        // The probe of the "probes" scene hears the topic "test", so the recorded input reaches nobody. The run must still
        // be valid: the scene id of the camera, the topic and the payload type all resolve.
        var recorder = new ScriptRecorder("probes", Size, "probe");
        recorder.Post(1, Key(InputKind.KeyDown, 87));
        recorder.SwitchCamera(2, "crew");
        Script script = Script.Parse(recorder.ToScript(3).ToJson());
        using World world = NewJoltWorld();
        SceneInstance scene = Probes(world);

        RunResult result = ScriptRunner.Run(script, world, scene);

        result.Dump("end").Tick.Should().Be(3);
        result.Dump("end").Camera.Should().Be("crew");
    }
}
