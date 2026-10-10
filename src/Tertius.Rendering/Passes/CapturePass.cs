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
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D(extent.Width, extent.Height, 1),
        };
        vk.CmdCopyImageToBuffer(commandBuffer, context.Output.Handle, ImageLayout.TransferSrcOptimal, buffer.Handle, 1, &region);
        // Makes the copy visible to the CPU once the frame's fence signals.
        barriers.Buffer(buffer, BufferUse.HostRead);
        barriers.Flush(vk, commandBuffer);
    }
}
