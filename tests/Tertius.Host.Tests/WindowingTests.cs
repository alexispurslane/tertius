using System.Numerics;
using Tertius.Host.Windowing;

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
}

/// <summary>CaptureMailbox: the handover of a capture from the simulation thread to the render thread.</summary>
public class CaptureMailboxTests
{
    [Fact]
    public void ARequestWaitsUntilTheRenderThreadHasSavedTheCapture()
    {
        var mailbox = new CaptureMailbox();
        string? taken = null;
        var renderThread = new Thread(() =>
        {
            while (!mailbox.TryTake(out taken))
                Thread.Yield();
            mailbox.Done();
        });
        renderThread.Start();

        mailbox.RequestAndWait("x.png", CancellationToken.None);

        renderThread.Join();
        taken.Should().Be("x.png");
    }

    [Fact]
    public void WithNoRequestThereIsNothingToTake()
    {
        new CaptureMailbox().TryTake(out _).Should().BeFalse();
    }

    [Fact]
    public void ARequestStopsWaitingWhenTheWindowCloses()
    {
        var mailbox = new CaptureMailbox();
        using var closing = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Action request = () => mailbox.RequestAndWait("x.png", closing.Token);

        request.Should().Throw<OperationCanceledException>();
    }
}

/// <summary>WindowPlayback: the pace of a script in the window, on a clock that the test controls.</summary>
public class WindowPlaybackTests
{
    [Fact]
    public void TheFirstTickRunsAtOnce()
    {
        var playback = new WindowPlayback(new TickPacer(0.02, 1), new CaptureMailbox(), CancellationToken.None, () => 0);

        playback.WaitForTick().Should().BeTrue();
    }

    [Fact]
    public void ALaterTickWaitsForItsStartTime()
    {
        var reads = new List<double>();
        double[] clock = [0, 0, 0.03];
        var playback = new WindowPlayback(new TickPacer(0.02, 1), new CaptureMailbox(), CancellationToken.None, () =>
        {
            reads.Add(clock[Math.Min(reads.Count, clock.Length - 1)]);
            return reads[^1];
        });

        playback.WaitForTick();
        playback.WaitForTick().Should().BeTrue();

        // The second tick starts at 0.02 s: the clock said 0 once, so the playback waited and read the clock again.
        reads.Should().Equal(0, 0, 0.03);
    }

    [Fact]
    public void LateTicksRunWithoutWaiting()
    {
        int reads = 0;
        var playback = new WindowPlayback(new TickPacer(0.02, 1), new CaptureMailbox(), CancellationToken.None, () => { reads++; return 1.0; });

        for (int i = 0; i < 5; i++)
            playback.WaitForTick().Should().BeTrue();

        reads.Should().Be(5, "each tick read the clock once and found itself late");
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
        using var closing = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var playback = new WindowPlayback(new TickPacer(60, 1), new CaptureMailbox(), closing.Token, () => 0);
        playback.WaitForTick();

        // The second tick would start a minute later.
        playback.WaitForTick().Should().BeFalse();
    }
}
