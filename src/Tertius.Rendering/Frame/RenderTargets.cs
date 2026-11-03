using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The screen-sized images shared by all frames in flight: the G-buffer, the depth buffer, the direct image, the estimate
/// target, the HDR target, the bloom levels, under the skin scatter or the screen-space light, the diffuse image, when SMAA is on, the images of SMAA (<see cref="SmaaTargets"/>), and under
/// <see cref="ConeTracedReflections"/>, the pyramids of the reflections (<see cref="ReflectionTargets"/>).
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
/// <item><term>shading</term>
/// <description>R8G8B8A8_UINT: a hair pixel's strand code in rg, 1 in b where the pixel's entity holds a light, the
/// shading model's code in a, all 0 where nothing lit draws (docs/plans/skin-and-hair.md, "The shading target").
/// </description></item>
/// <item><term>motion</term>
/// <description>R16G16B16A16_SFLOAT: xy the last drawn frame's place of the pixel's point minus this frame's place, in
/// units of the image's size, with no jitter; z the ratio of the point's view depth in the last frame to its view depth in
/// this frame (docs/plans/temporal-renderer.md, "Jitter"). (2, 2) where the point has no last place. The G-buffer clears
/// it to 0, and the surface test writes the sky's motion. It is also a storage image, for the surface test.
/// </description></item>
/// </list>
/// The depth buffer is D32_SFLOAT with reversed Z (1 near, 0 far, cleared to 0, compare GREATER). The direct image and the
/// estimate target are R16G16B16A16_SFLOAT (docs/plans/screen-space-illumination.md, "Images and buffers"): the direct
/// light of each pixel, and the bounce G in rgb with the occlusion V in a, (0, 0, 0, 1) under NoScreenSpaceLight.
/// The diffuse image is R16G16B16A16_SFLOAT too: the diffuse part of the direct light of each lit pixel, which the
/// bounce light sends on, and the skin mask, whose pixels the skin scatter spreads (docs/plans/skin-and-hair.md, "The
/// diffuse light D").
/// Each bloom level is an HDR image half the size of the one before, rounded up, from half the output's size. Every color
/// target can be copied out, for the read hooks of <see cref="VulkanRenderer"/>. Under reflections or the skin scatter
/// the HDR target is also a storage image, which the trace and the scatter's columns pass write; without both it is not,
/// so a frame is the frame from before them.
/// </remarks>
internal sealed class RenderTargets : IDisposable
{
    public const Format BaseColorFormat = Format.R8G8B8A8Srgb;
    public const Format NormalFormat = Format.R16G16B16A16SNorm;
    public const Format SpecularFormat = Format.R8G8B8A8Unorm;
    public const Format EmissiveFormat = Format.R16G16B16A16Sfloat;
    public const Format DistanceFormat = Format.R32Sfloat;
    public const Format ShadingFormat = Format.R8G8B8A8Uint;
    public const Format MotionFormat = Format.R16G16B16A16Sfloat;
    public const Format DepthFormat = Format.D32Sfloat;
    public const Format HdrFormat = Format.R16G16B16A16Sfloat;
    public const Format DirectFormat = Format.R16G16B16A16Sfloat;
    public const Format EstimateFormat = EstimatePacking.Format;
    public const Format DiffuseFormat = Format.R16G16B16A16Sfloat;

    /// <summary>
    /// The color attachments per pass, and the outputs of a fragment shader, that the renderer asks of a device
    /// (<see cref="ColorAttachmentProblem"/>).
    /// </summary>
    public const uint RequiredColorAttachments = 8;

    /// <summary>The binding of the estimate target in the G-buffer set (Shaders/gbuffer_inputs.glsl).</summary>
    public const uint EstimateBinding = 7;

    /// <summary>
    /// The binding of the direct image in the G-buffer set. Binding 8 is the lights' shadow atlas
    /// (docs/plans/screen-space-illumination.md, "Push constants and sets").
    /// </summary>
    public const uint DirectBinding = 9;

    /// <summary>
    /// The binding of the shading target in the G-buffer set. The other G-buffer targets take the bindings of their
    /// attachments, 0 to 4, and 5 to 9 are taken (Shaders/gbuffer_inputs.glsl).
    /// </summary>
    public const uint ShadingBinding = 10;

    // The upsample pass writes the target as a storage image, the clear as a transfer, and the ambient pass samples it.
    // VulkanRenderer.ReadEstimate copies it out.
    private const FormatFeatureFlags EstimateFeatures =
        FormatFeatureFlags.SampledImageBit
        | FormatFeatureFlags.StorageImageBit
        | FormatFeatureFlags.TransferDstBit
        | FormatFeatureFlags.TransferSrcBit;

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

    // True under reflections or the skin scatter, whose compute passes write the HDR target as a storage image.
    private readonly bool hdrStorage;

    // True under the skin scatter or the screen-space light, which read the diffuse image.
    private readonly bool diffuse;

    // True from the creation of the estimate target until the first frame after it records the clear to (0, 0, 0, 1).
    private bool estimateNeedsClear;

    /// <param name="glowLevels">The bloom levels, 0 without bloom.</param>
    /// <param name="smaaTables">The SMAA tables when SMAA is on, or null when it is off.</param>
    /// <param name="reflections">The resolution of the reflections' trace, to make their images, or null under <see cref="NoReflections"/>.</param>
    /// <param name="subsurface">True under <see cref="SeparableSubsurface"/>, whose columns pass writes the HDR target.</param>
    /// <param name="diffuse">True under the skin scatter or the screen-space light, which read the diffuse image.</param>
    public RenderTargets(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        Extent2D extent,
        int glowLevels,
        SmaaTables? smaaTables,
        TraceResolution? reflections,
        bool subsurface,
        bool diffuse
    )
    {
        if (reflections is { } traced)
            CheckReflectedScreen(device: device, extent: extent, resolution: traced);
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        hdrStorage = reflections is not null || subsurface;
        this.diffuse = diffuse;
        CheckFormats(device: device, reflections: reflections is not null, diffuse: diffuse);
        GBufferSet = layouts.AllocateGBufferSet();
        PostSet = layouts.AllocatePostSet();
        MeterSet = layouts.AllocateMeterSet();
        Glow = new GpuImage[glowLevels];
        GlowSources = glowLevels == 0 ? [] : new DescriptorSet[glowLevels + 1];
        for (int i = 0; i < GlowSources.Length; i++)
            GlowSources[i] = layouts.AllocateGlowSourceSet();
        Create(extent);
        if (reflections is { } resolution)
            Reflections = new(
                device: device,
                allocator: allocator,
                layouts: layouts,
                extent: extent,
                hdr: Hdr,
                resolution: resolution
            );
        if (smaaTables is not null)
            Smaa = new(device: device, allocator: allocator, layouts: layouts, tables: smaaTables, extent: extent);
    }

    public Extent2D Extent { get; private set; }
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

    /// <summary>The direct light of each pixel, pre-exposed, 0 for sky. The direct pass writes it.</summary>
    public GpuImage Direct { get; private set; }

    /// <summary>
    /// The estimate of each pixel: rgb the bounce, pre-exposed, a the occlusion. Cleared to (0, 0, 0, 1) in the first
    /// frame after each creation.
    /// </summary>
    public GpuImage Estimate { get; private set; }

    /// <summary>
    /// The diffuse image, or null without the skin scatter and the screen-space light. After the direct pass: rgb the
    /// diffuse part of each lit pixel's direct light, pre-exposed, a 1 at skin, else 0. The ambient pass adds D's ambient part at
    /// skin under the scatter.
    /// </summary>
    public GpuImage? Diffuse { get; private set; }

    /// <summary>The images of SMAA, or null when SMAA is off.</summary>
    public SmaaTargets? Smaa { get; }

    /// <summary>The pyramids of the reflections, or null under <see cref="NoReflections"/>.</summary>
    public ReflectionTargets? Reflections { get; }

    /// <summary>Set 2: the G-buffer, the estimate target and the direct image, for the passes that shade surfaces.</summary>
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
    /// Reflections are on, and a side is above <see cref="ReflectionTargets.MaxSide"/>, or the own-ray list passes the
    /// device's storage buffer range (<see cref="OwnRayList.RangeProblem"/>).
    /// </exception>
    public void Resize(Extent2D extent)
    {
        if (extent.Width == Extent.Width && extent.Height == Extent.Height)
            return;
        // Before anything is destroyed, so that a refused size leaves every target as it was.
        if (Reflections is not null)
            CheckReflectedScreen(device: device, extent: extent, resolution: Reflections.Resolution);
        DestroyImages();
        Create(extent);
        // The trace keeps its resolution across a resize.
        Reflections?.Create(extent: extent, hdr: Hdr, resolution: Reflections.Resolution);
        Smaa?.Resize(extent);
    }

    private static void CheckReflectedScreen(VulkanDevice device, Extent2D extent, TraceResolution resolution)
    {
        if (ReflectionTargets.ScreenProblem(extent.Width, extent.Height) is { } problem)
            throw new ArgumentOutOfRangeException(nameof(extent), problem);
        // A full-resolution trace binds no own-ray list, so a screen above the range still reflects at full resolution.
        if (
            resolution == TraceResolution.Half
            && OwnRayList.RangeProblem(extent, device.Properties.Limits.MaxStorageBufferRange) is { } range
        )
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
        nameof(Direct),
        nameof(Estimate)
    )]
    private void Create(Extent2D extent)
    {
        Extent = extent;
        BaseColor = ColorTarget(extent: extent, format: BaseColorFormat, name: "g-buffer base color");
        Normal = ColorTarget(extent: extent, format: NormalFormat, name: "g-buffer normal", readable: true);
        Specular = ColorTarget(extent: extent, format: SpecularFormat, name: "g-buffer specular");
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
            extraUsage: hdrStorage ? ImageUsageFlags.StorageBit : 0
        );
        Direct = ColorTarget(extent: extent, format: DirectFormat, name: "direct", readable: true);
        Estimate = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: EstimateFormat,
            usage: ImageUsageFlags.SampledBit
                | ImageUsageFlags.StorageBit
                | ImageUsageFlags.TransferDstBit
                | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: "estimate"
        );
        estimateNeedsClear = true;
        Diffuse = diffuse ? DiffuseImage(extent) : null;

        GBuffer = [BaseColor, Normal, Specular, Emissive, Distance, Shading, Motion];
        WriteSets();
        CreateGlow(extent);
    }

    /// <summary>Points the G-buffer, post and meter sets at the targets that <see cref="Create"/> made.</summary>
    private void WriteSets()
    {
        // The first five targets' bindings are their attachments' indices; the shading target came after the bindings
        // 5 to 9 were taken. No shading pass reads the motion target, so the set holds none.
        for (int i = 0; i < 5; i++)
            layouts.WriteImage(set: GBufferSet, binding: (uint)i, arrayElement: 0, image: GBuffer[i]);
        layouts.WriteImage(set: GBufferSet, binding: ShadingBinding, arrayElement: 0, image: Shading);
        layouts.WriteImage(set: GBufferSet, binding: EstimateBinding, arrayElement: 0, image: Estimate);
        layouts.WriteImage(set: GBufferSet, binding: DirectBinding, arrayElement: 0, image: Direct);

        layouts.WriteImage(set: PostSet, binding: 0, arrayElement: 0, image: Hdr);
        layouts.WriteImage(set: MeterSet, binding: 0, arrayElement: 0, image: Hdr);
        layouts.WriteImage(set: MeterSet, binding: 1, arrayElement: 0, image: Distance);
    }

    /// <summary>
    /// The diffuse image at <paramref name="extent"/>. The direct pass draws it, the bounce light's copy and the scatter
    /// passes read it as a storage image, the ambient pass adds to it at skin pixels as a storage image, and the read hook
    /// copies it.
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
        layouts.WriteImage(set: GlowSources[0], binding: 0, arrayElement: 0, image: Hdr);
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
    /// Clears the estimate target to the open estimate, no bounce, full visibility and an open share of 1, if this is the
    /// first frame since its creation, and does nothing otherwise.
    /// </summary>
    public void RecordEstimateClear(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers)
    {
        if (!estimateNeedsClear)
            return;
        barriers.Image(Estimate, ImageUse.TransferDestination, discard: true);
        barriers.Flush(vk, commandBuffer);
        ClearColorValue openEstimate = EstimatePacking.OpenTexel;
        var whole = new ImageSubresourceRange(
            aspectMask: ImageAspectFlags.ColorBit,
            baseMipLevel: 0,
            levelCount: 1,
            baseArrayLayer: 0,
            layerCount: 1
        );
        vk.CmdClearColorImage(
            commandBuffer: commandBuffer,
            image: Estimate.Handle,
            imageLayout: ImageLayout.TransferDstOptimal,
            pColor: in openEstimate,
            rangeCount: 1,
            pRanges: in whole
        );
        estimateNeedsClear = false;
    }

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

    private static void CheckFormats(VulkanDevice device, bool reflections, bool diffuse)
    {
        CheckMotionTarget(device);
        foreach (Format format in (Format[])[.. GBufferFormats, HdrFormat, DirectFormat])
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
        if ((device.FormatProperties(EstimateFormat).OptimalTilingFeatures & EstimateFeatures) != EstimateFeatures)
            throw new NotSupportedException(
                $"{EstimateFormat} cannot be sampled, stored, cleared and copied by a transfer on {device.DeviceName}."
            );
        if (reflections)
            CheckReflectionFormats(device);
        // The spec requires both of RGBA16F on every device (docs/plans/skin-and-hair.md, "Required device features").
        const FormatFeatureFlags diffuseFeatures =
            FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.StorageImageBit;
        if (
            diffuse
            && (device.FormatProperties(DiffuseFormat).OptimalTilingFeatures & diffuseFeatures) != diffuseFeatures
        )
            throw new NotSupportedException(
                $"{DiffuseFormat} cannot be a color attachment and a storage image on {device.DeviceName}."
            );
    }

    /// <summary>
    /// The builds write both pyramids as storage images and the trace samples them, the radiance pyramid trilinearly, and
    /// the trace writes the HDR target as a storage image. The depth pyramid's tail binds more storage images in one stage
    /// than Vulkan guarantees.
    /// </summary>
    private static void CheckReflectionFormats(VulkanDevice device)
    {
        if (
            DepthPyramidTail.StorageImageProblem(device.Properties.Limits.MaxPerStageDescriptorStorageImages) is
            { } storageImages
        )
            throw new NotSupportedException($"{storageImages} ({device.DeviceName})");
        const FormatFeatureFlags pyramid = FormatFeatureFlags.SampledImageBit | FormatFeatureFlags.StorageImageBit;
        const FormatFeatureFlags radiance = pyramid | FormatFeatureFlags.SampledImageFilterLinearBit;
        if ((device.FormatProperties(ReflectionTargets.DepthFormat).OptimalTilingFeatures & pyramid) != pyramid)
            throw new NotSupportedException(
                $"{ReflectionTargets.DepthFormat} cannot be sampled and stored on {device.DeviceName}."
            );
        if ((device.FormatProperties(ReflectionTargets.RadianceFormat).OptimalTilingFeatures & radiance) != radiance)
            throw new NotSupportedException(
                $"{ReflectionTargets.RadianceFormat} cannot be stored and filtered linearly on {device.DeviceName}."
            );
    }

    private void DestroyImages()
    {
        Reflections?.DestroyImages();
        foreach (GpuImage image in GBuffer)
            image.Dispose();
        Depth?.Dispose();
        Hdr?.Dispose();
        Direct?.Dispose();
        Estimate?.Dispose();
        Diffuse?.Dispose();
        foreach (GpuImage? level in Glow)
            level?.Dispose();
        GBuffer = [];
    }

    public void Dispose()
    {
        Smaa?.Dispose();
        DestroyImages();
    }
}
