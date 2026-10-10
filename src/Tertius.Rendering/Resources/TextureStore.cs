using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Every material texture, addressed by its index in one bindless array of sampled images (set 1). Indices 0 to 2
/// hold the defaults that <see cref="TextureId.None"/> resolves to. Textures get a full mip chain made with blits
/// when the format allows linear blits.
/// </summary>
internal sealed unsafe class TextureStore : IDisposable
{
    /// <summary>The size of the bindless array. The descriptor binding has a variable count of this many.</summary>
    public const int Capacity = 4096;
    /// <summary>Opaque white in sRGB: the default albedo.</summary>
    public const int White = 0;
    /// <summary>(0.5, 0.5, 1) in UNORM: the default normal map, a tangent-space normal of +Z.</summary>
    public const int FlatNormal = 1;
    /// <summary>(1, 1) in UNORM: the default specular-gloss map, which leaves the material's scalars as they are.</summary>
    public const int DefaultSpecularGloss = 2;
    /// <summary>The first index that <see cref="VulkanRenderer.CreateTexture"/> hands out.</summary>
    public const int FirstUserIndex = 3;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly List<GpuImage> images = new();

    public TextureStore(VulkanDevice device, DeviceAllocator allocator)
    {
        this.device = device;
        this.allocator = allocator;
        float anisotropy = MathF.Min(16f, device.Properties.Limits.MaxSamplerAnisotropy);
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            AnisotropyEnable = anisotropy > 1f,
            MaxAnisotropy = anisotropy,
            MinLod = 0f,
            MaxLod = Vk.LodClampNone,
        };
        device.Vk.CreateSampler(device.Device, &info, null, out Sampler sampler).Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, "material sampler");
        MaterialSampler = sampler;
    }

    /// <summary>Linear filtering, repeat addressing, trilinear mips and up to 16x anisotropy.</summary>
    public Sampler MaterialSampler { get; }

    /// <summary>Textures on the GPU. The bindless array is valid for indices below this.</summary>
    public int Count => images.Count;

    public GpuImage this[int index] => images[index];

    /// <summary>Resolves a material's texture slot: ids the renderer did not hand out mean the default.</summary>
    public static uint Resolve(TextureId texture, int fallback) =>
        texture.Value >= FirstUserIndex ? (uint)texture.Value : (uint)fallback;

    /// <summary>Records the upload of queued textures, creating the defaults on the first call.</summary>
    public void Upload(CommandBuffer commandBuffer, List<(int Id, TextureData Data)> pending, StagingArena staging, BarrierBatch barriers)
    {
        if (images.Count == 0)
        {
            Add(commandBuffer, new TextureData(1, 1, [255, 255, 255, 255], Srgb: true), "default white", staging, barriers);
            Add(commandBuffer, new TextureData(1, 1, [128, 128, 255, 255], Srgb: false), "default normal", staging, barriers);
            Add(commandBuffer, new TextureData(1, 1, [255, 255, 0, 255], Srgb: false), "default specular-gloss", staging, barriers);
        }
        foreach ((int id, TextureData data) in pending)
        {
            if (id != images.Count)
                throw new InvalidOperationException($"Texture {id} arrived out of order; expected {images.Count}.");
            Add(commandBuffer, data, $"texture {id}", staging, barriers);
        }
    }

    private void Add(CommandBuffer commandBuffer, TextureData data, string name, StagingArena staging, BarrierBatch barriers)
    {
        Format format = data.Srgb ? Format.R8G8B8A8Srgb : Format.R8G8B8A8Unorm;
        FormatFeatureFlags features = device.FormatProperties(format).OptimalTilingFeatures;
        const FormatFeatureFlags blitFeatures = FormatFeatureFlags.BlitSrcBit | FormatFeatureFlags.BlitDstBit | FormatFeatureFlags.SampledImageFilterLinearBit;
        bool mips = (features & blitFeatures) == blitFeatures;
        uint levels = mips ? (uint)BitOperations.Log2((uint)Math.Max(data.Width, data.Height)) + 1 : 1;
        var extent = new Extent2D((uint)data.Width, (uint)data.Height);
        GpuImage image = GpuImage.Create(device, allocator, extent, format,
            ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit, levels, name);
        images.Add(image);

        StagingSlice stage = staging.Allocate((ulong)data.Rgba.Length);
        data.Rgba.CopyTo(stage.Bytes);

        Vk vk = device.Vk;
        barriers.ImageLevels(image, 0, levels, ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
            PipelineStageFlags2.None, AccessFlags2.None, PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit);
        barriers.Flush(vk, commandBuffer);
        var region = new BufferImageCopy
        {
            BufferOffset = stage.Offset,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D(extent.Width, extent.Height, 1),
        };
        vk.CmdCopyBufferToImage(commandBuffer, stage.Buffer.Handle, image.Handle, ImageLayout.TransferDstOptimal, 1, &region);

        // Each level is blitted from the one above it, which first becomes a transfer source.
        int width = data.Width, height = data.Height;
        for (uint level = 1; level < levels; level++)
        {
            barriers.ImageLevels(image, level - 1, 1, ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal,
                PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit, PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferReadBit);
            barriers.Flush(vk, commandBuffer);
            int nextWidth = Math.Max(1, width / 2), nextHeight = Math.Max(1, height / 2);
            var blit = new ImageBlit
            {
                SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level - 1, 0, 1),
                DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level, 0, 1),
            };
            blit.SrcOffsets[1] = new Offset3D(width, height, 1);
            blit.DstOffsets[1] = new Offset3D(nextWidth, nextHeight, 1);
            vk.CmdBlitImage(commandBuffer, image.Handle, ImageLayout.TransferSrcOptimal, image.Handle, ImageLayout.TransferDstOptimal, 1, &blit, Filter.Linear);
            width = nextWidth;
            height = nextHeight;
        }

        // Every level but the last is a transfer source now; the last is still a transfer destination.
        if (levels > 1)
        {
            barriers.ImageLevels(image, 0, levels - 1, ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferReadBit, PipelineStageFlags2.FragmentShaderBit, AccessFlags2.ShaderSampledReadBit);
        }
        barriers.ImageLevels(image, levels - 1, 1, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit, PipelineStageFlags2.FragmentShaderBit, AccessFlags2.ShaderSampledReadBit);
        barriers.Flush(vk, commandBuffer);
        image.State = ResourceUses.Describe(ImageUse.SampledFragment);
    }

    public void Dispose()
    {
        foreach (GpuImage image in images)
            image.Dispose();
        images.Clear();
        device.Vk.DestroySampler(device.Device, MaterialSampler, null);
    }
}
