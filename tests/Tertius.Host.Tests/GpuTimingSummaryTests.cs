using Tertius.Host.Scripting;
using Tertius.Rendering;

namespace Tertius.Host.Tests;

/// <summary>
/// The summary of the GPU time of each pass and of the frame's GPU span over the finished frames of a headless run: each
/// frame counts once, and each pass and the span get the mean, the median and the 95th percentile of their times.
/// </summary>
public sealed class GpuTimingSummaryTests
{
    /// <summary>A frame whose span is the sum of its passes, as the timer's stamps make it.</summary>
    private static GpuTimings Frame(long frame, params PassTiming[] passes)
    {
        // A loop, not Sum: the allocation test builds its frames inside the code that it measures.
        double span = 0;
        foreach (PassTiming pass in passes)
            span += pass.Milliseconds;
        return new(Frame: frame, Passes: passes, SpanMilliseconds: span);
    }

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
    public void ANegativeFrameNumberIsNotAFinishedFrame()
    {
        var summary = new GpuTimingSummary();

        summary.Add(Frame(-1, new PassTiming("capture", 1)));

        summary.Frame().Should().BeNull();
        summary.Passes().Should().BeEmpty();
    }

    [Fact]
    public void AGpuRunTrimsOnlyOuterEndpointWhitespace()
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(0, new("shadow maps", 2), new("direct light", 3)));

        var read = summary.Runs([" shadow maps .. direct light "]);

        read.Problem.Should().BeNull();
        read.Ok.Should()
            .Equal(
                new RunSummary(
                    "shadow maps",
                    "direct light",
                    new SpanSummary(Frames: 1, MeanMs: 5, MedianMs: 5, P95Ms: 5)
                )
            );
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
    public void TheFrameSummaryHoldsTheMeanMedianAndNearestRankP95OfTheSpansOfTheFrames()
    {
        var summary = new GpuTimingSummary();
        // Twenty frames whose spans are 1 ms to 20 ms, in a shuffled order, each with a pass of a different time.
        int[] order = [7, 19, 1, 14, 3, 20, 11, 5, 16, 9, 2, 18, 13, 6, 10, 4, 17, 8, 12, 15];
        for (int frame = 0; frame < order.Length; frame++)
            summary.Add(
                new GpuTimings(
                    Frame: frame,
                    Passes: new PassTiming[] { new("lighting", 0.5) },
                    SpanMilliseconds: order[frame]
                )
            );

        summary
            .Frame()
            .Should()
            .Be(
                new SpanSummary(Frames: 20, MeanMs: 10.5, MedianMs: 10.5, P95Ms: 19),
                "the span is read, not summed from the passes"
            );
    }

    [Fact]
    public void AFrameThatSeveralRendersReportAddsItsSpanOnce()
    {
        var summary = new GpuTimingSummary();
        GpuTimings first = Frame(0, new PassTiming("lighting", 1));

        summary.Add(first);
        summary.Add(first);
        summary.Add(Frame(1, new PassTiming("lighting", 3)));

        summary.Frame().Should().Be(new SpanSummary(Frames: 2, MeanMs: 2, MedianMs: 2, P95Ms: 3));
    }

    [Fact]
    public void ASummaryOfNoFramesHasNoFrameSummary() => new GpuTimingSummary().Frame().Should().BeNull();

    [Fact]
    public void AGpuRunIncludesBothEndpointsAndEveryActualPassBetweenThem()
    {
        var summary = new GpuTimingSummary();
        summary.Add(
            Frame(0, new("before", 100), new("bloom", 1), new("tone map", 2), new("capture", 4), new("after", 200))
        );

        var read = summary.Runs(["bloom..capture"]);
        read.Problem.Should().BeNull();
        var runs = read.Ok!;

        runs.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new RunSummary("bloom", "capture", new SpanSummary(Frames: 1, MeanMs: 7, MedianMs: 7, P95Ms: 7)));
    }

    [Fact]
    public void SeveralGpuRunsKeepTheirRequestedOrderAndMayShareEndpoints()
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(0, new("bloom", 1), new("tone map", 2), new("capture", 4)));

        var read = summary.Runs(["tone map..capture", "bloom..tone map", "capture..capture"]);
        read.Problem.Should().BeNull();
        var runs = read.Ok!;

        runs.Select(run => run.Timing.MeanMs).Should().Equal(6, 3, 4);
        runs.Select(run => (run.First, run.Last))
            .Should()
            .Equal(("tone map", "capture"), ("bloom", "tone map"), ("capture", "capture"));
    }

    [Fact]
    public void GpuRunPercentilesSummarizePerFrameSumsRatherThanSumPassPercentiles()
    {
        var summary = new GpuTimingSummary();
        for (int frame = 0; frame < 20; frame++)
            summary.Add(Frame(frame, new("bloom", frame + 1), new("capture", 20 - frame)));

        var read = summary.Runs(["bloom..capture"]);
        read.Problem.Should().BeNull();
        SpanSummary timing = read.Ok!.Single().Timing;

        timing.Should().Be(new SpanSummary(Frames: 20, MeanMs: 21, MedianMs: 21, P95Ms: 21));
        summary.Passes().Sum(pass => pass.P95Ms).Should().Be(38, "each pass has a different slow frame");
    }

    [Fact]
    public void GpuRunMediansSummarizePerFrameSumsRatherThanSumPassMedians()
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(0, new("bloom", 1), new("capture", 1)));
        summary.Add(Frame(1, new("bloom", 1), new("capture", 10)));
        summary.Add(Frame(2, new("bloom", 10), new("capture", 1)));

        var read = summary.Runs(["bloom..capture"]);
        read.Problem.Should().BeNull();
        read.Ok!.Single().Timing.MedianMs.Should().Be(11);
    }

    [Fact]
    public void AGpuRunUsesEachFramesActualPassOrderEvenWhenAnInteriorPassIsSkipped()
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(0, new("first", 1), new("last", 4)));
        summary.Add(Frame(1, new("first", 2), new("conditional", 8), new("last", 6)));

        var read = summary.Runs(["first..last"]);
        read.Problem.Should().BeNull();
        read.Ok!.Single().Timing.Should().Be(new SpanSummary(Frames: 2, MeanMs: 10.5, MedianMs: 10.5, P95Ms: 16));
    }

    [Fact]
    public void AnEmptyRecordedPassIsAnEndpointNotAMissingPass()
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(0, new("bloom", 0), new("capture", 4)));

        var read = summary.Runs(["bloom..capture"]);
        read.Problem.Should().BeNull();
        read.Ok!.Single().Timing.MeanMs.Should().Be(4);
    }

    [Theory]
    [InlineData("unknown..capture", "unknown")]
    [InlineData("bloom..unknown", "unknown")]
    public void AnUnknownGpuRunEndpointNamesTheProblemAndTheAvailablePasses(string run, string missing)
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(0, new("bloom", 1), new("capture", 4)));

        summary
            .Runs([run])
            .Problem.Should()
            .Contain("--gpu-run")
            .And.Contain(missing)
            .And.Contain("Available passes: bloom, capture");
    }

    [Fact]
    public void AReversedGpuRunExplainsTheRequiredFrameOrder()
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(7, new("bloom", 1), new("capture", 4)));

        summary
            .Runs(["capture..bloom"])
            .Problem.Should()
            .Contain("capture..bloom")
            .And.Contain("before")
            .And.Contain("frame 7");
    }

    [Theory]
    [InlineData("bloom")]
    [InlineData("capture")]
    public void AGpuRunRejectsAMissingEndpointInAnyRecordedFrameWithoutDiscardingThatFrame(string missing)
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(7, new("bloom", 1), new("capture", 4)));
        summary.Add(Frame(9, new PassTiming(missing == "bloom" ? "capture" : "bloom", 2)));

        summary
            .Runs(["bloom..capture"])
            .Problem.Should()
            .Contain(missing)
            .And.Contain("frame 9")
            .And.Contain("not recorded");
        summary.Frame()!.Value.Frames.Should().Be(2);
    }

    [Fact]
    public void AGpuRunRejectsAnOrderThatReversesInALaterFrame()
    {
        var summary = new GpuTimingSummary();
        summary.Add(Frame(7, new("bloom", 1), new("capture", 4)));
        summary.Add(Frame(9, new("capture", 2), new("bloom", 3)));

        summary.Runs(["bloom..capture"]).Problem.Should().Contain("frame 9").And.Contain("before");
    }

    [Fact]
    public void RequestedGpuRunsNeedRecordedFrames()
    {
        new GpuTimingSummary().Runs(["bloom..capture"]).Problem.Should().Contain("no GPU timings");
    }

    [Fact]
    public void AGpuRunCountsEachFinishedFrameOnceAfterTheRendererRewritesItsStorage()
    {
        var summary = new GpuTimingSummary();
        PassTiming[] storage = [new("bloom", 1), new("capture", 2)];
        var first = new GpuTimings(Frame: 7, Passes: storage, SpanMilliseconds: 100);
        summary.Add(first);
        summary.Add(first);
        summary.Add(null);
        storage[0] = new("bloom", 4);
        storage[1] = new("capture", 8);
        summary.Add(new GpuTimings(Frame: 9, Passes: storage, SpanMilliseconds: 200));

        var read = summary.Runs(["bloom..capture"]);
        read.Problem.Should().BeNull();
        read.Ok!.Single().Timing.Should().Be(new SpanSummary(Frames: 2, MeanMs: 7.5, MedianMs: 7.5, P95Ms: 12));
        summary.Frame()!.Value.MeanMs.Should().Be(150, "the frame span is not replaced by a run sum");
    }

    [Fact]
    public void TheSummaryKeepsTheTimesOfAFrameAfterTheRendererRewritesItsStorage()
    {
        var summary = new GpuTimingSummary();
        PassTiming[] storage = [new("lighting", 1)];
        summary.Add(new GpuTimings(Frame: 0, Passes: storage, SpanMilliseconds: 1));

        storage[0] = new("lighting", 9);
        summary.Add(new GpuTimings(Frame: 1, Passes: storage, SpanMilliseconds: 9));

        summary.Passes().Single().MeanMs.Should().Be(5, "frame 0 took 1 ms and frame 1 took 9 ms");
    }

    [Fact]
    public void AddingAFrameWhosePassesTheSummaryKnowsAllocatesNothing()
    {
        var summary = new GpuTimingSummary();
        PassTiming[] passes = [new("shadow maps", 1), new("G-buffer", 2), new("lighting", 3)];
        long frame = 0;
        // The first frames name the passes and warm up the path of the add.
        for (; frame < 2; frame++)
            summary.Add(Frame(frame, passes));

        long allocated = Allocations.Fewest(() =>
        {
            for (int i = 0; i < 100; i++)
                summary.Add(Frame(frame++, passes));
        });

        allocated.Should().Be(0, "a headless run adds the times of each frame inside the loop that it measures");
    }
}
