// PROBE (ter-yglw, never commit to src): writes each finished frame's GPU span and pass times.
using System.Globalization;
using Tertius.Rendering;

namespace Tertius.Host;

/// <summary>
/// With TERTIUS_PROBE_FRAME_TIMES=path, one line per finished frame whose GPU work the renderer timed, once per frame
/// number: the frame number, its GPU span in ms, then "pass=ms" for each pass in recording order, with the pass's
/// spaces written as underscores.
/// </summary>
internal static class FrameTimeProbe
{
    private static readonly StreamWriter? Log = Environment.GetEnvironmentVariable("TERTIUS_PROBE_FRAME_TIMES") is
    { Length: > 0 } path
        ? new StreamWriter(path) { AutoFlush = true }
        : null;

    private static long last = -1;

    public static void Write(RenderStats stats)
    {
        if (Log is null || stats.GpuTimings is not { } timings || timings.Frame <= last)
            return;
        last = timings.Frame;
        var line = new System.Text.StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"{timings.Frame} {timings.SpanMilliseconds:F5}");
        foreach (PassTiming pass in timings.Passes.Span)
            line.Append(CultureInfo.InvariantCulture, $" {pass.Pass.Replace(' ', '_')}={pass.Milliseconds:F5}");
        Log.WriteLine(line.ToString());
    }
}
