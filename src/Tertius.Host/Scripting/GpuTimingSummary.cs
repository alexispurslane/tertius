using System.Diagnostics;
using Tertius.Rendering;

namespace Tertius.Host.Scripting;

/// <summary>The GPU time of one pass over the finished frames of a run, in milliseconds.</summary>
/// <param name="Pass">The pass's name, as <see cref="PassTiming.Pass"/> gives it.</param>
/// <param name="Frames">The frames that timed the pass.</param>
/// <param name="MeanMs">The mean time.</param>
/// <param name="MedianMs">The median time: the mean of the two middle times for an even count.</param>
/// <param name="P95Ms">The 95th percentile by nearest rank: the time at rank ⌈0.95 × frames⌉ from the smallest.</param>
internal readonly record struct PassSummary(string Pass, int Frames, double MeanMs, double MedianMs, double P95Ms);

/// <summary>Statistics of one GPU time per finished frame, in milliseconds: a frame span or an inclusive pass-run sum.</summary>
/// <param name="Frames">The frames that the summary holds.</param>
/// <param name="MeanMs">The mean time per frame.</param>
/// <param name="MedianMs">The median time: the mean of the two middle times for an even count.</param>
/// <param name="P95Ms">The 95th percentile by nearest rank: the time at rank ⌈0.95 × frames⌉ from the smallest.</param>
internal readonly record struct SpanSummary(int Frames, double MeanMs, double MedianMs, double P95Ms);

/// <summary>The inclusive run's endpoints and the statistics of its per-frame sums, in milliseconds.</summary>
internal readonly record struct RunSummary(string First, string Last, SpanSummary Timing);

/// <summary>
/// Collects the GPU timings that a run's renderer reports after each frame, once per finished frame, and summarizes each
/// pass, inclusive pass runs and the frames' GPU span (ARCHITECTURE.md, "GPU timings").
/// </summary>
internal sealed class GpuTimingSummary
{
    // Room for the times of about a minute of frames at 60 frames per second, so that a measurement run of that length
    // grows no list inside the loop that it measures.
    private const int FramesPerPass = 4096;

    // Each pass keeps first-seen order for the raw report. Samples retain their frame-list index and recording position
    // so a run uses actual neighbours even when a frame skips a pass.
    private readonly List<(string Pass, List<PassSample> Samples)> passes = [];

    // The GPU span of each frame, in the order added.
    private readonly List<(long Frame, double Milliseconds)> spans = [with(FramesPerPass)];

    /// <summary>
    /// Adds the times of a frame that the summary does not hold yet. A renderer reports one frame after several renders,
    /// and null when it has no timings, so both add nothing.
    /// </summary>
    public void Add(GpuTimings? timings)
    {
        if (timings is not { } frame)
            return;
        long lastFrame = spans.Count == 0 ? -1 : spans[^1].Frame;
        if (frame.Frame <= lastFrame)
            return;
        int frameIndex = spans.Count;
        spans.Add((frame.Frame, frame.SpanMilliseconds));
        for (int order = 0; order < frame.Passes.Length; order++)
        {
            PassTiming timing = frame.Passes.Span[order];
            SamplesOf(timing.Pass).Add(new(frameIndex, order, timing.Milliseconds));
        }
    }

    // A loop, not FindIndex: a lambda that captures the name would allocate for each pass of each frame.
    private List<PassSample> SamplesOf(string pass)
    {
        foreach ((string name, var samples) in passes)
            if (name == pass)
                return samples;
        var times = new List<PassSample>(FramesPerPass);
        passes.Add((pass, times));
        return times;
    }

    /// <summary>The summary of the frames' GPU spans, or null when the summary holds no frame.</summary>
    public SpanSummary? Frame()
    {
        if (spans.Count == 0)
            return null;
        return Statistics(spans.Select(span => span.Milliseconds));
    }

    /// <summary>The summary of each pass, in the order in which the frames first named them.</summary>
    public IReadOnlyList<PassSummary> Passes() =>
        [
            .. passes.Select(pass =>
            {
                SpanSummary timing = Statistics(pass.Samples.Select(sample => sample.Milliseconds));
                return new PassSummary(
                    Pass: pass.Pass,
                    Frames: timing.Frames,
                    MeanMs: timing.MeanMs,
                    MedianMs: timing.MedianMs,
                    P95Ms: timing.P95Ms
                );
            }),
        ];

    /// <summary>The endpoints of FIRST..LAST, or an actionable syntax error. Names match recorded pass names exactly.</summary>
    public static Result<(string First, string Last), string> ReadRun(string value)
    {
        string[] endpoints = value.Split("..", StringSplitOptions.None);
        bool oneSeparator =
            value.IndexOf("..", StringComparison.Ordinal) == value.LastIndexOf("..", StringComparison.Ordinal);
        bool valid =
            oneSeparator && endpoints.Length == 2 && endpoints.All(endpoint => !string.IsNullOrWhiteSpace(endpoint));
        if (!valid)
            return $"--gpu-run needs FIRST..LAST, such as 'bloom..capture', not '{value}'. Both endpoints are inclusive.";
        return (endpoints[0].Trim(), endpoints[1].Trim());
    }

    /// <summary>
    /// Summarizes each requested inclusive run in request order. Rejects unknown, missing or reversed endpoints instead
    /// of dropping frames. Interior passes follow each frame's actual recording order.
    /// </summary>
    public Result<IReadOnlyList<RunSummary>, string> Runs(IReadOnlyList<string> runs)
    {
        var summaries = new List<RunSummary>();
        if (runs.Count > 0 && spans.Count == 0)
            return "--gpu-run cannot select passes: no GPU timings were recorded. Use a script that captures frames and a renderer that supports timestamps.";
        foreach (string run in runs)
        {
            var parsed = ReadRun(run);
            if (parsed.Problem is { } syntaxProblem)
                return syntaxProblem;
            (string first, string last) = parsed.Ok ?? throw new UnreachableException();
            var summed = SumRun(first, last);
            if (summed.Problem is { } problem)
                return problem;
            summaries.Add(new(first, last, summed.Ok ?? throw new UnreachableException()));
        }
        return summaries;
    }

    private Result<SpanSummary, string> SumRun(string first, string last)
    {
        var firstRead = EndpointSamples(first);
        if (firstRead.Problem is { } firstProblem)
            return firstProblem;
        var lastRead = EndpointSamples(last);
        if (lastRead.Problem is { } lastProblem)
            return lastProblem;
        List<PassSample> firstSamples = firstRead.Ok ?? throw new UnreachableException();
        List<PassSample> lastSamples = lastRead.Ok ?? throw new UnreachableException();
        for (int frame = 0; frame < spans.Count; frame++)
            if (firstSamples[frame].Order > lastSamples[frame].Order)
                return $"--gpu-run '{first}..{last}' is reversed in frame {spans[frame].Frame}: '{first}' must be recorded before '{last}'.";
        double[] sums = new double[spans.Count];
        foreach ((_, var samples) in passes)
        foreach (PassSample sample in samples)
            if (
                sample.Order >= firstSamples[sample.FrameIndex].Order
                && sample.Order <= lastSamples[sample.FrameIndex].Order
            )
                sums[sample.FrameIndex] += sample.Milliseconds;
        return Statistics(sums);
    }

    private Result<List<PassSample>, string> EndpointSamples(string name)
    {
        if (passes.FirstOrDefault(pass => pass.Pass == name).Samples is not { } samples)
            return $"--gpu-run names unknown pass '{name}'. Available passes: {string.Join(", ", passes.Select(timing => timing.Pass))}.";
        for (int frame = 0; frame < spans.Count; frame++)
            if (frame >= samples.Count || samples[frame].FrameIndex != frame)
                return $"--gpu-run endpoint '{name}' was not recorded in frame {spans[frame].Frame}. Choose endpoints present in every recorded frame.";
        return samples;
    }

    /// <summary>The count, the mean, the median and the nearest-rank 95th percentile of at least one time.</summary>
    private static SpanSummary Statistics(IEnumerable<double> times)
    {
        double[] sorted = [.. times.Order()];
        int count = sorted.Length;
        double median = count % 2 == 1 ? sorted[count / 2] : (sorted[count / 2 - 1] + sorted[count / 2]) / 2;
        return new SpanSummary(
            Frames: count,
            MeanMs: sorted.Average(),
            MedianMs: median,
            P95Ms: sorted[(int)Math.Ceiling(0.95 * count) - 1]
        );
    }

    /// <summary>A copied pass time, with its index in the recorded frame list and its position in that frame.</summary>
    private readonly record struct PassSample(int FrameIndex, int Order, double Milliseconds);
}
