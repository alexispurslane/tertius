using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>The producer of <paramref name="signal"/>: it clears each part to <see cref="Value"/> or uploads
/// <see cref="Samples"/> per pixel. Uploads stay alive until the pass is disposed.</summary>
internal sealed unsafe class SamplePass(TemporalSignal signal, DeviceAllocator allocator) : RenderPass
{
    private readonly List<GpuBuffer> uploads = [];

    public Vector4 Value { get; set; }

    public Func<int, int, Vector4>? Samples { get; set; }

    /// <summary>The signal whose samples the pass writes.</summary>
    public TemporalSignal Signal => signal;

    public override string Name => "test sample";

    public override void Record(FrameContext context)
    {
        for (int part = 0; part < signal.Parts; part++)
            context.Barriers.Image(signal.Sample(part), ImageUse.TransferDestination, discard: true);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        if (Samples is { } samples)
        {
            Upload(context, samples);
            return;
        }
        var color = new ClearColorValue(float32_0: Value.X, float32_1: Value.Y, float32_2: Value.Z, float32_3: Value.W);
        var whole = new ImageSubresourceRange(
            aspectMask: ImageAspectFlags.ColorBit,
            baseMipLevel: 0,
            levelCount: 1,
            baseArrayLayer: 0,
            layerCount: 1
        );
        for (int part = 0; part < signal.Parts; part++)
            context.Vk.CmdClearColorImage(
                commandBuffer: context.CommandBuffer,
                image: signal.Sample(part).Handle,
                imageLayout: ImageLayout.TransferDstOptimal,
                pColor: &color,
                rangeCount: 1,
                pRanges: &whole
            );
    }

    private void Upload(FrameContext context, Func<int, int, Vector4> samples)
    {
        Extent2D extent = signal.Sample(0).Extent;
        int width = (int)extent.Width,
            height = (int)extent.Height;
        var texels = new Half[4 * width * height];
        for (int row = 0; row < height; row++)
        for (int column = 0; column < width; column++)
        {
            Vector4 value = samples(column, row);
            int at = 4 * (row * width + column);
            texels[at] = (Half)value.X;
            texels[at + 1] = (Half)value.Y;
            texels[at + 2] = (Half)value.Z;
            texels[at + 3] = (Half)value.W;
        }
        var staging = GpuBuffer.Create(
            device: context.Device,
            allocator: allocator,
            size: (ulong)(texels.Length * sizeof(Half)),
            usage: BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.Upload,
            name: "accumulator test samples"
        );
        uploads.Add(staging);
        staging.Write(texels);
        var region = new BufferImageCopy
        {
            ImageSubresource = new(
                aspectMask: ImageAspectFlags.ColorBit,
                mipLevel: 0,
                baseArrayLayer: 0,
                layerCount: 1
            ),
            ImageExtent = new(extent.Width, extent.Height, 1),
        };
        for (int part = 0; part < signal.Parts; part++)
            context.Vk.CmdCopyBufferToImage(
                commandBuffer: context.CommandBuffer,
                srcBuffer: staging.Handle,
                dstImage: signal.Sample(part).Handle,
                dstImageLayout: ImageLayout.TransferDstOptimal,
                regionCount: 1,
                pRegions: &region
            );
    }

    public override void Dispose()
    {
        foreach (GpuBuffer upload in uploads)
            upload.Dispose();
        base.Dispose();
    }
}
