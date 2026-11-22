using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// An offscreen output for tests, made through <see cref="VulkanRenderer.CreateOffscreen"/>: a real
/// <see cref="HeadlessTarget"/>, RGBA as the headless output or BGRA as a window swapchain often is, plus three test
/// features. It skips the next <see cref="FramesToSkip"/> frames, as a swapchain does while its window
/// is minimized, it can be <see cref="Minimized"/> until its next resize, and it can refuse captures, as a swapchain
/// on some surfaces does.
/// </summary>
internal sealed class TestOutput(VulkanDevice device, DeviceAllocator allocator, Extent2D extent, bool bgra)
    : IOutputTarget
{
    private readonly HeadlessTarget target = new(device: device, allocator: allocator, extent: extent, bgra: bgra);

    /// <summary>How many of the next frames find no image, so that the renderer skips them.</summary>
    public int FramesToSkip { get; set; }

    /// <summary>
    /// Whether the output acts as the swapchain of a minimized window: its extent is 0 × 0 and it has no image, until
    /// the next <see cref="Resize"/>.
    /// </summary>
    public bool Minimized { get; set; }

    /// <summary>Whether the images allow transfer reads. A swapchain on some surfaces does not.</summary>
    public bool CanCapture { get; init; } = true;

    public Format Format => target.Format;
    public Extent2D Extent => Minimized ? default : target.Extent;
    public bool EncodeSrgbInShader => target.EncodeSrgbInShader;
    public bool BgraOrder => target.BgraOrder;

    public GpuImage? Acquire(FrameResources frame)
    {
        if (Minimized)
            return null;
        if (FramesToSkip == 0)
            return target.Acquire(frame);
        FramesToSkip--;
        return null;
    }

    public void FinishRecording(BarrierBatch barriers, CommandBuffer commandBuffer) =>
        target.FinishRecording(barriers, commandBuffer);

    public void SubmitAndPresent(FrameResources frame) => target.SubmitAndPresent(frame);

    public void Resize(Extent2D extent)
    {
        Minimized = false;
        target.Resize(extent);
    }

    public void Dispose() => target.Dispose();
}
