using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>Copies the final image into the host-visible capture buffer when a capture was requested.</summary>
internal sealed unsafe class CapturePass : RenderPass
{
    public override string Name => "capture";

    public override void Record(FrameContext context)
    {
        GpuBuffer? buffer = context.CaptureBuffer;
        if (buffer == null)
            return;
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        barriers.Image(context.Output, ImageUse.TransferSource);
        barriers.Buffer(buffer, BufferUse.TransferDestination);
        barriers.Flush(vk, commandBuffer);
        Extent2D extent = context.Output.Extent;
        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            // Tightly packed rows.
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new(aspectMask: ImageAspectFlags.ColorBit, mipLevel: 0, baseArrayLayer: 0, layerCount: 1),
            ImageExtent = new(extent.Width, extent.Height, 1)
        };
        vk.CmdCopyImageToBuffer(commandBuffer: commandBuffer, srcImage: context.Output.Handle, srcImageLayout: ImageLayout.TransferSrcOptimal, dstBuffer: buffer.Handle, regionCount: 1,
            pRegions: &region);
        // Makes the copy visible to the CPU once the frame's fence signals.
        barriers.Buffer(buffer, BufferUse.HostRead);
        barriers.Flush(vk, commandBuffer);
    }
}
