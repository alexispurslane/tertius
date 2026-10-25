using System.Diagnostics;
using Tertius.Rendering;

namespace Tertius.Host.Scripting;

/// <summary>
/// What a headless run reads from its renderer after each shown frame: the GPU time of each pass, and the exposure in
/// stops. A capture waits until its frame has finished, so the stats after it describe that frame.
/// </summary>
internal sealed class FrameReadings
{
    private readonly List<float> exposures = [];

    /// <summary>The GPU time of each pass over the finished frames.</summary>
    public GpuTimingSummary Timings { get; } = new();

    /// <summary>The exposure in stops of each shown frame, in the order shown.</summary>
    public IReadOnlyList<float> Exposures => exposures;

    /// <summary>Adds the readings of a shown frame from the renderer's stats after its capture.</summary>
    public void Add(in RenderStats stats)
    {
        Timings.Add(stats.GpuTimings);
        exposures.Add(
            stats.Exposure?.Stops
                ?? throw new UnreachableException("A captured frame has finished, so it has its exposure.")
        );
    }
}
