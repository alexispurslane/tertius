using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The images and the lists of the screen tracer (https://github.com/alexispurslane/tertius/wiki/temporal-renderer, section 6, "The resolve"): the ray
/// plan's bits at each pixel, the records of each 2 × 2 block's ray and of each pixel's own ray for glossy reflections, and the
/// own-ray list for glossy reflections. Its two sets, one per parity of the drawn frames, also bind what the tracer reads: the depth
/// pyramid's finest level and its bounds, the direct light's two images, the surface test image, the motion target and the
/// reflection history images of that parity (<see cref="TemporalSignal.HitImages"/>), whose samples the resolve writes, and the
/// light pyramid that the guided reflection rays draw from (<see cref="LightPyramidImages"/>). The composite owns the separate light
/// set that binds the direct images and the slow histories (<see cref="CompositePass"/>). Every image holds the render
/// targets' size, the block records a quarter of it, and each stays in the General layout, but the light-change mask,
/// which FSR samples. The depth pyramid belongs to the targets (<see cref="PyramidImages"/>).
/// </summary>
internal sealed class ScreenTraceImages : IDisposable
{
    /// <summary>The format of the plan's bits: bit 0 marks an individual reflection ray (screen_trace.glsl).</summary>
    public const Format PlanFormat = Format.R8Uint;

    /// <summary>The format of a record's light: rgb the ray's light, a its hit distance.</summary>
    public const Format LightFormat = Format.R16G16B16A16Sfloat;

    /// <summary>
    /// The format of the hit light: rgb the light that leaves a pixel toward a reflection ray, pre-exposed and at
    /// most MaxRadiance, so below the largest half float, and a the pixel's moved mark, 1 or 0.
    /// </summary>
    public const Format HitLightFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The format of a record's ray: the direction's octahedral code, the half bits of log2 of its density, and RayTraced.</summary>
    public const Format RayFormat = Format.R16G16B16A16Uint;

    /// <summary>
    /// The format of the light-age record of a precise specular history: r the exact light at the pixel, g
    /// its age in frames or <see cref="NoLightAge"/>.
    /// </summary>
    public const Format LightAgeFormat = Format.R32G32Sfloat;

    /// <summary>The format of the light-change mask: AMD's transparency and composition mask, from 0 to 1.</summary>
    public const Format LightChangeMaskFormat = Format.R8Unorm;

    /// <summary>
    /// The light-age record's age of a pixel whose light was never seen to change, in the frame that first shows it: no
    /// evidence. Each later frame that shows it unchanged lowers the value by one, so the record holds −(N + 1) for a
    /// pixel seen for N frames, and an age of 0 or more once its light changed.
    /// </summary>
    public const float NoLightAge = -1;

    /// <summary>
    /// The format of the reflection share: one share per tile, from 0 to <see cref="GuidedShares.ReflectionCeiling"/>. A
    /// 32-bit float holds the share pass's result exactly. A half float would round it, and Vulkan leaves the rounding
    /// mode of that conversion to the driver: toward zero, every small step would round down, and a tile's share would
    /// drift toward 0 on one driver and not on another. Stage 1's probe stored halves (ter-fe63).
    /// </summary>
    public const Format ReflectionShareFormat = Format.R32Sfloat;

    /// <summary>The format of the block guide: x a block's guided density, y and z its evidence, w unused.</summary>
    public const Format BlockGuideFormat = Format.R32G32B32A32Sfloat;

    /// <summary>The blocks across a tile of the reflection share, and down it: a tile covers 16 × 16 render pixels.</summary>
    public const int TileBlocks = 8;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly RenderTargets targets;
    private readonly SurfaceHistory surfaces;
    private readonly PyramidImages pyramid;

    /// <param name="pyramid">The depth pyramid and its finest level.</param>
    /// <param name="specular">The specular signal, of one part.</param>
    /// <param name="guided">Whether a share of the reflection rays aims at bright pixels (<see cref="LightPyramidPass.SwitchedOn"/>).</param>
    public ScreenTraceImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        RenderTargets targets,
        SurfaceHistory surfaces,
        PyramidImages pyramid,
        TemporalSignal specular,
        bool guided
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.targets = targets;
        this.surfaces = surfaces;
        this.pyramid = pyramid;

        Specular = specular;
        Guided = guided;
        Sets = [layouts.AllocateScreenTraceSet(), layouts.AllocateScreenTraceSet()];
        LightPyramidImages = new(
            device: device,
            allocator: allocator,
            layouts: layouts,
            extent: targets.Extent,
            format: PyramidImages.LightFormat,
            name: "light pyramid",
            levelOneReadsFinest: true
        );
        Create();
    }

    /// <summary>The plan's bits at each pixel.</summary>
    public GpuImage Plan { get; private set; }

    /// <summary>The light of each block's ray.</summary>
    public GpuImage BlockLight { get; private set; }

    /// <summary>The direction, density and RayTraced of each block's ray.</summary>
    public GpuImage BlockRay { get; private set; }

    /// <summary>The light of each pixel's own ray.</summary>
    public GpuImage OwnLight { get; private set; }

    /// <summary>The direction, density and RayTraced of each pixel's own ray.</summary>
    public GpuImage OwnRay { get; private set; }

    /// <summary>
    /// The hit light (ticket ter-67y3): at each pixel that a walk can hit, the light that leaves it
    /// toward a ray of the kind, which the plan writes and a ray's hit reads. With a precise specular history, alpha holds
    /// the pixel's moved mark, 1 where its surface moved since the last draw, which a hit carries into its ray's record
    /// (https://github.com/alexispurslane/tertius/wiki/bounce-light-confidence, section 8.4). Otherwise alpha is 0.
    /// </summary>
    public GpuImage HitLight { get; private set; }

    /// <summary>
    /// The reflection share of each tile of <see cref="TileBlocks"/> × <see cref="TileBlocks"/> blocks
    /// (<see cref="GuidedShares"/>), over the tiles of the largest render size: the share of the tile's reflection rays that
    /// take the guided draw. The tracer reads it bilinearly at a pixel's block, and the share pass moves it after the
    /// resolve from this draw's evidence. Its content is the share after the last drawn frame, and it is undefined before
    /// the first: a draw at which the shares restart (<see cref="SharesRestart"/>) reads the ceiling in its place.
    /// </summary>
    public GpuImage ReflectionShare { get; private set; }

    /// <summary>
    /// The guide of each block's reflection ray, which the block pass writes while <see cref="Guided"/>: x the ray's guided
    /// density along its direction, y and z its evidence for its tile's share, the slope and the squared weight of
    /// <see cref="GuidedShares.EvidenceOf"/>, and w the share that drew it. The resolve weighs the ray at each pixel of the
    /// block by the mixture at that share of the pixel's lobe density and the ray's guided density.
    /// </summary>
    public GpuImage BlockGuide { get; private set; }

    /// <summary>
    /// Whether every tile's reflection share restarts at <see cref="GuidedShares.ReflectionCeiling"/> before
    /// <paramref name="frame"/>'s rays: when the draw resets every history, as at a camera cut, the first drawn frame and a
    /// still. From the ceiling, a share falls where the guided draws bring little within about 30 draws, and the specks
    /// that a lobe alone gives on a rough surface near a small bright sender do not show in the first draws after the cut
    /// (ter-fe63). A new render size keeps the shares, as a moving camera does: each tile then covers a few percent other
    /// pixels, and the dynamic render scale changes the size often, so a restart there would bring back the grain of a
    /// restart on every surface whose share had fallen. The share pass writes the ceiling at a restart into every tile of
    /// the largest render size, so that a larger size reads no share from before it.
    /// </summary>
    public static bool SharesRestart(FrameHistory.PendingFrame frame) => frame.Reset;

    /// <summary>The light-age records, one per parity, with a precise specular history: none with the original.</summary>
    private GpuImage[] lightAges = [];

    /// <summary>
    /// The light-age record that a frame of <paramref name="parity"/> writes, or null with the original specular history
    /// (the light-change age, ticket tiunr-7haj): per pixel the exact light, the luminance of the demodulated direct
    /// diffuse light and of the glow that the pixel sends, with the pre-exposure undone, and the frames since it last
    /// changed.
    /// </summary>
    public GpuImage? LightAge(int parity) => lightAges.Length == 0 ? null : lightAges[parity];

    /// <summary>The light-age record that the last drawn frame wrote, for a frame of <paramref name="parity"/>.</summary>
    public GpuImage? LastLightAge(int parity) => lightAges.Length == 0 ? null : lightAges[1 - parity];

    /// <summary>
    /// FSR 3.1's transparency and composition mask, or null with the original specular history: per pixel, how much FSR's
    /// history keeps to this frame's light, which the resolve writes from this frame's light-age records (ticket t1-4hij).
    /// </summary>
    public GpuImage? LightChangeMask { get; private set; }

    /// <summary>The own-ray list for glossy reflections, in the layout of <see cref="OwnRayList"/>.</summary>
    public GpuBuffer OwnRays { get; private set; }

    /// <summary>The tracer set of each parity of the drawn frames: the one that a draw of that parity binds.</summary>
    public DescriptorSet[] Sets { get; }

    /// <summary>The surface test image, whose pass bits a hit's history and the plan's lengths read.</summary>
    public GpuImage SurfaceTest => surfaces.Test;

    /// <summary>The G-buffer's motion target.</summary>
    public GpuImage Motion => targets.Motion;

    /// <summary>The specular signal.</summary>
    public TemporalSignal Specular { get; }

    /// <summary>
    /// Whether a share of the rays for glossy reflections aims at pixels drawn from the light pyramid (<see cref="GuidedShares"/>),
    /// or every ray comes from its kind's lobe (ARCHITECTURE.md, "Guided rays").
    /// </summary>
    public bool Guided { get; }

    /// <summary>
    /// The light pyramid: each pixel's guide light and the sums of its levels, which <see cref="LightPyramidPass"/> builds
    /// while <see cref="Guided"/>. It exists either way, because the tracer's sets bind it.
    /// </summary>
    public PyramidImages LightPyramidImages { get; }

    /// <summary>The images that the tracer writes and reads again: the plan and the four record images.</summary>
    public IEnumerable<GpuImage> Records => [Plan, BlockLight, BlockRay, OwnLight, OwnRay];

    /// <summary>
    /// Recreates the images and the lists at the targets' size and rewrites both sets. The device must be idle, and the
    /// targets, the surface history and the reflection history resize first, because the sets point at their images.
    /// </summary>
    public void Resize()
    {
        Dispose();
        LightPyramidImages.Create(targets.Extent);
        Create();
    }

    [MemberNotNull(
        nameof(Plan),
        nameof(BlockLight),
        nameof(BlockRay),
        nameof(OwnLight),
        nameof(OwnRay),
        nameof(HitLight),
        nameof(ReflectionShare),
        nameof(BlockGuide),
        nameof(OwnRays)
    )]
    private void Create()
    {
        Extent2D extent = targets.Extent;
        var blocks = new Extent2D((extent.Width + 1) / 2, (extent.Height + 1) / 2);
        Plan = CreateImage(extent: extent, format: PlanFormat, name: "screen trace plan");

        BlockLight = CreateImage(extent: blocks, format: LightFormat, name: "screen trace block light");
        BlockRay = CreateImage(extent: blocks, format: RayFormat, name: "screen trace block rays");
        OwnLight = CreateImage(extent: extent, format: LightFormat, name: "screen trace own light");
        OwnRay = CreateImage(extent: extent, format: RayFormat, name: "screen trace own rays");
        HitLight = CreateImage(extent: extent, format: HitLightFormat, name: "screen trace hit light");
        ReflectionShare = CreateImage(
            extent: new((blocks.Width + TileBlocks - 1) / TileBlocks, (blocks.Height + TileBlocks - 1) / TileBlocks),
            format: ReflectionShareFormat,
            name: "screen trace reflection share"
        );
        BlockGuide = CreateImage(extent: blocks, format: BlockGuideFormat, name: "screen trace block guide");

        OwnRays = CreateOwnRayList(extent);
        lightAges = Specular.KeepsLightAge
            ?
            [
                CreateImage(extent: extent, format: LightAgeFormat, name: "screen trace light age 0"),
                CreateImage(extent: extent, format: LightAgeFormat, name: "screen trace light age 1"),
            ]
            : [];
        LightChangeMask = Specular.HistoryMode.KeepsLightAge
            ? CreateImage(
                extent: extent,
                format: LightChangeMaskFormat,
                name: "screen trace light-change mask",
                sampled: true
            )
            : null;
        for (int parity = 0; parity < Sets.Length; parity++)
            WriteSet(parity);
        // The light pyramid's finest level reads this frame's direct diffuse light, and the surface test image and the motion
        // target, which give each pixel's surface share.
        layouts.WriteImage(
            set: LightPyramidImages.FinestSet,
            binding: 0,
            view: targets.DirectDiffuse.View,
            layout: ImageLayout.General
        );
        layouts.WriteStorageImage(set: LightPyramidImages.FinestSet, binding: 2, image: surfaces.Test);
        layouts.WriteStorageImage(set: LightPyramidImages.FinestSet, binding: 3, image: targets.Motion);
    }

    /// <summary>Writes the set of <paramref name="parity"/> in the bindings of screen_trace.glsl.</summary>
    private void WriteSet(int parity)
    {
        DescriptorSet set = Sets[parity];
        (GpuImage specularSample, GpuImage lastFastSpecular, GpuImage slowSpecular) = Specular.HitImages(
            part: 0,
            parity
        );
        WritePyramids(set);
        GpuImage[] storage =
        [
            targets.DirectDiffuse,
            targets.DirectSpecular,
            surfaces.Test,
            targets.Motion,
            lastFastSpecular,
            slowSpecular,
            Plan,
            BlockLight,
            BlockRay,
            OwnLight,
            OwnRay,
            specularSample,
        ];
        for (int image = 0; image < storage.Length; image++)
            layouts.WriteStorageImage(set: set, binding: (uint)(2 + image), image: storage[image]);
        layouts.WriteBuffer(set: set, binding: 14, type: DescriptorType.StorageBuffer, buffer: OwnRays);
        WriteLightAges(set, parity);
        // The diagnostic of uncovered surfaces, whose first channel the resolve writes.
        if (Specular.UncoveredDiagnostic(0) is { } diagnostic)
            layouts.WriteStorageImage(set: set, binding: 19, image: diagnostic);
        layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.ScreenTraceHitLightBinding, image: HitLight);
        layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.ScreenTraceShareBinding, image: ReflectionShare);
        layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.ScreenTraceBlockGuideBinding, image: BlockGuide);
    }

    /// <summary>
    /// Writes the light-change age's images of <paramref name="parity"/> after the lists, when the frame keeps the age:
    /// the last frame's record, this frame's, the specular sample's age, FSR's light-change mask.
    /// </summary>
    private void WriteLightAges(DescriptorSet set, int parity)
    {
        if (lightAges.Length == 0)
            return;
        layouts.WriteStorageImage(set: set, binding: 15, image: lightAges[1 - parity]);
        layouts.WriteStorageImage(set: set, binding: 16, image: lightAges[parity]);
        layouts.WriteStorageImage(
            set: set,
            binding: 17,
            image: Specular.SampleAge(0) ?? throw new UnreachableException("The light age makes the sample age.")
        );
        layouts.WriteStorageImage(
            set: set,
            binding: 18,
            image: LightChangeMask ?? throw new UnreachableException("The light age makes the light-change mask.")
        );
    }

    /// <summary>Writes the sampled images of <paramref name="set"/>: the depth pyramid at bindings 0 and 1, and the light
    /// pyramid from <see cref="DescriptorLayouts.ScreenTraceLightBinding"/>, each its finest level and then its levels.</summary>
    private void WritePyramids(DescriptorSet set)
    {
        layouts.WriteImage(set: set, binding: 0, view: pyramid.Finest.View, layout: ImageLayout.General);
        layouts.WriteImage(set: set, binding: 1, view: pyramid.Image.View, layout: ImageLayout.General);
        layouts.WriteImage(
            set: set,
            binding: DescriptorLayouts.ScreenTraceLightBinding,
            view: LightPyramidImages.Finest.View,
            layout: ImageLayout.General
        );
        layouts.WriteImage(
            set: set,
            binding: DescriptorLayouts.ScreenTraceLightBinding + 1,
            view: LightPyramidImages.Image.View,
            layout: ImageLayout.General
        );
    }

    /// <summary>
    /// A record image: written and read as a storage image, copied out by the read hooks, and with
    /// <paramref name="sampled"/> sampled by FSR 3.1.
    /// </summary>
    private GpuImage CreateImage(Extent2D extent, Format format, string name, bool sampled = false) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: format,
            usage: ImageUsageFlags.StorageBit
                | ImageUsageFlags.TransferSrcBit
                | (sampled ? ImageUsageFlags.SampledBit : 0),
            mipLevels: 1,
            name: name
        );

    /// <summary>The own-ray list of a kind: appended by the plan, read by the indirect dispatch, cleared and read back by transfers.</summary>
    private GpuBuffer CreateOwnRayList(Extent2D screen) =>
        GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: OwnRayList.Bytes(screen),
            usage: BufferUsageFlags.StorageBufferBit
                | BufferUsageFlags.IndirectBufferBit
                | BufferUsageFlags.TransferDstBit
                | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "screen trace reflection own rays"
        );

    /// <summary>Destroys the images and the lists, which <see cref="Resize"/> makes again. The device must be idle.</summary>
    public void Dispose()
    {
        foreach (GpuImage image in (GpuImage[])[.. Records, HitLight, ReflectionShare, BlockGuide, .. lightAges])
            image.Dispose();
        lightAges = [];
        LightPyramidImages.Dispose();
        LightChangeMask?.Dispose();
        LightChangeMask = null;
        OwnRays.Dispose();
    }
}
