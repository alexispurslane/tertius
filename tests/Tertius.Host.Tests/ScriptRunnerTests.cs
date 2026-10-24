using System.Numerics;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// ScriptRunner.Run: steps in file order, ticks in lock-step with the run steps, and posts that reach the entities that
/// their targets name. Each test builds its scene in a world with no physics.
/// </summary>
public sealed class ScriptRunnerTests
{
    private const string NudgeProbe =
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge", "payload": { "amount": 1.5 } }""";

    private const string CameraOnProbe = """{ "do": "camera", "entity": "probe" }""";

    private static RunResult Run(World world, SceneInstance scene, params string[] steps) =>
        ScriptRunner.Run(
            script: Script.Parse($$"""{ "scene": "test", "steps": [ {{string.Join(", ", steps)}} ] }"""),
            world: world,
            scene: scene
        );

    [Fact]
    public void RunStepsAdvanceTheWorldByTheirTicksInFileOrder()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        RunResult result = Run(
            world,
            scene,
            """{ "do": "run", "ticks": 3 }""",
            """{ "do": "dump", "name": "a" }""",
            """{ "do": "run", "ticks": 2 }""",
            """{ "do": "dump", "name": "b" }"""
        );

        result.Dump("a").Tick.Should().Be(3);
        result.Dump("b").Tick.Should().Be(5);
        world.TickNumber.Should().Be(5, "the run ends after the last step");
    }

    [Fact]
    public void APostedEventReachesItsRecipientOnTheSecondTickAfterThePost()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, NudgeProbe, """{ "do": "run", "ticks": 1 }""");
        ProbeOf(world, scene, "probe").Seen.Should().BeEmpty("the event is routed at the end of the first tick");

        Run(world, scene, """{ "do": "run", "ticks": 1 }""");
        ProbeOf(world, scene, "probe")
            .Seen.Should()
            .ContainSingle()
            .Which.Tick.Should()
            .Be(1, "the second tick of the run is tick 1");
    }

    [Fact]
    public void APostBetweenTwoRunStepsIsPostedAfterTheTicksBeforeIt()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, """{ "do": "run", "ticks": 4 }""", NudgeProbe, """{ "do": "run", "ticks": 2 }""");

        ProbeOf(world, scene, "probe").Seen.Should().ContainSingle().Which.Tick.Should().Be(5);
    }

    [Theory]
    [InlineData("""{ "kind": "entity", "id": "probe" }""", new[] { "probe" })]
    [InlineData("""{ "kind": "topic", "name": "test" }""", new[] { "probe" })]
    [InlineData("""{ "kind": "frame", "id": "ship" }""", new[] { "crew" })]
    [InlineData("""{ "kind": "broadcast" }""", new[] { "probe", "bystander", "crew" })]
    public void EachKindOfTargetReachesTheEntitiesThatItNames(string target, string[] reached)
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(
            world,
            scene,
            $$"""{ "do": "post", "to": {{target}}, "event": "Nudge" }""",
            """{ "do": "run", "ticks": 2 }"""
        );

        string[] all = ["probe", "bystander", "crew"];
        all.Where(id => ProbeOf(world, scene, id).Seen.Count > 0).Should().BeEquivalentTo(reached);
    }

    [Fact]
    public void AnInputEventPayloadIsReadInTheFormOfItsKind()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(
            world,
            scene,
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "InputEvent", "payload": { "kind": "mouseMove", "delta": [1.5, -2] } }""",
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "InputEvent", "payload": { "kind": "keyUp", "code": 87 } }""",
            """{ "do": "run", "ticks": 2 }"""
        );

        ProbeOf(world, scene, "probe")
            .Seen.Select(seen => seen.Event.As<InputEvent>())
            .Should()
            .Equal(InputEvent.MouseMove(new(1.5f, -2)), InputEvent.KeyUp(87));
    }

    [Fact]
    public void AnInputPostThatTheRecorderWroteBeforeTheUnionStillRuns()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        // The recorder wrote every member of the old struct, with zero for the member that the kind does not use.
        Run(
            world,
            scene,
            """{"do":"post","to":{"kind":"entity","id":"probe"},"event":"InputEvent","payload":{"kind":"keyDown","code":87,"delta":[0, 0]}}""",
            """{"do":"post","to":{"kind":"entity","id":"probe"},"event":"InputEvent","payload":{"kind":"mouseMove","code":0,"delta":[3, -4]}}""",
            """{ "do": "run", "ticks": 2 }"""
        );

        ProbeOf(world, scene, "probe")
            .Seen.Select(seen => seen.Event.As<InputEvent>())
            .Should()
            .Equal(InputEvent.KeyDown(87), InputEvent.MouseMove(new(3, -4)));
    }

    [Fact]
    public void APayloadIsReadIntoTheFieldsOfItsTypeByTheirCamelCaseNames()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(
            world,
            scene,
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Aim", "payload": { "direction": [1, 2, 3] } }""",
            """{ "do": "run", "ticks": 2 }"""
        );

        ProbeOf(world, scene, "probe").Seen.Single().Event.As<Aim>().Direction.Should().Be(new Vector3(1, 2, 3));
    }

    [Fact]
    public void AnEventIsFoundByTheCSharpNameOfAPayloadTypeInAnyLoadedAssembly()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, NudgeProbe, """{ "do": "run", "ticks": 2 }""");

        ProbeOf(world, scene, "probe").Seen.Single().Event.As<Nudge>().Amount.Should().Be(1.5f);
    }

    [Fact]
    public void AnEntityFieldOfAPayloadIsReadFromASceneId()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(
            world,
            scene,
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Tag", "payload": { "other": "crew" } }""",
            """{ "do": "run", "ticks": 2 }"""
        );

        ProbeOf(world, scene, "probe").Seen.Single().Event.As<Tag>().Other.Should().Be(scene["crew"]);
    }

    [Fact]
    public void AMissingPayloadPostsTheDefaultPayload()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(
            world,
            scene,
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge" }""",
            """{ "do": "run", "ticks": 2 }"""
        );

        ProbeOf(world, scene, "probe").Seen.Single().Event.As<Nudge>().Amount.Should().Be(0);
    }

    [Theory]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Shove" }""", "Shove")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "ghost" }, "event": "Nudge" }""", "ghost")]
    [InlineData("""{ "do": "post", "to": { "kind": "frame", "id": "probe" }, "event": "Nudge" }""", "probe")]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge", "payload": { "force": 1 } }""",
        "force"
    )]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Tag", "payload": { "other": "ghost" } }""",
        "ghost"
    )]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Twin" }""", "Twin")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Label" }""", "Label")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Huge" }""", "Huge")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Fleeting" }""", "Fleeting")]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Address", "payload": { "value": 1 } }""",
        "Address"
    )]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Fixed", "payload": { "amount": 5 } }""",
        "amount"
    )]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge", "payload": { "amount": 1e39 } }""",
        "amount"
    )]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Aim", "payload": { "direction": [1e39, 0, 0] } }""",
        "direction"
    )]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Aim", "payload": { "direction": [1, 2] } }""",
        "direction"
    )]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Aim", "payload": { "point": [4, 5] } }""",
        "point"
    )]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Spin", "payload": { "attitude": [0, 0.6, 0] } }""",
        "attitude"
    )]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "InputEvent", "payload": { "kind": 4 } }""",
        "kind"
    )]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "InputEvent", "payload": { "kind": "mouseMove", "code": 7, "delta": [1.5, -2] } }""",
        "code"
    )]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "InputEvent", "payload": { "kind": "keyDown" } }""",
        "code"
    )]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "InputEvent" }""", "kind")]
    [InlineData("""{ "do": "camera", "entity": "ghost" }""", "ghost")]
    public void AStepThatCannotRunStopsTheRunBeforeTheFirstTick(string badStep, string named)
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Action run = () =>
            Run(world, scene, """{ "do": "run", "ticks": 3 }""", badStep, """{ "do": "run", "ticks": 3 }""");

        run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2").And.Contain(named);
        world.TickNumber.Should().Be(0);
    }

    [Theory]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "crew" }, "event": "Nudge" }""")]
    [InlineData("""{ "do": "post", "to": { "kind": "frame", "id": "ship" }, "event": "Nudge" }""")]
    [InlineData(
        """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Link", "payload": { "frame": "ship" } }"""
    )]
    [InlineData("""{ "do": "camera", "entity": "crew" }""")]
    public void AStepThatNamesAnEntityThatIsGoneStopsTheRunBeforeTheFirstTick(string step)
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        // The crew dies with the ship: destroying a frame moves its contents to the parent, so destroy the crew first.
        world.Destroy(scene["crew"]);
        world.Destroy(scene["ship"]);

        Action run = () => Run(world, scene, step, """{ "do": "run", "ticks": 1 }""");

        run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 1").And.Contain("not alive");
        world.TickNumber.Should().Be(0);
    }

    [Fact]
    public void AScriptBuiltInCodeIsCheckedLikeAParsedOne()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        var script = new Script("test", [new RunStep(1), new DumpStep("../escape")]);

        Action run = () => ScriptRunner.Run(script: script, world: world, scene: scene);

        run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2");
        world.TickNumber.Should().Be(0);
    }

    [Fact]
    public void EveryFieldTypeWithASpecialFormIsReadFromItsScriptForm()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(
            world,
            scene,
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Aim", "payload": { "direction": [1, 2, 3], "point": [4, 5, 6] } }""",
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Spin", "payload": { "turn": [0, 0, 0.6, 0.8], "attitude": [0, 0.6, 0, 0.8] } }""",
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Link", "payload": { "channel": "test", "frame": "ship" } }""",
            """{ "do": "run", "ticks": 2 }"""
        );

        List<(long Tick, Event Event)> seen = ProbeOf(world, scene, "probe").Seen;
        seen.Should().HaveCount(3);
        Aim aim = seen[0].Event.As<Aim>();
        aim.Direction.Should().Be(new Vector3(1, 2, 3));
        aim.Point.Should().Be(new Double3(4, 5, 6));
        Spin spin = seen[1].Event.As<Spin>();
        spin.Turn.Should().Be(new Quaternion(x: 0, y: 0, z: 0.6f, w: 0.8f));
        spin.Attitude.Should().Be(new DQuaternion(X: 0, Y: 0.6, Z: 0, W: 0.8));
        Link link = seen[2].Event.As<Link>();
        link.Channel.Should().Be(Topics.Get("test"));
        link.Frame.Should().Be(world.GetOwnedFrame(scene["ship"]));
    }

    [Fact]
    public void ACameraStepMakesTheEntityTheWorldsCamera()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, """{ "do": "camera", "entity": "crew" }""");

        world.Camera.Should().Be(scene["crew"]);
    }

    [Fact]
    public void ADumpNamesTheCameraThatTheStepsBeforeItChose()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        RunResult result = Run(
            world,
            scene,
            """{ "do": "dump", "name": "before" }""",
            """{ "do": "camera", "entity": "crew" }""",
            """{ "do": "dump", "name": "after" }"""
        );

        result.Dump("before").Camera.Should().BeNull("the probes scene sets no camera");
        result.Dump("after").Camera.Should().Be("crew");
    }

    [Fact]
    public void ACaptureWithoutARendererStopsTheRunBeforeTheFirstTick()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        string folder = NewFolder();
        try
        {
            Action run = () =>
                ScriptRunner.Run(
                    script: Script.Parse(
                        """{ "scene": "test", "steps": [ { "do": "camera", "entity": "probe" }, { "do": "capture", "name": "now" } ] }"""
                    ),
                    world: world,
                    scene: scene,
                    outputFolder: folder
                );

            run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2").And.Contain("renderer");
            world.TickNumber.Should().Be(0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ACaptureWithNoLiveCameraStopsTheRunBeforeTheFirstTick()
    {
        using World world = NewWorld();
        // The probes scene sets no camera, and no camera step comes before the capture.
        SceneInstance scene = Probes(world);

        Action run = () =>
            Run(world, scene, """{ "do": "run", "ticks": 1 }""", """{ "do": "capture", "name": "now" }""");

        run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2").And.Contain("camera");
        world.TickNumber.Should().Be(0);
    }

    [Fact]
    public void ADumpNamesNoCameraAfterTheCameraEntityIsGone()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        Run(world, scene, """{ "do": "camera", "entity": "crew" }""");
        world.Destroy(scene["crew"]);

        RunResult result = Run(world, scene, """{ "do": "dump", "name": "after" }""");

        result.Dump("after").Camera.Should().BeNull("the camera entity is not alive");
    }

    /// <summary>A playback that lets every tick run and notes the world's outline setting just before each one.</summary>
    private sealed class NotesTheOutlineSetting(World world) : IPlayback
    {
        public List<bool> BeforeEachTick { get; } = [];

        public bool WaitForTick()
        {
            BeforeEachTick.Add(world.ShowFrameOutlines);
            return true;
        }

        public bool Capture(World world, string name) =>
            throw new InvalidOperationException("no captures in this test");
    }

    [Fact]
    public void AnOutlinesStepChangesTheWorldsOutlineSettingBetweenTheTicksBeforeAndAfterIt()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        var playback = new NotesTheOutlineSetting(world);

        ScriptRunner.Run(
            script: Script.Parse(
                """
                { "scene": "test", "steps": [ { "do": "run", "ticks": 1 }, { "do": "outlines", "show": true }, { "do": "run", "ticks": 2 }, { "do": "outlines", "show": false }, { "do": "run", "ticks": 1 } ] }
                """
            ),
            world: world,
            scene: scene,
            playback: playback
        );

        playback.BeforeEachTick.Should().Equal(false, true, true, false);
    }

    [Fact]
    public void AnOutlinesStepNeedsNoCameraAndNoRenderer()
    {
        using World world = NewWorld();
        // The probes scene sets no camera, and the run has no playback.
        SceneInstance scene = Probes(world);

        Run(world, scene, """{ "do": "outlines", "show": true }""");

        world.ShowFrameOutlines.Should().BeTrue();
    }

    /// <summary>
    /// A playback that lets <paramref name="ticks"/> ticks run and then stops the run, as closing the window does. It
    /// draws no capture: it notes the tick count and the name of each one, and lets the run go on.
    /// </summary>
    private sealed class NotesTheCaptures(int ticks = int.MaxValue) : IPlayback
    {
        private int run;

        public List<(long Tick, string Name)> Captured { get; } = [];

        public bool WaitForTick() => run++ < ticks;

        public bool Capture(World world, string name)
        {
            Captured.Add((world.TickNumber, name));
            return true;
        }
    }

    /// <summary>Runs the steps on the scene with an output folder that the test deletes, and the playback.</summary>
    private static RunResult RunWithCaptures(
        World world,
        SceneInstance scene,
        string folder,
        IPlayback playback,
        params string[] steps
    ) =>
        ScriptRunner.Run(
            script: Script.Parse($$"""{ "scene": "test", "steps": [ {{string.Join(", ", steps)}} ] }"""),
            world: world,
            scene: scene,
            outputFolder: folder,
            playback: playback
        );

    [Fact]
    public void AFilmCapturesEachFrameAfterItsTicksAndNamesItByItsNumberFromZeroInSixDigits()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        string folder = NewFolder();
        var playback = new NotesTheCaptures();
        try
        {
            RunResult result = RunWithCaptures(
                world: world,
                scene: scene,
                folder: folder,
                playback: playback,
                CameraOnProbe,
                """{ "do": "run", "ticks": 1 }""",
                """{ "do": "film", "name": "climb", "frames": 3, "every": 2 }"""
            );

            // The film starts after tick 1, and its frame k comes after (k + 1) × 2 of its own ticks.
            string[] frames = ["climb-000000", "climb-000001", "climb-000002"];
            playback.Captured.Should().Equal((3, frames[0]), (5, frames[1]), (7, frames[2]));
            result.Captures.Should().Equal(frames);
            world.TickNumber.Should().Be(7, "a film of 3 frames every 2 ticks runs 6 ticks");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TwoFilmsInARowWithTheSameTicksBeforeEachFrameGiveEvenlySpacedFrames()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        string folder = NewFolder();
        var playback = new NotesTheCaptures();
        try
        {
            RunWithCaptures(
                world: world,
                scene: scene,
                folder: folder,
                playback: playback,
                CameraOnProbe,
                """{ "do": "film", "name": "a", "frames": 2, "every": 3 }""",
                """{ "do": "film", "name": "b", "frames": 2, "every": 3 }"""
            );

            playback.Captured.Select(capture => capture.Tick).Should().Equal(3, 6, 9, 12);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ACaptureAndAFilmNeedNoOutputFolderBecauseThePlaybackKnowsWhereTheirImagesGo()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        var playback = new NotesTheCaptures();

        RunResult result = ScriptRunner.Run(
            script: Script.Parse(
                $$"""
                { "scene": "test", "steps": [ {{CameraOnProbe}}, { "do": "capture", "name": "first" }, { "do": "film", "name": "climb", "frames": 1, "every": 1 } ] }
                """
            ),
            world: world,
            scene: scene,
            outputFolder: null,
            playback: playback
        );

        playback.Captured.Should().Equal((0, "first"), (1, "climb-000000"));
        result.Captures.Should().Equal("first", "climb-000000");
    }

    [Theory]
    [InlineData(false, true, "camera")]
    [InlineData(true, false, "renderer")]
    public void AFilmNeedsALiveCameraAndARendererBeforeTheFirstTick(bool cameraStep, bool hasPlayback, string named)
    {
        using World world = NewWorld();
        // The probes scene sets no camera, so only the camera step gives the film one.
        SceneInstance scene = Probes(world);
        string folder = NewFolder();
        try
        {
            string first = cameraStep ? CameraOnProbe : """{ "do": "dump", "name": "start" }""";
            var script = Script.Parse(
                $$"""{ "scene": "test", "steps": [ {{first}}, { "do": "run", "ticks": 1 }, { "do": "film", "name": "climb", "frames": 2, "every": 1 } ] }"""
            );

            Action run = () =>
                ScriptRunner.Run(
                    script: script,
                    world: world,
                    scene: scene,
                    outputFolder: folder,
                    playback: hasPlayback ? new NotesTheCaptures() : null
                );

            run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 3 (film)").And.Contain(named);
            world.TickNumber.Should().Be(0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void APlaybackThatStopsDuringTheTicksOfAFilmEndsTheRunWithTheFramesBeforeIt()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        string folder = NewFolder();
        try
        {
            RunResult result = RunWithCaptures(
                world: world,
                scene: scene,
                folder: folder,
                playback: new NotesTheCaptures(ticks: 5),
                CameraOnProbe,
                """{ "do": "film", "name": "climb", "frames": 5, "every": 2 }""",
                """{ "do": "dump", "name": "after" }"""
            );

            result.Stopped.Should().BeTrue();
            world.TickNumber.Should().Be(5);
            result.Captures.Should().Equal("climb-000000", "climb-000001");
            result.Dumps.Should().BeEmpty("the steps after the film do not run");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void APlaybackThatStopsDuringACaptureOfAFilmEndsTheRun()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        string folder = NewFolder();
        try
        {
            RunResult result = RunWithCaptures(
                world: world,
                scene: scene,
                folder: folder,
                playback: new ClosesDuringCaptures(),
                CameraOnProbe,
                """{ "do": "film", "name": "climb", "frames": 3, "every": 2 }""",
                """{ "do": "dump", "name": "after" }"""
            );

            result.Stopped.Should().BeTrue();
            world.TickNumber.Should().Be(2, "the first frame waits for its capture after 2 ticks");
            result.Captures.Should().BeEmpty("the capture did not finish");
            result.Dumps.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A playback that lets a number of ticks run and then stops the run, as closing the window does.</summary>
    private sealed class StopsAfter(int ticks) : IPlayback
    {
        private int run;

        public bool WaitForTick() => run++ < ticks;

        public bool Capture(World world, string name) =>
            throw new InvalidOperationException("no captures in this test");
    }

    [Fact]
    public void APlaybackThatStopsEndsTheRunAndTheStepsAfterItDoNotRun()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        RunResult result = ScriptRunner.Run(
            script: Script.Parse(
                """
                { "scene": "test", "steps": [ { "do": "run", "ticks": 2 }, { "do": "dump", "name": "a" }, { "do": "run", "ticks": 5 }, { "do": "dump", "name": "b" } ] }
                """
            ),
            world: world,
            scene: scene,
            playback: new StopsAfter(4)
        );

        result.Stopped.Should().BeTrue();
        world.TickNumber.Should().Be(4);
        result.Dumps.Select(d => d.Name).Should().Equal("a");
    }

    [Fact]
    public void ARunThatEndsAfterItsLastStepWasNotStopped()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, """{ "do": "run", "ticks": 2 }""").Stopped.Should().BeFalse();
    }

    /// <summary>A playback whose ticks all run and whose captures report that the window closed while they waited.</summary>
    private sealed class ClosesDuringCaptures : IPlayback
    {
        public bool WaitForTick() => true;

        public bool Capture(World world, string name) => false;
    }

    [Fact]
    public void APlaybackThatStopsDuringACaptureEndsTheRunWithTheFilesBeforeIt()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        string folder = NewFolder();
        try
        {
            RunResult result = ScriptRunner.Run(
                script: Script.Parse(
                    """
                    { "scene": "test", "steps": [ { "do": "camera", "entity": "probe" }, { "do": "dump", "name": "a" }, { "do": "capture", "name": "b" }, { "do": "run", "ticks": 3 }, { "do": "dump", "name": "c" } ] }
                    """
                ),
                world: world,
                scene: scene,
                outputFolder: folder,
                playback: new ClosesDuringCaptures()
            );

            result.Stopped.Should().BeTrue();
            result.Dumps.Select(d => d.Name).Should().Equal("a");
            result.Captures.Should().BeEmpty("the capture did not finish");
            world.TickNumber.Should().Be(0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
