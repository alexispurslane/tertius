using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Every material texture, in one bindless array of sampled images (set 1). The first <see cref="BuiltInCount"/>
/// slots hold the built-in defaults that a material's <see cref="TextureId.None"/> slots resolve to. Texture n from
/// <see cref="VulkanRenderer.CreateTexture"/> lives in slot n + <see cref="BuiltInCount"/>, so no
/// <see cref="TextureId"/> names a built-in, and ids start at 0 without any of them meaning "default". Textures get
/// a full mip chain made with blits when the format allows linear blits.
/// </summary>
internal sealed unsafe class TextureStore : IDisposable
{
    /// <summary>The size of the bindless array. The descriptor binding has a variable count of this many.</summary>
    public const int Capacity = 4096;

    /// <summary>The slot of opaque white in sRGB: the default albedo.</summary>
    public const int White = 0;

    /// <summary>The slot of (0.5, 0.5, 1) in UNORM: the default normal map, a tangent-space normal of +Z.</summary>
    public const int FlatNormal = 1;

    /// <summary>The slot of (1, 1) in UNORM: the default specular-gloss map, which leaves the material's scalars as they are.</summary>
    public const int DefaultSpecularGloss = 2;

    /// <summary>The slots the built-in defaults take, before the first texture that the renderer hands out.</summary>
    public const int BuiltInCount = 3;

    /// <summary>The most textures that <see cref="VulkanRenderer.CreateTexture"/> can create.</summary>
    public const int UserCapacity = Capacity - BuiltInCount;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly List<GpuImage> images = [];

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
        device
            .Vk.CreateSampler(
                device: device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pSampler: out Sampler sampler
            )
            .Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, "material sampler");
        MaterialSampler = sampler;
    }

    /// <summary>Linear filtering, repeat addressing, trilinear mips and up to 16x anisotropy.</summary>
    public Sampler MaterialSampler { get; }

    /// <summary>Slots on the GPU, the built-in defaults included. The bindless array is valid for slots below this.</summary>
    public int Count => images.Count;

    /// <summary>Textures from <see cref="VulkanRenderer.CreateTexture"/> on the GPU.</summary>
    public int UserCount => Math.Max(images.Count - BuiltInCount, 0);

    public GpuImage this[int slot] => images[slot];

    /// <summary>The bindless slot of a texture that the renderer handed out.</summary>
    public static int Slot(TextureId texture) => texture.Value + BuiltInCount;

    /// <summary>
    /// The bindless slot for a material's texture: <paramref name="fallback"/>, a built-in default, for
    /// <see cref="TextureId.None"/>, and the texture's own slot otherwise. The upload queue has already rejected ids
    /// it did not hand out, and it queues a texture no later than the materials that use it.
    /// </summary>
    public static uint Resolve(TextureId texture, int fallback) =>
        texture.IsNone ? (uint)fallback : (uint)Slot(texture);

    /// <summary>Records the upload of queued textures, creating the built-in defaults on the first call.</summary>
    public void Upload(
        CommandBuffer commandBuffer,
        List<(TextureId Id, TextureData Data)> pending,
        StagingArena staging,
        BarrierBatch barriers
    )
    {
        if (images.Count == 0)
        {
            // In slot order: White, FlatNormal, DefaultSpecularGloss.
            Add(
                commandBuffer: commandBuffer,
                data: new(Width: 1, Height: 1, Rgba: [255, 255, 255, 255], Srgb: true),
                name: "default white",
                staging: staging,
                barriers: barriers
            );
            Add(
                commandBuffer: commandBuffer,
                data: new(Width: 1, Height: 1, Rgba: [128, 128, 255, 255], Srgb: false),
                name: "default normal",
                staging: staging,
                barriers: barriers
            );
            Add(
                commandBuffer: commandBuffer,
                data: new(Width: 1, Height: 1, Rgba: [255, 255, 0, 255], Srgb: false),
                name: "default specular-gloss",
                staging: staging,
                barriers: barriers
            );
        }

        foreach ((TextureId id, TextureData data) in pending)
        {
            // The upload queue hands out dense ids under one lock and drains them in order, so each id names the next
            // slot. If that broke, a material would sample another texture.
            Contract.Assert(Slot(id) == images.Count);
            Add(
                commandBuffer: commandBuffer,
                data: data,
                name: $"texture {id.Value}",
                staging: staging,
                barriers: barriers
            );
        }
    }

    private void Add(
        CommandBuffer commandBuffer,
        TextureData data,
        string name,
        StagingArena staging,
        BarrierBatch barriers
    )
    {
        Format format = data.Srgb ? Format.R8G8B8A8Srgb : Format.R8G8B8A8Unorm;
        FormatFeatureFlags features = device.FormatProperties(format).OptimalTilingFeatures;
        const FormatFeatureFlags blitFeatures =
            FormatFeatureFlags.BlitSrcBit
            | FormatFeatureFlags.BlitDstBit
            | FormatFeatureFlags.SampledImageFilterLinearBit;
        bool mips = (features & blitFeatures) == blitFeatures;
        uint levels = mips ? (uint)BitOperations.Log2((uint)Math.Max(data.Width, data.Height)) + 1 : 1;
        var extent = new Extent2D((uint)data.Width, (uint)data.Height);
        var image = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: format,
            usage: ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: levels,
            name: name
        );
        images.Add(image);

        StagingSlice stage = staging.Allocate((ulong)data.Rgba.Length);
        data.Rgba.CopyTo(stage.Bytes);

        Vk vk = device.Vk;
        barriers.ImageLevels(
            image: image,
            baseMip: 0,
            mipCount: levels,
            oldLayout: ImageLayout.Undefined,
            newLayout: ImageLayout.TransferDstOptimal,
            srcStages: PipelineStageFlags2.None,
            srcAccess: AccessFlags2.None,
            dstStages: PipelineStageFlags2.AllTransferBit,
            dstAccess: AccessFlags2.TransferWriteBit
        );
        barriers.Flush(vk, commandBuffer);
        var region = new BufferImageCopy
        {
            BufferOffset = stage.Offset,
            ImageSubresource = new(
                aspectMask: ImageAspectFlags.ColorBit,
                mipLevel: 0,
                baseArrayLayer: 0,
                layerCount: 1
            ),
            ImageExtent = new(extent.Width, extent.Height, 1),
        };
        vk.CmdCopyBufferToImage(
            commandBuffer: commandBuffer,
            srcBuffer: stage.Buffer.Handle,
            dstImage: image.Handle,
            dstImageLayout: ImageLayout.TransferDstOptimal,
            regionCount: 1,
            pRegions: &region
        );

        // Each level is blitted from the one above it, which first becomes a transfer source.
        int width = data.Width,
            height = data.Height;
        for (uint level = 1; level < levels; level++)
        {
            barriers.ImageLevels(
                image: image,
                baseMip: level - 1,
                mipCount: 1,
                oldLayout: ImageLayout.TransferDstOptimal,
                newLayout: ImageLayout.TransferSrcOptimal,
                srcStages: PipelineStageFlags2.AllTransferBit,
                srcAccess: AccessFlags2.TransferWriteBit,
                dstStages: PipelineStageFlags2.AllTransferBit,
                dstAccess: AccessFlags2.TransferReadBit
            );
            barriers.Flush(vk, commandBuffer);
            int nextWidth = Math.Max(1, width / 2),
                nextHeight = Math.Max(1, height / 2);
            var blit = new ImageBlit
            {
                SrcSubresource = new(
                    aspectMask: ImageAspectFlags.ColorBit,
                    mipLevel: level - 1,
                    baseArrayLayer: 0,
                    layerCount: 1
                ),
                DstSubresource = new(
                    aspectMask: ImageAspectFlags.ColorBit,
                    mipLevel: level,
                    baseArrayLayer: 0,
                    layerCount: 1
                ),
            };
            blit.SrcOffsets[1] = new(width, height, 1);
            blit.DstOffsets[1] = new(nextWidth, nextHeight, 1);
            vk.CmdBlitImage(
                commandBuffer: commandBuffer,
                srcImage: image.Handle,
                srcImageLayout: ImageLayout.TransferSrcOptimal,
                dstImage: image.Handle,
                dstImageLayout: ImageLayout.TransferDstOptimal,
                regionCount: 1,
                pRegions: &blit,
                filter: Filter.Linear
            );
            width = nextWidth;
            height = nextHeight;
        }

        // Every level but the last is a transfer source now; the last is still a transfer destination.
        if (levels > 1)
            barriers.ImageLevels(
                image: image,
                baseMip: 0,
                mipCount: levels - 1,
                oldLayout: ImageLayout.TransferSrcOptimal,
                newLayout: ImageLayout.ShaderReadOnlyOptimal,
                srcStages: PipelineStageFlags2.AllTransferBit,
                srcAccess: AccessFlags2.TransferReadBit,
                dstStages: PipelineStageFlags2.FragmentShaderBit,
                dstAccess: AccessFlags2.ShaderSampledReadBit
            );
        barriers.ImageLevels(
            image: image,
            baseMip: levels - 1,
            mipCount: 1,
            oldLayout: ImageLayout.TransferDstOptimal,
            newLayout: ImageLayout.ShaderReadOnlyOptimal,
            srcStages: PipelineStageFlags2.AllTransferBit,
            srcAccess: AccessFlags2.TransferWriteBit,
            dstStages: PipelineStageFlags2.FragmentShaderBit,
            dstAccess: AccessFlags2.ShaderSampledReadBit
        );
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
