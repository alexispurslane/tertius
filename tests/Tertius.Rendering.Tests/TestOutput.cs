using Silk.NET.Vulkan;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>
/// An offscreen output for tests, made through <see cref="VulkanRenderer.CreateOffscreen"/>: an image of a chosen sRGB
/// format, RGBA as the headless output or BGRA as a window swapchain often is. It skips the next
/// <see cref="FramesToSkip"/> frames, as a swapchain does while its window is minimized.
/// </summary>
internal sealed class TestOutput(VulkanDevice device, DeviceAllocator allocator, Extent2D extent, Format format) : IOutputTarget
{
    private GpuImage image = Create(device, allocator, extent, format);

    /// <summary>How many of the next frames find no image, so that the renderer skips them.</summary>
    public int FramesToSkip { get; set; }

    public Format Format => format;
    public Extent2D Extent => image.Extent;
    public bool EncodeSrgbInShader => false;
    public bool BgraOrder => format == Format.B8G8R8A8Srgb;
    /// <summary>Whether the images allow transfer reads. A swapchain on some surfaces does not.</summary>
    public bool CanCapture { get; init; } = true;

    public GpuImage? Acquire(FrameResources frame)
    {
        if (FramesToSkip == 0)
            return image;
        FramesToSkip--;
        return null;
    }

    public void FinishRecording(BarrierBatch barriers, CommandBuffer commandBuffer)
    {
    }

    public void SubmitAndPresent(FrameResources frame) => QueueSubmission.Submit(device, frame, default, PipelineStageFlags2.None, default);

    public void Resize(Extent2D extent)
    {
        image.Dispose();
        image = Create(device, allocator, extent, format);
    }

    public void Dispose() => image.Dispose();

    private static GpuImage Create(VulkanDevice device, DeviceAllocator allocator, Extent2D extent, Format format) =>
        GpuImage.Create(device, allocator, extent, format, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit, 1, "test output");
}
