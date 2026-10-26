using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The local shadow atlas: one 2D array depth image of <see cref="Tiles"/> layers of <see cref="TileSize"/>² texels, one
/// tile per layer, in the cascades' format, with reversed depth cleared to 0. The frames in flight share it, as they share
/// the cascades. Its bindings and views: ARCHITECTURE.md, "Local shadows".
/// </summary>
internal sealed unsafe class ShadowAtlas : IDisposable
{
    /// <summary>The binding of the atlas in the G-buffer set, partially bound: empty with <see cref="NoLocalShadows"/>.</summary>
    public const uint AtlasBinding = 8;

    /// <summary>The binding of the frame's tile views (<see cref="GpuShadowTile"/>) in the frame set, partially bound.</summary>
    public const uint TileViewsBinding = 15;

    private readonly VulkanDevice device;
    private readonly ImageView[] layerViews;

    /// <exception cref="ArgumentOutOfRangeException">Tiles is above the device's maxImageArrayLayers.</exception>
    public ShadowAtlas(VulkanDevice device, DeviceAllocator allocator, LocalShadowAtlas setting)
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
    }

    /// <summary>The layers of the atlas: the budget of tiles.</summary>
    public int Tiles { get; }

    /// <summary>The side of every tile in texels.</summary>
    public int TileSize { get; }

    public Format Format { get; }

    /// <summary>The array image. Its default view spans every tile.</summary>
    public GpuImage Image { get; }

    /// <summary>The view of one tile's layer, for rendering into it.</summary>
    public ImageView LayerView(int layer) => layerViews[layer];

    public void Dispose()
    {
        foreach (ImageView view in layerViews)
            if (view.Handle != 0)
                device.Vk.DestroyImageView(device.Device, view, null);
        Array.Clear(layerViews);
        Image.Dispose();
    }
}
