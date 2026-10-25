using Tertius.Host.Scripting;
using Tertius.Rendering;

namespace Tertius.Host.Tests;

/// <summary>
/// The summary of the GPU time of each pass over the finished frames of a headless run: each frame counts once, and
/// each pass gets the mean, the median and the 95th percentile of its times.
/// </summary>
public sealed class GpuTimingSummaryTests
{
    private static GpuTimings Frame(long frame, params PassTiming[] passes) => new(frame, passes);

    [Fact]
    public void AFrameThatSeveralRendersReportCountsOnce()
    {
        var summary = new GpuTimingSummary();
        GpuTimings first = Frame(0, new PassTiming("lighting", 1));

        summary.Add(first);
        summary.Add(first);
        summary.Add(Frame(1, new PassTiming("lighting", 3)));

        summary.Passes().Should().Equal(new PassSummary(Pass: "lighting", Frames: 2, MeanMs: 2, MedianMs: 2, P95Ms: 3));
    }

    [Fact]
    public void ARenderWithNoTimingsAddsNothing()
    {
        var summary = new GpuTimingSummary();

        summary.Add(null);

        summary.Passes().Should().BeEmpty();
    }

    [Fact]
    public void ThePercentilesOfAPassAreTheNearestRanksOfItsSortedTimes()
    {
        var summary = new GpuTimingSummary();
        // Twenty frames whose times are 1 ms to 20 ms, in a shuffled order.
        int[] order = [7, 19, 1, 14, 3, 20, 11, 5, 16, 9, 2, 18, 13, 6, 10, 4, 17, 8, 12, 15];
        for (int frame = 0; frame < order.Length; frame++)
            summary.Add(Frame(frame, new PassTiming("G-buffer", order[frame])));

        PassSummary gBuffer = summary.Passes().Single();

        gBuffer.MeanMs.Should().Be(10.5);
        gBuffer.MedianMs.Should().Be(10.5, "the median of an even count is the mean of the two middle times");
        gBuffer.P95Ms.Should().Be(19, "the 95th percentile of 20 times is the 19th smallest");
    }

    [Fact]
    public void TheMedianOfAnOddCountOfFramesIsTheMiddleTime()
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(0, new PassTiming("tone map", 5)));
        summary.Add(Frame(1, new PassTiming("tone map", 1)));
        summary.Add(Frame(2, new PassTiming("tone map", 2)));

        summary.Passes().Single().MedianMs.Should().Be(2);
    }

    [Fact]
    public void ThePassesKeepTheOrderInWhichTheFramesFirstNameThem()
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(0, new PassTiming("shadow maps", 1), new PassTiming("lighting", 1)));
        summary.Add(
            Frame(1, new PassTiming("shadow maps", 1), new PassTiming("probe", 1), new PassTiming("lighting", 1))
        );

        summary
            .Passes()
            .Select(pass => (pass.Pass, pass.Frames))
            .Should()
            .Equal(("shadow maps", 2), ("lighting", 2), ("probe", 1));
    }

    [Fact]
    public void TheSummaryKeepsTheTimesOfAFrameAfterTheRendererRewritesItsStorage()
    {
        var summary = new GpuTimingSummary();
        PassTiming[] storage = [new("lighting", 1)];
        summary.Add(new GpuTimings(0, storage));

        storage[0] = new("lighting", 9);
        summary.Add(new GpuTimings(1, storage));

        summary.Passes().Single().MeanMs.Should().Be(5, "frame 0 took 1 ms and frame 1 took 9 ms");
    }
}
