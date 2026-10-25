using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The BRDF tables of <see cref="BrdfTables.Shared"/> on the GPU, uploaded in the first frame and never changed
/// (docs/plans/pbr-brdf.md, "The tables"). Vulkan requires linear filtering of both formats on every device.
/// </summary>
internal sealed unsafe class BrdfTableImages : IDisposable
{
    private readonly VulkanDevice device;
    private bool uploaded;

    public BrdfTableImages(VulkanDevice device, DeviceAllocator allocator)
    {
        this.device = device;
        const ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit;
        Dfg = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: new(BrdfTables.DfgSize, BrdfTables.DfgSize),
            format: Format.R16G16Sfloat,
            usage: usage,
            mipLevels: 1,
            name: "dfg table"
        );
        LobeSky = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: new(BrdfTables.LobeSkyWidth, BrdfTables.LobeSkyHeight),
            format: Format.R16Sfloat,
            usage: usage,
            mipLevels: 1,
            name: "lobe-sky table"
        );
    }

    /// <summary>R16G16_SFLOAT, (S, E₁) over μ across and r down.</summary>
    public GpuImage Dfg { get; }

    /// <summary>R16_SFLOAT, the lobe's sky share over (d·up + 1) / 2 across and r down.</summary>
    public GpuImage LobeSky { get; }

    /// <summary>Records the copies of both tables on the first call, and nothing after it.</summary>
    public void Upload(CommandBuffer commandBuffer, StagingArena staging, BarrierBatch barriers)
    {
        if (uploaded)
            return;
        BrdfTables tables = BrdfTables.Shared;
        barriers.Image(Dfg, ImageUse.TransferDestination, discard: true);
        barriers.Image(LobeSky, ImageUse.TransferDestination, discard: true);
        barriers.Flush(device.Vk, commandBuffer);
        Copy(commandBuffer: commandBuffer, staging: staging, image: Dfg, texels: MemoryMarshal.AsBytes(tables.Dfg));
        Copy(
            commandBuffer: commandBuffer,
            staging: staging,
            image: LobeSky,
            texels: MemoryMarshal.AsBytes(tables.LobeSky)
        );
        // The shading passes of this frame read the tables, so the barrier makes the copies visible to them first.
        barriers.Image(Dfg, ImageUse.SampledFragment);
        barriers.Image(LobeSky, ImageUse.SampledFragment);
        barriers.Flush(device.Vk, commandBuffer);
        uploaded = true;
    }

    private void Copy(CommandBuffer commandBuffer, StagingArena staging, GpuImage image, ReadOnlySpan<byte> texels)
    {
        StagingSlice stage = staging.Allocate((ulong)texels.Length);
        texels.CopyTo(stage.Bytes);
        var region = new BufferImageCopy
        {
            BufferOffset = stage.Offset,
            ImageSubresource = new(
                aspectMask: ImageAspectFlags.ColorBit,
                mipLevel: 0,
                baseArrayLayer: 0,
                layerCount: 1
            ),
            ImageExtent = new(image.Extent.Width, image.Extent.Height, 1),
        };
        device.Vk.CmdCopyBufferToImage(
            commandBuffer: commandBuffer,
            srcBuffer: stage.Buffer.Handle,
            dstImage: image.Handle,
            dstImageLayout: ImageLayout.TransferDstOptimal,
            regionCount: 1,
            pRegions: &region
        );
    }

    public void Dispose()
    {
        LobeSky.Dispose();
        Dfg.Dispose();
    }
}
