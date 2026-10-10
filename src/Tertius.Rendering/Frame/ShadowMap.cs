using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The sun shadow map: one depth layer per cascade in a 2D array image, shared by all frames in flight like the
/// G-buffer (every use goes through <see cref="BarrierBatch"/>). The lighting and transparent passes sample the whole
/// array (<see cref="GpuImage.View"/>, G-buffer set binding 5) with the comparison sampler of
/// <see cref="DescriptorLayouts.ShadowSampler"/>; the shadow pass renders each layer through its own view.
/// </summary>
/// <remarks>
/// Depth is reversed like the main view: 1 on the sun side of a cascade, 0 on the far side, cleared to 0, compare
/// GREATER. D32_SFLOAT is used when it can be filtered for hardware PCF; otherwise D16_UNORM, with a shallower box
/// (<see cref="DepthRangeScale"/>) so that its 16 bits still resolve about 30 cm over the 19 km depth of the last
/// cascade.
/// </remarks>
internal sealed unsafe class ShadowMap : IDisposable
{
    public const int DefaultSize = 2048;
    public const int MinimumSize = 256;

    private const FormatFeatureFlags RequiredFeatures =
        FormatFeatureFlags.DepthStencilAttachmentBit | FormatFeatureFlags.SampledImageBit | FormatFeatureFlags.SampledImageFilterLinearBit;

    private readonly VulkanDevice device;
    private readonly ImageView[] layerViews = new ImageView[ShadowCascades.Count];

    public ShadowMap(VulkanDevice device, DeviceAllocator allocator, int size)
    {
        this.device = device;
        if (size < MinimumSize || size > device.Properties.Limits.MaxImageDimension2D)
            throw new ArgumentOutOfRangeException(nameof(size), $"The shadow map size must be {MinimumSize} to {device.Properties.Limits.MaxImageDimension2D}.");
        Size = size;
        Format = ChooseFormat(device);
        Image = GpuImage.Create(device, allocator, new Extent2D((uint)size, (uint)size), Format,
            ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit, 1,
            "sun shadow cascades", ShadowCascades.Count);
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

    /// <summary>The side of every cascade in texels.</summary>
    public int Size { get; }

    /// <summary>
    /// How many times deeper than wide each cascade box is (see <see cref="ShadowCascades"/>, Depth): 16 with D32's
    /// precision to spare, 4 with D16, which would otherwise lose too much of it.
    /// </summary>
    public float DepthRangeScale => Format == Format.D32Sfloat ? 16f : 4f;
    public Format Format { get; }
    /// <summary>The array image. Its default view spans every cascade.</summary>
    public GpuImage Image { get; }

    /// <summary>The view of one cascade's layer, for rendering into it.</summary>
    public ImageView LayerView(int cascade) => layerViews[cascade];

    public static Format ChooseFormat(VulkanDevice device)
    {
        foreach (Format format in (ReadOnlySpan<Format>)[Format.D32Sfloat, Format.D16Unorm])
        {
            if ((device.FormatProperties(format).OptimalTilingFeatures & RequiredFeatures) == RequiredFeatures)
                return format;
        }
        throw new NotSupportedException($"No depth format can be a filtered shadow map on {device.DeviceName}.");
    }

    public void Dispose()
    {
        foreach (ImageView view in layerViews)
        {
            if (view.Handle != 0)
                device.Vk.DestroyImageView(device.Device, view, null);
        }
        Array.Clear(layerViews);
        Image.Dispose();
    }
}
