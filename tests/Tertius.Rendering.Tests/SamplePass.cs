using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>The producer of <paramref name="signal"/>: it clears each part to <see cref="Value"/> or uploads
/// <see cref="Samples"/> per pixel, and clears each part's sample age to <see cref="AgeCode"/> or uploads
/// <see cref="AgeCodes"/>. Uploads stay alive until the
/// pass is disposed.</summary>
internal sealed unsafe class SamplePass(TemporalSignal signal, DeviceAllocator allocator) : RenderPass
{
    private readonly List<GpuBuffer> uploads = [];

    public Vector4 Value { get; set; }

    public Func<int, int, Vector4>? Samples { get; set; }

    /// <summary>
    /// The sample age of every sample (TemporalSignal.SampleAge, sample_age.glsl): 0, the default, for no evidence, as the
    /// resolve writes it where no ray brings a light age and nothing moves. Its light code, in
    /// <see cref="TemporalSignal.LightCodeMask"/>, is the age in frames of the light that the rays saw, plus 1, and
    /// <see cref="TemporalSignal.SampleMotionBit"/> says that the sample saw motion.
    /// </summary>
    public uint AgeCode { get; set; }

    /// <summary>
    /// The sample age of each pixel's sample, by column and row, in place of <see cref="AgeCode"/> where it is set.
    /// </summary>
    public Func<int, int, ushort>? AgeCodes { get; set; }

    /// <summary>The signal whose samples the pass writes.</summary>
    public TemporalSignal Signal => signal;

    public override string Name => "test sample";

    public override void Record(FrameContext context)
    {
        for (int part = 0; part < signal.Parts; part++)
        {
            context.Barriers.Image(signal.Sample(part), ImageUse.TransferDestination, discard: true);
            if (signal.SampleAge(part) is { } age)
                context.Barriers.Image(age, ImageUse.TransferDestination, discard: true);
        }
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        if (AgeCodes is { } codes)
            UploadAges(context, codes);
        else
            ClearAges(context);
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

    /// <summary>Clears each part's sample age to <see cref="AgeCode"/>.</summary>
    private void ClearAges(FrameContext context)
    {
        var code = new ClearColorValue(uint32_0: AgeCode);
        var whole = new ImageSubresourceRange(
            aspectMask: ImageAspectFlags.ColorBit,
            baseMipLevel: 0,
            levelCount: 1,
            baseArrayLayer: 0,
            layerCount: 1
        );
        for (int part = 0; part < signal.Parts; part++)
            if (signal.SampleAge(part) is { } age)
                context.Vk.CmdClearColorImage(
                    commandBuffer: context.CommandBuffer,
                    image: age.Handle,
                    imageLayout: ImageLayout.TransferDstOptimal,
                    pColor: &code,
                    rangeCount: 1,
                    pRanges: &whole
                );
    }

    /// <summary>Uploads <paramref name="codes"/> into each part's sample age.</summary>
    private void UploadAges(FrameContext context, Func<int, int, ushort> codes)
    {
        Extent2D extent = signal.Sample(0).Extent;
        int width = (int)extent.Width,
            height = (int)extent.Height;
        ushort[] texels = new ushort[width * height];
        for (int row = 0; row < height; row++)
        for (int column = 0; column < width; column++)
            texels[row * width + column] = codes(column, row);
        Copy(
            context: context,
            texels: texels,
            extent: extent,
            target: part => signal.SampleAge(part)?.Handle,
            name: "accumulator test sample ages"
        );
    }

    /// <summary>Copies <paramref name="texels"/> into the image of each part that <paramref name="target"/> names.</summary>
    private void Copy<T>(FrameContext context, T[] texels, Extent2D extent, Func<int, Image?> target, string name)
        where T : unmanaged
    {
        var staging = GpuBuffer.Create(
            device: context.Device,
            allocator: allocator,
            size: (ulong)(texels.Length * sizeof(T)),
            usage: BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.Upload,
            name: name
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
            if (target(part) is { } image)
                context.Vk.CmdCopyBufferToImage(
                    commandBuffer: context.CommandBuffer,
                    srcBuffer: staging.Handle,
                    dstImage: image,
                    dstImageLayout: ImageLayout.TransferDstOptimal,
                    regionCount: 1,
                    pRegions: &region
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
        Copy(
            context: context,
            texels: texels,
            extent: extent,
            target: part => signal.Sample(part).Handle,
            name: "accumulator test samples"
        );
    }

    public override void Dispose()
    {
        foreach (GpuBuffer upload in uploads)
            upload.Dispose();
        base.Dispose();
    }
}
