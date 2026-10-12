using System.Numerics;
using System.Text.Json;
using Silk.NET.Input;
using Tertius.Host.Scripting;
using Tertius.Host.Windowing;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// A window with no window. It carries the calls of the <see cref="InputRouter"/> to the <see cref="FreePlay"/> as the
/// window host does, and it stands in for the wall clock: it lets a fixed number of ticks run, back to back. Before each
/// tick it runs the actions that the test set for that tick, as the main thread does between ticks.
/// </summary>
internal sealed class TestWindow : IInputTarget, IPlayback
{
    private readonly SortedDictionary<int, List<Action<InputRouter>>> actions = new();
    private readonly int ticks;
    private int started;
    private bool closed;

    public TestWindow(int ticks)
    {
        this.ticks = ticks;
        Router = new InputRouter(freePlay: true, this);
    }

    public InputRouter Router { get; }

    /// <summary>The free play that the window feeds. Set it before the first tick.</summary>
    public FreePlay? FreePlay { get; set; }

    /// <summary>Runs after each tick, at the start of the wait for the next one.</summary>
    public Action? AfterTick { get; set; }

    public List<bool> FlyChanges { get; } = new();

    /// <summary>Runs <paramref name="act"/> on the router before the tick that starts when <paramref name="tick"/> ticks have run.</summary>
    public TestWindow At(int tick, Action<InputRouter> act)
    {
        if (!actions.TryGetValue(tick, out List<Action<InputRouter>>? list))
            actions.Add(tick, list = new List<Action<InputRouter>>());
        list.Add(act);
        return this;
    }

    public void Post(InputEvent e) => FreePlay!.Post(e);
    public void Fly(FlyKey key, bool down) { }
    public void Turn(Vector2 movement) { }
    public void FlyChanged(bool flying) => FlyChanges.Add(flying);
    public void Close() => closed = true;
    public void NextView() => FreePlay!.NextView();

    public bool WaitForTick()
    {
        if (started > 0)
            AfterTick?.Invoke();
        if (closed || started >= ticks)
            return false;
        if (actions.TryGetValue(started, out List<Action<InputRouter>>? list))
            foreach (Action<InputRouter> act in list)
                act(Router);
        // The window closed during the wait, as when Esc ends the wait for the next tick.
        if (closed)
            return false;
        started++;
        return true;
    }

    public bool Capture(World world, string path) => throw new InvalidOperationException("Free play captures nothing.");
}

/// <summary>FreePlay: what the window's input does to the world between two ticks, and the script that records it.</summary>
public sealed class FreePlayTests : IDisposable
{
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string RecordPath => Path.Combine(folder, "record.json");

    private static readonly ScriptWindow Size = new(1280, 720);

    /// <summary>
    /// A scene with no physics bodies. The root frame <c>space</c> holds <c>probe</c>, which hears the <c>input</c> topic,
    /// and the two views <c>eye-a</c>, the camera at the start, and <c>eye-b</c>.
    /// </summary>
    private static readonly Scene InputProbeScene = new("input-probe", (world, _) =>
    {
        var scene = new SceneInstance();
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        scene.Add("space", space);
        Entity probe = world.Spawn(new SpawnDesc { Frame = space, Components = [new Probe()] });
        world.Subscribe(probe, Topics.Input);
        scene.Add("probe", probe);
        scene.Add("eye-a", world.Spawn(new SpawnDesc { Frame = space }));
        scene.Add("eye-b", world.Spawn(new SpawnDesc { Frame = space }));
        scene.AddView("eye-a");
        scene.AddView("eye-b");
        world.Camera = scene["eye-a"];
        return scene;
    });

    /// <summary>The scene <see cref="InputProbeScene"/> in a world on the real Jolt backend.</summary>
    private static (World World, SceneInstance Scene) InputProbe()
    {
        World world = NewJoltWorld();
        return (world, InputProbeScene.Build(world, RenderResources.None));
    }

    /// <summary>Plays <paramref name="window"/>'s ticks in free play on the probe scene, recording to <see cref="RecordPath"/>.</summary>
    private FreePlay Play(World world, SceneInstance scene, TestWindow window, bool record = true)
    {
        var freePlay = new FreePlay(world, scene, record ? new RecordTo(RecordPath, "input-probe", Size) : null);
        window.FreePlay = freePlay;
        freePlay.Run(window);
        return freePlay;
    }

    private static Script Written(string path) => Script.Parse(File.ReadAllText(path));

    /// <summary>The (tick, code) of each post step of a script: the number of ticks that run before the post, and the key code.</summary>
    private static (long Tick, int Code)[] PostsOf(Script script)
    {
        var posts = new List<(long, int)>();
        long tick = 0;
        foreach (ScriptStep step in script.Steps)
        {
            if (step is RunStep run)
                tick += run.Ticks;
            else if (step is PostStep post)
                posts.Add((tick, post.Payload!.Value.Deserialize<InputEvent>(ScriptJson.Scripts).Code));
        }
        return posts.ToArray();
    }

    private static (long Tick, int Code)[] SeenInput(World world, SceneInstance scene) =>
        ProbeOf(world, scene, "probe").Seen.Select(seen => (seen.Tick, seen.Event.As<InputEvent>().Code)).ToArray();

    [Fact]
    public void AKeyThatTheWindowGetsBeforeATickReachesTheWorldDuringTheTickAfterThat()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var window = new TestWindow(6).At(3, router => router.Key(Key.W, down: true));

            Play(world, scene, window, record: false);

            // Posted before tick 3 (3 ticks done), routed at the end of tick 3, handled during tick 4 (4 ticks done).
            SeenInput(world, scene).Should().Equal((4L, (int)Key.W));
        }
    }

    [Fact]
    public void WithoutARecordNoFileIsWritten()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var window = new TestWindow(3).At(1, router => router.Key(Key.W, down: true));

            FreePlay freePlay = Play(world, scene, window, record: false);

            File.Exists(RecordPath).Should().BeFalse();
            freePlay.RecordFailure.Should().BeNull();
            freePlay.RecordWritten.Should().BeFalse();
        }
    }

    [Fact]
    public void TheRecordNamesTheTickBeforeWhichEachEventWasPosted()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var window = new TestWindow(12)
                .At(3, router => router.Key(Key.W, down: true))
                .At(10, router => router.Key(Key.W, down: false));

            FreePlay freePlay = Play(world, scene, window);

            freePlay.RecordFailure.Should().BeNull();
            freePlay.RecordWritten.Should().BeTrue();
            Script script = Written(RecordPath);
            script.Scene.Should().Be("input-probe");
            script.Window.Should().Be(Size);
            ScriptRecorderTests.Describe(script).Should().Equal(
                "camera eye-a",
                "run 3",
                "post input InputEvent keyDown 87",
                "run 7",
                "post input InputEvent keyUp 87",
                "run 2",
                "dump end");
        }
    }

    [Fact]
    public void AReplayHandlesEveryEventOnTheSameTickAsTheSessionDid()
    {
        (World session, SceneInstance sessionScene) = InputProbe();
        using (session)
        {
            var window = new TestWindow(22)
                .At(0, router => router.Key(Key.W, down: true))
                .At(7, router => router.Key(Key.A, down: true))
                .At(7, router => router.MouseButton(MouseButton.Left, down: true))
                .At(19, router => router.Key(Key.W, down: false));
            Play(session, sessionScene, window);

            using World replay = NewJoltWorld();
            SceneInstance replayScene = InputProbeScene.Build(replay, RenderResources.None);
            ScriptRunner.Run(Written(RecordPath), replay, replayScene);

            SeenInput(replay, replayScene).Should().Equal(SeenInput(session, sessionScene));
            // Two events from one gap between ticks reach the world together, and the replay handles them on the same tick.
            SeenInput(session, sessionScene).Should().Equal((1L, (int)Key.W), (8L, (int)Key.A), (8L, (int)MouseButton.Left), (20L, (int)Key.W));
        }
    }

    [Fact]
    public void TheViewKeyIsACameraStepBeforeTheTickThatSawTheNewCamera()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var window = new TestWindow(5).At(2, router =>
            {
                router.Key(Key.V, down: true);
                router.Key(Key.V, down: false);
            });

            Play(world, scene, window);

            world.Camera.Should().Be(scene["eye-b"]);
            ScriptRecorderTests.Describe(Written(RecordPath)).Should().Equal("camera eye-a", "run 2", "camera eye-b", "run 3", "dump end");
            SeenInput(world, scene).Should().BeEmpty("the game never sees the view key");
        }
    }

    [Fact]
    public void EveryViewSwitchIsRecordedWhenTwoFallBetweenTheSameTicks()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var window = new TestWindow(4).At(2, router =>
            {
                router.Key(Key.V, down: true);
                router.Key(Key.V, down: true);
            });

            Play(world, scene, window);

            ScriptRecorderTests.Describe(Written(RecordPath)).Should().Equal("camera eye-a", "run 2", "camera eye-b", "camera eye-a", "run 2", "dump end");
        }
    }

    [Fact]
    public void ACameraThatTheSceneDoesNotNameCannotBeAStepSoTheRecordLeavesItOut()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            world.Camera = world.Spawn(new SpawnDesc { Frame = world.GetOwnedFrame(scene["space"]) });

            Play(world, scene, new TestWindow(2));

            ScriptRecorderTests.Describe(Written(RecordPath)).Should().Equal("run 2", "dump end");
        }
    }

    [Fact]
    public void AWorldWithNoCameraGivesARecordWithNoCameraStep()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            world.Camera = Entity.Null;

            Play(world, scene, new TestWindow(2));

            ScriptRecorderTests.Describe(Written(RecordPath)).Should().Equal("run 2", "dump end");
        }
    }

    [Fact]
    public void TheFlyCameraChangesNothingInTheWorldSoItLeavesNoStepInTheRecord()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var window = new TestWindow(6)
                .At(1, router => router.Key(Key.F1, down: true))
                .At(2, router =>
                {
                    router.MouseMove(new Vector2(10, 10));
                    router.MouseMove(new Vector2(30, 5));
                    router.Key(Key.W, down: true);
                    router.Key(Key.W, down: false);
                })
                .At(3, router => router.Key(Key.F1, down: true));

            Play(world, scene, window);

            window.FlyChanges.Should().Equal(true, false);
            ScriptRecorderTests.Describe(Written(RecordPath)).Should().Equal("camera eye-a", "run 6", "dump end");
        }
    }

    [Fact]
    public void SwitchingTheFlyCameraOnRecordsTheReleasesThatTheGameGets()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var window = new TestWindow(5)
                .At(1, router => router.Key(Key.W, down: true))
                .At(2, router => router.Key(Key.F1, down: true));

            Play(world, scene, window);

            ScriptRecorderTests.Describe(Written(RecordPath)).Should().Equal(
                "camera eye-a",
                "run 1",
                "post input InputEvent keyDown 87",
                "run 1",
                "post input InputEvent keyUp 87",
                "run 3",
                "dump end");
        }
    }

    [Fact]
    public void EscapeClosesTheWindowAndTheRecordIsWrittenWithTheTicksThatRan()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var window = new TestWindow(1000)
                .At(2, router => router.Key(Key.W, down: true))
                .At(5, router => router.Key(Key.Escape, down: true));

            Play(world, scene, window);

            world.TickNumber.Should().Be(5);
            ScriptRecorderTests.Describe(Written(RecordPath)).Should().Equal(
                "camera eye-a", "run 2", "post input InputEvent keyDown 87", "run 3", "dump end");
        }
    }

    [Fact]
    public void ARunThatEndsWithAnExceptionStillWritesTheRecordUpToTheLastTickThatCompleted()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var window = new TestWindow(100)
                .At(2, router => router.Key(Key.W, down: true))
                .At(7, _ => throw new InvalidOperationException("the run broke on purpose"));

            Action play = () => Play(world, scene, window);

            play.Should().Throw<InvalidOperationException>().WithMessage("*on purpose*");
            ScriptRecorderTests.Describe(Written(RecordPath)).Should().Equal(
                "camera eye-a", "run 2", "post input InputEvent keyDown 87", "run 5", "dump end");
        }
    }

    [Fact]
    public void AFileThatCannotBeWrittenIsReportedAndTheRunStillEndsNormally()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            string taken = Path.Combine(folder, "taken");
            File.WriteAllText(taken, "a file, not a folder");
            var window = new TestWindow(3);
            var freePlay = new FreePlay(world, scene, new RecordTo(Path.Combine(taken, "record.json"), "input-probe", Size));
            window.FreePlay = freePlay;

            freePlay.Run(window);

            world.TickNumber.Should().Be(3);
            freePlay.RecordFailure.Should().NotBeNull();
            freePlay.RecordFailure.Should().BeAssignableTo<IOException>();
            freePlay.RecordWritten.Should().BeFalse();
        }
    }

    [Fact]
    public void ARecordIntoAFolderThatDoesNotExistMakesTheFolder()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            string path = Path.Combine(folder, "records", "today", "record.json");
            var window = new TestWindow(2);
            var freePlay = new FreePlay(world, scene, new RecordTo(path, "input-probe", Size));
            window.FreePlay = freePlay;

            freePlay.Run(window);

            freePlay.RecordFailure.Should().BeNull();
            File.Exists(path).Should().BeTrue();
        }
    }

    /// <summary>Plays three ticks of the probe scene, recording to <paramref name="path"/>.</summary>
    private static (RecordTo Record, FreePlay FreePlay) PlayThreeTicks(string path)
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var record = new RecordTo(path, "input-probe", Size);
            var window = new TestWindow(3);
            var freePlay = new FreePlay(world, scene, record);
            window.FreePlay = freePlay;
            freePlay.Run(window);
            return (record, freePlay);
        }
    }

    [Fact]
    public void ARecordThatWasWrittenIsReportedByItsPathOnStandardOutputAndLeavesTheExitCodeAlone()
    {
        (RecordTo record, FreePlay freePlay) = PlayThreeTicks(RecordPath);
        var output = new StringWriter();
        var error = new StringWriter();

        int code = WindowHost.ReportRecord(record, freePlay, ScriptCommand.Ok, output, error);

        code.Should().Be(ScriptCommand.Ok);
        output.ToString().Should().Be(RecordPath + Environment.NewLine);
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public void ARecordThatCannotBeWrittenIsReportedOnStandardErrorWithTheFileErrorCode()
    {
        string taken = Path.Combine(folder, "taken");
        File.WriteAllText(taken, "a file, not a folder");
        (RecordTo record, FreePlay freePlay) = PlayThreeTicks(Path.Combine(taken, "record.json"));
        var output = new StringWriter();
        var error = new StringWriter();

        int code = WindowHost.ReportRecord(record, freePlay, ScriptCommand.Ok, output, error);

        code.Should().Be(ScriptCommand.FileError);
        error.ToString().Should().Contain("Cannot write the record").And.Contain("taken");
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public void ARecordThatCannotBeWrittenDoesNotHideTheExitCodeOfARunThatFailedForAnotherReason()
    {
        string taken = Path.Combine(folder, "taken");
        File.WriteAllText(taken, "a file, not a folder");
        (RecordTo record, FreePlay freePlay) = PlayThreeTicks(Path.Combine(taken, "record.json"));
        var error = new StringWriter();

        int code = WindowHost.ReportRecord(record, freePlay, ScriptCommand.InternalError, new StringWriter(), error);

        code.Should().Be(ScriptCommand.InternalError);
        error.ToString().Should().Contain("Cannot write the record");
    }

    [Fact]
    public async Task InputFromAnotherThreadIsRecordedAtTheTickBeforeWhichTheWorldGotIt()
    {
        (World world, SceneInstance scene) = InputProbe();
        using (world)
        {
            var freePlay = new FreePlay(world, scene, new RecordTo(RecordPath, "input-probe", Size));
            // The main thread of the window posts while the simulation thread ticks.
            Task poster = Task.Run(() =>
            {
                for (int code = 0; code < 1500; code++)
                {
                    freePlay.Post(new InputEvent { Kind = InputKind.KeyDown, Code = code });
                    if (code % 100 == 99)
                        Thread.Sleep(1);
                }
            }, TestContext.Current.CancellationToken);

            freePlay.Run(new UntilPosterIsDone(poster));
            await poster;

            (long Tick, int Code)[] recorded = PostsOf(Written(RecordPath));
            recorded.Select(post => post.Code).Should().Equal(Enumerable.Range(0, 1500), "every event arrives, in the order it was posted");
            recorded.Select(post => post.Tick).Distinct().Count().Should().BeGreaterThan(1, "the events arrived during many different ticks");
            SeenInput(world, scene).Should().Equal(recorded.Select(post => (post.Tick + 1, post.Code)),
                "the world handles each event one tick after the tick that the record names");
        }
    }

    /// <summary>Lets ticks run back to back until the poster is done, and three more, so that the last events are delivered and handled.</summary>
    private sealed class UntilPosterIsDone(Task poster) : IPlayback
    {
        private int after;

        public bool WaitForTick() => !poster.IsCompleted || after++ < 3;

        public bool Capture(World world, string path) => throw new InvalidOperationException("Free play captures nothing.");
    }
}

/// <summary>
/// The acceptance test of the record: a session in the orbit scene, driven through the router as the window does, is
/// written to a file, and the file replays headless to the state that the session ended in.
/// </summary>
public sealed class FreePlayReplayTests : IDisposable
{
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static (World World, SceneInstance Scene) Orbit()
    {
        World world = NewJoltWorld();
        return (world, Tertius.Demo.DemoScenes.All.Single(s => s.Name == "orbit").Build(world, RenderResources.None));
    }

    private static string Json(StateDump dump, SceneInstance scene) => JsonSerializer.Serialize(dump, ScriptJson.Dumps(scene));

    [Fact]
    public void AReplayOfARecordedOrbitSessionEndsInTheStateOfTheSession()
    {
        string path = Path.Combine(folder, "session.json");
        (World session, SceneInstance scene) = Orbit();
        using (session)
        {
            var dumps = new DumpRecorder(session, scene, EventPayloads.FromLoadedAssemblies());
            StateDump start = dumps.Take("start");
            var window = new TestWindow(300) { AfterTick = dumps.RecordRoutedEvents }
                // The view key goes from the chase view to the far view, and from there to the person's view.
                .At(5, router => Tap(router, Key.V))
                .At(10, router => Tap(router, Key.V))
                .At(12, router =>
                {
                    router.MouseMove(new Vector2(100, 100));
                    router.MouseMove(new Vector2(160, 90));
                })
                .At(20, router => router.Key(Key.W, down: true))
                .At(60, router => router.Key(Key.Space, down: true))
                .At(62, router => router.Key(Key.Space, down: false))
                .At(100, router => Tap(router, Key.E))
                .At(120, router => router.MouseButton(MouseButton.Left, down: true))
                .At(125, router => router.MouseButton(MouseButton.Left, down: false))
                .At(150, router => router.MouseMove(new Vector2(200, 140)))
                .At(200, router => router.Key(Key.W, down: false))
                .At(210, router => router.Key(Key.S, down: true))
                // The fly camera is on from tick 220 to tick 240. Turning it on lets go of S, and nothing else reaches the game.
                .At(220, router => router.Key(Key.F1, down: true))
                .At(230, router => router.MouseMove(new Vector2(300, 300)))
                .At(240, router => router.Key(Key.F1, down: true))
                // Two presses between two ticks: from the person's view past the chase view to the far view.
                .At(250, router =>
                {
                    Tap(router, Key.V);
                    Tap(router, Key.V);
                })
                .At(270, router => router.Scroll(new Vector2(0, 1)));
            var freePlay = new FreePlay(session, scene, new RecordTo(path, "orbit", ScriptWindow.Default));
            window.FreePlay = freePlay;

            freePlay.Run(window);

            freePlay.RecordFailure.Should().BeNull();
            StateDump sessionEnd = dumps.Take("end");
            sessionEnd.Tick.Should().Be(300);
            sessionEnd.Camera.Should().Be("far");
            Double3.Distance(sessionEnd.Entity("person").Pose.Position, start.Entity("person").Pose.Position)
                .Should().BeGreaterThan(3, "the session moved the person, so that the replay has something to get wrong");

            Script script = Script.Load(path);
            script.Steps.OfType<CameraStep>().Select(step => step.Entity).Should().Equal("chase", "far", "person", "chase", "far");
            script.Steps.OfType<PostStep>().Count().Should().BeGreaterThan(10);

            using World replay = NewJoltWorld();
            SceneInstance replayScene = Tertius.Demo.DemoScenes.All.Single(s => s.Name == "orbit").Build(replay, RenderResources.None);
            StateDump replayEnd = ScriptRunner.Run(script, replay, replayScene).Dump("end");

            Json(replayEnd, replayScene).Should().Be(Json(sessionEnd, scene));
        }
    }

    private static void Tap(InputRouter router, Key key)
    {
        router.Key(key, down: true);
        router.Key(key, down: false);
    }
}
