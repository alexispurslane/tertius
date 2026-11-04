namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="TimedSpan"/>: the one writer of the marker lines of a measurement's timed span in this project, from which
/// tools/gpu_clock.py takes gpu-clock's clock over the timed work alone.
/// </summary>
public sealed class TimedSpanTests
{
    [Fact]
    public void TheTimedWorkRunsBetweenTheMarkerLinesAndItsResultComesBack()
    {
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;

        int result = TimedSpan.Run(() =>
        {
            output.WriteLine("the timed work");
            return 7;
        });

        result.Should().Be(7);
        output
            .Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Should()
            .Equal(["timed-start", "the timed work", "timed-end"], "gpu_clock.py averages the samples between the two lines");
    }
}
