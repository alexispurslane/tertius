using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// An offscreen sRGB image that stands in for a window. Captures read from it. Its format is R8G8B8A8_SRGB, or
/// B8G8R8A8_SRGB when <c>bgra</c> is set, for a test that needs the BGRA order of a window's swapchain. These are the
/// only two formats, because a capture must know the byte order and the hardware must apply the sRGB curve.
/// </summary>
internal sealed class HeadlessTarget : IOutputTarget
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private GpuImage image;

    public HeadlessTarget(VulkanDevice device, DeviceAllocator allocator, Extent2D extent, bool bgra)
    {
        this.device = device;
        this.allocator = allocator;
        BgraOrder = bgra;
        Format = bgra ? Format.B8G8R8A8Srgb : Format.R8G8B8A8Srgb;
        image = CreateImage(extent);
    }

    public Format Format { get; }
    public Extent2D Extent => image.Extent;
    public bool EncodeSrgbInShader => false;
    public bool BgraOrder { get; }
    public bool CanCapture => true;

    public GpuImage? Acquire(FrameResources frame) => image;

    public void FinishRecording(BarrierBatch barriers, CommandBuffer commandBuffer) { }

    public void SubmitAndPresent(FrameResources frame) =>
        QueueSubmission.Submit(
            device: device,
            frame: frame,
            wait: default,
            waitStages: PipelineStageFlags2.None,
            signal: default
        );

    public void Resize(Extent2D extent)
    {
        if (extent.Width == image.Extent.Width && extent.Height == image.Extent.Height)
            return;
        image.Dispose();
        image = CreateImage(extent);
    }

    private GpuImage CreateImage(Extent2D extent) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: Format,
            usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: "headless output"
        );

    public void Dispose() => image.Dispose();
}
