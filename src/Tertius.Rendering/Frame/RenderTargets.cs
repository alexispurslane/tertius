using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The screen-sized images shared by all frames in flight: the G-buffer, the depth buffer, the direct light's two images,
/// the HDR target, the output-size bloom levels, under the skin scatter, the diffuse image, under FSR the upscale
/// targets, and under <see cref="ScreenTrace"/>, the depth pyramid (<see cref="PyramidImages"/>).
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
/// <item><term>specular</term><description>R8G8B8A8_UINT: the 8-bit codes of metallic, roughness r₀, reflectance and the lobe's
/// sky share. The G-buffer pass rounds them itself: a UNORM store may take either of the two nearest codes, and a code
/// apart moved a sharp highlight's tail by 14 % on NVIDIA (ticket ter-8esk).</description></item>
/// <item><term>emissive</term><description>R16G16B16A16_SFLOAT: emitted radiance.</description></item>
/// <item><term>distance</term><description>R32_SFLOAT: Euclidean distance to the eye; 0 means sky.</description></item>
/// <item><term>shading</term>
/// <description>R8G8B8A8_UINT: a hair pixel's strand code in rg, 1 in b where the pixel's entity holds a light, the
/// shading model's code in a, all 0 where nothing lit draws (docs/plans/skin-and-hair.md, "The shading target").
/// </description></item>
/// <item><term>motion</term>
/// <description>R16G16B16A16_SFLOAT: xy the last drawn frame's place of the pixel's point minus this frame's place, in
/// units of the image's size, with no jitter; z the ratio of the point's view depth in the last frame to its view depth in
/// this frame (docs/plans/temporal-renderer.md, "Jitter"); alpha 1 where the surface test chose the pixel place of the pixel
/// history rule (screen.glsl), else 0. (2, 2) where the point has no last place. The G-buffer clears it to 0, and the
/// surface test writes the sky's motion and the alpha. It is also a storage image, for the surface test.
/// </description></item>
/// </list>
/// The depth buffer is D32_SFLOAT with reversed Z (1 near, 0 far, cleared to 0, compare GREATER). The direct light's images
/// are R16G16B16A16_SFLOAT: the diffuse and the specular part of each lit pixel's direct light, each divided by its material
/// factor and pre-exposed, 0 for sky and impostors, which the direct pass draws and the composite and the screen tracer
/// read as storage images (docs/plans/temporal-renderer.md, section 7). The diffuse image is R16G16B16A16_SFLOAT: the diffuse
/// light D of each skin pixel, which the composite writes, and the skin mask, whose pixels the skin scatter spreads
/// (docs/plans/skin-and-hair.md, "The diffuse light D").
/// Each bloom level is an HDR image half the size of the one before, rounded up, from half the output's size. Every color
/// target can be copied out, for the read hooks of <see cref="VulkanRenderer"/>. Under the skin scatter the HDR target is
/// also a storage image, which the scatter's columns pass writes; without it it is not, so a frame is the frame from before
/// the scatter.
/// </remarks>
internal sealed class RenderTargets : IDisposable
{
    public const Format BaseColorFormat = Format.R8G8B8A8Srgb;
    public const Format NormalFormat = Format.R16G16B16A16SNorm;
    public const Format SpecularFormat = Format.R8G8B8A8Uint;
    public const Format EmissiveFormat = Format.R16G16B16A16Sfloat;
    public const Format DistanceFormat = Format.R32Sfloat;
    public const Format ShadingFormat = Format.R8G8B8A8Uint;
    public const Format MotionFormat = Format.R16G16B16A16Sfloat;
    public const Format DepthFormat = Format.D32Sfloat;
    public const Format HdrFormat = Format.R16G16B16A16Sfloat;
    public const Format DiffuseFormat = Format.R16G16B16A16Sfloat;
    public const Format DirectFormat = Format.R16G16B16A16Sfloat;

    /// <summary>
    /// The color attachments per pass, and the outputs of a fragment shader, that the renderer asks of a device
    /// (<see cref="ColorAttachmentProblem"/>).
    /// </summary>
    public const uint RequiredColorAttachments = 8;

    /// <summary>
    /// The binding of the shading target in the G-buffer set. The other G-buffer targets take the bindings of their
    /// attachments, 0 to 4, 5, 6 and 8 are taken, and 7 and 9 are free (Shaders/gbuffer_inputs.glsl).
    /// </summary>
    public const uint ShadingBinding = 10;

    /// <summary>The G-buffer color formats in attachment order, matching the outputs of Shaders/gbuffer.frag.</summary>
    public static readonly Format[] GBufferFormats =
    [
        BaseColorFormat,
        NormalFormat,
        SpecularFormat,
        EmissiveFormat,
        DistanceFormat,
        ShadingFormat,
        MotionFormat,
    ];

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly UpscalingSetting upscaling;

    // True under the skin scatter, whose columns pass writes the HDR target as a storage image and which reads the
    // diffuse image.
    private readonly bool diffuse;

    /// <param name="glowLevels">The bloom levels, 0 without bloom.</param>
    /// <param name="depthPyramid">True under <see cref="ScreenTrace"/>, whose rays walk the depth pyramid.</param>
    /// <param name="subsurface">
    /// True under <see cref="SeparableSubsurface"/>, whose columns pass writes the HDR target and which reads the diffuse
    /// image.
    /// </param>
    public RenderTargets(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        Extent2D extent,
        int glowLevels,
        UpscalingSetting upscaling,
        bool depthPyramid,
        bool subsurface
    )
    {
        if (depthPyramid)
            CheckTracedScreen(device: device, extent: extent);
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.upscaling = upscaling;
        if (upscaling is Fsr3Upscaling)
            Upscale = new(
                device: device,
                allocator: allocator,
                largestRenderSize: upscaling.RenderExtent(extent, upscaling.LargestScale),
                outputSize: extent
            );
        diffuse = subsurface;
        CheckFormats(device: device, depthPyramid: depthPyramid, diffuse: diffuse);

        GBufferSet = layouts.AllocateGBufferSet();
        PostSet = layouts.AllocatePostSet();
        MeterSet = layouts.AllocateMeterSet();

        Glow = new GpuImage[glowLevels];
        GlowSources = glowLevels == 0 ? [] : new DescriptorSet[glowLevels + 1];
        for (int i = 0; i < GlowSources.Length; i++)
            GlowSources[i] = layouts.AllocateGlowSourceSet();
        Create(extent);
        if (depthPyramid)
            Pyramid = new(
                device: device,
                allocator: allocator,
                layouts: layouts,
                extent: Extent,
                format: PyramidImages.DepthFormat,
                name: "depth pyramid"
            );
    }

    public Extent2D Extent { get; private set; }
    public Extent2D OutputExtent { get; private set; }
    public UpscaleTargets? Upscale { get; }
    public GpuImage PostHdr => Upscale?.Output ?? Hdr;
    public GpuImage BaseColor { get; private set; }
    public GpuImage Normal { get; private set; }
    public GpuImage Specular { get; private set; }
    public GpuImage Emissive { get; private set; }
    public GpuImage Distance { get; private set; }

    /// <summary>The shading model of each pixel and a hair pixel's strand, the sixth G-buffer target.</summary>
    public GpuImage Shading { get; private set; }

    /// <summary>The motion target, the seventh G-buffer target, which no shading pass reads.</summary>
    public GpuImage Motion { get; private set; }

    public GpuImage Depth { get; private set; }
    public GpuImage Hdr { get; private set; }

    /// <summary>
    /// The diffuse part of each lit pixel's direct light after the direct pass: rgb the light divided by the diffuse factor,
    /// pre-exposed, a 1, and 0 for sky and impostors.
    /// </summary>
    public GpuImage DirectDiffuse { get; private set; }

    /// <summary>The specular part of each lit pixel's direct light, as <see cref="DirectDiffuse"/> holds the diffuse part.</summary>
    public GpuImage DirectSpecular { get; private set; }

    /// <summary>
    /// The diffuse image, or null without the skin scatter. After the composite: rgb the diffuse light D of each skin pixel,
    /// pre-exposed, a 1 at skin, and 0 elsewhere.
    /// </summary>
    public GpuImage? Diffuse { get; private set; }

    /// <summary>The depth pyramid, or null under <see cref="NoScreenTrace"/>.</summary>
    public PyramidImages? Pyramid { get; }

    /// <summary>Set 2: the G-buffer, for the passes that shade surfaces.</summary>
    public DescriptorSet GBufferSet { get; }

    /// <summary>The HDR image for tone mapping.</summary>
    public DescriptorSet PostSet { get; }

    /// <summary>The HDR image and the distance, for the exposure's meter.</summary>
    public DescriptorSet MeterSet { get; }

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
    /// <exception cref="ArgumentOutOfRangeException">
    /// The screen tracer is on, and a side is above <see cref="PyramidImages.MaxSide"/>, or an own-ray list passes
    /// the device's storage buffer range (<see cref="OwnRayList.RangeProblem"/>).
    /// </exception>
    public void Resize(Extent2D extent)
    {
        if (extent.Width == OutputExtent.Width && extent.Height == OutputExtent.Height)
            return;
        // Before anything is destroyed, so that a refused size leaves every target as it was.
        if (Pyramid is not null)
            CheckTracedScreen(device: device, extent: extent);
        DestroyImages();
        Create(extent);
        Pyramid?.Create(Extent);
    }

    /// <summary>The screen tracer's limits on the screen: the depth pyramid's levels, and the own-ray lists' range.</summary>
    private static void CheckTracedScreen(VulkanDevice device, Extent2D extent)
    {
        if (PyramidImages.ScreenProblem(extent.Width, extent.Height) is { } problem)
            throw new ArgumentOutOfRangeException(nameof(extent), problem);
        if (OwnRayList.RangeProblem(extent, device.Properties.Limits.MaxStorageBufferRange) is { } range)
            throw new ArgumentOutOfRangeException(nameof(extent), range);
    }

    /// <summary>Makes every target at <paramref name="extent"/> and writes the sets that read them.</summary>
    [MemberNotNull(
        nameof(BaseColor),
        nameof(Normal),
        nameof(Specular),
        nameof(Emissive),
        nameof(Distance),
        nameof(Shading),
        nameof(Motion),
        nameof(Depth),
        nameof(Hdr),
        nameof(DirectDiffuse),
        nameof(DirectSpecular)
    )]
    private void Create(Extent2D extent)
    {
        OutputExtent = extent;
        extent = upscaling.RenderExtent(extent, upscaling.LargestScale);
        Extent = extent;
        Upscale?.Resize(extent, OutputExtent);
        BaseColor = ColorTarget(extent: extent, format: BaseColorFormat, name: "g-buffer base color");
        Normal = ColorTarget(extent: extent, format: NormalFormat, name: "g-buffer normal", readable: true);
        Specular = ColorTarget(extent: extent, format: SpecularFormat, name: "g-buffer specular", readable: true);
        Emissive = ColorTarget(extent: extent, format: EmissiveFormat, name: "g-buffer emissive");
        Distance = ColorTarget(extent: extent, format: DistanceFormat, name: "g-buffer distance", readable: true);
        Shading = ColorTarget(extent: extent, format: ShadingFormat, name: "g-buffer shading", readable: true);
        // A storage image too, for the surface test, which writes the sky's motion.
        Motion = ColorTarget(
            extent: extent,
            format: MotionFormat,
            name: "g-buffer motion",
            readable: true,
            extraUsage: ImageUsageFlags.StorageBit
        );

        Depth = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: DepthFormat,
            usage: ImageUsageFlags.DepthStencilAttachmentBit,
            mipLevels: 1,
            name: "depth"
        );
        Hdr = ColorTarget(
            extent: extent,
            format: HdrFormat,
            name: "hdr",
            readable: true,
            extraUsage: diffuse ? ImageUsageFlags.StorageBit : 0
        );
        DirectDiffuse = DirectImage(extent: extent, name: "direct diffuse");
        DirectSpecular = DirectImage(extent: extent, name: "direct specular");
        Diffuse = diffuse ? DiffuseImage(extent) : null;

        GBuffer = [BaseColor, Normal, Specular, Emissive, Distance, Shading, Motion];
        WriteSets();
        CreateGlow(OutputExtent);
    }

    /// <summary>Points the G-buffer, direct, post and meter sets at the targets that <see cref="Create"/> made.</summary>
    private void WriteSets()
    {
        // The first five targets' bindings are their attachments' indices; the shading target came after the bindings
        // 5 to 9 were taken, and binding 9 is free since the direct image went. No shading pass reads the motion target, so the set holds none.
        for (int i = 0; i < 5; i++)
            layouts.WriteImage(set: GBufferSet, binding: (uint)i, arrayElement: 0, image: GBuffer[i]);
        layouts.WriteImage(set: GBufferSet, binding: ShadingBinding, arrayElement: 0, image: Shading);
        layouts.WriteImage(set: PostSet, binding: 0, arrayElement: 0, image: PostHdr);
        layouts.WriteImage(set: MeterSet, binding: 0, arrayElement: 0, image: Hdr);
        layouts.WriteImage(set: MeterSet, binding: 1, arrayElement: 0, image: Distance);
    }

    /// <summary>
    /// A direct light image at <paramref name="extent"/>. The direct pass draws it, the composite and the tracer read it as
    /// a storage image, and the light pyramid samples the diffuse one.
    /// </summary>
    private GpuImage DirectImage(Extent2D extent, string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: DirectFormat,
            usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit,
            mipLevels: 1,
            name: name
        );

    /// <summary>
    /// The diffuse image at <paramref name="extent"/>. The composite draws it, the scatter passes read it as a storage
    /// image, and the read hook copies it.
    /// </summary>
    private GpuImage DiffuseImage(Extent2D extent) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: DiffuseFormat,
            usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: "diffuse"
        );

    /// <summary>Makes the bloom levels at their sizes for <paramref name="extent"/>, and points every set at them.</summary>
    private void CreateGlow(Extent2D extent)
    {
        if (Glow.Length == 0)
            return;
        layouts.WriteImage(set: GlowSources[0], binding: 0, arrayElement: 0, image: PostHdr);
        Extent2D level = extent;
        for (int i = 0; i < Glow.Length; i++)
        {
            level = new((level.Width + 1) / 2, (level.Height + 1) / 2);
            Glow[i] = ColorTarget(extent: level, format: HdrFormat, name: $"bloom level {i}", readable: true);
            layouts.WriteImage(set: GlowSources[i + 1], binding: 0, arrayElement: 0, image: Glow[i]);
        }

        layouts.WriteImage(set: PostSet, binding: 1, arrayElement: 0, image: Glow[0]);
    }

    /// <summary>
    /// An image of one level that passes draw into and later passes sample. A <paramref name="readable"/> image can also
    /// be copied out, for the renderer's read hooks. The image has <paramref name="extraUsage"/> beside.
    /// </summary>
    private GpuImage ColorTarget(
        Extent2D extent,
        Format format,
        string name,
        bool readable = false,
        ImageUsageFlags extraUsage = 0
    ) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: format,
            usage: ImageUsageFlags.ColorAttachmentBit
                | ImageUsageFlags.SampledBit
                | (readable ? ImageUsageFlags.TransferSrcBit : 0)
                | extraUsage,
            mipLevels: 1,
            name: name
        );

    /// <summary>
    /// Why a device with <paramref name="maxColorAttachments"/> color attachments per pass and
    /// <paramref name="maxFragmentOutputAttachments"/> outputs of a fragment shader cannot draw the G-buffer, or null when
    /// it can. The renderer asks for 8 of each, which every desktop Linux driver, lavapipe and MoltenVK offer, though the
    /// G-buffer draws 7 (docs/plans/temporal-renderer.md, "Portable Vulkan"). The spec's least is 4 of each.
    /// </summary>
    internal static string? ColorAttachmentProblem(uint maxColorAttachments, uint maxFragmentOutputAttachments)
    {
        if (maxColorAttachments < RequiredColorAttachments)
            return $"The G-buffer needs a device with maxColorAttachments of at least {RequiredColorAttachments}, not {maxColorAttachments}.";
        if (maxFragmentOutputAttachments < RequiredColorAttachments)
            return $"The G-buffer needs a device with maxFragmentOutputAttachments of at least {RequiredColorAttachments}, not {maxFragmentOutputAttachments}.";
        return null;
    }

    /// <summary>
    /// The G-buffer draws seven color attachments, and the surface test writes the motion target as a storage image
    /// (docs/plans/temporal-renderer.md, "Portable Vulkan").
    /// </summary>
    private static void CheckMotionTarget(VulkanDevice device)
    {
        if (
            ColorAttachmentProblem(
                device.Properties.Limits.MaxColorAttachments,
                device.Properties.Limits.MaxFragmentOutputAttachments
            ) is
            { } attachments
        )
            throw new NotSupportedException($"{attachments} ({device.DeviceName})");
        // The spec requires storage support of RGBA16F on every device.
        if ((device.FormatProperties(MotionFormat).OptimalTilingFeatures & FormatFeatureFlags.StorageImageBit) == 0)
            throw new NotSupportedException($"{MotionFormat} cannot be a storage image on {device.DeviceName}.");
    }

    private static void CheckFormats(VulkanDevice device, bool depthPyramid, bool diffuse)
    {
        CheckMotionTarget(device);
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
        if (depthPyramid)
            CheckPyramidFormats(device);
        // The spec requires all three of RGBA16F on every device (docs/plans/skin-and-hair.md, "Required device
        // features"). The light pyramid samples the direct light.
        const FormatFeatureFlags drawnAndStored =
            FormatFeatureFlags.ColorAttachmentBit
            | FormatFeatureFlags.StorageImageBit
            | FormatFeatureFlags.SampledImageBit;
        foreach (Format format in diffuse ? (Format[])[DirectFormat, DiffuseFormat] : [DirectFormat])
            if ((device.FormatProperties(format).OptimalTilingFeatures & drawnAndStored) != drawnAndStored)
                throw new NotSupportedException(
                    $"{format} cannot be a sampled color attachment and a storage image on {device.DeviceName}."
                );
    }

    /// <summary>
    /// The builds write the depth pyramid as a storage image and the tracer samples it. The depth pyramid's tail binds more
    /// storage images in one stage than Vulkan guarantees.
    /// </summary>
    private static void CheckPyramidFormats(VulkanDevice device)
    {
        if (
            PyramidTail.StorageImageProblem(device.Properties.Limits.MaxPerStageDescriptorStorageImages) is
            { } storageImages
        )
            throw new NotSupportedException($"{storageImages} ({device.DeviceName})");
        const FormatFeatureFlags pyramid = FormatFeatureFlags.SampledImageBit | FormatFeatureFlags.StorageImageBit;
        foreach (Format format in (Format[])[PyramidImages.DepthFormat, Format.R32Sfloat])
            if ((device.FormatProperties(format).OptimalTilingFeatures & pyramid) != pyramid)
                throw new NotSupportedException($"{format} cannot be sampled and stored on {device.DeviceName}.");
    }

    private void DestroyImages()
    {
        Pyramid?.Dispose();
        foreach (GpuImage image in GBuffer)
            image.Dispose();
        Depth?.Dispose();
        Hdr?.Dispose();
        DirectDiffuse?.Dispose();
        DirectSpecular?.Dispose();
        Diffuse?.Dispose();
        foreach (GpuImage? level in Glow)
            level?.Dispose();
        GBuffer = [];
    }

    public void Dispose()
    {
        Upscale?.Dispose();
        DestroyImages();
    }
}
