using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>An offscreen R8G8B8A8_SRGB image that stands in for a window. Captures read from it.</summary>
internal sealed class HeadlessTarget : IOutputTarget
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private GpuImage image;

    public HeadlessTarget(VulkanDevice device, DeviceAllocator allocator, Extent2D extent)
    {
        this.device = device;
        this.allocator = allocator;
        image = CreateImage(extent);
    }

    public Format Format => Format.R8G8B8A8Srgb;
    public Extent2D Extent => image.Extent;
    public bool EncodeSrgbInShader => false;
    public bool BgraOrder => false;
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
