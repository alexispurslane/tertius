using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The images and the lists of the screen tracer (docs/plans/temporal-renderer.md, section 6, "The resolve"): the ray
/// plan's bits at each pixel, the records of each 2 × 2 block's ray and of each pixel's own ray of each kind, and the
/// own-ray list of each kind. Its two sets, one per parity of the drawn frames, also bind what the tracer reads: the depth
/// pyramid's finest level and its bounds, the direct light's two images, the surface test image, the motion target and the
/// two signals' images of that parity (<see cref="TemporalSignal.HitImages"/>), whose samples the resolve writes, and the
/// light pyramid that the guided diffuse rays draw from (<see cref="LightPyramidImages"/>). The composite owns the separate light
/// set that binds the direct images and the slow histories (<see cref="CompositePass"/>). Every image holds the render
/// targets' size, the block records a quarter of it, and each stays in the General layout, but the light-change mask,
/// which FSR samples. The depth pyramid belongs to the targets (<see cref="PyramidImages"/>).
/// </summary>
internal sealed class ScreenTraceImages : IDisposable
{
    /// <summary>The format of the plan's bits: bit 0 an own reflection ray, bit 1 an own diffuse ray (screen_trace.glsl).</summary>
    public const Format PlanFormat = Format.R8Uint;

    /// <summary>The format of a record's light: rgb the ray's light, a its hit distance.</summary>
    public const Format LightFormat = Format.R16G16B16A16Sfloat;

    /// <summary>
    /// The format of the hit light: rgb the light that leaves a pixel toward a ray of the layer's kind, pre-exposed and at
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

    /// <summary>The two ray kinds, which are the layers of the record images: 0 reflection rays, 1 diffuse rays.</summary>
    public const int Kinds = 2;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly RenderTargets targets;
    private readonly SurfaceHistory surfaces;
    private readonly PyramidImages pyramid;

    /// <param name="pyramid">The depth pyramid and its finest level.</param>
    /// <param name="diffuse">The diffuse signal, of one part, whose sample the resolve writes.</param>
    /// <param name="specular">The specular signal, of one part.</param>
    /// <param name="filter">The diffuse signal's filter, or null with the filter off (<see cref="DiffuseFilter.Selected"/>).</param>
    /// <param name="guided">Whether a share of the diffuse rays aims at bright pixels (<see cref="LightPyramidPass.SwitchedOn"/>).</param>
    public ScreenTraceImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        RenderTargets targets,
        SurfaceHistory surfaces,
        PyramidImages pyramid,
        TemporalSignal diffuse,
        TemporalSignal specular,
        DiffuseFilter? filter,
        bool guided
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.targets = targets;
        this.surfaces = surfaces;
        this.pyramid = pyramid;

        Diffuse = diffuse;
        Specular = specular;
        Filter = filter;
        Guided = guided;
        Sets = [layouts.AllocateScreenTraceSet(), layouts.AllocateScreenTraceSet()];
        LightPyramidImages = new(
            device: device,
            allocator: allocator,
            layouts: layouts,
            extent: targets.Extent,
            format: PyramidImages.LightFormat,
            name: "light pyramid"
        );
        Create();
    }

    /// <summary>The plan's bits at each pixel.</summary>
    public GpuImage Plan { get; private set; }

    /// <summary>The light of each block's ray, one layer per kind.</summary>
    public GpuImage BlockLight { get; private set; }

    /// <summary>The direction, density and RayTraced of each block's ray, one layer per kind.</summary>
    public GpuImage BlockRay { get; private set; }

    /// <summary>The light of each pixel's own ray, one layer per kind.</summary>
    public GpuImage OwnLight { get; private set; }

    /// <summary>The direction, density and RayTraced of each pixel's own ray, one layer per kind.</summary>
    public GpuImage OwnRay { get; private set; }

    /// <summary>
    /// The hit light, one layer per kind (ticket ter-67y3): at each pixel that a walk can hit, the light that leaves it
    /// toward a ray of the kind, which the plan writes and a ray's hit reads. With a precise specular history, alpha holds
    /// the pixel's moved mark, 1 where its surface moved since the last draw, which a hit carries into its ray's record
    /// (docs/plans/bounce-light-confidence.md, section 8.4). Otherwise alpha is 0.
    /// </summary>
    public GpuImage HitLight { get; private set; }

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

    /// <summary>The own-ray list of each kind, in the layout of <see cref="OwnRayList"/>.</summary>
    public GpuBuffer[] OwnRays { get; private set; } = [];

    /// <summary>The tracer set of each parity of the drawn frames: the one that a draw of that parity binds.</summary>
    public DescriptorSet[] Sets { get; }

    /// <summary>The surface test image, whose pass bits a hit's history and the plan's lengths read.</summary>
    public GpuImage SurfaceTest => surfaces.Test;

    /// <summary>The G-buffer's motion target.</summary>
    public GpuImage Motion => targets.Motion;

    /// <summary>The diffuse signal.</summary>
    public TemporalSignal Diffuse { get; }

    /// <summary>The specular signal.</summary>
    public TemporalSignal Specular { get; }

    /// <summary>The diffuse signal's filter, or null with the filter off.</summary>
    public DiffuseFilter? Filter { get; }

    /// <summary>
    /// Whether <see cref="LightPyramid.GuidedShare"/> of the diffuse rays aim at pixels drawn from the light pyramid, or
    /// every diffuse ray comes from the cosine lobe (ARCHITECTURE.md, "Guided diffuse rays").
    /// </summary>
    public bool Guided { get; }

    /// <summary>
    /// The light pyramid: each pixel's guide light and the sums of its levels, which <see cref="LightPyramidPass"/> builds
    /// while <see cref="Guided"/>. It exists either way, because the tracer's sets bind it.
    /// </summary>
    public PyramidImages LightPyramidImages { get; }

    /// <summary>
    /// The diffuse light that the composite shows: the filtered light, or with the filter off the diffuse slow history.
    /// </summary>
    public GpuImage ShownDiffuse => Filter?.Filtered ?? Diffuse.Slow(0);

    /// <summary>
    /// The last drawn frame's diffuse light that a ray's hit reads in a frame of <paramref name="parity"/>: the filtered
    /// light, so that a bright speck does not spread through the hits, or with the filter off the last fast history.
    /// </summary>
    public GpuImage HitDiffuse(int parity) => Filter?.Filtered ?? Diffuse.HitImages(part: 0, parity).LastFast;

    /// <summary>The images that the tracer writes and reads again: the plan and the four record images.</summary>
    public IEnumerable<GpuImage> Records => [Plan, BlockLight, BlockRay, OwnLight, OwnRay];

    /// <summary>
    /// Recreates the images and the lists at the targets' size and rewrites both sets. The device must be idle, and the
    /// targets, the surface history and both signals resize first, because the sets point at their images.
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
        nameof(HitLight)
    )]
    private void Create()
    {
        Extent2D extent = targets.Extent;
        var blocks = new Extent2D((extent.Width + 1) / 2, (extent.Height + 1) / 2);
        Plan = CreateImage(extent: extent, format: PlanFormat, layers: 1, name: "screen trace plan");

        BlockLight = CreateImage(extent: blocks, format: LightFormat, layers: Kinds, name: "screen trace block light");
        BlockRay = CreateImage(extent: blocks, format: RayFormat, layers: Kinds, name: "screen trace block rays");
        OwnLight = CreateImage(extent: extent, format: LightFormat, layers: Kinds, name: "screen trace own light");
        OwnRay = CreateImage(extent: extent, format: RayFormat, layers: Kinds, name: "screen trace own rays");
        HitLight = CreateImage(extent: extent, format: HitLightFormat, layers: Kinds, name: "screen trace hit light");

        OwnRays = [CreateOwnRayList(extent, "reflection"), CreateOwnRayList(extent, "diffuse")];
        lightAges = Specular.KeepsLightAge
            ?
            [
                CreateImage(extent: extent, format: LightAgeFormat, layers: 1, name: "screen trace light age 0"),
                CreateImage(extent: extent, format: LightAgeFormat, layers: 1, name: "screen trace light age 1"),
            ]
            : [];
        LightChangeMask = Specular.HistoryMode.KeepsLightAge
            ? CreateImage(
                extent: extent,
                format: LightChangeMaskFormat,
                layers: 1,
                name: "screen trace light-change mask",
                sampled: true
            )
            : null;
        for (int parity = 0; parity < Sets.Length; parity++)
            WriteSet(parity);
        // The light pyramid's finest level reads this frame's direct diffuse light.
        layouts.WriteImage(
            set: LightPyramidImages.FinestSet,
            binding: 0,
            view: targets.DirectDiffuse.View,
            layout: ImageLayout.General
        );
    }

    /// <summary>Writes the set of <paramref name="parity"/> in the bindings of screen_trace.glsl.</summary>
    private void WriteSet(int parity)
    {
        DescriptorSet set = Sets[parity];
        (GpuImage diffuseSample, _, GpuImage slowDiffuse) = Diffuse.HitImages(part: 0, parity);
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
            HitDiffuse(parity),
            lastFastSpecular,
            slowDiffuse,
            slowSpecular,
            Plan,
            BlockLight,
            BlockRay,
            OwnLight,
            OwnRay,
            diffuseSample,
            specularSample,
        ];
        for (int image = 0; image < storage.Length; image++)
            layouts.WriteStorageImage(set: set, binding: (uint)(2 + image), image: storage[image]);
        for (int kind = 0; kind < Kinds; kind++)
            layouts.WriteBuffer(
                set: set,
                binding: (uint)(2 + storage.Length + kind),
                type: DescriptorType.StorageBuffer,
                buffer: OwnRays[kind]
            );
        // The light-change age after the lists: the last frame's record, this frame's, the specular sample's age, FSR's
        // light-change mask and the diffuse sample's age.
        if (lightAges.Length != 0)
        {
            layouts.WriteStorageImage(set: set, binding: 19, image: lightAges[1 - parity]);
            layouts.WriteStorageImage(set: set, binding: 20, image: lightAges[parity]);
            layouts.WriteStorageImage(
                set: set,
                binding: 21,
                image: Specular.SampleAge(0) ?? throw new UnreachableException("The light age makes the sample age.")
            );
            layouts.WriteStorageImage(
                set: set,
                binding: 22,
                image: LightChangeMask ?? throw new UnreachableException("The light age makes the light-change mask.")
            );
            layouts.WriteStorageImage(
                set: set,
                binding: 23,
                image: Diffuse.SampleAge(0) ?? throw new UnreachableException("The light age makes the sample age.")
            );
        }
        // The diagnostic of uncovered surfaces, whose first channel the resolve writes.
        if (Specular.UncoveredDiagnostic(0) is { } diagnostic)
            layouts.WriteStorageImage(set: set, binding: 24, image: diagnostic);
        layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.ScreenTraceHitLightBinding, image: HitLight);
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
    private GpuImage CreateImage(Extent2D extent, Format format, uint layers, string name, bool sampled = false) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: format,
            usage: ImageUsageFlags.StorageBit
                | ImageUsageFlags.TransferSrcBit
                | (sampled ? ImageUsageFlags.SampledBit : 0),
            mipLevels: 1,
            name: name,
            arrayLayers: layers,
            arrayView: layers > 1
        );

    /// <summary>The own-ray list of a kind: appended by the plan, read by the indirect dispatch, cleared and read back by transfers.</summary>
    private GpuBuffer CreateOwnRayList(Extent2D screen, string kind) =>
        GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: OwnRayList.Bytes(screen),
            usage: BufferUsageFlags.StorageBufferBit
                | BufferUsageFlags.IndirectBufferBit
                | BufferUsageFlags.TransferDstBit
                | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: $"screen trace {kind} own rays"
        );

    /// <summary>Destroys the images and the lists, which <see cref="Resize"/> makes again. The device must be idle.</summary>
    public void Dispose()
    {
        foreach (GpuImage image in (GpuImage[])[.. Records, HitLight, .. lightAges])
            image.Dispose();
        lightAges = [];
        LightPyramidImages.Dispose();
        LightChangeMask?.Dispose();
        LightChangeMask = null;
        foreach (GpuBuffer list in OwnRays)
            list.Dispose();
        OwnRays = [];
    }
}
