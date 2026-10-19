using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// An offscreen sRGB image that stands in for a window. Captures read from it. Its format is R8G8B8A8_SRGB, or
/// B8G8R8A8_SRGB for a test that needs the BGRA order of a window's swapchain.
/// </summary>
internal sealed class HeadlessTarget : IOutputTarget
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private GpuImage image;

    public HeadlessTarget(VulkanDevice device, DeviceAllocator allocator, Extent2D extent, Format format = Format.R8G8B8A8Srgb)
    {
        this.device = device;
        this.allocator = allocator;
        Format = format;
        image = CreateImage(extent);
    }

    public Format Format { get; }
    public Extent2D Extent => image.Extent;
    public bool EncodeSrgbInShader => false;
    public bool BgraOrder => Format == Format.B8G8R8A8Srgb;
    public bool CanCapture => true;

    public GpuImage? Acquire(FrameResources frame) => image;

    public void FinishRecording(BarrierBatch barriers, CommandBuffer commandBuffer)
    {
    }

    public void SubmitAndPresent(FrameResources frame) => QueueSubmission.Submit(device, frame, default, PipelineStageFlags2.None, default);

    public void Resize(Extent2D extent)
    {
        if (extent.Width == image.Extent.Width && extent.Height == image.Extent.Height)
            return;
        image.Dispose();
        image = CreateImage(extent);
    }

    private GpuImage CreateImage(Extent2D extent) => GpuImage.Create(device, allocator, extent, Format,
        ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit, 1, "headless output");

    public void Dispose() => image.Dispose();
}
