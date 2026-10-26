using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The screen-sized images shared by all frames in flight: the G-buffer, the depth buffer, the direct image, the estimate
/// target, the HDR target, the bloom levels, when SMAA is on, the images of SMAA (<see cref="SmaaTargets"/>), when the
/// reflections or the screen-space light read it, the depth pyramid (<see cref="DepthPyramidImage"/>), and under
/// <see cref="ConeTracedReflections"/>, the radiance pyramid of the reflections (<see cref="ReflectionTargets"/>).
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
/// The depth buffer is D32_SFLOAT with reversed Z (1 near, 0 far, cleared to 0, compare GREATER). The direct image and the
/// estimate target are R16G16B16A16_SFLOAT (docs/plans/screen-space-illumination.md, "Images and buffers"): the direct
/// light of each pixel, and the bounce G in rgb with the occlusion V in a, (0, 0, 0, 1) under NoScreenSpaceLight.
/// Each bloom level is an HDR image half the size of the one before, rounded up, from half the output's size. Every color
/// target can be copied out, for the read hooks of <see cref="VulkanRenderer"/>. Under reflections the HDR target is
/// also a storage image, which the trace writes; without them it is not, so a frame is the frame from before them.
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
    public const Format DirectFormat = Format.R16G16B16A16Sfloat;
    public const Format EstimateFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The binding of the estimate target in the G-buffer set (Shaders/gbuffer_inputs.glsl).</summary>
    public const uint EstimateBinding = 7;

    /// <summary>
    /// The binding of the direct image in the G-buffer set. Binding 8 is the lights' shadow atlas
    /// (docs/plans/screen-space-illumination.md, "Push constants and sets").
    /// </summary>
    public const uint DirectBinding = 9;

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
    ];

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    // True from the creation of the estimate target until the first frame after it records the clear to (0, 0, 0, 1).
    private bool estimateNeedsClear;

    /// <param name="glowLevels">The bloom levels, 0 without bloom.</param>
    /// <param name="smaaTables">The SMAA tables when SMAA is on, or null when it is off.</param>
    /// <param name="reflections">The resolution of the reflections' trace, to make their images, or null under <see cref="NoReflections"/>.</param>
    /// <param name="screenSpaceLight">True to make the depth pyramid for the far rays, under <see cref="VisibilityBitmaskLight"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The depth pyramid is made, and a side is above <see cref="DepthPyramidImage.MaxSide"/>.</exception>
    public RenderTargets(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        Extent2D extent,
        int glowLevels,
        SmaaTables? smaaTables,
        TraceResolution? reflections,
        bool screenSpaceLight
    )
    {
        // One condition for the depth pyramid's existence: any reader (docs/plans/screen-space-illumination.md, decision 33).
        bool depthPyramid = reflections is not null || screenSpaceLight;
        if (depthPyramid)
            CheckPyramidScreen(extent);
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        CheckFormats(device: device, reflections: reflections is not null);
        GBufferSet = layouts.AllocateGBufferSet();
        PostSet = layouts.AllocatePostSet();
        MeterSet = layouts.AllocateMeterSet();
        Glow = new GpuImage[glowLevels];
        GlowSources = glowLevels == 0 ? [] : new DescriptorSet[glowLevels + 1];
        for (int i = 0; i < GlowSources.Length; i++)
            GlowSources[i] = layouts.AllocateGlowSourceSet();
        Create(extent: extent, hdrStorage: reflections is not null);
        if (depthPyramid)
            DepthPyramidImage = new(device: device, allocator: allocator, layouts: layouts, extent: extent);
        if (reflections is { } resolution && DepthPyramidImage is { } pyramid)
            Reflections = new(
                device: device,
                allocator: allocator,
                layouts: layouts,
                extent: extent,
                hdr: Hdr,
                resolution: resolution,
                depthPyramid: pyramid
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
    public GpuImage Depth { get; private set; }
    public GpuImage Hdr { get; private set; }

    /// <summary>The direct light of each pixel, pre-exposed, 0 for sky. The direct pass writes it.</summary>
    public GpuImage Direct { get; private set; }

    /// <summary>
    /// The estimate of each pixel: rgb the bounce, pre-exposed, a the occlusion. Cleared to (0, 0, 0, 1) in the first
    /// frame after each creation.
    /// </summary>
    public GpuImage Estimate { get; private set; }

    /// <summary>The images of SMAA, or null when SMAA is off.</summary>
    public SmaaTargets? Smaa { get; }

    /// <summary>The radiance pyramid and the trace set of the reflections, or null under <see cref="NoReflections"/>.</summary>
    public ReflectionTargets? Reflections { get; }

    /// <summary>The depth pyramid, or null when neither the reflections nor the screen-space light read it.</summary>
    public DepthPyramidImage? DepthPyramidImage { get; }

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
    /// <exception cref="ArgumentOutOfRangeException">The depth pyramid exists, and a side is above <see cref="DepthPyramidImage.MaxSide"/>.</exception>
    public void Resize(Extent2D extent)
    {
        if (extent.Width == Extent.Width && extent.Height == Extent.Height)
            return;
        // Before anything is destroyed, so that a refused size leaves every target as it was.
        if (DepthPyramidImage is not null)
            CheckPyramidScreen(extent);
        DestroyImages();
        Create(extent: extent, hdrStorage: Reflections is not null);
        // The depth pyramid before the reflections, whose trace set reads it.
        DepthPyramidImage?.Resize(extent);
        // The trace keeps its resolution across a resize.
        Reflections?.Create(extent: extent, hdr: Hdr, resolution: Reflections.Resolution);
        Smaa?.Resize(extent);
    }

    private static void CheckPyramidScreen(Extent2D extent)
    {
        if (DepthPyramidImage.ScreenProblem(extent.Width, extent.Height) is { } problem)
            throw new ArgumentOutOfRangeException(nameof(extent), problem);
    }

    /// <summary>Makes every target at <paramref name="extent"/> and writes the sets that read them.</summary>
    /// <param name="hdrStorage">True to make the HDR target a storage image too, for the reflections' trace.</param>
    [MemberNotNull(
        nameof(BaseColor),
        nameof(Normal),
        nameof(Specular),
        nameof(Emissive),
        nameof(Distance),
        nameof(Depth),
        nameof(Hdr),
        nameof(Direct),
        nameof(Estimate)
    )]
    private void Create(Extent2D extent, bool hdrStorage)
    {
        Extent = extent;
        BaseColor = ColorTarget(extent: extent, format: BaseColorFormat, name: "g-buffer base color");
        Normal = ColorTarget(extent: extent, format: NormalFormat, name: "g-buffer normal", readable: true);
        Specular = ColorTarget(extent: extent, format: SpecularFormat, name: "g-buffer specular");
        Emissive = ColorTarget(extent: extent, format: EmissiveFormat, name: "g-buffer emissive");
        Distance = ColorTarget(extent: extent, format: DistanceFormat, name: "g-buffer distance", readable: true);
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
        GBuffer = [BaseColor, Normal, Specular, Emissive, Distance];
        for (int i = 0; i < GBuffer.Length; i++)
            layouts.WriteImage(set: GBufferSet, binding: (uint)i, arrayElement: 0, image: GBuffer[i]);
        layouts.WriteImage(set: GBufferSet, binding: EstimateBinding, arrayElement: 0, image: Estimate);
        layouts.WriteImage(set: GBufferSet, binding: DirectBinding, arrayElement: 0, image: Direct);
        layouts.WriteImage(set: PostSet, binding: 0, arrayElement: 0, image: Hdr);
        layouts.WriteImage(set: MeterSet, binding: 0, arrayElement: 0, image: Hdr);
        layouts.WriteImage(set: MeterSet, binding: 1, arrayElement: 0, image: Distance);
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
    /// Clears the estimate target to (0, 0, 0, 1), no bounce and full visibility, if this is the first frame since its
    /// creation, and does nothing otherwise.
    /// </summary>
    public void RecordEstimateClear(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers)
    {
        if (!estimateNeedsClear)
            return;
        barriers.Image(Estimate, ImageUse.TransferDestination, discard: true);
        barriers.Flush(vk, commandBuffer);
        var openEstimate = new ClearColorValue(float32_0: 0, float32_1: 0, float32_2: 0, float32_3: 1);
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

    private static void CheckFormats(VulkanDevice device, bool reflections)
    {
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
    }

    /// <summary>
    /// The build writes the radiance pyramid as a storage image and the trace samples it trilinearly, and the trace writes
    /// the HDR target as a storage image. <see cref="DepthPyramidImage"/> checks its own format and its tail's limit.
    /// </summary>
    private static void CheckReflectionFormats(VulkanDevice device)
    {
        const FormatFeatureFlags radiance =
            FormatFeatureFlags.SampledImageBit
            | FormatFeatureFlags.StorageImageBit
            | FormatFeatureFlags.SampledImageFilterLinearBit;
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
        foreach (GpuImage? level in Glow)
            level?.Dispose();
        GBuffer = [];
    }

    public void Dispose()
    {
        Smaa?.Dispose();
        DestroyImages();
        DepthPyramidImage?.Dispose();
    }
}
