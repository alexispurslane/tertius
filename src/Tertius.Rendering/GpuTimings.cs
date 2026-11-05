namespace Tertius.Rendering;

/// <summary>The GPU time of one pass of the frame graph in one frame.</summary>
/// <param name="Pass">The pass's <see cref="RenderPass.Name"/>, such as "G-buffer".</param>
/// <param name="Milliseconds">The time from the end of the work before the pass to the end of the pass's own work.</param>
public readonly record struct PassTiming(string Pass, double Milliseconds)
{
    internal FrameGraph.Stage Stage { get; init; }
}

/// <summary>The GPU time of each pass of one frame whose GPU work finished.</summary>
/// <param name="Frame">The number of that frame, counted from 0 by <see cref="VulkanRenderer.Render"/>.</param>
/// <param name="Passes">One timing per pass, in recording order.</param>
/// <param name="SpanMilliseconds">The frame's GPU span: from the start of its first pass to the end of its last pass.</param>
public readonly record struct GpuTimings(long Frame, ReadOnlyMemory<PassTiming> Passes, double SpanMilliseconds);
