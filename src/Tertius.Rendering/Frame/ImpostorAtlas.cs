using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The impostor atlas: a fixed number of 512 × 512 tiles, each holding one captured object, plus the depth buffer
/// a capture renders with. Shared by all frames in flight like the G-buffer; every use goes through
/// <see cref="BarrierBatch"/>.
/// </summary>
/// <remarks>
/// <para>Each tile is one layer of two 2D array images with full mip chains:</para>
/// <list type="table">
/// <item><term>color</term><description>R16G16B16A16_SFLOAT: the lit HDR color premultiplied by coverage, and the
/// coverage (1 inside the silhouette, 0 outside). Premultiplied, so that the mip chain and bilinear filtering
/// average color only over covered texels; the billboard divides by coverage again.</description></item>
/// <item><term>depth offset</term><description>R16_SFLOAT: the surface's offset from the bounding sphere's center
/// toward the eye, in units of the capture extent ([-1, 1]), premultiplied by coverage like the color.</description>
/// </item>
/// </list>
/// <para>Layers instead of one large 2D image: a capture renders straight into its tile with the 512 × 512 depth
/// buffer (attachments must cover the render area), and filtering at low mip levels never mixes neighboring
/// tiles. 64 tiles take as much memory as a 4096 × 4096 atlas: 512² × 10 bytes × 4/3 for the mips ≈ 3.3 MB each,
/// about 213 MB in all.</para>
/// </remarks>
internal sealed unsafe class ImpostorAtlas : IDisposable
{
    public const int TileSize = ImpostorManager.TileSize;
    public const Format ColorFormat = Format.R16G16B16A16Sfloat;
    public const Format DepthOffsetFormat = Format.R16Sfloat;
    public const Format CaptureDepthFormat = Format.D32Sfloat;

    /// <summary>A full mip chain of a 512 × 512 tile.</summary>
    public const uint MipLevels = 10;

    /// <summary>The default number of tiles: the 64 tiles of a 4096 × 4096 atlas.</summary>
    public const int DefaultTiles = 64;

    private const FormatFeatureFlags RequiredFeatures =
        FormatFeatureFlags.ColorAttachmentBit
        | FormatFeatureFlags.SampledImageBit
        | FormatFeatureFlags.SampledImageFilterLinearBit
        | FormatFeatureFlags.BlitSrcBit
        | FormatFeatureFlags.BlitDstBit;

    private readonly VulkanDevice device;
    private readonly ImageView[] colorViews;
    private readonly ImageView[] depthOffsetViews;

    public ImpostorAtlas(VulkanDevice device, DeviceAllocator allocator, DescriptorLayouts layouts, int tiles)
    {
        this.device = device;
        uint maxLayers = device.Properties.Limits.MaxImageArrayLayers;
        if (tiles < 1 || tiles > maxLayers)
            throw new ArgumentOutOfRangeException(nameof(tiles), $"The impostor atlas needs 1 to {maxLayers} tiles.");
        foreach (Format format in (ReadOnlySpan<Format>)[ColorFormat, DepthOffsetFormat])
            if ((device.FormatProperties(format).OptimalTilingFeatures & RequiredFeatures) != RequiredFeatures)
                throw new NotSupportedException(
                    $"{format} cannot be a filtered, blittable impostor atlas on {device.DeviceName}."
                );

        Tiles = tiles;
        colorViews = new ImageView[tiles];
        depthOffsetViews = new ImageView[tiles];
        var extent = new Extent2D(TileSize, TileSize);
        const ImageUsageFlags usage =
            ImageUsageFlags.ColorAttachmentBit
            | ImageUsageFlags.SampledBit
            | ImageUsageFlags.TransferSrcBit
            | ImageUsageFlags.TransferDstBit;
        try
        {
            Color = GpuImage.Create(
                device: device,
                allocator: allocator,
                extent: extent,
                format: ColorFormat,
                usage: usage,
                mipLevels: MipLevels,
                name: "impostor atlas color",
                arrayLayers: (uint)tiles,
                arrayView: true
            );
            DepthOffset = GpuImage.Create(
                device: device,
                allocator: allocator,
                extent: extent,
                format: DepthOffsetFormat,
                usage: usage,
                mipLevels: MipLevels,
                name: "impostor atlas depth offset",
                arrayLayers: (uint)tiles,
                arrayView: true
            );
            CaptureDepth = GpuImage.Create(
                device: device,
                allocator: allocator,
                extent: extent,
                format: CaptureDepthFormat,
                usage: ImageUsageFlags.DepthStencilAttachmentBit,
                mipLevels: 1,
                name: "impostor capture depth"
            );
            for (int i = 0; i < tiles; i++)
            {
                colorViews[i] = Color.CreateLayerView((uint)i);
                depthOffsetViews[i] = DepthOffset.CreateLayerView((uint)i);
            }

            // The pool frees the set when the descriptor layouts are disposed.
            Set = layouts.AllocateAtlasSet();
            layouts.WriteImage(set: Set, binding: 0, arrayElement: 0, image: Color);
            layouts.WriteImage(set: Set, binding: 1, arrayElement: 0, image: DepthOffset);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The number of tiles (array layers).</summary>
    public int Tiles { get; }

    public GpuImage Color { get; }
    public GpuImage DepthOffset { get; }

    /// <summary>The depth buffer of a capture: D32, reversed (1 nearest the eye).</summary>
    public GpuImage CaptureDepth { get; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.ImpostorLayout"/>: both arrays and the atlas sampler.</summary>
    public DescriptorSet Set { get; }

    /// <summary>The level-0 view of one tile of the color array, for rendering a capture.</summary>
    public ImageView ColorView(int tile) => colorViews[tile];

    /// <summary>The level-0 view of one tile of the depth-offset array, for rendering a capture.</summary>
    public ImageView DepthOffsetView(int tile) => depthOffsetViews[tile];

    public void Dispose()
    {
        for (int i = 0; i < colorViews.Length; i++)
        {
            if (colorViews[i].Handle != 0)
                device.Vk.DestroyImageView(device.Device, colorViews[i], null);
            if (depthOffsetViews[i].Handle != 0)
                device.Vk.DestroyImageView(device.Device, depthOffsetViews[i], null);
        }

        Array.Clear(colorViews);
        Array.Clear(depthOffsetViews);
        CaptureDepth?.Dispose();
        DepthOffset?.Dispose();
        Color?.Dispose();
    }
}
