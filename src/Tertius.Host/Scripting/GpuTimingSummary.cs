using Tertius.Rendering;

namespace Tertius.Host.Scripting;

/// <summary>The GPU time of one pass over the finished frames of a run, in milliseconds.</summary>
/// <param name="Pass">The pass's name, as <see cref="PassTiming.Pass"/> gives it.</param>
/// <param name="Frames">The frames that timed the pass.</param>
/// <param name="MeanMs">The mean time.</param>
/// <param name="MedianMs">The median time: the mean of the two middle times for an even count.</param>
/// <param name="P95Ms">The 95th percentile by nearest rank: the time at rank ⌈0.95 × frames⌉ from the smallest.</param>
internal readonly record struct PassSummary(string Pass, int Frames, double MeanMs, double MedianMs, double P95Ms);

/// <summary>
/// Collects the GPU timings that a run's renderer reports after each frame, once per finished frame, and summarizes each
/// pass (docs/plans/rendering-upgrade.md, "Rules for every member").
/// </summary>
internal sealed class GpuTimingSummary
{
    // Room for the times of about a minute of frames at 60 frames per second, so that a measurement run of that length
    // grows no list inside the loop that it measures.
    private const int FramesPerPass = 4096;

    // Each pass in the order in which the frames first name it, with its time in each frame that timed it.
    private readonly List<(string Pass, List<double> Milliseconds)> passes = [];
    private long lastFrame = -1;

    /// <summary>
    /// Adds the times of a frame that the summary does not hold yet. A renderer reports one frame after several renders,
    /// and null when it has no timings, so both add nothing.
    /// </summary>
    public void Add(GpuTimings? timings)
    {
        if (timings is not { } frame || frame.Frame <= lastFrame)
            return;
        lastFrame = frame.Frame;
        foreach (PassTiming timing in frame.Passes.Span)
            TimesOf(timing.Pass).Add(timing.Milliseconds);
    }

    // A loop, not FindIndex: a lambda that captures the name would allocate for each pass of each frame.
    private List<double> TimesOf(string pass)
    {
        foreach ((string name, List<double> milliseconds) in passes)
            if (name == pass)
                return milliseconds;
        var times = new List<double>(FramesPerPass);
        passes.Add((pass, times));
        return times;
    }

    /// <summary>The summary of each pass, in the order in which the frames first named them.</summary>
    public IReadOnlyList<PassSummary> Passes() =>
        [
            .. passes.Select(pass =>
            {
                double[] sorted = [.. pass.Milliseconds.Order()];
                int count = sorted.Length;
                double median = count % 2 == 1 ? sorted[count / 2] : (sorted[count / 2 - 1] + sorted[count / 2]) / 2;
                return new PassSummary(
                    Pass: pass.Pass,
                    Frames: count,
                    MeanMs: sorted.Average(),
                    MedianMs: median,
                    P95Ms: sorted[(int)Math.Ceiling(0.95 * count) - 1]
                );
            }),
        ];
}
