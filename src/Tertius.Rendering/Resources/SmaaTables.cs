using System.Reflection;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The two precomputed tables of SMAA on the GPU, from the reference files in ThirdParty/Smaa, uploaded in the first
/// frame and never changed (docs/plans/anti-aliasing.md, "Design"). The bytes of each file are its texels in memory
/// order, top row first, as the images store them.
/// </summary>
internal sealed class SmaaTables : IDisposable
{
    public const int AreaWidth = 160,
        AreaHeight = 560,
        SearchWidth = 64,
        SearchHeight = 16;

    private readonly VulkanDevice device;
    private bool uploaded;

    public SmaaTables(VulkanDevice device, DeviceAllocator allocator)
    {
        this.device = device;
        const ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit;
        Area = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: new(AreaWidth, AreaHeight),
            format: Format.R8G8Unorm,
            usage: usage,
            mipLevels: 1,
            name: "smaa area table"
        );
        Search = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: new(SearchWidth, SearchHeight),
            format: Format.R8Unorm,
            usage: usage,
            mipLevels: 1,
            name: "smaa search table"
        );
    }

    /// <summary>R8G8_UNORM, 160 × 560: the area of a pixel that each crossing pattern and distance covers.</summary>
    public GpuImage Area { get; }

    /// <summary>R8_UNORM, 64 × 16: how far a search step may jump over the edges that it reads.</summary>
    public GpuImage Search { get; }

    /// <summary>The bytes of an embedded table file, checked against the size of its image.</summary>
    /// <exception cref="InvalidDataException">The file holds a different number of bytes.</exception>
    public static byte[] Load(string file, int bytes)
    {
        string resource = $"Tertius.Rendering.Smaa.{file}";
        using Stream stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"The table {resource} is not embedded.");
        if (stream.Length != bytes)
            throw new InvalidDataException($"{resource} holds {stream.Length} bytes, not {bytes}.");
        byte[] texels = new byte[bytes];
        stream.ReadExactly(texels);
        return texels;
    }

    /// <summary>Records the copies of both tables on the first call, and nothing after it.</summary>
    public void Upload(CommandBuffer commandBuffer, StagingArena staging, BarrierBatch barriers)
    {
        if (uploaded)
            return;
        barriers.Image(Area, ImageUse.TransferDestination, discard: true);
        barriers.Image(Search, ImageUse.TransferDestination, discard: true);
        barriers.Flush(device.Vk, commandBuffer);
        staging.CopyToImage(
            commandBuffer: commandBuffer,
            image: Area,
            texels: Load("AreaTex.bin", AreaWidth * AreaHeight * 2)
        );
        staging.CopyToImage(
            commandBuffer: commandBuffer,
            image: Search,
            texels: Load("SearchTex.bin", SearchWidth * SearchHeight)
        );
        // The SMAA weight pass of this frame reads the tables, so the barrier makes the copies visible to it first.
        barriers.Image(Area, ImageUse.SampledFragment);
        barriers.Image(Search, ImageUse.SampledFragment);
        barriers.Flush(device.Vk, commandBuffer);
        uploaded = true;
    }

    public void Dispose()
    {
        Search.Dispose();
        Area.Dispose();
    }
}
