using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// An offscreen output for tests over an R8G8B8A8_UNORM image, as a window whose surface offers only UNORM formats has.
/// The image stores what the shader writes, so the pass that writes the output must apply the sRGB curve itself.
/// </summary>
internal sealed class UnormOutput(VulkanDevice device, DeviceAllocator allocator, Extent2D extent) : IOutputTarget
{
    private GpuImage image = CreateImage(device, allocator, extent);

    public Format Format => Format.R8G8B8A8Unorm;
    public Extent2D Extent => image.Extent;
    public bool EncodeSrgbInShader => true;
    public bool BgraOrder => false;
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
        image.Dispose();
        image = CreateImage(device, allocator, extent);
    }

    private static GpuImage CreateImage(VulkanDevice device, DeviceAllocator allocator, Extent2D extent) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: Format.R8G8B8A8Unorm,
            usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: "unorm test output"
        );

    public void Dispose() => image.Dispose();
}
