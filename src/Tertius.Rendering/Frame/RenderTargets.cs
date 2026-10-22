using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The screen-sized images shared by all frames in flight: the G-buffer, the depth buffer and the HDR target.
/// Sharing is safe because every use goes through <see cref="BarrierBatch"/>, and barriers order against earlier
/// submissions on the queue. Resizing recreates everything; the caller makes sure the device is idle.
/// </summary>
/// <remarks>
/// G-buffer layout:
/// <list type="table">
/// <item><term>albedo</term>
/// <description>R8G8B8A8_SRGB: linear albedo, stored with the sRGB curve for precision in the darks.</description></item>
/// <item><term>normal</term>
/// <description>R16G16B16A16_SNORM: two octahedral-encoded world-space normals: the shading normal
/// (after normal mapping) in rg, the interpolated vertex normal in ba. The shadow lookup biases along the second.</description>
/// </item>
/// <item><term>specular-gloss</term><description>R8G8_UNORM: specular intensity and gloss.</description></item>
/// <item><term>emissive</term><description>R16G16B16A16_SFLOAT: emitted radiance.</description></item>
/// <item><term>distance</term><description>R32_SFLOAT: Euclidean distance to the eye; 0 means sky.</description></item>
/// </list>
/// The depth buffer is D32_SFLOAT with reversed Z (1 near, 0 far, cleared to 0, compare GREATER).
/// </remarks>
internal sealed class RenderTargets : IDisposable
{
    public const Format AlbedoFormat = Format.R8G8B8A8Srgb;
    public const Format NormalFormat = Format.R16G16B16A16SNorm;
    public const Format SpecularGlossFormat = Format.R8G8Unorm;
    public const Format EmissiveFormat = Format.R16G16B16A16Sfloat;
    public const Format DistanceFormat = Format.R32Sfloat;
    public const Format DepthFormat = Format.D32Sfloat;
    public const Format HdrFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The G-buffer color formats in attachment order, matching the outputs of Shaders/gbuffer.frag.</summary>
    public static readonly Format[] GBufferFormats =
        [AlbedoFormat, NormalFormat, SpecularGlossFormat, EmissiveFormat, DistanceFormat];

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    public RenderTargets(VulkanDevice device, DeviceAllocator allocator, DescriptorLayouts layouts, Extent2D extent)
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        CheckFormats(device);
        GBufferSet = layouts.AllocateGBufferSet();
        PostSet = layouts.AllocatePostSet();
        Create(extent);
    }

    public Extent2D Extent { get; private set; }
    public GpuImage Albedo { get; private set; } = null!;
    public GpuImage Normal { get; private set; } = null!;
    public GpuImage SpecularGloss { get; private set; } = null!;
    public GpuImage Emissive { get; private set; } = null!;
    public GpuImage Distance { get; private set; } = null!;
    public GpuImage Depth { get; private set; } = null!;
    public GpuImage Hdr { get; private set; } = null!;

    /// <summary>Set 2: the G-buffer for the lighting and transparent passes.</summary>
    public DescriptorSet GBufferSet { get; }

    /// <summary>The HDR image for tone mapping.</summary>
    public DescriptorSet PostSet { get; }

    /// <summary>The G-buffer color targets in attachment order.</summary>
    public GpuImage[] GBuffer { get; private set; } = [];

    /// <summary>Recreates every target at a new size. The device must be idle.</summary>
    public void Resize(Extent2D extent)
    {
        if (extent.Width == Extent.Width && extent.Height == Extent.Height)
            return;
        DestroyImages();
        Create(extent);
    }

    private void Create(Extent2D extent)
    {
        const ImageUsageFlags target = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit;
        Extent = extent;
        Albedo = GpuImage.Create(device, allocator, extent, AlbedoFormat, target, 1, "g-buffer albedo");
        Normal = GpuImage.Create(device, allocator, extent, NormalFormat, target, 1, "g-buffer normal");
        SpecularGloss = GpuImage.Create(device, allocator, extent, SpecularGlossFormat, target, 1,
            "g-buffer specular-gloss");
        Emissive = GpuImage.Create(device, allocator, extent, EmissiveFormat, target, 1, "g-buffer emissive");
        Distance = GpuImage.Create(device, allocator, extent, DistanceFormat, target, 1, "g-buffer distance");
        Depth = GpuImage.Create(device, allocator, extent, DepthFormat, ImageUsageFlags.DepthStencilAttachmentBit, 1,
            "depth");
        Hdr = GpuImage.Create(device, allocator, extent, HdrFormat, target, 1, "hdr");
        GBuffer = [Albedo, Normal, SpecularGloss, Emissive, Distance];
        for (int i = 0; i < GBuffer.Length; i++)
            layouts.WriteImage(GBufferSet, (uint)i, 0, GBuffer[i]);
        layouts.WriteImage(PostSet, 0, 0, Hdr);
    }

    private static void CheckFormats(VulkanDevice device)
    {
        foreach (Format format in (Format[])[.. GBufferFormats, HdrFormat])
        {
            FormatFeatureFlags features = device.FormatProperties(format).OptimalTilingFeatures;
            if ((features & (FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit)) !=
                (FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit))
                throw new NotSupportedException(
                    $"{format} cannot be a sampled color attachment on {device.DeviceName}.");
        }

        if ((device.FormatProperties(HdrFormat).OptimalTilingFeatures & FormatFeatureFlags.ColorAttachmentBlendBit) ==
            0)
            throw new NotSupportedException($"{HdrFormat} does not support blending on {device.DeviceName}.");
        if ((device.FormatProperties(DepthFormat).OptimalTilingFeatures &
             FormatFeatureFlags.DepthStencilAttachmentBit) == 0)
            throw new NotSupportedException($"{DepthFormat} cannot be a depth attachment on {device.DeviceName}.");
    }

    private void DestroyImages()
    {
        foreach (GpuImage image in GBuffer)
            image.Dispose();
        Depth?.Dispose();
        Hdr?.Dispose();
        GBuffer = [];
    }

    public void Dispose() => DestroyImages();
}
