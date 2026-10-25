using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The screen-sized images shared by all frames in flight: the G-buffer, the depth buffer, the HDR target and the bloom
/// levels.
/// Sharing is safe because every use goes through <see cref="BarrierBatch"/>, and barriers order against earlier
/// submissions on the queue. Resizing recreates everything; the caller makes sure the device is idle.
/// </summary>
/// <remarks>
/// G-buffer layout:
/// <list type="table">
/// <item><term>base color</term>
/// <description>R8G8B8A8_SRGB: linear base color, stored with the sRGB curve for precision in the darks.</description></item>
/// <item><term>normal</term>
/// <description>R16G16B16A16_SNORM: two octahedral-encoded world-space normals: the shading normal
/// (after normal mapping) in rg, the interpolated vertex normal in ba. The shadow lookup biases along the second.</description>
/// </item>
/// <item><term>specular</term><description>R8G8B8A8_UNORM: metallic, roughness r₀, reflectance, the lobe's sky share.</description></item>
/// <item><term>emissive</term><description>R16G16B16A16_SFLOAT: emitted radiance.</description></item>
/// <item><term>distance</term><description>R32_SFLOAT: Euclidean distance to the eye; 0 means sky.</description></item>
/// </list>
/// The depth buffer is D32_SFLOAT with reversed Z (1 near, 0 far, cleared to 0, compare GREATER).
/// Each bloom level is an HDR image half the size of the one before, rounded up, from half the output's size.
/// </remarks>
internal sealed class RenderTargets : IDisposable
{
    public const Format BaseColorFormat = Format.R8G8B8A8Srgb;
    public const Format NormalFormat = Format.R16G16B16A16SNorm;
    public const Format SpecularFormat = Format.R8G8B8A8Unorm;
    public const Format EmissiveFormat = Format.R16G16B16A16Sfloat;
    public const Format DistanceFormat = Format.R32Sfloat;
    public const Format DepthFormat = Format.D32Sfloat;
    public const Format HdrFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The G-buffer color formats in attachment order, matching the outputs of Shaders/gbuffer.frag.</summary>
    public static readonly Format[] GBufferFormats =
    [
        BaseColorFormat,
        NormalFormat,
        SpecularFormat,
        EmissiveFormat,
        DistanceFormat,
    ];

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    /// <param name="glowLevels">The bloom levels, 0 without bloom.</param>
    public RenderTargets(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        Extent2D extent,
        int glowLevels
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        CheckFormats(device);
        GBufferSet = layouts.AllocateGBufferSet();
        PostSet = layouts.AllocatePostSet();
        Glow = new GpuImage[glowLevels];
        GlowSources = glowLevels == 0 ? [] : new DescriptorSet[glowLevels + 1];
        for (int i = 0; i < GlowSources.Length; i++)
            GlowSources[i] = layouts.AllocateGlowSourceSet();
        Create(extent);
    }

    public Extent2D Extent { get; private set; }
    public GpuImage BaseColor { get; private set; }
    public GpuImage Normal { get; private set; }
    public GpuImage Specular { get; private set; }
    public GpuImage Emissive { get; private set; }
    public GpuImage Distance { get; private set; }
    public GpuImage Depth { get; private set; }
    public GpuImage Hdr { get; private set; }

    /// <summary>Set 2: the G-buffer for the lighting and transparent passes.</summary>
    public DescriptorSet GBufferSet { get; }

    /// <summary>The HDR image for tone mapping.</summary>
    public DescriptorSet PostSet { get; }

    /// <summary>The G-buffer color targets in attachment order.</summary>
    public GpuImage[] GBuffer { get; private set; } = [];

    /// <summary>The bloom levels, largest first: empty without bloom. After the bloom pass, level 0 holds every level summed.</summary>
    public GpuImage[] Glow { get; }

    /// <summary>
    /// The sets that the draws of the bloom pass read: the HDR image first, then each level of <see cref="Glow"/>. Empty
    /// without bloom.
    /// </summary>
    public DescriptorSet[] GlowSources { get; }

    /// <summary>Recreates every target at a new size. The device must be idle.</summary>
    public void Resize(Extent2D extent)
    {
        if (extent.Width == Extent.Width && extent.Height == Extent.Height)
            return;
        DestroyImages();
        Create(extent);
    }

    [MemberNotNull(
        nameof(BaseColor),
        nameof(Normal),
        nameof(Specular),
        nameof(Emissive),
        nameof(Distance),
        nameof(Depth),
        nameof(Hdr)
    )]
    private void Create(Extent2D extent)
    {
        Extent = extent;
        BaseColor = ColorTarget(extent: extent, format: BaseColorFormat, name: "g-buffer base color");
        Normal = ColorTarget(extent: extent, format: NormalFormat, name: "g-buffer normal");
        Specular = ColorTarget(extent: extent, format: SpecularFormat, name: "g-buffer specular");
        Emissive = ColorTarget(extent: extent, format: EmissiveFormat, name: "g-buffer emissive");
        Distance = ColorTarget(extent: extent, format: DistanceFormat, name: "g-buffer distance");
        Depth = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: DepthFormat,
            usage: ImageUsageFlags.DepthStencilAttachmentBit,
            mipLevels: 1,
            name: "depth"
        );
        Hdr = ColorTarget(extent: extent, format: HdrFormat, name: "hdr");
        GBuffer = [BaseColor, Normal, Specular, Emissive, Distance];
        for (int i = 0; i < GBuffer.Length; i++)
            layouts.WriteImage(set: GBufferSet, binding: (uint)i, arrayElement: 0, image: GBuffer[i]);
        layouts.WriteImage(set: PostSet, binding: 0, arrayElement: 0, image: Hdr);
        CreateGlow(extent);
    }

    /// <summary>Makes the bloom levels at their sizes for <paramref name="extent"/>, and points every set at them.</summary>
    private void CreateGlow(Extent2D extent)
    {
        if (Glow.Length == 0)
            return;
        layouts.WriteImage(set: GlowSources[0], binding: 0, arrayElement: 0, image: Hdr);
        Extent2D level = extent;
        for (int i = 0; i < Glow.Length; i++)
        {
            level = new((level.Width + 1) / 2, (level.Height + 1) / 2);
            Glow[i] = ColorTarget(extent: level, format: HdrFormat, name: $"bloom level {i}");
            layouts.WriteImage(set: GlowSources[i + 1], binding: 0, arrayElement: 0, image: Glow[i]);
        }

        layouts.WriteImage(set: PostSet, binding: 1, arrayElement: 0, image: Glow[0]);
    }

    /// <summary>An image of one level that passes draw into and later passes sample.</summary>
    private GpuImage ColorTarget(Extent2D extent, Format format, string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: format,
            usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            mipLevels: 1,
            name: name
        );

    private static void CheckFormats(VulkanDevice device)
    {
        foreach (Format format in (Format[])[.. GBufferFormats, HdrFormat])
        {
            FormatFeatureFlags features = device.FormatProperties(format).OptimalTilingFeatures;
            if (
                (features & (FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit))
                != (FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit)
            )
                throw new NotSupportedException(
                    $"{format} cannot be a sampled color attachment on {device.DeviceName}."
                );
        }

        if (
            (device.FormatProperties(HdrFormat).OptimalTilingFeatures & FormatFeatureFlags.ColorAttachmentBlendBit) == 0
        )
            throw new NotSupportedException($"{HdrFormat} does not support blending on {device.DeviceName}.");
        if (
            (device.FormatProperties(DepthFormat).OptimalTilingFeatures & FormatFeatureFlags.DepthStencilAttachmentBit)
            == 0
        )
            throw new NotSupportedException($"{DepthFormat} cannot be a depth attachment on {device.DeviceName}.");
    }

    private void DestroyImages()
    {
        foreach (GpuImage image in GBuffer)
            image.Dispose();
        Depth?.Dispose();
        Hdr?.Dispose();
        foreach (GpuImage? level in Glow)
            level?.Dispose();
        GBuffer = [];
    }

    public void Dispose() => DestroyImages();
}
