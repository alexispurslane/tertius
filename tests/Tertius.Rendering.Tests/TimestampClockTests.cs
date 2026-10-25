namespace Tertius.Rendering.Tests;

/// <summary>
/// The clock of the graphics queue's timestamps, on the CPU: when it can time the passes at all, how it turns two ticks
/// into milliseconds, and how it turns the query results of one frame into the time of each pass.
/// </summary>
public sealed class TimestampClockTests
{
    [Fact]
    public void AQueueWithValidTimestampBitsAndAPositivePeriodTimesThePasses() =>
        new TimestampClock(ValidBits: 64, Period: 1f).Supported.Should().BeTrue();

    [Fact]
    public void AQueueWithNoValidTimestampBitsCannotTimeThePasses() =>
        new TimestampClock(ValidBits: 0, Period: 1f)
            .Supported.Should()
            .BeFalse("Vulkan gives 0 valid bits for a queue that does not write timestamps");

    [Fact]
    public void AClockWhosePeriodIsZeroCannotTimeThePasses() =>
        new TimestampClock(ValidBits: 64, Period: 0f)
            .Supported.Should()
            .BeFalse("every pass would take 0 ms whatever it did");

    [Fact]
    public void TheTimeBetweenTwoTicksIsTheirDifferenceTimesThePeriodInMilliseconds() =>
        new TimestampClock(ValidBits: 64, Period: 41.667f)
            .Milliseconds(start: 1_000, end: 25_000)
            .Should()
            .BeApproximately(24_000 * (double)41.667f / 1e6, 1e-9);

    [Fact]
    public void ACounterWithFewerThan64BitsThatWrapsBetweenTwoTicksStillGivesTheTimeBetweenThem() =>
        new TimestampClock(ValidBits: 36, Period: 1f)
            .Milliseconds(start: (1UL << 36) - 500, end: 1_500)
            .Should()
            .BeApproximately(2_000 / 1e6, 1e-12, "the counter counts 500 ticks to its wrap and 1,500 after it");

    [Fact]
    public void ThePassesOfAFrameTakeTheTimeBetweenTheStampBeforeThemAndTheStampAfterThem()
    {
        var clock = new TimestampClock(ValidBits: 64, Period: 1f);
        // Pairs of a timestamp in ticks and its availability, one pair for each query.
        ulong[] results = [1_000_000, 1, 1_250_000, 1, 1_250_000, 1, 4_250_000, 1];
        var timings = new PassTiming[3];

        double? span = clock.Fill(results: results, passes: ["shadow maps", "probe", "G-buffer"], timings: timings);

        span.Should().NotBeNull();
        timings
            .Should()
            .Equal(new PassTiming("shadow maps", 0.25), new PassTiming("probe", 0), new PassTiming("G-buffer", 3));
    }

    [Fact]
    public void TheGpuSpanOfAFrameIsTheTimeFromTheStampBeforeItsFirstPassToTheStampAfterItsLastPass()
    {
        var clock = new TimestampClock(ValidBits: 64, Period: 1f);
        // Pairs of a timestamp in ticks and its availability: before the first pass, after each of three passes.
        ulong[] results = [1_000_000, 1, 1_250_000, 1, 1_250_000, 1, 4_250_000, 1];

        double? span = clock.Fill(
            results: results,
            passes: ["shadow maps", "probe", "G-buffer"],
            timings: new PassTiming[3]
        );

        span.Should().Be(3.25);
    }

    [Fact]
    public void TheGpuSpanOfAFrameWrapsAtTheValidBitsAsTheCounterDoes()
    {
        var clock = new TimestampClock(ValidBits: 36, Period: 1f);
        ulong last = (1UL << 36) - 500;
        ulong[] results = [last, 1, last + 250, 1, 1_500, 1];

        double? span = clock.Fill(results: results, passes: ["lighting", "bloom"], timings: new PassTiming[2]);

        span.Should()
            .NotBeNull()
            .And.BeApproximately(2_000 / 1e6, 1e-12, "the counter counts 500 ticks to its wrap and 1,500 after it");
    }

    [Fact]
    public void AFrameWithAQueryWhoseResultIsNotAvailableHasNoGpuSpan()
    {
        var clock = new TimestampClock(ValidBits: 64, Period: 1f);
        // The stamp after the last pass is not written yet, so its result reads as a timestamp of 0.
        ulong[] results = [1_000_000, 1, 1_250_000, 1, 0, 0];

        double? span = clock.Fill(results: results, passes: ["shadow maps", "G-buffer"], timings: new PassTiming[2]);

        span.Should().BeNull("a result that is not available gives no span, never one read from a timestamp of 0");
    }

    [Fact]
    public void AFrameWithAQueryWhoseResultIsNotAvailableHasNoTimingsAndLeavesTheTimingsAsTheyWere()
    {
        var clock = new TimestampClock(ValidBits: 64, Period: 1f);
        ulong[] results = [1_000_000, 1, 1_250_000, 0];
        var earlier = new PassTiming("lighting", 0.5);
        PassTiming[] timings = [earlier];

        double? span = clock.Fill(results: results, passes: ["lighting"], timings: timings);

        span.Should().BeNull("a result that is not available is no timing, never a time of 0");
        timings.Should().Equal(earlier);
    }
}
