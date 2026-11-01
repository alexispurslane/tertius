using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The local shadow atlas: one 2D array depth image of <see cref="Tiles"/> layers of <see cref="TileSize"/>² texels, one
/// tile per layer, in the cascades' format, with reversed depth cleared to 0, and its <see cref="Bounds"/>, derived from
/// each tile when <see cref="LocalShadowPass"/> draws it. The frames in flight share both, as they share the cascades. Its
/// bindings and views: ARCHITECTURE.md, "Local shadows".
/// </summary>
internal sealed unsafe class ShadowAtlas : IDisposable
{
    /// <summary>The binding of the atlas in the G-buffer set, partially bound: empty with <see cref="NoLocalShadows"/>.</summary>
    public const uint AtlasBinding = 8;

    /// <summary>The binding of <see cref="Bounds"/> in the G-buffer set, partially bound: empty with <see cref="NoLocalShadows"/>.</summary>
    public const uint BoundsBinding = 11;

    /// <summary>The format of <see cref="Bounds"/>: R the nearest depth of a window, G its farthest.</summary>
    public const Format BoundsFormat = Format.R32G32Sfloat;

    /// <summary>The binding of the frame's tile views (<see cref="GpuShadowTile"/>) in the frame set, partially bound.</summary>
    public const uint TileViewsBinding = 15;

    private readonly VulkanDevice device;
    private readonly ImageView[] layerViews;

    /// <exception cref="ArgumentOutOfRangeException">Tiles is above the device's maxImageArrayLayers.</exception>
    public ShadowAtlas(VulkanDevice device, DeviceAllocator allocator, DescriptorLayouts layouts, LocalShadowAtlas setting)
    {
        this.device = device;
        uint maxLayers = device.Properties.Limits.MaxImageArrayLayers;
        if ((uint)setting.Tiles > maxLayers)
            throw new ArgumentOutOfRangeException(
                nameof(setting),
                $"LocalShadowAtlas.Tiles must be at most the device's maxImageArrayLayers, {maxLayers}, not {setting.Tiles}."
            );
        Tiles = setting.Tiles;
        TileSize = setting.TileSize;
        Format = ShadowMap.ChooseFormat(device);
        layerViews = new ImageView[Tiles];
        Image = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: new((uint)TileSize, (uint)TileSize),
            format: Format,
            usage: ImageUsageFlags.DepthStencilAttachmentBit
                | ImageUsageFlags.SampledBit
                | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: "local shadow atlas",
            arrayLayers: (uint)Tiles,
            // The shaders declare an array, also for an atlas of one tile.
            arrayView: true
        );
        Bounds = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: new((uint)TileSize / 2, (uint)TileSize / 2),
            format: BoundsFormat,
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: "local shadow bounds",
            arrayLayers: (uint)Tiles,
            arrayView: true
        );
        try
        {
            for (int i = 0; i < layerViews.Length; i++)
                layerViews[i] = Image.CreateLayerView((uint)i);
        }
        catch
        {
            Dispose();
            throw;
        }

        BoundsSet = layouts.AllocateShadowBoundsSet();
        layouts.WriteImage(set: BoundsSet, binding: 0, arrayElement: 0, image: Image);
        layouts.WriteStorageImage(set: BoundsSet, binding: 1, image: Bounds);
    }

    /// <summary>The layers of the atlas: the budget of tiles.</summary>
    public int Tiles { get; }

    /// <summary>The side of every tile in texels.</summary>
    public int TileSize { get; }

    public Format Format { get; }

    /// <summary>The array image. Its default view spans every tile.</summary>
    public GpuImage Image { get; }

    /// <summary>
    /// The bounds of every tile, half the tile's side, one layer per tile: texel k holds the nearest (R) and the farthest
    /// (G) reversed depth of the tile's texels 2k to 2k + 5 along each axis, clamped to the tile (Shaders/shadow_bounds.comp).
    /// A layer's bounds are those of the tile that the layer held when <see cref="LocalShadowPass"/> last drew it.
    /// </summary>
    public GpuImage Bounds { get; }

    /// <summary>The set of <see cref="DescriptorLayouts.ShadowBoundsLayout"/>: the atlas and <see cref="Bounds"/>.</summary>
    public DescriptorSet BoundsSet { get; }

    /// <summary>
    /// Declares that the next pass samples the tiles in <paramref name="use"/>: the atlas and its bounds, which every tile
    /// lookup reads first (tileShadow in Shaders/shadows.glsl).
    /// </summary>
    public void DeclareSampled(BarrierBatch barriers, ImageUse use)
    {
        barriers.Image(Image, use);
        barriers.Image(Bounds, use);
    }

    /// <summary>The view of one tile's layer, for rendering into it.</summary>
    public ImageView LayerView(int layer) => layerViews[layer];

    public void Dispose()
    {
        foreach (ImageView view in layerViews)
            if (view.Handle != 0)
                device.Vk.DestroyImageView(device.Device, view, null);
        Array.Clear(layerViews);
        Bounds.Dispose();
        Image.Dispose();
    }
}
