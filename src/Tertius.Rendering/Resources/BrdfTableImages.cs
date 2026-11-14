using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The BRDF tables of <see cref="BrdfTables.Shared"/>, the LTC table of <see cref="LtcTable.Shared"/> and the blue-noise
/// mask of <see cref="BlueNoiseMask.Shared"/> on the GPU, uploaded in the first frame and never changed
/// (docs/plans/pbr-brdf.md, "The tables", and ARCHITECTURE.md, "Rectangle lights" and "Randomness and determinism").
/// Vulkan requires linear filtering of the tables' three formats, and sampling of the mask's, on every device.
/// </summary>
internal sealed class BrdfTableImages : IDisposable
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
        // R16G16B16A16_SFLOAT, not R32G32B32A32_SFLOAT, whose linear filtering a device need not offer.
        Ltc = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: new(LtcTable.Size, LtcTable.Size),
            format: Format.R16G16B16A16Sfloat,
            usage: usage,
            mipLevels: 1,
            name: "ltc table"
        );
        BlueNoise = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: new(BlueNoiseMask.ImageSide, BlueNoiseMask.ImageSide),
            format: Format.R8G8Uint,
            usage: usage,
            mipLevels: 1,
            name: "blue-noise mask"
        );
    }

    /// <summary>R16G16_SFLOAT, (S, E₁) over μ across and r down.</summary>
    public GpuImage Dfg { get; }

    /// <summary>R16_SFLOAT, the lobe's sky share over (d·up + 1) / 2 across and r down.</summary>
    public GpuImage LobeSky { get; }

    /// <summary>R16G16B16A16_SFLOAT, the LTC's inverse matrix over √(1 − μ) across and r down.</summary>
    public GpuImage Ltc { get; }

    /// <summary>R8G8_UINT, the reflection rays' blue-noise mask: the radial number and the angle of each texel.</summary>
    public GpuImage BlueNoise { get; }

    /// <summary>Records the copies of the tables and the mask on the first call, and nothing after it.</summary>
    public void Upload(CommandBuffer commandBuffer, StagingArena staging, BarrierBatch barriers)
    {
        if (uploaded)
            return;
        BrdfTables tables = BrdfTables.Shared;
        barriers.Image(Dfg, ImageUse.TransferDestination, discard: true);
        barriers.Image(LobeSky, ImageUse.TransferDestination, discard: true);
        barriers.Image(Ltc, ImageUse.TransferDestination, discard: true);
        barriers.Image(BlueNoise, ImageUse.TransferDestination, discard: true);
        barriers.Flush(device.Vk, commandBuffer);

        staging.CopyToImage(commandBuffer: commandBuffer, image: Dfg, texels: MemoryMarshal.AsBytes(tables.Dfg));
        staging.CopyToImage(
            commandBuffer: commandBuffer,
            image: LobeSky,
            texels: MemoryMarshal.AsBytes(tables.LobeSky)
        );
        staging.CopyToImage(
            commandBuffer: commandBuffer,
            image: Ltc,
            texels: MemoryMarshal.AsBytes(LtcTable.Shared.Texels)
        );
        staging.CopyToImage(commandBuffer: commandBuffer, image: BlueNoise, texels: BlueNoiseMask.Shared.Texels);

        // The shading passes of this frame read the tables, the lighting in fragment shaders and the reflections' trace
        // in a compute shader, so the barrier makes the copies visible to both stages. No pass declares the tables later.
        barriers.Image(Dfg, ImageUse.SampledFragmentCompute);
        barriers.Image(LobeSky, ImageUse.SampledFragmentCompute);
        barriers.Image(Ltc, ImageUse.SampledFragmentCompute);
        barriers.Image(BlueNoise, ImageUse.SampledFragmentCompute);
        barriers.Flush(device.Vk, commandBuffer);
        uploaded = true;
    }

    public void Dispose()
    {
        BlueNoise.Dispose();
        Ltc.Dispose();
        LobeSky.Dispose();
        Dfg.Dispose();
    }
}
