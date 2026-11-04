using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>The values that the tests of <see cref="FrameHistory"/> share.</summary>
internal static class FrameHistoryTesting
{
    /// <summary>The clock step of a draw that continues the last one: one tick at 60 Hz, as a real-time film steps.</summary>
    public const float FilmStep = 1f / 60f;
}

/// <summary>
/// The still rule of <see cref="FrameHistory"/> (docs/plans/temporal-renderer.md, decision 32 and section 10, "The still
/// rule"): which headless draws are stills, how many draws a still takes, which of them resets, how far each adapts the
/// exposure and which refreshes every face of the map, and the warm-up count that the exposure gives. No GPU needed.
/// </summary>
public sealed class StillRuleTests
{
    private static readonly Extent2D Output = new(1280, 720);

    /// <summary>The rule of a headless renderer with <paramref name="warmUpDraws"/> warm-up draws per still.</summary>
    private static FrameHistory Headless(int warmUpDraws = 32) =>
        new(new StillRule(Headless: true, WarmUpDraws: warmUpDraws));

    private static RenderSnapshot Snapshot()
    {
        var snapshot = new RenderSnapshot { CameraCuts = 1 };
        snapshot.Chain.Add(new(FrameId: 7, Rotation: Quaternion.Identity, PreviousRotation: Quaternion.Identity));
        return snapshot;
    }

    private static DrawnCamera CameraOf(RenderSnapshot snapshot) =>
        new(
            EyeInFrame: snapshot.OriginInFrame,
            FrameId: snapshot.OriginFrameId,
            Rotation: Quaternion.Identity,
            TanHalfX: 1f,
            TanHalfY: 0.5625f,
            RootRotation: Quaternion.Identity
        );

    /// <summary>Begins draw <paramref name="draw"/> of a snapshot at <paramref name="step"/>, without committing it.</summary>
    private static FrameHistory.PendingFrame Begin(FrameHistory history, float step, int draw) =>
        history.Begin(snapshot: Snapshot(), step: step, draw: draw, outputSize: Output, renderSize: Output);

    /// <summary>Begins and commits every draw of a snapshot at <paramref name="step"/>, and gives each one's pending state as it began.</summary>
    private static (bool Reset, long DrawIndex, float ExposureStep, bool RefreshesEveryFace)[] DrawAll(
        FrameHistory history,
        float step
    )
    {
        int draws = history.DrawsOf(step);
        var seen = new (bool Reset, long DrawIndex, float ExposureStep, bool RefreshesEveryFace)[draws];
        for (int draw = 0; draw < draws; draw++)
        {
            FrameHistory.PendingFrame pending = Begin(history, step, draw);
            seen[draw] = (pending.Reset, pending.DrawIndex, pending.ExposureStep, pending.RefreshesEveryFace);
            pending.Write(CameraOf(Snapshot()));
            history.Drew(pending);
        }

        return seen;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(FrameHistory.MeteringStepSeconds)]
    [InlineData(6f / 60f)]
    public void AHeadlessDrawAfterAStepOfZeroEndlessOrAtLeastTheMeteringStepIsAStill(float step) =>
        Headless().DrawsOf(step).Should().Be(33, "a still draws its 32 warm-up draws before the shown draw");

    [Fact]
    public void AHeadlessFilmOfFourTicksPerFrameContinuesItsHistories()
    {
        FrameHistory history = Headless();
        DrawAll(history, step: FrameHistoryTesting.FilmStep);

        (bool reset, long drawIndex, float exposureStep, bool refreshes) = DrawAll(history, step: 4f / 60f).Single();

        reset.Should().BeFalse();
        drawIndex.Should().Be(1);
        exposureStep.Should().Be(4f / 60f, "a frame that continues the last one adapts by its own step");
        refreshes.Should().BeFalse();
    }

    [Fact]
    public void AWindowDrawAfterALongStepIsNoStill()
    {
        var history = new FrameHistory(StillRule.Window);

        history.DrawsOf(1f).Should().Be(1, "a window's steps are frame times, and its frames continue the last one");
        history.DrawsOf(float.PositiveInfinity).Should().Be(1);
    }

    [Fact]
    public void TheFirstDrawOfAStillResetsEveryHistoryAndEveryLaterDrawContinuesIt()
    {
        FrameHistory history = Headless(warmUpDraws: 3);
        DrawAll(history, step: FrameHistoryTesting.FilmStep);

        (bool Reset, long DrawIndex, float ExposureStep, bool RefreshesEveryFace)[] draws = DrawAll(history, step: 1f);

        draws.Select(d => d.Reset).Should().Equal(true, false, false, false);
        draws.Select(d => d.DrawIndex).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void EachWarmUpDrawAdaptsTheExposureByTheStillsStepOverTheWarmUpCountAndTheShownDrawByNone() =>
        DrawAll(Headless(warmUpDraws: 4), step: 2f)
            .Select(d => d.ExposureStep)
            .Should()
            .Equal(0.5f, 0.5f, 0.5f, 0.5f, 0f);

    [Theory]
    [InlineData(0f)]
    [InlineData(float.PositiveInfinity)]
    public void EachWarmUpDrawOfAStillAfterAStepOfZeroOrAnEndlessStepAdaptsByAnEndlessStep(float step) =>
        DrawAll(Headless(warmUpDraws: 2), step: step)
            .Select(d => d.ExposureStep)
            .Should()
            .Equal(float.PositiveInfinity, float.PositiveInfinity, 0f);

    [Fact]
    public void AStillWithoutWarmUpDrawsIsOneDrawThatResetsAndAdaptsByItsStep()
    {
        FrameHistory history = Headless(warmUpDraws: 0);
        DrawAll(history, step: FrameHistoryTesting.FilmStep);

        (bool reset, _, float exposureStep, bool refreshes) = DrawAll(history, step: 1f).Single();

        reset.Should().BeTrue();
        exposureStep.Should().Be(1f);
        refreshes.Should().BeTrue();
    }

    [Fact]
    public void OnlyTheFirstDrawOfAStillRefreshesEveryFaceOfTheMap() =>
        DrawAll(Headless(warmUpDraws: 3), step: 1f)
            .Select(d => d.RefreshesEveryFace)
            .Should()
            .Equal(true, false, false, false);

    [Fact]
    public void AStillUnderAFixedExposureTakesNoWarmUpDraw() =>
        StillRule
            .For(new RendererOptions { Exposure = new FixedExposure(0) }, headless: true)
            .WarmUpDraws.Should()
            .Be(0, "no history holds more than one frame, and the exposure needs no draw to adapt in");

    [Fact]
    public void AStillUnderAnAutomaticExposureTakesOneWarmUpDraw() =>
        StillRule
            .For(new RendererOptions { Exposure = AutoExposure.Default }, headless: true)
            .WarmUpDraws.Should()
            .Be(1, "the exposure needs a draw to adapt in, today's metering draw");
}
