using System.Text.Json;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>ScriptRecorder: the steps of a script that replays a session, and the JSON that a recorded script is written as.</summary>
public class ScriptRecorderTests
{
    private static readonly ScriptWindow Size = new(1280, 720);

    private static InputEvent PayloadOf(PostStep post) =>
        post.Payload!.Value.Deserialize<InputEvent>(ScriptJson.Scripts);

    /// <summary>A step as one short line, so that a test states the steps it expects as text.</summary>
    internal static string Describe(ScriptStep step) => step switch
    {
        RunStep run => $"run {run.Ticks}",
        CameraStep camera => $"camera {camera.Entity}",
        DumpStep dump => $"dump {dump.Name}",
        PostStep { To: ToTopic topic } post => $"post {topic.Name} {post.Event} {Describe(PayloadOf(post))}",
        _ => throw new InvalidOperationException($"The test does not describe the step {step}.")
    };

    // An input event as a script names its kind, then its code or its delta: a key that went down is "keyDown 87".
    private static string Describe(InputEvent e) => e switch
    {
        KeyChanged(var code, var isDown) => $"{(isDown ? "keyDown" : "keyUp")} {code}",
        MouseButtonChanged(var code, var isDown) => $"{(isDown ? "mouseDown" : "mouseUp")} {code}",
        MouseMoved(var delta) => $"mouseMove {delta}",
        Scrolled(var delta) => $"scroll {delta}",
        null => throw new InvalidOperationException("The test does not describe the empty input event.")
    };

    internal static string[] Describe(Script script) => [.. script.Steps.Select(Describe)];

    [Fact]
    public void ARecordOfNoInputStartsAtTheCameraRunsForItsTicksAndEndsWithADump()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");

        var script = recorder.ToScript(endTick: 120);

        script.Scene.Should().Be("orbit");
        script.Window.Should().Be(Size);
        Describe(script).Should().Equal("camera chase", "run 120", "dump end");
    }

    [Fact]
    public void AnInputEventIsPostedToTheInputTopicAfterTheTicksThatCameBeforeIt()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");

        recorder.Post(0, InputEvent.KeyDown(87));
        recorder.Post(5, InputEvent.KeyUp(87));
        var script = recorder.ToScript(endTick: 8);

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

        recorder.Post(4, InputEvent.KeyDown(87));
        recorder.SwitchCamera(4, "far");
        recorder.Post(4, InputEvent.MouseDown(0));
        var script = recorder.ToScript(endTick: 4);

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

        recorder.Post(5, InputEvent.KeyDown(87));
        var script = recorder.ToScript(endTick: 5);

        Describe(script).Should().Equal("camera chase", "run 5", "post input InputEvent keyDown 87", "dump end");
    }

    [Fact]
    public void ACameraSwitchIsACameraStepAfterTheTicksThatCameBeforeIt()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");

        recorder.SwitchCamera(30, "far");
        var script = recorder.ToScript(endTick: 31);

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
        InputEvent moved = InputEvent.MouseMove(new(0.1f, -1f / 3f));

        recorder.Post(2, moved);

        var post = (PostStep)recorder.ToScript(2).Steps.Single(step => step is PostStep).Value!;
        PayloadOf(post).TryGetValue(out MouseMoved read).Should().BeTrue();
        BitConverter.SingleToInt32Bits(read.Delta.X).Should().Be(BitConverter.SingleToInt32Bits(0.1f));
        BitConverter.SingleToInt32Bits(read.Delta.Y).Should().Be(BitConverter.SingleToInt32Bits(-1f / 3f));
    }

    [Fact]
    public void ATickBeforeAnEarlierStepIsRefused()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");
        recorder.Post(5, InputEvent.KeyDown(87));

        Action post = () => recorder.Post(3, InputEvent.KeyUp(87));
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

        var script = recorder.ToScript(endTick: 3_000_000_000);

        Describe(script).Should().Equal("run 2147483647", "run 852516353", "dump end");
    }

    [Fact]
    public void AskingForTheScriptTwiceGivesTheSameScriptAndKeepsRecording()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");
        recorder.Post(1, InputEvent.KeyDown(87));

        string[] first = Describe(recorder.ToScript(3));
        string[] again = Describe(recorder.ToScript(3));
        recorder.Post(4, InputEvent.KeyUp(87));

        again.Should().Equal(first);
        Describe(recorder.ToScript(4)).Should().Equal(
            "camera chase", "run 1", "post input InputEvent keyDown 87", "run 3", "post input InputEvent keyUp 87",
            "dump end");
    }

    [Fact]
    public void TheJsonOfARecordParsesBackAsTheSameScript()
    {
        var recorder = new ScriptRecorder("orbit", new(800, 600), "chase");
        recorder.Post(2, InputEvent.KeyDown(87));
        recorder.Post(2, InputEvent.MouseMove(new(12.5f, -3f)));
        recorder.SwitchCamera(9, "far");
        recorder.Post(9, InputEvent.Scroll(new(0, 1)));
        var script = recorder.ToScript(20);

        Script parsed = Script.Parse(script.ToJson());

        parsed.Scene.Should().Be("orbit");
        parsed.Window.Should().Be(new ScriptWindow(800, 600));
        parsed.Steps.Should().HaveCount(script.Steps.Count);
        parsed.Steps.Select(step => step.Value!.GetType()).Should()
            .Equal(script.Steps.Select(step => step.Value!.GetType()));
        var moved = (PostStep)parsed.Steps[3].Value!;
        (PayloadOf(moved) == InputEvent.MouseMove(new(12.5f, -3f))).Should().BeTrue();
        var scrolled = (PostStep)parsed.Steps[6].Value!;
        (PayloadOf(scrolled) == InputEvent.Scroll(new(0, 1))).Should().BeTrue();
        ((CameraStep)parsed.Steps[0].Value!).Entity.Should().Be("chase");
        ((CameraStep)parsed.Steps[5].Value!).Entity.Should().Be("far");
        ((DumpStep)parsed.Steps[^1].Value!).Name.Should().Be("end");
    }

    [Fact]
    public void TheJsonHasOneStepOnEachLine()
    {
        var recorder = new ScriptRecorder("orbit", Size, "chase");
        for (int tick = 0; tick < 10; tick++)
            recorder.Post(tick, InputEvent.KeyDown(65 + tick));
        var script = recorder.ToScript(10);

        string json = script.ToJson();

        string[] stepLines = [.. json.Split('\n').Where(line => line.Contains("\"do\""))];
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
        recorder.Post(1, InputEvent.KeyDown(87));
        recorder.SwitchCamera(2, "crew");
        Script script = Script.Parse(recorder.ToScript(3).ToJson());
        using World world = NewJoltWorld();
        SceneInstance scene = Probes(world);

        RunResult result = ScriptRunner.Run(script: script, world: world, scene: scene);

        result.Dump("end").Tick.Should().Be(3);
        result.Dump("end").Camera.Should().Be("crew");
    }
}
