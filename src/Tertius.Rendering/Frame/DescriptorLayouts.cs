using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The descriptor set layouts, pipeline layouts and pools shared by all passes.
/// <list type="table">
/// <item><term>set 0, frame</term><description>frame constants, instances, materials, lights, occluders, material sampler,
/// frame outlines, puffs, the BRDF tables (bindings 8 and 9) with their sampler (binding 10, immutable), and the exposure
/// buffer (binding 11), which every frame's set shares, and the environment map's specular map (binding 12, partially
/// bound), its sampler (binding 13, immutable) and its irradiance cube (binding 14, partially bound), which stay empty
/// with the map off, the tile views of the local shadows (binding 15, partially bound), empty without the atlas, the
/// motion rows (binding 17), the kept deformed positions of the last drawn frame (binding 18, partially bound), empty
/// without the deformation. One per frame in flight.</description></item>
/// <item><term>set 1, textures</term>
/// <description>the bindless texture array: variable count, partially bound, update-after-bind. One per frame in flight.</description>
/// </item>
/// <item><term>set 2, G-buffer</term>
/// <description>the G-buffer targets for the direct pass, the composite, the screen tracer and the transparent pass
/// (rewritten on resize), the shadow cascades of every cascade set (binding 5) and their comparison sampler (binding 6,
/// immutable), which the vertex stage also reads for the lit puffs, the local shadow atlas (binding 8, partially bound),
/// which the comparison sampler also reads and which stays empty without the atlas, the shading target (binding 10) and
/// the atlas's bounds (binding 11, partially bound), empty without the atlas. Bindings 7 and 9 are free. One.</description></item>
/// <item><term>set 3, impostor atlas</term><description>the impostor atlas's color and depth-offset arrays and their
/// sampler (immutable), for the billboards in the G-buffer pass. One, written once.</description></item>
/// <item><term>post</term><description>the HDR image for tone mapping, and the summed bloom levels (binding 1) with the
/// linear clamp sampler (binding 2, immutable), which only the tone map with bloom reads. One, rewritten on resize.</description></item>
/// <item><term>glow source</term><description>the image that one draw of the bloom pass reads, with the linear clamp
/// sampler (binding 1, immutable): one for the HDR image and one per bloom level. Rewritten on resize.</description></item>
/// <item><term>meter</term><description>the HDR image and the G-buffer distance for the exposure's meter. One, rewritten on
/// resize.</description></item>
/// <item><term>exposure</term><description>the histogram and the exposure buffer for the meter and the adapt step. One,
/// written once.</description></item>
/// <item><term>environment prefilter</term><description>the environment map's radiance map with its sampler, the
/// irradiance buffer and the buffer of its partial sums, for the prefilter's compute stage. One with the map on, its
/// radiance map rewritten when the map's images grow.</description></item>
/// <item><term>environment levels</term><description>four level views of the radiance map or the specular map as storage
/// images. One per dispatch of the map's mips and of its specular levels with the map on, which the pool holds for the
/// largest map, rewritten when the map's images grow.</description></item>
/// <item><term>pyramid level</term><description>set 3 of <see cref="PyramidLayout"/>: the level that one dispatch of a
/// pyramid's build reads and the level that it writes. One per level of each pyramid, the depth and the light pyramid, and
/// one for each finest level, rewritten on resize.</description></item>
/// <item><term>pyramid tail</term><description>set 3 of <see cref="PyramidTailLayout"/> for the tail of a pyramid's build: the
/// mip before the tail and the tail's mips. One per pyramid, rewritten on resize.</description></item>
/// <item><term>scatter</term><description>set 3 of <see cref="ScatterLayout"/>: the diffuse image, the row image and the
/// HDR image as storage images, and the kernel as a uniform buffer, for the skin scatter's passes. One, allocated under
/// <see cref="SeparableSubsurface"/> and rewritten on resize.</description></item>
/// <item><term>shadow bounds</term><description>the set of <see cref="ShadowBoundsLayout"/>: the local shadow atlas and
/// its bounds as a storage image, for the compute stage. One with the atlas, written once.</description></item>
/// <item><term>deform</term><description>the set of <see cref="DeformLayout"/>: the deform region's window of the vertex
/// buffer, the deform vertices, the morph deltas, the frame's pose rows and its job table, five storage buffers for the
/// compute stage (docs/plans/skinning-and-morphs.md, "The pass"), and this frame's kept positions (binding 5). One per
/// frame in flight.</description></item>
/// <item><term>surface test</term><description>set 3 of <see cref="SurfaceTestLayout"/>: the last surface history and
/// the last device depth as sampled images, and the motion target, this frame's surface history, the surface test image
/// and this frame's device depth as storage images (docs/plans/temporal-renderer.md, "The surface test"). Two, one per parity of the drawn frames,
/// rewritten on resize.</description></item>
/// <item><term>signal</term><description>set 3 of <see cref="AccumulateLayout"/>: one part of a signal's images, the
/// surface test image and the motion target, as storage images for the compute stage, and the last surface history and
/// the last device depth as sampled images (<see cref="TemporalSignal"/>). Its bindings are numbered, with gaps: 0 to 10,
/// 11 with the light age, 12 with an instrumentation, <see cref="BlockSumsBinding"/>, the part's block sums, always, and
/// the reflections' retained deviation at <see cref="LastDeviationBinding"/> and <see cref="DeviationBinding"/> with a
/// precise history. One per part and parity, rewritten on resize.</description></item>
/// <item><term>screen trace</term><description>set 3 of <see cref="ScreenTraceLayout"/>: the depth pyramid's finest
/// level and its bounds as sampled images, the tracer's inputs and outputs as storage images, the own-ray list of each
/// kind, and the light pyramid's finest level and sums as sampled images (<see cref="ScreenTraceImages"/>). One per parity
/// of the drawn frames, rewritten on resize.</description></item>
/// <item><term>filter</term><description>set 3 of <see cref="DiffuseFilterLayout"/>: the input and the output of one
/// dispatch of the diffuse filter, the diffuse slow history and this frame's diffuse fast history, as storage images for
/// the compute stage (<see cref="DiffuseFilter"/>). One per dispatch and parity, rewritten on resize.</description></item>
/// <item><term>light</term><description>set 3 of <see cref="CompositeLayout"/>: the direct light's two images and the two
/// indirect signals' slow histories, plus the existing specular sample for the opt-in reflection footprint, as storage images for the fragment stage (<see cref="CompositePass"/>). One,
/// rewritten on resize.</description></item>
/// </list>
/// Every pass that draws meshes or reads the G-buffer uses <see cref="SceneLayout"/>, so sets 0 and 1 stay bound
/// across pipeline changes. <see cref="ImpostorLayout"/> and <see cref="CompositeLayout"/> add set 3 and are
/// compatible with it for sets 0 to 2. The
/// compute layouts of the depth pyramid, <see cref="PyramidLayout"/>, of the screen tracer, <see cref="ScreenTraceLayout"/>,
/// of the accumulator and of the skin scatter, <see cref="ScatterLayout"/>, take sets 0 to 2 too, whose bindings that their
/// shaders read include the compute stage (docs/plans/screen-space-reflections.md, "Descriptor sets").
/// </summary>
internal sealed unsafe class DescriptorLayouts : IDisposable
{
    /// <summary>The surface test sets: one per parity of the drawn frames (<see cref="SurfaceHistory"/>).</summary>
    private const uint SurfaceTestSets = 2;

    /// <summary>The screen tracer sets: one per parity of the drawn frames (<see cref="ScreenTraceImages"/>).</summary>
    private const uint ScreenTraceSets = 2;

    /// <summary>The sampled images of a screen tracer set: the depth pyramid's finest level and its bounds.</summary>
    private const uint ScreenTraceSampledImages = 2;

    /// <summary>
    /// The storage images of a screen tracer set: the direct light's two images, the surface test image, the motion target,
    /// the two signals' last fast and slow histories, the plan, the four record images and the two samples.
    /// </summary>
    private const uint ScreenTraceStorageImages = 15;

    /// <summary>The storage buffers of a screen tracer set: the own-ray list of each kind.</summary>
    private const uint ScreenTraceStorageBuffers = 2;

    /// <summary>
    /// The sampled images of the light pyramid in a screen tracer set, at <see cref="ScreenTraceLightBinding"/> and the
    /// binding after it: its finest level and its sums (screen_trace.glsl).
    /// </summary>
    private const uint ScreenTraceLightImages = 2;

    /// <summary>
    /// The first binding of the light pyramid in a screen tracer set, after the light age's bindings 19 to 23 and its
    /// diagnostic's binding 24.
    /// </summary>
    public const uint ScreenTraceLightBinding = 25;

    /// <summary>
    /// The storage images that the light-change age of a precise specular history adds to a screen tracer set after its
    /// lists, at bindings 19 to 23: the last and this frame's light-age records, the specular sample's age, FSR's
    /// light-change mask and the diffuse sample's age, and at binding 24 the uncovered-surface diagnostic
    /// (<see cref="HistoryInstrumentation.LightAgeDiagnostic"/>).
    /// </summary>
    private uint ScreenTraceAgeImages =>
        (HistoryMode.KeepsLightAge ? 5u : 0u)
        + (Instrumentation == HistoryInstrumentation.LightAgeDiagnostic ? 1u : 0u);

    /// <summary>
    /// The signal sets: one per part and parity of the screen tracer's two signals of one part, and of a test's signal of
    /// two parts (<see cref="TemporalSignal"/>).
    /// </summary>
    private const uint SignalSets = 8;

    /// <summary>
    /// The storage images of a signal set: nine, with the light-change age of a precise specular history the signal's
    /// sample age at binding 11, and at binding 12 the instrumentation's image, the uncovered-surface diagnostic or the accumulation
    /// trace, the block sums at <see cref="BlockSumsBinding"/>, and with a precise history the reflections' retained
    /// deviation pair. The original history refuses the instrumentation (the constructor).
    /// </summary>
    private uint SignalSetImages =>
        10u
        + (HistoryMode.KeepsLightAge ? 1u : 0u)
        + (Instrumentation == HistoryInstrumentation.None ? 0u : 1u)
        + (HistoryMode.KeepsRetainedDeviation ? 2u : 0u);

    /// <summary>
    /// The binding of a signal set that holds the part's block sums (<see cref="TemporalSignal.BlockSums"/>), after the
    /// optional bindings 11 and 12, so that it keeps its number in every history mode (accumulate_blocks.glsl).
    /// </summary>
    public const uint BlockSumsBinding = 13;

    /// <summary>
    /// The binding of a signal set that holds the reflections' retained deviation of the last draw, with a precise history
    /// (<see cref="TemporalSignal.DeviationImage"/>): the precise shaders declare it after the block sums.
    /// </summary>
    public const uint LastDeviationBinding = 14;

    /// <summary>The binding of a signal set that holds this draw's retained deviation of the reflections.</summary>
    public const uint DeviationBinding = 15;

    /// <summary>The sampled images of a signal set: the last surface history and the last device depth.</summary>
    private const uint SignalSetSampledImages = 2;

    /// <summary>
    /// The filter sets of one diffuse filter, the screen tracer's or a test's (<see cref="DiffuseFilter.Sets"/>).
    /// </summary>
    private const uint FilterSets = DiffuseFilter.Sets;

    /// <summary>
    /// The storage images of a filter set: a dispatch's input and output, the diffuse slow history and this frame's diffuse
    /// fast history.
    /// </summary>
    private const uint FilterSetImages = 4;

    /// <summary>
    /// The storage images of the light set: the direct light's two images, the two indirect signals' histories, and the
    /// reflection-sharing prototype's specular sample and distance guide (<see cref="CompositePass"/>).
    /// </summary>
    private const uint LightSetImages = 6;

    /// <summary>
    /// The storage buffers of the frame set: bindings 1 to 4, 6, 7, 11, 15, 17 and 18, which the vertex stage all sees. The
    /// fragment stage sees all but binding 18.
    /// </summary>
    public const uint FrameSetStorageBuffers = 10;

    /// <summary>
    /// The storage buffers of the deform set: the region's window, the deform vertices, the deltas, the pose rows, the job
    /// table and this frame's kept positions. At most <see cref="FrameSetStorageBuffers"/>, which every device that the
    /// renderer accepts offers a stage, because the scene layout needs that many.
    /// </summary>
    public const uint DeformSetStorageBuffers = 6;

    /// <summary>The binding of the motion rows in the frame set.</summary>
    public const uint MotionsBinding = 17;

    /// <summary>The binding of the last drawn frame's kept positions in the frame set.</summary>
    public const uint KeptPositionsBinding = 18;

    /// <summary>The binding of the frame set that holds the LTC table of the rectangle lights (brdf.glsl, ltcTable).</summary>
    public const uint LtcTableBinding = 19;

    /// <summary>The binding of this frame's kept positions in the deform set.</summary>
    public const uint DeformKeptPositionsBinding = 5;

    /// <summary>The pyramids of the screen tracer: the depth pyramid and the light pyramid (<see cref="PyramidImages"/>).</summary>
    public const uint Pyramids = 2;

    /// <summary>
    /// The pyramid level sets of the largest pyramids: levels 1 to 14, for a 16,384 px side
    /// (<see cref="DepthPyramid.MaxLevels"/>), and the finest level, of each pyramid.
    /// </summary>
    public const uint PyramidLevelSets = Pyramids * (DepthPyramid.MaxLevels + 1);

    /// <summary>
    /// The levels sets of the largest map: one per dispatch of the downsample of its radiance map and of its distance cube,
    /// one per dispatch of its specular map, and one for its irradiance cube.
    /// </summary>
    private static readonly uint EnvironmentLevelsSets = (uint)(
        2 * PrefilterLevels.DownsampleDispatches(PrefilterLevels.RadianceLevels(EnvironmentMapSetting.MaxFaceSize))
        + PrefilterLevels.Dispatches(PrefilterLevels.Count(EnvironmentMapSetting.MaxFaceSize))
        + 1
    );

    private readonly VulkanDevice device;

    // No handle until the constructor makes the pools, so a Dispose after a refused device destroys none.
    private readonly SetPool pool = new(default, 0);
    private readonly SetPool texturePool = new(default, 0);

    /// <summary>The descriptors that device limits bound in each set layout that this object made, by handle.</summary>
    private readonly Dictionary<ulong, LimitedDescriptors> setDescriptors = [];

    /// <summary>The descriptors that device limits bound in each pipeline layout that this object made, by handle.</summary>
    private readonly Dictionary<ulong, LimitedDescriptors> layoutDescriptors = [];

    /// <param name="glowLevels">The bloom levels, 0 without bloom: the pool holds one glow source set more than that.</param>
    /// <param name="historyMode">
    /// The renderer's specular history: the light-change age of a precise history adds the light-age records and the two
    /// samples' ages to the screen tracer set, and the signal's sample age to every signal set.
    /// </param>
    /// <param name="instrumentation">The renderer's instrumentation of the specular history, which adds its images.</param>
    /// <exception cref="ArgumentException">
    /// The diagnostic or the accumulation trace is on with the original half-precision history.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// A stage of a pipeline layout sees more storage buffers, storage images or sampled images than the device lets one
    /// stage see (<see cref="LimitedDescriptors.Problem"/>).
    /// </exception>
    public DescriptorLayouts(
        VulkanDevice device,
        int framesInFlight,
        int glowLevels,
        SpecularHistoryMode historyMode = SpecularHistoryMode.PreciseUncapped,
        HistoryInstrumentation instrumentation = HistoryInstrumentation.None
    )
    {
        if (instrumentation != HistoryInstrumentation.None && !historyMode.KeepsLightAge)
            throw new ArgumentException(
                $"TERTIUS_REFLECTION_HISTORY_AGREEMENT={instrumentation} needs a precise specular history, not TERTIUS_SPECULAR_HISTORY=original."
            );
        this.device = device;
        HistoryMode = historyMode;
        Instrumentation = instrumentation;

        LinearClampSampler = CreateLevelZeroSampler(Filter.Linear, "linear clamp sampler");
        EnvironmentSampler = CreateTrilinearClampSampler("environment sampler");

        FrameSet = CreateFrameSetLayout(LinearClampSampler, EnvironmentSampler);
        TextureSet = CreateTextureSetLayout();
        ShadowSampler = CreateShadowSampler();
        GBufferSet = CreateGBufferSetLayout(ShadowSampler);
        PostSet = CreateImagesLayout(images: 2, sampler: LinearClampSampler, name: "post set");
        GlowSourceSet = CreateImagesLayout(images: 1, sampler: LinearClampSampler, name: "glow source set");
        AtlasSampler = CreateTrilinearClampSampler("impostor atlas sampler");
        AtlasSet = CreateImagesLayout(images: 2, sampler: AtlasSampler, name: "impostor atlas set");

        (MeterSet, ExposureSet) = CreateExposureSetLayouts();
        (EnvironmentPrefilterSet, EnvironmentLevelsSet) = CreateEnvironmentSetLayouts(EnvironmentSampler);
        (PyramidLevelSet, PyramidTailSet) = CreatePyramidSetLayouts();
        DeformSet = CreateDeformSetLayout();
        ScatterSet = CreateScatterSetLayout();
        ShadowBoundsSet = CreateShadowBoundsSetLayout();

        // The temporal lighting's sets.
        SurfaceTestSet = CreateSurfaceTestSetLayout();
        (SignalSet, LightSet) = CreateTemporalSetLayouts();
        FilterSet = CreateFilterSetLayout();
        ScreenTraceSet = CreateScreenTraceSetLayout();

        (SceneLayout, ImpostorLayout, CompositeLayout, OverlayLayout, PostLayout, BloomLayout) =
            CreateGraphicsLayouts();
        (
            ExposureLayout,
            EnvironmentPrefilterLayout,
            PyramidLayout,
            PyramidTailLayout,
            DeformLayout,
            ScatterLayout,
            ShadowBoundsLayout
        ) = CreateComputeLayouts();

        (SurfaceTestLayout, AccumulateLayout, DiffuseFilterLayout, ScreenTraceLayout) = CreateTemporalLayouts();

        (pool, texturePool) = CreatePools(framesInFlight: (uint)framesInFlight, glowSourceSets: (uint)glowLevels + 1);
    }

    /// <summary>
    /// The temporal lighting's compute layouts: sets 0 to 2 and each one's own set 3, after the sets that they name.
    /// </summary>
    private (
        PipelineLayout SurfaceTest,
        PipelineLayout Accumulate,
        PipelineLayout Filter,
        PipelineLayout Trace
    ) CreateTemporalLayouts() =>
        (
            ComputeLayout<SurfaceTestPushConstants>(
                sets: [FrameSet, TextureSet, GBufferSet, SurfaceTestSet],
                name: "surface test layout"
            ),
            ComputeLayout<AccumulatePushConstants>(
                sets: [FrameSet, TextureSet, GBufferSet, SignalSet],
                name: "accumulate layout"
            ),
            ComputeLayout<DiffuseFilterPushConstants>(
                sets: [FrameSet, TextureSet, GBufferSet, FilterSet],
                name: "diffuse filter layout"
            ),
            ComputeLayout<ScreenTracePushConstants>(
                sets: [FrameSet, TextureSet, GBufferSet, ScreenTraceSet],
                name: "screen trace layout"
            )
        );

    public DescriptorSetLayout FrameSet { get; }
    public DescriptorSetLayout TextureSet { get; }
    public DescriptorSetLayout GBufferSet { get; }
    public DescriptorSetLayout PostSet { get; }

    /// <summary>The one image that a draw of the bloom pass reads, with the linear clamp sampler.</summary>
    public DescriptorSetLayout GlowSourceSet { get; }

    /// <summary>Set 0 of <see cref="ExposureLayout"/>: the HDR image and the G-buffer distance, for the compute stage.</summary>
    public DescriptorSetLayout MeterSet { get; }

    /// <summary>Set 1 of <see cref="ExposureLayout"/>: the histogram and the exposure buffer, for the compute stage.</summary>
    public DescriptorSetLayout ExposureSet { get; }

    /// <summary>
    /// Set 0 of <see cref="EnvironmentPrefilterLayout"/>: the radiance map and its sampler (docs/plans/environment-map.md,
    /// "Images and buffers").
    /// </summary>
    public DescriptorSetLayout EnvironmentPrefilterSet { get; }

    /// <summary>
    /// Set 1 of <see cref="EnvironmentPrefilterLayout"/>: four level views of one of the map's images, each of every map's
    /// layers (six per map), as storage images in bindings 0 to 3. A dispatch of the specular map writes them, a dispatch
    /// of the downsample reads binding 0 and writes the others, and the convolution of the irradiance cube writes binding 0.
    /// </summary>
    public DescriptorSetLayout EnvironmentLevelsSet { get; }

    /// <summary>
    /// Trilinear filtering over every level, clamped to the edge: the immutable sampler of the environment map's cubes,
    /// in the frame set (binding 13), which reads the specular map and the irradiance cube, and the prefilter set (binding
    /// 1). Vulkan filters a cube across its faces.
    /// </summary>
    public Sampler EnvironmentSampler { get; }

    /// <summary>
    /// The shadow map sampler: hardware PCF (linear filtering of GREATER_OR_EQUAL comparisons, so a receiver at least
    /// as near the light as the stored caster is lit), clamped to the edge.
    /// </summary>
    public Sampler ShadowSampler { get; }

    /// <summary>
    /// Linear filtering, clamped to the edge, one level and no anisotropy: the immutable sampler of the BRDF tables (frame
    /// set, binding 10), the post set (binding 2) and every glow source set (binding 1).
    /// </summary>
    public Sampler LinearClampSampler { get; }

    /// <summary>The impostor atlas: color and depth offset (2D arrays) and <see cref="AtlasSampler"/>.</summary>
    public DescriptorSetLayout AtlasSet { get; }

    /// <summary>Trilinear filtering, clamped to the edge, for the impostor atlas and the trace's reads of the pyramids.</summary>
    public Sampler AtlasSampler { get; }

    /// <summary>Sets 0 to 2 plus <see cref="DrawPushConstants"/> for the vertex and fragment stages.</summary>
    public PipelineLayout SceneLayout { get; }

    /// <summary>Sets 0 to 2 of <see cref="SceneLayout"/> and the light set, with the draw push constants, for the composite.</summary>
    public PipelineLayout CompositeLayout { get; }

    /// <summary>
    /// <see cref="SceneLayout"/> plus the atlas set as set 3, for impostor billboards. The two have the same push
    /// constants and sets 0 to 2, so binding set 3 with this layout keeps the scene sets bound.
    /// </summary>
    public PipelineLayout ImpostorLayout { get; }

    /// <summary>
    /// Sets 0 to 2, as in <see cref="SceneLayout"/>, plus <see cref="OutlinePushConstants"/> for the fragment stage, for
    /// the outline pass. Its push constants differ, so it is not compatible with <see cref="SceneLayout"/>: the pass binds
    /// the scene sets again with it.
    /// </summary>
    public PipelineLayout OverlayLayout { get; }

    /// <summary>The post set plus <see cref="ToneMapPushConstants"/>.</summary>
    public PipelineLayout PostLayout { get; }

    /// <summary>The glow source set plus <see cref="BloomPushConstants"/>.</summary>
    public PipelineLayout BloomLayout { get; }

    /// <summary>The meter set and the exposure set plus <see cref="ExposureAdaptPushConstants"/>, for both compute pipelines.</summary>
    public PipelineLayout ExposureLayout { get; }

    /// <summary>
    /// The prefilter set and the levels set of one dispatch, plus <see cref="EnvironmentPrefilterPushConstants"/>, for the
    /// radiance map's mips, the prefilter and the irradiance. So a pipeline needs four storage images, the four that
    /// every Vulkan device offers per stage.
    /// </summary>
    public PipelineLayout EnvironmentPrefilterLayout { get; }

    /// <summary>
    /// The level that one dispatch of a pyramid's build reads, a sampled image (binding 0), and the level that it writes,
    /// a storage image (binding 1), for the compute stage.
    /// </summary>
    public DescriptorSetLayout PyramidLevelSet { get; }

    /// <summary>
    /// The mip before a pyramid's tail, a sampled image (binding 0), and the tail's mips, an array of
    /// <see cref="PyramidTail.Mips"/> storage images (binding 1), for the compute stage.
    /// </summary>
    public DescriptorSetLayout PyramidTailSet { get; }

    /// <summary>
    /// Sets 0 to 2 of <see cref="SceneLayout"/>, a pyramid level set and <see cref="PyramidTailPushConstants"/>, for the
    /// pyramid builds' dispatches of one level, which read no push constants.
    /// </summary>
    public PipelineLayout PyramidLayout { get; }

    /// <summary>
    /// <see cref="PyramidLayout"/> with the pyramid tail set in place of the level set, for a tail. Its push constants
    /// are the same, so the scene sets that the builds bind stay bound across the two layouts.
    /// </summary>
    public PipelineLayout PyramidTailLayout { get; }

    /// <summary>
    /// Set 3 of <see cref="ScatterLayout"/>: the diffuse image, the row image and the HDR image as storage images (bindings 0
    /// to 2), and the kernel as a uniform buffer (binding 3) (Shaders/skin_scatter.glsl).
    /// </summary>
    public DescriptorSetLayout ScatterSet { get; }

    /// <summary>Sets 0 to 2 of <see cref="SceneLayout"/> and the scatter set, with no push constants, for the scatter passes.</summary>
    public PipelineLayout ScatterLayout { get; }

    /// <summary>
    /// The set of <see cref="ShadowBoundsLayout"/>: the local shadow atlas as a sampled image (binding 0) and its bounds as
    /// a storage image (binding 1) (Shaders/shadow_bounds.comp).
    /// </summary>
    public DescriptorSetLayout ShadowBoundsSet { get; }

    /// <summary>The shadow bounds set and <see cref="ShadowBoundsPushConstants"/>, for the dispatch of each drawn tile's bounds.</summary>
    public PipelineLayout ShadowBoundsLayout { get; }

    /// <summary>The deform pass's one set: the region's window, the deform vertices, the morph deltas, the pose rows and the job table.</summary>
    public DescriptorSetLayout DeformSet { get; }

    /// <summary>The deform pass's layout: the deform set and the push constants of its one dispatch.</summary>
    public PipelineLayout DeformLayout { get; }

    /// <summary>
    /// Set 3 of <see cref="SurfaceTestLayout"/>: the last surface history (binding 0, sampled), as storage images the
    /// motion target (binding 1), this frame's surface history (binding 2), the surface test image (binding 3) and this
    /// frame's device depth (binding 4), and the last device depth (binding 5, sampled) (Shaders/surface_test.comp).
    /// </summary>
    public DescriptorSetLayout SurfaceTestSet { get; }

    /// <summary>Sets 0 to 2 of <see cref="SceneLayout"/>, the surface test set and <see cref="SurfaceTestPushConstants"/>.</summary>
    public PipelineLayout SurfaceTestLayout { get; }

    /// <summary>
    /// One part of a signal's images for its accumulation, all storage images for the compute stage: 0 the sample, 1 the
    /// slow history, 2 the last frame's fast history, 3 this frame's, 4 the scratch, 5 the surface test image and 6 the
    /// motion target (docs/plans/temporal-renderer.md, "Images").
    /// </summary>
    public DescriptorSetLayout SignalSet { get; }

    /// <summary>
    /// The renderer's specular history, selected once per renderer. The layouts own it: the signal scratch and every
    /// signal read it from here, so their images always match the bindings of these layouts.
    /// </summary>
    public SpecularHistoryMode HistoryMode { get; }

    /// <summary>The instrumentation of the specular history, selected once with <see cref="HistoryMode"/>.</summary>
    public HistoryInstrumentation Instrumentation { get; }

    /// <summary>The light set: <see cref="LightSetImages"/> storage images for the fragment stage, which the composite reads.</summary>
    public DescriptorSetLayout LightSet { get; }

    /// <summary>Sets 0 to 2 of <see cref="SceneLayout"/>, the signal set and <see cref="AccumulatePushConstants"/>, for both dispatches of an accumulation.</summary>
    public PipelineLayout AccumulateLayout { get; }

    /// <summary>
    /// The images and lists of the screen tracer for the compute stage, in the bindings of screen_trace.glsl: 0 the depth
    /// pyramid's finest level and 1 its bounds, sampled, then 2 to 16 storage images and 17 and 18 the own-ray lists.
    /// </summary>
    public DescriptorSetLayout ScreenTraceSet { get; }

    /// <summary>Set 3 of <see cref="DiffuseFilterLayout"/>: the images of one dispatch of the diffuse filter.</summary>
    public DescriptorSetLayout FilterSet { get; }

    /// <summary>The layout of the diffuse filter's dispatches: sets 0 to 2, the filter set and the push constants.</summary>
    public PipelineLayout DiffuseFilterLayout { get; }

    /// <summary>Sets 0 to 2 of <see cref="SceneLayout"/>, the screen tracer set and <see cref="ScreenTracePushConstants"/>, for the tracer's four passes.</summary>
    public PipelineLayout ScreenTraceLayout { get; }

    /// <summary>
    /// The storage buffers, storage images and sampled images that each shader stage of <paramref name="layout"/> sees,
    /// outside the sets that are updated after bind: the counts that the device's per-stage limits bound.
    /// </summary>
    public LimitedDescriptors DescriptorsOf(PipelineLayout layout) => layoutDescriptors[layout.Handle];

    public DescriptorSet AllocateFrameSet() => Allocate(pool, FrameSet, null);

    public DescriptorSet AllocateGBufferSet() => Allocate(pool, GBufferSet, null);

    public DescriptorSet AllocatePostSet() => Allocate(pool, PostSet, null);

    public DescriptorSet AllocateGlowSourceSet() => Allocate(pool, GlowSourceSet, null);

    public DescriptorSet AllocateAtlasSet() => Allocate(pool, AtlasSet, null);

    public DescriptorSet AllocateMeterSet() => Allocate(pool, MeterSet, null);

    public DescriptorSet AllocateExposureSet() => Allocate(pool, ExposureSet, null);

    public DescriptorSet AllocateEnvironmentPrefilterSet() => Allocate(pool, EnvironmentPrefilterSet, null);

    public DescriptorSet AllocateEnvironmentLevelsSet() => Allocate(pool, EnvironmentLevelsSet, null);

    public DescriptorSet AllocatePyramidLevelSet() => Allocate(pool, PyramidLevelSet, null);

    public DescriptorSet AllocatePyramidTailSet() => Allocate(pool, PyramidTailSet, null);

    public DescriptorSet AllocateDeformSet() => Allocate(pool, DeformSet, null);

    public DescriptorSet AllocateScatterSet() => Allocate(pool, ScatterSet, null);

    public DescriptorSet AllocateShadowBoundsSet() => Allocate(pool, ShadowBoundsSet, null);

    public DescriptorSet AllocateSurfaceTestSet() => Allocate(pool, SurfaceTestSet, null);

    public DescriptorSet AllocateSignalSet() => Allocate(pool, SignalSet, null);

    public DescriptorSet AllocateLightSet() => Allocate(pool, LightSet, null);

    public DescriptorSet AllocateScreenTraceSet() => Allocate(pool, ScreenTraceSet, null);

    public DescriptorSet AllocateFilterSet() => Allocate(pool, FilterSet, null);

    public DescriptorSet AllocateTextureSet()
    {
        uint count = TextureStore.Capacity;
        var variable = new DescriptorSetVariableDescriptorCountAllocateInfo
        {
            SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
            DescriptorSetCount = 1,
            PDescriptorCounts = &count,
        };
        return Allocate(texturePool, TextureSet, &variable);
    }

    public void WriteBuffer(DescriptorSet set, uint binding, DescriptorType type, GpuBuffer buffer) =>
        WriteBuffer(set: set, binding: binding, type: type, buffer: buffer, offset: 0, range: Vk.WholeSize);

    /// <summary>Points <paramref name="binding"/> at <paramref name="range"/> bytes of <paramref name="buffer"/> from <paramref name="offset"/>.</summary>
    public void WriteBuffer(
        DescriptorSet set,
        uint binding,
        DescriptorType type,
        GpuBuffer buffer,
        ulong offset,
        ulong range
    )
    {
        var info = new DescriptorBufferInfo(buffer.Handle, offset, range);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DescriptorCount = 1,
            DescriptorType = type,
            PBufferInfo = &info,
        };
        device.Vk.UpdateDescriptorSets(
            device: device.Device,
            descriptorWriteCount: 1,
            pDescriptorWrites: &write,
            descriptorCopyCount: 0,
            pDescriptorCopies: null
        );
    }

    public void WriteSampler(DescriptorSet set, uint binding, Sampler sampler)
    {
        var info = new DescriptorImageInfo(sampler, default, ImageLayout.Undefined);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.Sampler,
            PImageInfo = &info,
        };
        device.Vk.UpdateDescriptorSets(
            device: device.Device,
            descriptorWriteCount: 1,
            pDescriptorWrites: &write,
            descriptorCopyCount: 0,
            pDescriptorCopies: null
        );
    }

    /// <summary>Writes a sampled image that shaders read in <see cref="ImageLayout.ShaderReadOnlyOptimal"/>.</summary>
    public void WriteImage(DescriptorSet set, uint binding, uint arrayElement, GpuImage image) =>
        WriteView(
            set: set,
            binding: binding,
            arrayElement: arrayElement,
            type: DescriptorType.SampledImage,
            view: image.View,
            layout: ImageLayout.ShaderReadOnlyOptimal
        );

    /// <summary>Writes a view of a sampled image that shaders read in <paramref name="layout"/>.</summary>
    public void WriteImage(DescriptorSet set, uint binding, ImageView view, ImageLayout layout) =>
        WriteView(
            set: set,
            binding: binding,
            arrayElement: 0,
            type: DescriptorType.SampledImage,
            view: view,
            layout: layout
        );

    /// <summary>Writes a storage image in the General layout.</summary>
    public void WriteStorageImage(DescriptorSet set, uint binding, GpuImage image) =>
        WriteStorageImage(set: set, binding: binding, view: image.View);

    /// <summary>Writes a view of a storage image in the General layout, such as one mip of a pyramid.</summary>
    public void WriteStorageImage(DescriptorSet set, uint binding, ImageView view) =>
        WriteStorageImage(set: set, binding: binding, arrayElement: 0, view: view);

    /// <summary>Writes a view of a storage image in the General layout into one element of an array binding.</summary>
    public void WriteStorageImage(DescriptorSet set, uint binding, uint arrayElement, ImageView view) =>
        WriteView(
            set: set,
            binding: binding,
            arrayElement: arrayElement,
            type: DescriptorType.StorageImage,
            view: view,
            layout: ImageLayout.General
        );

    /// <summary>Writes the view of a combined image sampler whose sampler is immutable, read in <see cref="ImageLayout.ShaderReadOnlyOptimal"/>.</summary>
    public void WriteSampledView(DescriptorSet set, uint binding, ImageView view) =>
        WriteView(
            set: set,
            binding: binding,
            arrayElement: 0,
            type: DescriptorType.CombinedImageSampler,
            view: view,
            layout: ImageLayout.ShaderReadOnlyOptimal
        );

    /// <summary>Writes one image descriptor of <paramref name="type"/>.</summary>
    private void WriteView(
        DescriptorSet set,
        uint binding,
        uint arrayElement,
        DescriptorType type,
        ImageView view,
        ImageLayout layout
    )
    {
        var info = new DescriptorImageInfo(default, view, layout);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = arrayElement,
            DescriptorCount = 1,
            DescriptorType = type,
            PImageInfo = &info,
        };
        device.Vk.UpdateDescriptorSets(
            device: device.Device,
            descriptorWriteCount: 1,
            pDescriptorWrites: &write,
            descriptorCopyCount: 0,
            pDescriptorCopies: null
        );
    }

    /// <summary>A binding of one storage buffer.</summary>
    private static DescriptorSetLayoutBinding StorageBuffer(uint binding, ShaderStageFlags stages) =>
        new(binding: binding, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: stages);

    /// <summary>A binding of one sampled image.</summary>
    private static DescriptorSetLayoutBinding Image(uint binding, ShaderStageFlags stages) =>
        new(binding: binding, descriptorType: DescriptorType.SampledImage, descriptorCount: 1, stageFlags: stages);

    /// <summary>A binding of one immutable sampler, which must outlive the creation of the layout.</summary>
    private static DescriptorSetLayoutBinding ImmutableSampler(
        uint binding,
        ShaderStageFlags stages,
        Sampler* sampler
    ) =>
        new(
            binding: binding,
            descriptorType: DescriptorType.Sampler,
            descriptorCount: 1,
            stageFlags: stages,
            pImmutableSamplers: sampler
        );

    /// <summary>
    /// A layout of <paramref name="images"/> sampled images for the fragment stage, at bindings 0 on, and
    /// <paramref name="sampler"/> as the immutable sampler of the binding after them.
    /// </summary>
    private DescriptorSetLayout CreateImagesLayout(int images, Sampler sampler, string name)
    {
        Span<DescriptorSetLayoutBinding> bindings = stackalloc DescriptorSetLayoutBinding[images + 1];
        for (int i = 0; i < images; i++)
            bindings[i] = Image(binding: (uint)i, stages: ShaderStageFlags.FragmentBit);
        bindings[images] = ImmutableSampler(
            binding: (uint)images,
            stages: ShaderStageFlags.FragmentBit,
            sampler: &sampler
        );
        return CreateLayout(bindings: bindings, name: name);
    }

    /// <summary>
    /// The layout of the bindless textures. They live in their own set because only update-after-bind layouts may hold
    /// more than maxPerStageDescriptorSampledImages (256 on MoltenVK) descriptors.
    /// </summary>
    private DescriptorSetLayout CreateTextureSetLayout()
    {
        Span<DescriptorSetLayoutBinding> textures =
        [
            new(
                binding: 0,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: TextureStore.Capacity,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
        ];
        DescriptorBindingFlags textureFlags =
            DescriptorBindingFlags.PartiallyBoundBit
            | DescriptorBindingFlags.VariableDescriptorCountBit
            | DescriptorBindingFlags.UpdateAfterBindBit;
        var bindingFlags = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 1,
            PBindingFlags = &textureFlags,
        };
        return CreateLayout(
            bindings: textures,
            name: "texture set",
            next: &bindingFlags,
            flags: DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit
        );
    }

    /// <summary>
    /// The layout of the G-buffer set: five G-buffer images, the shadow cascades, <paramref name="shadowSampler"/> as an
    /// immutable sampler, the local shadow atlas, partially bound, the shading target, the sixth
    /// G-buffer image, at binding 10, and the atlas's bounds, partially bound. The sampler
    /// is immutable because portability implementations may not allow updating comparison sampler descriptors
    /// (VkPhysicalDevicePortabilitySubsetFeaturesKHR::mutableComparisonSamplers).
    /// </summary>
    private DescriptorSetLayout CreateGBufferSetLayout(Sampler shadowSampler)
    {
        const ShaderStageFlags graphics = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        // Compute shaders read the G-buffer: the depth pyramid's builds, the screen tracer and the accumulator.
        const ShaderStageFlags shading = ShaderStageFlags.FragmentBit | ShaderStageFlags.ComputeBit;
        Span<DescriptorSetLayoutBinding> gbuffer =
        [
            Image(binding: 0, stages: shading),
            Image(binding: 1, stages: shading),
            Image(binding: 2, stages: shading),
            Image(binding: 3, stages: shading),
            Image(binding: 4, stages: shading),
            // The vertex shader of the lit puffs samples the cascades once per puff, at its center.
            Image(binding: 5, stages: graphics),
            ImmutableSampler(binding: 6, stages: graphics, sampler: &shadowSampler),
            // The lit puffs sample the tiles in the vertex stage, as they do the cascades. The compute shaders that include
            // the lighting declare the atlas but never use it.
            Image(binding: ShadowAtlas.AtlasBinding, stages: graphics),
            // The screen tracer reads each pixel's surface, its model and its light mark included, and a hair receiver's
            // own card, which its rays pass through.
            Image(binding: RenderTargets.ShadingBinding, stages: shading),
            // Every stage that samples the tiles reads their bounds first.
            Image(binding: ShadowAtlas.BoundsBinding, stages: graphics),
        ];
        // The flags follow the bindings' order in the list, which skips binding 9.
        Span<DescriptorBindingFlags> flags = stackalloc DescriptorBindingFlags[gbuffer.Length];
        for (int i = 0; i < gbuffer.Length; i++)
            if (gbuffer[i].Binding is ShadowAtlas.AtlasBinding or ShadowAtlas.BoundsBinding)
                flags[i] = DescriptorBindingFlags.PartiallyBoundBit;
        return CreateLayout(bindings: gbuffer, name: "g-buffer set", bindingFlags: flags);
    }

    /// <summary>The pipeline layouts of the graphics passes, after the set layouts that they hold.</summary>
    private (
        PipelineLayout Scene,
        PipelineLayout Impostor,
        PipelineLayout Composite,
        PipelineLayout Overlay,
        PipelineLayout Post,
        PipelineLayout Bloom
    ) CreateGraphicsLayouts()
    {
        const ShaderStageFlags graphics = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        var drawPush = new PushConstantRange(graphics, 0, (uint)Unsafe.SizeOf<DrawPushConstants>());
        ReadOnlySpan<DescriptorSetLayout> sceneSets = [FrameSet, TextureSet, GBufferSet, AtlasSet];
        PipelineLayout sceneLayout = CreatePipelineLayout(sets: sceneSets[..3], push: drawPush, name: "scene layout");
        PipelineLayout impostorLayout = CreatePipelineLayout(sets: sceneSets, push: drawPush, name: "impostor layout");
        PipelineLayout compositeLayout = CreatePipelineLayout(
            sets: [FrameSet, TextureSet, GBufferSet, LightSet],
            push: drawPush,
            name: "composite layout"
        );
        PipelineLayout overlayLayout = CreatePipelineLayout(
            sets: sceneSets[..3],
            push: new(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<OutlinePushConstants>()),
            name: "overlay layout"
        );
        PipelineLayout postLayout = CreatePipelineLayout(
            sets: [PostSet],
            push: new(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<ToneMapPushConstants>()),
            name: "post layout"
        );
        PipelineLayout bloomLayout = CreatePipelineLayout(
            sets: [GlowSourceSet],
            push: new(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<BloomPushConstants>()),
            name: "bloom layout"
        );
        return (sceneLayout, impostorLayout, compositeLayout, overlayLayout, postLayout, bloomLayout);
    }

    /// <summary>
    /// The pipeline layouts of the compute passes: the exposure, the environment map's prefilter, the depth pyramid, the
    /// deformation, the skin scatter and the shadow bounds.
    /// </summary>
    private (
        PipelineLayout Exposure,
        PipelineLayout EnvironmentPrefilter,
        PipelineLayout Pyramid,
        PipelineLayout PyramidTail,
        PipelineLayout Deform,
        PipelineLayout Scatter,
        PipelineLayout ShadowBounds
    ) CreateComputeLayouts()
    {
        PipelineLayout exposure = ComputeLayout<ExposureAdaptPushConstants>(
            sets: [MeterSet, ExposureSet],
            name: "exposure layout"
        );
        PipelineLayout environmentPrefilter = ComputeLayout<EnvironmentPrefilterPushConstants>(
            sets: [EnvironmentPrefilterSet, EnvironmentLevelsSet],
            name: "environment prefilter layout"
        );
        PipelineLayout pyramid = ComputeLayout<PyramidTailPushConstants>(
            sets: [FrameSet, TextureSet, GBufferSet, PyramidLevelSet],
            name: "pyramid layout"
        );
        PipelineLayout pyramidTail = ComputeLayout<PyramidTailPushConstants>(
            sets: [FrameSet, TextureSet, GBufferSet, PyramidTailSet],
            name: "pyramid tail layout"
        );
        // The deform set alone, so its compute stage sees six storage buffers, within the ten that the scene layout
        // already needs of every device.
        PipelineLayout deform = ComputeLayout<DeformPushConstants>(sets: [DeformSet], name: "deform layout");
        PipelineLayout scatter = CreatePipelineLayout(
            sets: [FrameSet, TextureSet, GBufferSet, ScatterSet],
            push: null,
            name: "scatter layout"
        );
        PipelineLayout shadowBounds = ComputeLayout<ShadowBoundsPushConstants>(
            sets: [ShadowBoundsSet],
            name: "shadow bounds layout"
        );
        return (exposure, environmentPrefilter, pyramid, pyramidTail, deform, scatter, shadowBounds);
    }

    /// <summary>A layout of <paramref name="sets"/> whose one push constant range is a <typeparamref name="TPush"/> for the compute stage.</summary>
    private PipelineLayout ComputeLayout<TPush>(ReadOnlySpan<DescriptorSetLayout> sets, string name)
        where TPush : unmanaged =>
        CreatePipelineLayout(
            sets: sets,
            push: new(ShaderStageFlags.ComputeBit, 0, (uint)Unsafe.SizeOf<TPush>()),
            name: name
        );

    /// <summary>The sizes of the shared pool, which holds <paramref name="glowSourceSets"/> glow source sets.</summary>
    private DescriptorPoolSize[] SharedPoolSizes(uint frames, uint glowSourceSets) =>
        [
            // One per frame set, and the scatter set's kernel.
            new(DescriptorType.UniformBuffer, frames + 1),
            // Those of each frame set and each deform set, the two of the exposure set and the two lists of each screen
            // tracer set.
            new(
                DescriptorType.StorageBuffer,
                (FrameSetStorageBuffers + DeformSetStorageBuffers) * frames
                    + 2
                    + ScreenTraceStorageBuffers * ScreenTraceSets
            ),
            // Three per frame set (the material sampler and the immutable linear clamp and environment samplers), the
            // immutable samplers of the G-buffer, atlas, post and prefilter sets, and one per glow source set.
            new(DescriptorType.Sampler, 3 * frames + 4 + glowSourceSets),
            // 20 for the G-buffer, post and atlas sets, five per frame set for the tables, the specular map, the irradiance
            // cube and the distance cube, one per glow source set, two for the meter set, one per pyramid level set, one for
            // each pyramid's tail set, one for the prefilter set, one for the shadow bounds set, two for each surface test
            // set, two for each signal set and four for each screen tracer set.
            new(
                DescriptorType.SampledImage,
                20
                    + 5 * frames
                    + glowSourceSets
                    + 2
                    + PyramidLevelSets
                    + Pyramids
                    + 1
                    + 1
                    + 2 * SurfaceTestSets
                    + SignalSetSampledImages * SignalSets
                    + (ScreenTraceSampledImages + ScreenTraceLightImages) * ScreenTraceSets
            ),
            // One per pyramid level set, each tail set's mips, four per environment levels set, three for the scatter set,
            // one for the shadow bounds set, four per surface test set, SignalSetImages per signal set, six for the light
            // set, fifteen per screen tracer set, plus ScreenTraceAgeImages, and four per filter set.
            new(
                DescriptorType.StorageImage,
                PyramidLevelSets
                    + Pyramids * PyramidTail.Mips
                    + 4 * EnvironmentLevelsSets
                    + 3
                    + 1
                    + 4 * SurfaceTestSets
                    + SignalSetImages * SignalSets
                    + LightSetImages
                    + (ScreenTraceStorageImages + ScreenTraceAgeImages) * ScreenTraceSets
                    + FilterSetImages * FilterSets
            ),
        ];

    /// <summary>The shared pool, which holds <paramref name="glowSourceSets"/> glow source sets, and the pool of the texture sets.</summary>
    private (SetPool Shared, SetPool Textures) CreatePools(uint framesInFlight, uint glowSourceSets)
    {
        uint frames = framesInFlight;
        // The frame sets, the deform sets, the G-buffer, post, atlas, meter, exposure, prefilter, scatter, shadow
        // bounds and light sets, the glow source sets, the pyramid level sets, each pyramid's tail set, the environment
        // levels sets, the surface test sets, the signal sets, the screen tracer sets and the filter sets. The tracer's sets
        // exist even with the tracer off: they cost nothing per frame.
        SetPool shared = CreatePool(
            sizes: SharedPoolSizes(frames, glowSourceSets),
            maxSets: 2 * frames
                + 10
                + Pyramids
                + glowSourceSets
                + PyramidLevelSets
                + EnvironmentLevelsSets
                + SurfaceTestSets
                + SignalSets
                + ScreenTraceSets
                + FilterSets,
            flags: 0,
            name: "descriptor pool"
        );
        Span<DescriptorPoolSize> textureSizes = [new(DescriptorType.SampledImage, frames * TextureStore.Capacity)];
        SetPool textures = CreatePool(
            sizes: textureSizes,
            maxSets: frames,
            flags: DescriptorPoolCreateFlags.UpdateAfterBindBit,
            name: "texture descriptor pool"
        );
        return (shared, textures);
    }

    /// <summary>The layouts of the meter set and the exposure set, each with two bindings that the compute stage reads.</summary>
    private (DescriptorSetLayout Meter, DescriptorSetLayout Exposure) CreateExposureSetLayouts()
    {
        Span<DescriptorSetLayoutBinding> meter =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            Image(binding: 1, stages: ShaderStageFlags.ComputeBit),
        ];
        Span<DescriptorSetLayoutBinding> exposure =
        [
            new(
                binding: 0,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            ),
            new(
                binding: 1,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            ),
        ];
        return (
            CreateLayout(bindings: meter, name: "meter set"),
            CreateLayout(bindings: exposure, name: "exposure set")
        );
    }

    /// <summary>
    /// The layouts of the prefilter set, which holds the radiance map and <paramref name="environmentSampler"/> as its
    /// immutable sampler, and of the levels set, which holds four storage images.
    /// </summary>
    private (DescriptorSetLayout Prefilter, DescriptorSetLayout Levels) CreateEnvironmentSetLayouts(
        Sampler environmentSampler
    )
    {
        Span<DescriptorSetLayoutBinding> prefilter =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            ImmutableSampler(binding: 1, stages: ShaderStageFlags.ComputeBit, sampler: &environmentSampler),
        ];
        Span<DescriptorSetLayoutBinding> levels =
        [
            StorageImage(binding: 0),
            StorageImage(binding: 1),
            StorageImage(binding: 2),
            StorageImage(binding: 3),
        ];
        return (
            CreateLayout(bindings: prefilter, name: "environment prefilter set"),
            CreateLayout(bindings: levels, name: "environment levels set")
        );
    }

    /// <summary>The layouts of the pyramid level set and the pyramid tail set, whose bindings the compute stage reads.</summary>
    private (DescriptorSetLayout Level, DescriptorSetLayout PyramidTail) CreatePyramidSetLayouts()
    {
        Span<DescriptorSetLayoutBinding> level =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            StorageImage(binding: 1),
        ];
        Span<DescriptorSetLayoutBinding> tail =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            StorageImage(binding: 1) with
            {
                DescriptorCount = PyramidTail.Mips,
            },
        ];
        return (
            CreateLayout(bindings: level, name: "pyramid level set"),
            CreateLayout(bindings: tail, name: "pyramid tail set")
        );
    }

    /// <summary>The layout of the deform set: <see cref="DeformSetStorageBuffers"/> storage buffers for the compute stage.</summary>
    private DescriptorSetLayout CreateDeformSetLayout()
    {
        Span<DescriptorSetLayoutBinding> bindings = stackalloc DescriptorSetLayoutBinding[(int)DeformSetStorageBuffers];
        for (uint binding = 0; binding < DeformSetStorageBuffers; binding++)
            bindings[(int)binding] = new(
                binding: binding,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            );
        return CreateLayout(bindings: bindings, name: "deform set");
    }

    /// <summary>
    /// The layout of the scatter set: three storage images and the kernel's uniform buffer, for the compute stage. The
    /// diffuse image, binding 0, is also for the fragment stage, where the ambient pass adds to it.
    /// </summary>
    private DescriptorSetLayout CreateScatterSetLayout()
    {
        Span<DescriptorSetLayoutBinding> scatter =
        [
            StorageImage(binding: 0),
            StorageImage(binding: 1),
            StorageImage(binding: 2),
            new(
                binding: 3,
                descriptorType: DescriptorType.UniformBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            ),
        ];
        return CreateLayout(bindings: scatter, name: "scatter set");
    }

    /// <summary>The layout of the shadow bounds set: the atlas as a sampled image and its bounds as a storage image, for the compute stage.</summary>
    private DescriptorSetLayout CreateShadowBoundsSetLayout()
    {
        Span<DescriptorSetLayoutBinding> bounds =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            StorageImage(binding: 1),
        ];
        return CreateLayout(bindings: bounds, name: "shadow bounds set");
    }

    /// <summary>
    /// The layout of the surface test set: the last surface history and the last device depth as sampled images and four
    /// storage images, for the compute stage.
    /// </summary>
    private DescriptorSetLayout CreateSurfaceTestSetLayout()
    {
        Span<DescriptorSetLayoutBinding> surface =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            StorageImage(binding: 1),
            StorageImage(binding: 2),
            StorageImage(binding: 3),
            StorageImage(binding: 4),
            Image(binding: 5, stages: ShaderStageFlags.ComputeBit),
        ];
        return CreateLayout(bindings: surface, name: "surface test set");
    }

    /// <summary>The layout of the screen tracer set (<see cref="ScreenTraceSet"/>).</summary>
    private DescriptorSetLayout CreateScreenTraceSetLayout()
    {
        const uint lists = ScreenTraceSampledImages + ScreenTraceStorageImages;
        // The bindings before the light pyramid's two images, which start at ScreenTraceLightBinding.
        int beforeLight = (int)(lists + ScreenTraceStorageBuffers + ScreenTraceAgeImages);
        Span<DescriptorSetLayoutBinding> bindings =
            stackalloc DescriptorSetLayoutBinding[beforeLight + (int)ScreenTraceLightImages];
        for (uint binding = 0; binding < ScreenTraceSampledImages; binding++)
            bindings[(int)binding] = Image(binding: binding, stages: ShaderStageFlags.ComputeBit);
        for (uint binding = ScreenTraceSampledImages; binding < lists; binding++)
            bindings[(int)binding] = StorageImage(binding);
        for (uint binding = lists; binding < lists + ScreenTraceStorageBuffers; binding++)
            bindings[(int)binding] = new(
                binding: binding,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            );
        for (uint binding = lists + ScreenTraceStorageBuffers; binding < beforeLight; binding++)
            bindings[(int)binding] = StorageImage(binding);
        for (uint image = 0; image < ScreenTraceLightImages; image++)
            bindings[beforeLight + (int)image] = Image(
                binding: ScreenTraceLightBinding + image,
                stages: ShaderStageFlags.ComputeBit
            );
        return CreateLayout(bindings: bindings, name: "screen trace set");
    }

    /// <summary>The layouts of the signal set (<see cref="SignalSet"/>) and the light set (<see cref="LightSet"/>).</summary>
    private (DescriptorSetLayout Signal, DescriptorSetLayout Light) CreateTemporalSetLayouts()
    {
        // The bindings by number: 0 to 10, 11 with the light age, 12 with an instrumentation, then the block sums, and the
        // reflections' retained deviation pair with a precise history.
        Span<DescriptorSetLayoutBinding> signal =
            stackalloc DescriptorSetLayoutBinding[(int)(SignalSetImages + SignalSetSampledImages)];
        int count = 0;
        for (uint binding = 0; binding < BlockSumsBinding; binding++)
            if (
                binding <= 10
                || binding == 11 && HistoryMode.KeepsLightAge
                || binding == 12 && Instrumentation != HistoryInstrumentation.None
            )
                signal[count++] = binding is 7 or 8
                    ? Image(binding: binding, stages: ShaderStageFlags.ComputeBit)
                    : StorageImage(binding);
        signal[count] = StorageImage(BlockSumsBinding);
        if (HistoryMode.KeepsRetainedDeviation)
        {
            signal[count + 1] = StorageImage(LastDeviationBinding);
            signal[count + 2] = StorageImage(DeviationBinding);
        }
        Span<DescriptorSetLayoutBinding> light = stackalloc DescriptorSetLayoutBinding[(int)LightSetImages];
        for (uint binding = 0; binding < LightSetImages; binding++)
            light[(int)binding] = StorageImage(binding: binding, stages: ShaderStageFlags.FragmentBit);
        return (CreateLayout(bindings: signal, name: "signal set"), CreateLayout(bindings: light, name: "light set"));
    }

    /// <summary>The layout of the filter set: <see cref="FilterSetImages"/> storage images for the compute stage.</summary>
    private DescriptorSetLayout CreateFilterSetLayout()
    {
        Span<DescriptorSetLayoutBinding> filter = stackalloc DescriptorSetLayoutBinding[(int)FilterSetImages];
        for (uint binding = 0; binding < FilterSetImages; binding++)
            filter[(int)binding] = StorageImage(binding);
        return CreateLayout(bindings: filter, name: "filter set");
    }

    /// <summary>A binding of one storage image for the compute stage.</summary>
    private static DescriptorSetLayoutBinding StorageImage(uint binding) =>
        StorageImage(binding, ShaderStageFlags.ComputeBit);

    /// <summary>A binding of one storage image for <paramref name="stages"/>.</summary>
    private static DescriptorSetLayoutBinding StorageImage(uint binding, ShaderStageFlags stages) =>
        new(binding: binding, descriptorType: DescriptorType.StorageImage, descriptorCount: 1, stageFlags: stages);

    private Sampler CreateShadowSampler()
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            CompareEnable = true,
            CompareOp = CompareOp.GreaterOrEqual,
            MinLod = 0f,
            MaxLod = 0f,
        };
        device
            .Vk.CreateSampler(
                device: device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pSampler: out Sampler sampler
            )
            .Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, "shadow comparison sampler");
        return sampler;
    }

    /// <summary>
    /// The layout of set 0, whose binding 10 holds <paramref name="linearClamp"/> and binding 13
    /// <paramref name="environment"/> as their immutable samplers. Bindings 12, 14 and 16 are partially bound: they stay
    /// empty with the map off, as the bindless array's unused slots do (docs/plans/environment-map.md, decision 29, and
    /// docs/plans/environment-map-distances.md, decision 29). Binding 15, the tile views, is partially bound too: it stays
    /// empty without the local shadow atlas. So is binding 18, the kept positions, which stays empty without the
    /// deformation.
    /// </summary>
    private DescriptorSetLayout CreateFrameSetLayout(Sampler linearClamp, Sampler environment)
    {
        const ShaderStageFlags graphics = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        // Compute shaders read these bindings: the depth pyramid's builds, the screen tracer and the accumulator read the
        // frame constants, the tables and the exposure.
        const ShaderStageFlags graphicsAndCompute = graphics | ShaderStageFlags.ComputeBit;
        const ShaderStageFlags shading = ShaderStageFlags.FragmentBit | ShaderStageFlags.ComputeBit;
        Span<DescriptorSetLayoutBinding> frame =
        [
            new(
                binding: 0,
                descriptorType: DescriptorType.UniformBuffer,
                descriptorCount: 1,
                stageFlags: graphicsAndCompute
            ),
            StorageBuffer(binding: 1, stages: graphics),
            StorageBuffer(binding: 2, stages: graphics),
            StorageBuffer(binding: 3, stages: graphics),
            StorageBuffer(binding: 4, stages: graphics),
            new(binding: 5, descriptorType: DescriptorType.Sampler, descriptorCount: 1, stageFlags: graphics),
            StorageBuffer(binding: 6, stages: graphics),
            StorageBuffer(binding: 7, stages: graphics),
            // The tables sit in the frame set because the impostor capture binds only sets 0 and 1.
            Image(binding: 8, stages: shading),
            Image(binding: 9, stages: shading),
            new(
                binding: 10,
                descriptorType: DescriptorType.Sampler,
                descriptorCount: 1,
                stageFlags: shading,
                pImmutableSamplers: &linearClamp
            ),
            StorageBuffer(binding: 11, stages: graphicsAndCompute),
            Image(binding: 12, stages: shading),
            // The lit puffs read the irradiance cube's mean in the vertex stage, through this sampler.
            ImmutableSampler(binding: 13, stages: graphicsAndCompute, sampler: &environment),
            Image(binding: 14, stages: graphicsAndCompute),
            // The lit puffs sample the tiles in the vertex stage. The reflections' compute shaders declare the views but
            // never use them.
            StorageBuffer(binding: ShadowAtlas.TileViewsBinding, stages: graphics),
            // The distance cubes, which the shading passes search in the fragment and compute stages.
            Image(binding: 16, stages: shading),
            // The motion rows, which the G-buffer's vertex stage and the billboards' fragment stage read.
            StorageBuffer(binding: MotionsBinding, stages: graphics),
            // The last drawn frame's kept positions, which the G-buffer's vertex stage reads.
            StorageBuffer(binding: KeptPositionsBinding, stages: ShaderStageFlags.VertexBit),
            // The LTC table of the rectangle lights, which every pass that shades lights reads beside the other tables.
            Image(binding: LtcTableBinding, stages: shading),
        ];
        Span<DescriptorBindingFlags> flags = stackalloc DescriptorBindingFlags[frame.Length];
        foreach (uint binding in (ReadOnlySpan<uint>)[12, 14, ShadowAtlas.TileViewsBinding, 16, KeptPositionsBinding])
            flags[(int)binding] = DescriptorBindingFlags.PartiallyBoundBit;
        return CreateLayout(bindings: frame, name: "frame set", bindingFlags: flags);
    }

    /// <summary>Trilinear, clamped to the edge, over every level: the sampler of the impostor atlas and the environment map's cubes.</summary>
    private Sampler CreateTrilinearClampSampler(string name)
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MinLod = 0f,
            MaxLod = Vk.LodClampNone,
        };
        device
            .Vk.CreateSampler(
                device: device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pSampler: out Sampler sampler
            )
            .Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, name);
        return sampler;
    }

    /// <summary>A sampler of level 0 only, clamped to the edge, with no anisotropy.</summary>
    private Sampler CreateLevelZeroSampler(Filter filter, string name)
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = filter,
            MinFilter = filter,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MinLod = 0f,
            MaxLod = 0f,
        };
        device
            .Vk.CreateSampler(
                device: device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pSampler: out Sampler sampler
            )
            .Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, name);
        return sampler;
    }

    /// <summary>A layout whose bindings carry <paramref name="bindingFlags"/>, one per binding, such as partially bound.</summary>
    private DescriptorSetLayout CreateLayout(
        Span<DescriptorSetLayoutBinding> bindings,
        string name,
        ReadOnlySpan<DescriptorBindingFlags> bindingFlags
    )
    {
        fixed (DescriptorBindingFlags* p = bindingFlags)
        {
            var info = new DescriptorSetLayoutBindingFlagsCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
                BindingCount = (uint)bindingFlags.Length,
                PBindingFlags = p,
            };
            return CreateLayout(bindings: bindings, name: name, next: &info);
        }
    }

    private DescriptorSetLayout CreateLayout(
        Span<DescriptorSetLayoutBinding> bindings,
        string name,
        void* next = null,
        DescriptorSetLayoutCreateFlags flags = 0
    )
    {
        fixed (DescriptorSetLayoutBinding* p = bindings)
        {
            var info = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                PNext = next,
                Flags = flags,
                BindingCount = (uint)bindings.Length,
                PBindings = p,
            };
            device
                .Vk.CreateDescriptorSetLayout(
                    device: device.Device,
                    pCreateInfo: &info,
                    pAllocator: null,
                    pSetLayout: out DescriptorSetLayout layout
                )
                .Check("vkCreateDescriptorSetLayout");
            device.SetName(ObjectType.DescriptorSetLayout, layout.Handle, name);
            bool updatedAfterBind = (flags & DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit) != 0;
            var counts = default(LimitedDescriptors);
            foreach (DescriptorSetLayoutBinding binding in bindings)
                counts = counts.With(binding: binding, updatedAfterBind: updatedAfterBind);
            setDescriptors[layout.Handle] = counts;
            return layout;
        }
    }

    /// <param name="push">The one push constant range, or null for a layout without push constants.</param>
    private PipelineLayout CreatePipelineLayout(
        ReadOnlySpan<DescriptorSetLayout> sets,
        PushConstantRange? push,
        string name
    )
    {
        var counts = default(LimitedDescriptors);
        foreach (DescriptorSetLayout set in sets)
            counts = counts.Plus(setDescriptors[set.Handle]);
        // Before the layout exists: a layout above a per-stage limit breaks the valid usage of vkCreatePipelineLayout.
        if (counts.Problem(device.Properties.Limits) is { } problem)
        {
            Dispose();
            throw new NotSupportedException($"The {name}: {problem} ({device.DeviceName})");
        }
        PipelineLayout layout;
        PushConstantRange range = push ?? default;
        fixed (DescriptorSetLayout* setLayouts = sets)
        {
            var info = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = (uint)sets.Length,
                PSetLayouts = setLayouts,
                PushConstantRangeCount = push is null ? 0u : 1u,
                PPushConstantRanges = &range,
            };
            device
                .Vk.CreatePipelineLayout(
                    device: device.Device,
                    pCreateInfo: &info,
                    pAllocator: null,
                    pPipelineLayout: out layout
                )
                .Check("vkCreatePipelineLayout");
        }
        device.SetName(ObjectType.PipelineLayout, layout.Handle, name);
        layoutDescriptors[layout.Handle] = counts;
        return layout;
    }

    private SetPool CreatePool(
        Span<DescriptorPoolSize> sizes,
        uint maxSets,
        DescriptorPoolCreateFlags flags,
        string name
    )
    {
        fixed (DescriptorPoolSize* p = sizes)
        {
            var info = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                Flags = flags,
                MaxSets = maxSets,
                PoolSizeCount = (uint)sizes.Length,
                PPoolSizes = p,
            };
            device
                .Vk.CreateDescriptorPool(
                    device: device.Device,
                    pCreateInfo: &info,
                    pAllocator: null,
                    pDescriptorPool: out DescriptorPool created
                )
                .Check("vkCreateDescriptorPool");
            device.SetName(ObjectType.DescriptorPool, created.Handle, name);
            return new SetPool(created, maxSets);
        }
    }

    /// <summary>
    /// A set of <paramref name="layout"/> from <paramref name="from"/>. The pool refuses a set past its <c>maxSets</c>
    /// itself, because the spec lets a driver give one past it or refuse it (ARCHITECTURE.md, "Descriptor pools").
    /// </summary>
    /// <exception cref="VulkanException">The pool holds no more sets, or the driver refused one.</exception>
    private DescriptorSet Allocate(SetPool from, DescriptorSetLayout layout, void* next)
    {
        if (from.Given == from.MaxSets)
            throw new VulkanException("vkAllocateDescriptorSets", Result.ErrorOutOfPoolMemory);
        var info = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            PNext = next,
            DescriptorPool = from.Handle,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };
        device.Vk.AllocateDescriptorSets(device.Device, &info, out DescriptorSet set).Check("vkAllocateDescriptorSets");
        from.Given++;
        return set;
    }

    /// <summary>A descriptor pool, the most sets that it holds, and the sets that it gave. No set returns to it.</summary>
    private sealed class SetPool(DescriptorPool handle, uint maxSets)
    {
        public DescriptorPool Handle { get; } = handle;

        public uint MaxSets { get; } = maxSets;

        public uint Given { get; set; }
    }

    public void Dispose()
    {
        Vk vk = device.Vk;
        // Destroying a pool frees its sets.
        vk.DestroyDescriptorPool(device.Device, pool.Handle, null);
        vk.DestroyDescriptorPool(device.Device, texturePool.Handle, null);

        // The tables hold every layout that this object made, also while a refused device stops the constructor.
        foreach (ulong layout in layoutDescriptors.Keys)
            vk.DestroyPipelineLayout(device.Device, new PipelineLayout(layout), null);
        foreach (ulong set in setDescriptors.Keys)
            vk.DestroyDescriptorSetLayout(device.Device, new DescriptorSetLayout(set), null);

        vk.DestroySampler(device.Device, ShadowSampler, null);
        vk.DestroySampler(device.Device, AtlasSampler, null);
        vk.DestroySampler(device.Device, LinearClampSampler, null);
        vk.DestroySampler(device.Device, EnvironmentSampler, null);
    }
}

/// <summary>
/// The descriptors of a set layout or a pipeline layout that the device's per-stage limits bound: the storage buffers,
/// storage images and sampled images that each shader stage sees, outside the sets that are updated after bind.
/// </summary>
internal readonly record struct LimitedDescriptors(
    PerStageCounts StorageBuffers,
    PerStageCounts StorageImages,
    PerStageCounts SampledImages
)
{
    /// <summary>
    /// These descriptors and those of <paramref name="binding"/>. The descriptors of a set that is
    /// <paramref name="updatedAfterBind"/> count against the update-after-bind limits instead, so they do not count here.
    /// </summary>
    public LimitedDescriptors With(in DescriptorSetLayoutBinding binding, bool updatedAfterBind) =>
        updatedAfterBind
            ? this
            : binding.DescriptorType switch
            {
                DescriptorType.StorageBuffer => this with
                {
                    StorageBuffers = StorageBuffers.Plus(binding.StageFlags, binding.DescriptorCount),
                },
                DescriptorType.StorageImage => this with
                {
                    StorageImages = StorageImages.Plus(binding.StageFlags, binding.DescriptorCount),
                },
                DescriptorType.SampledImage or DescriptorType.CombinedImageSampler => this with
                {
                    SampledImages = SampledImages.Plus(binding.StageFlags, binding.DescriptorCount),
                },
                _ => this,
            };

    /// <summary>These descriptors and <paramref name="other"/>, stage by stage.</summary>
    public LimitedDescriptors Plus(LimitedDescriptors other) =>
        new(
            StorageBuffers: StorageBuffers.Plus(other.StorageBuffers),
            StorageImages: StorageImages.Plus(other.StorageImages),
            SampledImages: SampledImages.Plus(other.SampledImages)
        );

    /// <summary>
    /// Why a device with <paramref name="limits"/> cannot make a pipeline layout with these descriptors, or null when it
    /// can: one stage sees more of a type than the device's maxPerStageDescriptor limit of that type. Vulkan guarantees
    /// only 4 storage buffers, 4 storage images and 16 sampled images (ARCHITECTURE.md, "Required device features").
    /// </summary>
    public string? Problem(in PhysicalDeviceLimits limits) =>
        Above(StorageBuffers.Most, limits.MaxPerStageDescriptorStorageBuffers, "storage buffers")
        ?? Above(StorageImages.Most, limits.MaxPerStageDescriptorStorageImages, "storage images")
        ?? Above(SampledImages.Most, limits.MaxPerStageDescriptorSampledImages, "sampled images");

    private static string? Above(uint count, uint limit, string descriptors) =>
        count > limit
            ? $"a shader stage sees {count} {descriptors}, above the device's limit of {limit} {descriptors} in a stage."
            : null;
}

/// <summary>The descriptors of one type that the vertex, fragment and compute stages each see.</summary>
internal readonly record struct PerStageCounts(uint Vertex, uint Fragment, uint Compute)
{
    /// <summary>The most that one stage sees.</summary>
    public uint Most => Math.Max(Vertex, Math.Max(Fragment, Compute));

    /// <summary>These and <paramref name="count"/> more for each of <paramref name="stages"/>.</summary>
    public PerStageCounts Plus(ShaderStageFlags stages, uint count) =>
        new(
            Vertex: Vertex + ((stages & ShaderStageFlags.VertexBit) != 0 ? count : 0),
            Fragment: Fragment + ((stages & ShaderStageFlags.FragmentBit) != 0 ? count : 0),
            Compute: Compute + ((stages & ShaderStageFlags.ComputeBit) != 0 ? count : 0)
        );

    /// <summary>These and <paramref name="other"/>, stage by stage.</summary>
    public PerStageCounts Plus(PerStageCounts other) =>
        new(Vertex: Vertex + other.Vertex, Fragment: Fragment + other.Fragment, Compute: Compute + other.Compute);
}
