using System.Numerics;
using Tertius.Host.Windowing;
using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>TickPacer: the wall-clock pace of the ticks in the window, and the interpolation alpha.</summary>
public class TickPacerTests
{
    [Theory]
    [InlineData(1, 0.02)]
    [InlineData(0.5, 0.04)]
    [InlineData(2, 0.01)]
    public void ATickLastsItsSimulationTimeDividedByTheSpeedOnTheWallClock(double speed, double seconds)
    {
        new TickPacer(0.02, speed).SecondsPerTick.Should().BeApproximately(seconds, 1e-15);
    }

    [Fact]
    public void EachTickStartsOneTickLengthAfterThePreviousOne()
    {
        var pacer = new TickPacer(0.02, 1);

        pacer.StartOf(0).Should().Be(0);
        pacer.StartOf(3).Should().BeApproximately(0.06, 1e-15);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.01, 0.5)]
    [InlineData(0.05, 1)]
    [InlineData(-0.01, 0)]
    public void TheAlphaGrowsFromTheNewestSnapshotOverOneTickAndStopsAtOne(double secondsSincePublish, float alpha)
    {
        new TickPacer(0.02, 1).Alpha(secondsSincePublish).Should().BeApproximately(alpha, 1e-6f);
    }

    [Fact]
    public void TheAlphaFollowsTheWallClockLengthOfATick()
    {
        // At half speed, a tick lasts 0.04 s, so 0.01 s after a snapshot is a quarter of the way.
        new TickPacer(0.02, 0.5).Alpha(0.01).Should().BeApproximately(0.25f, 1e-6f);
    }

    [Theory]
    [InlineData(0.02, 0)]
    [InlineData(0.02, -1)]
    [InlineData(0.02, double.NaN)]
    [InlineData(0.02, double.PositiveInfinity)]
    [InlineData(0, 1)]
    public void ASpeedOrATickLengthThatIsNotPositiveAndFiniteIsRefused(double tickSeconds, double speed)
    {
        Action make = () => new TickPacer(tickSeconds, speed);

        make.Should().Throw<ArgumentOutOfRangeException>();
    }
}

/// <summary>FlyCamera: the debug camera that F1 switches to.</summary>
public class FlyCameraTests
{
    private static Vector3 Moved(FlyCamera camera, FlyKey key, float seconds = 1)
    {
        camera.SetKey(key, true);
        camera.Advance(seconds);
        camera.SetKey(key, false);
        return camera.Position;
    }

    private static void ExpectNear(Vector3 actual, Vector3 expected) =>
        Vector3.Distance(actual, expected).Should().BeLessThan(1e-4f, $"{actual} is not {expected}");

    [Fact]
    public void ItStartsAtTheWorldsCameraWithItsRotation()
    {
        var camera = new FlyCamera();
        Quaternion turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);

        camera.Start(turned);

        camera.Position.Should().Be(Vector3.Zero);
        camera.Rotation.Should().Be(turned);
    }

    [Fact]
    public void HoldingForwardForOneSecondMovesFiveMetersAlongTheView()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity);

        ExpectNear(Moved(camera, FlyKey.Forward), new Vector3(0, 0, -5));
    }

    [Fact]
    public void ItMovesInTheDirectionOfTheWorldCamerasRotation()
    {
        var camera = new FlyCamera();
        // Turned 90 degrees about +Y, the view looks along -X.
        camera.Start(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));

        ExpectNear(Moved(camera, FlyKey.Forward), new Vector3(-5, 0, 0));
    }

    [Fact]
    public void TheFastKeyMovesTenTimesFaster()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity);
        camera.SetKey(FlyKey.Fast, true);

        ExpectNear(Moved(camera, FlyKey.Right), new Vector3(50, 0, 0));
    }

    [Fact]
    public void MovingTheMouseRightTurnsTheViewRight()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity);

        camera.Turn(new Vector2(MathF.PI / 2 / FlyCamera.TurnPerPixel, 0));

        ExpectNear(Moved(camera, FlyKey.Forward), new Vector3(5, 0, 0));
    }

    [Fact]
    public void UpAndDownMoveAlongTheFramesUpAxisWhereverTheViewLooks()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity);
        camera.Turn(new Vector2(0, MathF.PI / 4 / FlyCamera.TurnPerPixel));

        ExpectNear(Moved(camera, FlyKey.Up), new Vector3(0, 5, 0));
        ExpectNear(Moved(camera, FlyKey.Down), Vector3.Zero);
    }

    [Fact]
    public void TheViewStopsShortOfStraightDown()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity);

        camera.Turn(new Vector2(0, 100_000));

        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, camera.Rotation);
        forward.Y.Should().BeLessThan(-0.99f, "the view looks almost straight down");
        forward.Z.Should().BeLessThan(0, "the view does not flip over backwards");
    }

    [Fact]
    public void ApplyingItSetsThePresentAndThePreviousPoseSoNothingInterpolates()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity);
        Moved(camera, FlyKey.Left);
        var render = new RenderCamera { Position = Vector3.One, PreviousPosition = Vector3.UnitX, VerticalFov = 1.1f };

        camera.Apply(ref render);

        render.Position.Should().Be(camera.Position);
        render.PreviousPosition.Should().Be(camera.Position);
        render.Rotation.Should().Be(camera.Rotation);
        render.PreviousRotation.Should().Be(camera.Rotation);
        render.VerticalFov.Should().Be(1.1f);
    }
}

/// <summary>More FlyCamera behavior: every key, other frame times, combined turns, restarts and frame changes.</summary>
public class FlyCameraMotionTests
{
    private static void ExpectNear(Vector3 actual, Vector3 expected) =>
        Vector3.Distance(actual, expected).Should().BeLessThan(1e-3f, $"{actual} is not {expected}");

    private static Vector3 Held(FlyCamera camera, FlyKey key, float seconds)
    {
        camera.SetKey(key, true);
        camera.Advance(seconds);
        camera.SetKey(key, false);
        return camera.Position;
    }

    [Theory]
    [InlineData(FlyKey.Forward, 0, 0, -5)]
    [InlineData(FlyKey.Back, 0, 0, 5)]
    [InlineData(FlyKey.Left, -5, 0, 0)]
    [InlineData(FlyKey.Right, 5, 0, 0)]
    [InlineData(FlyKey.Up, 0, 5, 0)]
    [InlineData(FlyKey.Down, 0, -5, 0)]
    public void EachMovementKeyMovesFiveMetersASecondAlongItsAxis(FlyKey key, float x, float y, float z)
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity);

        ExpectNear(Held(camera, key, 1), new Vector3(x, y, z));
    }

    [Fact]
    public void AQuarterSecondMovesAQuarterAsFar()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity);

        ExpectNear(Held(camera, FlyKey.Forward, 0.25f), new Vector3(0, 0, -1.25f));
    }

    [Fact]
    public void ATurnRightAppliesAboutTheFramesUpAxisAfterTheStartRotation()
    {
        var camera = new FlyCamera();
        // The start looks 30 degrees down. A turn right by 90 degrees about the frame's +Y then takes the view's right,
        // which stays level, from +X to +Z.
        camera.Start(Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 6));
        camera.Turn(new Vector2(MathF.PI / 2 / FlyCamera.TurnPerPixel, 0));

        ExpectNear(Held(camera, FlyKey.Right, 1), new Vector3(0, 0, 5));
    }

    [Fact]
    public void AYawAndAPitchTogetherPointTheViewBetweenThem()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity);
        // Right 90 degrees and down 45 degrees: the view looks along +X and down, (0.707, -0.707, 0).
        camera.Turn(new Vector2(MathF.PI / 2 / FlyCamera.TurnPerPixel, MathF.PI / 4 / FlyCamera.TurnPerPixel));

        ExpectNear(Held(camera, FlyKey.Forward, 1), new Vector3(3.5355f, -3.5355f, 0));
    }

    [Fact]
    public void StartingAgainForgetsThePositionTheTurnAndTheKeys()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity);
        camera.SetKey(FlyKey.Forward, true);
        camera.Advance(1);
        camera.Turn(new Vector2(100, 50));
        Quaternion again = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f);

        camera.Start(again);

        camera.Position.Should().Be(Vector3.Zero);
        camera.Rotation.Should().Be(again);
        camera.Advance(1);
        camera.Position.Should().Be(Vector3.Zero, "no key is held after a start");
    }

    [Fact]
    public void FollowingTheWorldCameraInTheSameFrameKeepsTheFlyCameraWhereItIs()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity, frame: 7);
        Held(camera, FlyKey.Forward, 1);

        camera.Follow(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1), frame: 7);

        ExpectNear(camera.Position, new Vector3(0, 0, -5));
        camera.Rotation.Should().Be(Quaternion.Identity);
    }

    [Fact]
    public void FollowingTheWorldCameraIntoAnotherFrameRestartsTheFlyCameraThere()
    {
        var camera = new FlyCamera();
        camera.Start(Quaternion.Identity, frame: 7);
        Held(camera, FlyKey.Forward, 1);
        Quaternion inNewFrame = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1);

        camera.Follow(inNewFrame, frame: 8);

        camera.Position.Should().Be(Vector3.Zero);
        camera.Rotation.Should().Be(inNewFrame);
        camera.Frame.Should().Be(8);
    }
}

/// <summary>MouseTracker: mouse movements from the positions that the window reports.</summary>
public class MouseTrackerTests
{
    [Fact]
    public void TheFirstPositionMovesNothingAndEachLaterOneMovesByTheDifference()
    {
        var mouse = new MouseTracker();

        mouse.MoveTo(new Vector2(100, 50)).Should().Be(Vector2.Zero);
        mouse.MoveTo(new Vector2(103, 46)).Should().Be(new Vector2(3, -4));
    }

    [Fact]
    public void AfterForgettingThePositionTheNextOneMovesNothing()
    {
        var mouse = new MouseTracker();
        mouse.MoveTo(new Vector2(100, 50));

        mouse.Forget();

        mouse.MoveTo(new Vector2(900, 700)).Should().Be(Vector2.Zero);
    }
}

/// <summary>CaptureMailbox: the handover of a capture from the simulation thread to the render thread.</summary>
public class CaptureMailboxTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private static CancellationToken TestStop => TestContext.Current.CancellationToken;

    private static string TakeWithin(CaptureMailbox mailbox)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string path;
        while (!mailbox.TryTake(out path))
        {
            clock.Elapsed.Should().BeLessThan(Bound, "the request must arrive");
            Thread.Yield();
        }
        return path;
    }

    /// <summary>True if the task ends within <paramref name="time"/>.</summary>
    private static async Task<bool> EndsWithin(Task task, TimeSpan time) =>
        await Task.WhenAny(task, Task.Delay(time, TestStop)) == task;

    [Fact]
    public async Task ARequestWaitsUntilTheRenderThreadHasSavedTheCapture()
    {
        var mailbox = new CaptureMailbox();
        Task request = Task.Run(() => mailbox.RequestAndWait("x.png", CancellationToken.None), TestStop);

        TakeWithin(mailbox).Should().Be("x.png");
        (await EndsWithin(request, TimeSpan.FromMilliseconds(100))).Should().BeFalse("the capture is taken but not saved yet");

        mailbox.Done();
        await request.WaitAsync(Bound, TestStop);
    }

    [Fact]
    public async Task ARequestIsTakenOnce()
    {
        var mailbox = new CaptureMailbox();
        Task request = Task.Run(() => mailbox.RequestAndWait("x.png", CancellationToken.None), TestStop);
        TakeWithin(mailbox);

        mailbox.TryTake(out _).Should().BeFalse();
        mailbox.Done();
        await request.WaitAsync(Bound, TestStop);
    }

    [Fact]
    public void WithNoRequestThereIsNothingToTake()
    {
        new CaptureMailbox().TryTake(out _).Should().BeFalse();
    }

    [Fact]
    public async Task ARequestStopsWaitingWhenTheWindowCloses()
    {
        var mailbox = new CaptureMailbox();
        using var closing = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Task request = Task.Run(() => mailbox.RequestAndWait("x.png", closing.Token), TestStop);

        await FluentActions.Awaiting(() => request.WaitAsync(Bound, TestStop)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ARequestThatTheWindowCancelsBeforeItIsTakenIsTakenBack()
    {
        var mailbox = new CaptureMailbox();
        using var closing = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        Task request = Task.Run(() => mailbox.RequestAndWait("x.png", closing.Token), TestStop);
        await FluentActions.Awaiting(() => request.WaitAsync(Bound, TestStop)).Should().ThrowAsync<OperationCanceledException>();

        mailbox.TryTake(out _).Should().BeFalse("the render thread must not draw a capture that nobody waits for");
    }
}

/// <summary>WindowPlayback: the pace of a script in the window, on a clock that the test controls.</summary>
public class WindowPlaybackTests
{
    [Fact]
    public void TheFirstTickRunsAtOnce()
    {
        var reads = new List<double>();
        var playback = new WindowPlayback(new TickPacer(0.02, 1), new CaptureMailbox(), CancellationToken.None, Clock(reads, 0));

        playback.WaitForTick().Should().BeTrue();
        reads.Should().Equal(0);
    }

    [Fact]
    public void ALaterTickWaitsForItsStartTime()
    {
        var reads = new List<double>();
        var playback = new WindowPlayback(new TickPacer(0.02, 1), new CaptureMailbox(), CancellationToken.None, Clock(reads, 0, 0, 0.03));

        playback.WaitForTick();
        playback.WaitForTick().Should().BeTrue();

        // The second tick starts at 0.02 s: the clock said 0 once, so the playback waited and read the clock again.
        reads.Should().Equal(0, 0, 0.03);
    }

    /// <summary>
    /// A clock that answers each read with the next value of the list. A read past the end fails the test, so a mutant
    /// that waits forever on a frozen clock fails instead of hanging the run.
    /// </summary>
    private static Func<double> Clock(List<double> reads, params double[] values) => () =>
    {
        if (reads.Count >= values.Length)
            throw new InvalidOperationException($"The clock was read {reads.Count + 1} times, more than the {values.Length} that the test expects.");
        reads.Add(values[reads.Count]);
        return reads[^1];
    };

    [Fact]
    public void ATickLateByLessThanOneTickRunsAtOnceAndKeepsTheSchedule()
    {
        var reads = new List<double>();
        var playback = new WindowPlayback(new TickPacer(0.02, 1), new CaptureMailbox(), CancellationToken.None, Clock(reads, 0, 0.03, 0.04));

        for (int i = 0; i < 3; i++)
            playback.WaitForTick().Should().BeTrue();

        // Tick 1 starts 0.01 s late. Tick 2 keeps its start at 0.04 s, so it runs at once when the clock says 0.04.
        reads.Should().Equal(0, 0.03, 0.04);
    }

    [Fact]
    public void AfterAStallTheScheduleMovesSoTheTicksAfterItKeepTheirPace()
    {
        var reads = new List<double>();
        var playback = new WindowPlayback(new TickPacer(0.02, 1), new CaptureMailbox(), CancellationToken.None, Clock(reads, 0, 0.5, 0.5, 0.52));

        for (int i = 0; i < 3; i++)
            playback.WaitForTick().Should().BeTrue();

        // Tick 1, due at 0.02 s, starts at 0.5 s, after a stall such as a capture. Tick 2 then waits one tick after it,
        // until 0.52 s, instead of running at once to catch up.
        reads.Should().Equal(0, 0.5, 0.5, 0.52);
    }

    [Fact]
    public void EachStallMovesTheScheduleAgain()
    {
        var reads = new List<double>();
        var playback = new WindowPlayback(new TickPacer(0.02, 1), new CaptureMailbox(), CancellationToken.None, Clock(reads, 0, 0.5, 0.5, 0.52, 1.0, 1.0, 1.02));

        for (int i = 0; i < 5; i++)
            playback.WaitForTick().Should().BeTrue();

        // A second stall at tick 3, due at 0.54 s, starts at 1.0 s. Tick 4 then waits one tick after it, until 1.02 s.
        reads.Should().Equal(0, 0.5, 0.5, 0.52, 1.0, 1.0, 1.02);
    }

    [Fact]
    public void NoTickRunsAfterTheWindowCloses()
    {
        using var closing = new CancellationTokenSource();
        var playback = new WindowPlayback(new TickPacer(0.02, 1), new CaptureMailbox(), closing.Token, () => 0);
        closing.Cancel();

        playback.WaitForTick().Should().BeFalse();
    }

    [Fact]
    public void AWaitForALaterTickEndsWhenTheWindowCloses()
    {
        using var closing = new CancellationTokenSource();
        var playback = new WindowPlayback(new TickPacer(3600, 1), new CaptureMailbox(), closing.Token, () => 0);
        playback.WaitForTick().Should().BeTrue();
        closing.CancelAfter(TimeSpan.FromMilliseconds(50));

        // The second tick would start an hour later.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        playback.WaitForTick().Should().BeFalse();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ASpeedSoSlowThatATickLastsMonthsStillWaitsAndStops()
    {
        using var closing = new CancellationTokenSource();
        // At this speed, one tick lasts about 190 days of wall clock, longer than one wait can be.
        var playback = new WindowPlayback(new TickPacer(1.0 / 60, 1e-9), new CaptureMailbox(), closing.Token, () => 0);
        playback.WaitForTick().Should().BeTrue();
        closing.CancelAfter(TimeSpan.FromMilliseconds(50));

        bool second = await Task.Run(playback.WaitForTick, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        second.Should().BeFalse();
    }

    [Fact]
    public void ACapturePublishesTheStateOfItsMomentForTheRenderThread()
    {
        using World world = HostTesting.NewWorld();
        HostTesting.Probes(world);
        // The render thread has taken every snapshot so far.
        world.PublishRenderSnapshot();
        world.RenderSnapshots.TryAcquireLatest(out _);
        var mailbox = new CaptureMailbox();
        var playback = new WindowPlayback(new TickPacer(0.02, 1), mailbox, CancellationToken.None, () => 0);
        bool fresh = false;
        var renderThread = new Thread(() =>
        {
            while (!mailbox.TryTake(out _))
                Thread.Yield();
            fresh = world.RenderSnapshots.TryAcquireLatest(out _);
            mailbox.Done();
        });
        renderThread.Start();

        playback.Capture(world, "x.png").Should().BeTrue();

        renderThread.Join();
        fresh.Should().BeTrue("the capture publishes a snapshot of its moment before the render thread draws it");
    }

    [Fact]
    public async Task ACaptureThatTheWindowClosesOnStopsTheRun()
    {
        using World world = HostTesting.NewWorld();
        using var closing = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var playback = new WindowPlayback(new TickPacer(0.02, 1), new CaptureMailbox(), closing.Token, () => 0);

        // Nobody serves the capture, so only the closing ends the wait.
        bool captured = await Task.Run(() => playback.Capture(world, "x.png"), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        captured.Should().BeFalse();
    }
}

public class ViewSwitchTests
{
    private static (World World, SceneInstance Scene) Orbit()
    {
        World world = HostTesting.NewJoltWorld();
        return (world, Tertius.Demo.DemoScenes.All.Single(s => s.Name == "orbit").Build(world, RenderResources.None));
    }

    [Fact]
    public void OnePressMovesTheCameraToTheNextView()
    {
        (World world, SceneInstance scene) = Orbit();
        using (world)
        {
            var views = new ViewSwitch(scene);

            views.Press();
            views.Apply(world);

            world.Camera.Should().Be(scene["far"]);
        }
    }

    [Fact]
    public void EveryPressBetweenTwoTicksCounts()
    {
        (World world, SceneInstance scene) = Orbit();
        using (world)
        {
            var views = new ViewSwitch(scene);

            // At a low --speed, ticks are far apart, and two presses can fall between them.
            views.Press();
            views.Press();
            views.Apply(world);

            // From the chase view, past the far view.
            world.Camera.Should().Be(scene["person"]);
        }
    }

    [Fact]
    public void EachSwitchIsReportedWithTheCameraItMadeInTheOrderItHappened()
    {
        (World world, SceneInstance scene) = Orbit();
        using (world)
        {
            var views = new ViewSwitch(scene);
            var switched = new List<Entity>();

            views.Press();
            views.Press();
            views.Apply(world, switched.Add);

            switched.Should().Equal(scene["far"], scene["person"]);
        }
    }

    [Fact]
    public void ASceneWithNoViewsSwitchesNothingAndReportsNothing()
    {
        using World world = HostTesting.NewJoltWorld();
        SceneInstance scene = HostTesting.Probes(world);
        world.Camera = scene["probe"];
        var views = new ViewSwitch(scene);
        var switched = new List<Entity>();

        views.Press();
        views.Apply(world, switched.Add);

        world.Camera.Should().Be(scene["probe"]);
        switched.Should().BeEmpty();
    }

    [Fact]
    public void APressCountsOnce()
    {
        (World world, SceneInstance scene) = Orbit();
        using (world)
        {
            var views = new ViewSwitch(scene);

            views.Press();
            views.Apply(world);
            views.Apply(world);

            world.Camera.Should().Be(scene["far"]);
        }
    }
}
