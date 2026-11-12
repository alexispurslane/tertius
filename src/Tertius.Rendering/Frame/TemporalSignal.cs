using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The history of one signal: demodulated, pre-exposed light accumulated over frames at each pixel of the render size
/// (docs/plans/temporal-renderer.md, section 8, "The accumulator"). Its producer writes the sample of each part in each
/// frame. <see cref="Accumulate"/> then reprojects the last frame's history through the surface test, rescales it by the
/// exposure ratio, blends the sample in, fixes short histories from their neighbors and clamps the slow history into the
/// fast history's box. A pixel whose sample is not finite keeps its history, and one without a history takes the
/// environment's closed form. In a reset frame, the surface test fails every tap, so every history starts again.
/// </summary>
/// <remarks>
/// <para>
/// The screen tracer's two signals, under <see cref="ScreenTrace"/>, are the renderer's only signals: the direct light keeps
/// one frame (docs/plans/temporal-renderer.md, decision 29). The accumulator's tests make signals of their own
/// (<see cref="VulkanRenderer.CreateSignal(SignalParameters, string, int, bool)"/>).
/// </para>
/// <para>
/// Each part owns a sample, a slow history and two fast histories, which alternate by the parity of the drawn frames
/// (<see cref="FrameHistory.PendingFrame.Parity"/>), RGBA16F at the targets' size except the experiment's RGBA32F slow history. The slow history holds the length
/// in alpha, the specular signal's sample and fast history the hit distance, 0 for a miss, and the diffuse signal's fast
/// history the deviation of its samples' luminance. A signal that feeds the diffuse filter also owns the filter's input,
/// which its spatial dispatch writes (<see cref="FilterInput"/>). The sample is a color
/// attachment of its producer, and every other image stays in the General layout. The signals share the scratch images
/// (<see cref="SignalScratch"/>). With a precise history, the specular part also owns its sample's light age, which the
/// resolve writes and the temporal dispatch reads at binding 11 (the light-change age, ticket tiunr-7haj).
/// </para>
/// <para>Invariants, checked by <see cref="AssertInvariants"/>:</para>
/// <list type="bullet">
/// <item>Every image of every part has the size of the targets that the signal binds.</item>
/// <item>The signal sets hold one set per part and parity.</item>
/// <item>The parameter set has a cap and a fast cap of at least 1.</item>
/// </list>
/// </remarks>
internal sealed unsafe class TemporalSignal : IDisposable
{
    /// <summary>The sample, fast-history and guide format, also used by the original slow history.</summary>
    public const Format SignalFormat = Format.R16G16B16A16Sfloat;

    /// <summary>
    /// The format of the specular sample's light age with a precise history: 0 for no evidence, else the age in frames
    /// plus 1.
    /// </summary>
    public const Format SampleAgeFormat = Format.R16Uint;

    /// <summary>
    /// The format of the diagnostic of uncovered surfaces under <see cref="HistoryInstrumentation.LightAgeDiagnostic"/>: r the
    /// fewest frames seen among the never-changed surfaces that the specular sample's rays hit, or −1 for none, which the
    /// resolve writes; g the slow history's length just before its blend, after the light-age cut, or −1 without a
    /// sample; b 1 where the sample holds at least the cut share of the slow history's luminance, else 0; a the length
    /// before the light-age cut. The temporal dispatch writes g, b and a.
    /// </summary>
    public const Format UncoveredDiagnosticFormat = Format.R32G32B32A32Sfloat;

    /// <summary>The width and the height of a workgroup of both dispatches: the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly RenderTargets targets;
    private readonly SurfaceHistory surfaces;
    private readonly SignalScratch scratch;
    private readonly Pipeline temporal;
    private readonly Pipeline spatial;
    private readonly bool singleDiffuseHistory;
    private readonly bool reflectionDistanceHistory;
    private readonly PartImages[] parts;

    /// <summary>The signal sets, [part, parity].</summary>
    private readonly DescriptorSet[,] sets;

    /// <param name="targets">The render targets, whose size the images take, and whose motion target the sets bind.</param>
    /// <param name="surfaces">The surface history, whose test image and last images the sets bind.</param>
    /// <param name="scratch">The scratch images, which every signal shares.</param>
    /// <param name="name">The signal's name in the images' names and the passes' names, such as "indirect diffuse".</param>
    /// <param name="parts">The signal's parts, such as 2 for a diffuse and a specular part.</param>
    /// <param name="filtered">Whether a diffuse filter filters the signal (<see cref="FeedsFilter"/>).</param>
    /// <exception cref="NotSupportedException">The format is not a color attachment and a storage image on the device.</exception>
    public TemporalSignal(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        ShaderLibrary shaders,
        RenderTargets targets,
        SurfaceHistory surfaces,
        SignalScratch scratch,
        SignalParameters parameters,
        string name,
        int parts,
        bool filtered
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parts, MostParts);
        CheckDevice(device);

        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.targets = targets;
        this.surfaces = surfaces;
        this.scratch = scratch;

        Name = name;
        Parameters = parameters;
        HistoryMode = parameters.Light == SignalLight.Specular ? scratch.HistoryMode : SpecularHistoryMode.Original;
        Instrumentation =
            parameters.Light == SignalLight.Specular ? scratch.Instrumentation : HistoryInstrumentation.None;
        if (
            HistoryMode != SpecularHistoryMode.Original
            && (
                device.FormatProperties(Format.R32G32B32A32Sfloat).OptimalTilingFeatures
                & FormatFeatureFlags.StorageImageBit
            ) == 0
        )
            throw new NotSupportedException($"RGBA32F specular history storage is unavailable on {device.DeviceName}.");

        this.parts = new PartImages[parts];
        sets = new DescriptorSet[parts, 2];
        for (int part = 0; part < parts; part++)
        for (int parity = 0; parity < 2; parity++)
            sets[part, parity] = layouts.AllocateSignalSet();

        var prototype = IndirectLightPrototype.Select(
            enabled: Environment.GetEnvironmentVariable("TERTIUS_INDIRECT_LIGHT_PROTOTYPE") == "1",
            disableNeighborCap: Environment.GetEnvironmentVariable("TERTIUS_INDIRECT_LIGHT_NEIGHBOR_CAP") == "0",
            light: parameters.Light
        );
        singleDiffuseHistory = prototype.SingleDiffuseHistory;
        FeedsFilter = filtered && !singleDiffuseHistory;
        reflectionDistanceHistory =
            parameters.Light == SignalLight.Specular
            && Environment.GetEnvironmentVariable("TERTIUS_REFLECTION_SHARING") == "1";
        temporal = CreatePipeline(
            shaders,
            singleDiffuseHistory ? "diffuse_single_temporal" : "temporal",
            prototype.LimitIncoming
        );
        spatial = CreatePipeline(
            shaders,
            (singleDiffuseHistory, FeedsFilter) switch
            {
                (true, _) => "diffuse_single_spatial",
                (false, true) => "spatial_filtered",
                (false, false) => "spatial",
            }
        );
        Create();
    }

    /// <summary>The pipeline of the dispatch of <paramref name="kind"/>, such as "temporal", "spatial" or "spatial_filtered": accumulate_kind.comp.</summary>
    private Pipeline CreatePipeline(ShaderLibrary shaders, string kind, bool limitIncoming = false) =>
        new ComputePipelineDesc
        {
            Name = $"accumulate {Name} {kind}",
            Shader = HistoryShader($"accumulate_{kind}.comp"),
            Layout = layouts.AccumulateLayout,
            Constants =
                limitIncoming || reflectionDistanceHistory
                    ? new ShaderConstants(EnvironmentMap: false)
                    {
                        NeighborLimitPrototype = limitIncoming,
                        ReflectionSharingPrototype = reflectionDistanceHistory,
                    }
                    : null,
        }.Create(device, shaders);

    /// <summary>The signal's name in the images' names and the passes' names, such as "indirect diffuse".</summary>
    public string Name { get; }

    /// <summary>
    /// Whether the signal keeps fast histories of its own: not under the single-history prototype of the diffuse light,
    /// whose fast history is its slow history, so that the diffuse filter finds no deviation there.
    /// </summary>
    public bool KeepsFastHistory => !singleDiffuseHistory;

    /// <summary>
    /// Whether the signal feeds the diffuse filter: it was made filtered and keeps fast histories, whose deviation the
    /// filter's input takes. Its spatial dispatch then writes <see cref="FilterInput"/> (accumulate_spatial_filtered.comp).
    /// </summary>
    public bool FeedsFilter { get; }

    /// <summary>The signal's parts.</summary>
    public int Parts => parts.Length;

    /// <summary>The signal's parameter set.</summary>
    public SignalParameters Parameters { get; }

    /// <summary>The specular history's storage and age policy, or original storage for other signals.</summary>
    public SpecularHistoryMode HistoryMode { get; }

    /// <summary>The instrumentation of the specular history, or none for other signals.</summary>
    public HistoryInstrumentation Instrumentation { get; }

    /// <summary>
    /// The shader variant whose declared slow-history format matches this signal's storage (<see cref="HistoryShader(string,
    /// SpecularHistoryMode, HistoryInstrumentation)"/>).
    /// </summary>
    public string HistoryShader(string shader) => HistoryShader(shader, HistoryMode, Instrumentation);

    /// <summary>
    /// The variant of <paramref name="shader"/> for a history of <paramref name="mode"/>. The precise variants keep the
    /// light-change age. Under an instrumentation, the shaders of its variant take that variant, which also writes its
    /// image (<see cref="HistoryInstrumentationSelection"/>).
    /// </summary>
    internal static string HistoryShader(
        string shader,
        SpecularHistoryMode mode,
        HistoryInstrumentation instrumentation
    )
    {
        if (mode == SpecularHistoryMode.Original)
            return shader;
        string suffix =
            instrumentation.Variant is { } variant && variant.Shaders.Contains(shader)
                ? variant.Suffix
                : ShaderVariant.PreciseSuffix;
        return ShaderVariant.Of(shader, suffix);
    }

    /// <summary>The most parts of a signal: the descriptor pool holds the sets of a test's signal of two parts.</summary>
    public const int MostParts = 2;

    /// <summary>The sample of <paramref name="part"/>, which the producer writes in each frame: rgb the light, a 1.</summary>
    public GpuImage Sample(int part) => parts[part].Sample;

    /// <summary>
    /// The slow history of <paramref name="part"/> after the last accumulation: rgb the light, a the length. The diffuse
    /// filter is its second writer: its first step replaces the diffuse slow history's light (<see cref="DiffuseFilter"/>).
    /// </summary>
    public GpuImage Slow(int part) => parts[part].Slow;

    /// <summary>
    /// The fast history of <paramref name="part"/> that the frame of <paramref name="parity"/> writes: rgb the light, a the
    /// specular signal's hit distance or the diffuse signal's deviation of its samples' luminance (<see cref="DiffuseFilter"/>).
    /// </summary>
    public GpuImage Fast(int part, int parity) => parts[part].Fast(parity);

    public GpuImage ReflectionGuide(int part) => parts[part].ReflectionGuide;

    /// <summary>
    /// The diffuse filter's input of <paramref name="part"/> after the last accumulation, which the spatial dispatch of a
    /// signal that <see cref="FeedsFilter"/> writes: rgb the slow history's light, a the deviation of its samples'
    /// luminance, 0 off a lit surface.
    /// </summary>
    /// <exception cref="InvalidOperationException">The signal feeds no filter.</exception>
    public GpuImage FilterInput(int part) =>
        parts[part].FilterInputs is [var input]
            ? input
            : throw new InvalidOperationException($"The signal {Name} feeds no diffuse filter.");

    /// <summary>
    /// The light age of the sample of <paramref name="part"/>, which the resolve writes with a precise specular history
    /// (<see cref="SampleAgeFormat"/>), or null without it.
    /// </summary>
    public GpuImage? SampleAge(int part) => parts[part].Ages is [var age, ..] ? age : null;

    /// <summary>
    /// The diagnostic of uncovered surfaces of <paramref name="part"/> under <see cref="HistoryInstrumentation.LightAgeDiagnostic"/>
    /// (<see cref="UncoveredDiagnosticFormat"/>), or null without it. Only the capture tool and the tests read it.
    /// </summary>
    public GpuImage? UncoveredDiagnostic(int part) => parts[part].Ages is [_, var diagnostic] ? diagnostic : null;

    /// <summary>
    /// The layers of the accumulation trace (<see cref="AccumulationTrace(int)"/>), each RGBA32F at the targets' size. The
    /// temporal dispatch writes layers 0 to 3 and 8 and the spatial dispatch layers 4 to 7, for every pixel, 0 for sky. The
    /// spatial dispatch stores only values that it computes anyway, because an arithmetic of the trace's own changed the
    /// driver's rounding of the slow history:
    /// <list type="number">
    /// <item>The length that the last draw accepted at the pixel's own texel, the length after the reprojection and
    /// before the blend, the sum of the taps' weights, and flags: bits 0 to 3 the taps that hold weight, 4 the reflected
    /// place won, 5 it was tried, 6 the surface place holds a history, 7 the reflected place does, 8 the pixel has a
    /// sample, 9 it shows a surface, 10 it read at its pixel place (the pixel history rule, screen.glsl), 11 its tried
    /// reflected place is that place's pixel place.</item>
    /// <item>The four taps' bilinear weights times their pass bits, in the order of the surface test.</item>
    /// <item>Where the place that won lies relative to the pixel, in texels of the last image, so that tap 0 is its
    /// floor; the sample's hit distance, 0 for a miss; the sample's luminance, 0 without one.</item>
    /// <item>The least and the most length of the taps that hold weight, and the luminance of the reprojected fast and
    /// slow histories before the exposure ratio, 0 without a history.</item>
    /// <item>The length before and after the anti-lag.</item>
    /// <item>The slow history after the fix and before the clamp, and 1 where the clamp ran, else 0.</item>
    /// <item>The box's mean, the mean of the 3 × 3 fast histories, and the lag, 0 without the clamp.</item>
    /// <item>The fast histories' standard deviation, 0 without the clamp.</item>
    /// <item>The weight of the taps that pass the surface test at the pixel place and at the sample place, then of those
    /// that pass the specular test at each (ticket tiunr-wk29). The pixel place is the pixel moved by the scene's motion
    /// alone; under the control nothing reads it, but the trace weighs it.</item>
    /// </list>
    /// </summary>
    public const int TraceLayers = 9;

    /// <summary>The format of each layer of the accumulation trace.</summary>
    public const Format TraceFormat = Format.R32G32B32A32Sfloat;

    /// <summary>
    /// The accumulation trace of <paramref name="part"/> under <see cref="HistoryInstrumentation.ControlTrace"/>
    /// (<see cref="TraceLayers"/>), or null without it. Only the capture tool and the tests read it.
    /// </summary>
    public GpuImage? AccumulationTrace(int part) => parts[part].Traces is [var trace] ? trace : null;

    /// <summary>
    /// The images of <paramref name="part"/> that the screen tracer binds in a frame of <paramref name="parity"/>: the
    /// sample, which its resolve writes, the fast history that the last drawn frame wrote, which its hits read, and the
    /// slow history, whose length its ray plan reads before this frame's accumulation. Each pass binds one set of its own,
    /// because a pipeline gets at most four sets on every driver, so the tracer's set binds these images.
    /// </summary>
    public (GpuImage Sample, GpuImage LastFast, GpuImage Slow) HitImages(int part, int parity) =>
        (parts[part].Sample, parts[part].LastFast(parity), parts[part].Slow);

    /// <summary>
    /// Records both dispatches of each part: the temporal dispatch reprojects and blends into the scratch image and this
    /// frame's fast history, and the spatial dispatch fixes, clamps and writes the slow history. Its reads of the surface
    /// test's outputs follow the surface test pass, and its read of each sample follows the producer.
    /// </summary>
    public void Accumulate(FrameContext context)
    {
        Contract.Requires(context.Targets == targets && FitsTargets());
        int parity = context.History.Parity;
        BarrierBatch barriers = context.Barriers;
        barriers.Image(targets.Distance, ImageUse.SampledCompute);
        barriers.Image(targets.Normal, ImageUse.SampledCompute);
        barriers.Image(targets.Shading, ImageUse.SampledCompute);
        barriers.Image(surfaces.Test, ImageUse.StorageCompute);
        barriers.Image(targets.Motion, ImageUse.StorageCompute);

        // The closed form's material, and the last frame's surfaces of the reflected point's surface test.
        barriers.Image(targets.BaseColor, ImageUse.SampledCompute);
        barriers.Image(targets.Specular, ImageUse.SampledCompute);
        barriers.Image(targets.Emissive, ImageUse.SampledCompute);
        barriers.Image(surfaces.Last(parity), ImageUse.SampledCompute);
        barriers.Image(surfaces.LastDepth(parity), ImageUse.SampledCompute);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        for (int part = 0; part < parts.Length; part++)
        {
            PartImages images = parts[part];
            GpuImage scratched = scratch.Part(part, HistoryMode);
            if (!reflectionDistanceHistory && HistoryMode != SpecularHistoryMode.Original)
                barriers.Image(scratch.Part(part), ImageUse.StorageCompute);
            barriers.Image(images.Sample, ImageUse.StorageCompute);
            foreach (GpuImage age in images.Ages)
                barriers.Image(age, ImageUse.StorageCompute);
            foreach (GpuImage trace in images.Traces)
                barriers.Image(trace, ImageUse.StorageCompute);
            barriers.Image(images.Slow, ImageUse.StorageCompute);
            if (!singleDiffuseHistory)
                barriers.Image(images.LastFast(parity), ImageUse.StorageCompute);
            // Discarded: the temporal dispatch writes every pixel of each.
            if (!singleDiffuseHistory)
                barriers.Image(images.Fast(parity), ImageUse.StorageCompute, discard: true);
            if (reflectionDistanceHistory)
            {
                barriers.Image(images.ReflectionGuide, ImageUse.StorageCompute);
                barriers.Image(images.ReflectionGuideScratch, ImageUse.StorageCompute, discard: true);
            }
            // After the last signal's spatial dispatch, which read it.
            barriers.Image(scratched, ImageUse.StorageCompute, discard: true);
            Dispatch(context: context, pipeline: temporal, set: sets[part, parity]);

            // The spatial dispatch reads the neighbors' results of the temporal one.
            barriers.Image(scratched, ImageUse.StorageCompute);
            if (!reflectionDistanceHistory && HistoryMode != SpecularHistoryMode.Original)
                barriers.Image(scratch.Part(part), ImageUse.StorageCompute);
            if (!singleDiffuseHistory)
                barriers.Image(images.Fast(parity), ImageUse.StorageCompute);
            barriers.Image(images.Slow, ImageUse.StorageCompute);
            foreach (GpuImage trace in images.Traces)
                barriers.Image(trace, ImageUse.StorageCompute);
            if (reflectionDistanceHistory)
            {
                barriers.Image(images.ReflectionGuideScratch, ImageUse.StorageCompute);
                barriers.Image(images.ReflectionGuide, ImageUse.StorageCompute, discard: true);
            }
            // Discarded: the dispatch writes every pixel, after the last frame's filter read it.
            foreach (GpuImage input in images.FilterInputs)
                barriers.Image(input, ImageUse.StorageCompute, discard: true);
            Dispatch(context: context, pipeline: spatial, set: sets[part, parity]);
        }
    }

    /// <summary>
    /// Recreates the images at the size of the targets that the signal binds, and rewrites every set. The device must be
    /// idle, and the surface history resized first, because the sets point at its test image. A new size resets every
    /// history (<see cref="FrameHistory.Begin"/>), so the images' contents need not survive.
    /// </summary>
    public void Resize()
    {
        Contract.Requires(SameExtent(surfaces.Test.Extent, targets.Extent));
        Contract.Requires(SameExtent(scratch.Part(0, HistoryMode).Extent, targets.Extent));
        DestroyImages();
        Create();
        Contract.Ensures(FitsTargets());
    }

    /// <summary>Checks the invariants in the class remarks.</summary>
    internal void AssertInvariants()
    {
        Contract.Invariant(FitsTargets());
        Contract.Invariant(sets.GetLength(0) == parts.Length && sets.GetLength(1) == 2);
        Contract.Invariant(Parameters.Cap >= 1 && Parameters.FastCap >= 1);
    }

    /// <summary>Whether every image of every part has the targets' size. It allocates nothing, so a frame can check it.</summary>
    private bool FitsTargets()
    {
        foreach (PartImages images in parts)
            if (!images.Fit(targets.Extent))
                return false;
        return true;
    }

    private static bool SameExtent(Extent2D a, Extent2D b) => a.Width == b.Width && a.Height == b.Height;

    private void Dispatch(FrameContext context, Pipeline pipeline, DescriptorSet set)
    {
        context.BeginComputePass(pipeline: pipeline, layout: layouts.AccumulateLayout, passSet: set);
        var push = AccumulatePushConstants.Of(Parameters, context.History.Reset);
        if (HistoryMode == SpecularHistoryMode.PreciseUncapped)
        {
            push.Cap = float.PositiveInfinity;
            push.SmoothCap = float.PositiveInfinity;
        }
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layouts.AccumulateLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(AccumulatePushConstants),
            pValues: &push
        );
        Extent2D extent = context.Extent;
        context.Vk.CmdDispatch(
            commandBuffer: context.CommandBuffer,
            groupCountX: (extent.Width + GroupSize - 1) / GroupSize,
            groupCountY: (extent.Height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
    }

    private void Create()
    {
        Extent2D extent = targets.Extent;
        for (int part = 0; part < parts.Length; part++)
        {
            parts[part] = new PartImages(
                Sample: CreateImage(
                    extent,
                    $"{Name} sample {part}",
                    ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit
                ),
                Slow: CreateImage(
                    extent: extent,
                    imageName: $"{Name} slow history {part}",
                    extraUsage: 0,
                    format: HistoryMode == SpecularHistoryMode.Original ? SignalFormat : Format.R32G32B32A32Sfloat
                ),
                Fasts: singleDiffuseHistory
                    ? []
                    :
                    [
                        CreateImage(extent, $"{Name} fast history {part}.0", 0),
                        CreateImage(extent, $"{Name} fast history {part}.1", 0),
                    ],
                Guides: reflectionDistanceHistory
                    ?
                    [
                        CreateImage(extent, $"{Name} distance guide {part}", 0),
                        CreateImage(extent, $"{Name} distance guide scratch {part}", 0),
                    ]
                    : [],
                Ages: (HistoryMode.KeepsLightAge, Instrumentation) switch
                {
                    (false, _) => [],
                    (true, not HistoryInstrumentation.LightAgeDiagnostic) => [CreateSampleAge(extent, part)],
                    (true, HistoryInstrumentation.LightAgeDiagnostic) =>
                    [
                        CreateSampleAge(extent, part),
                        CreateImage(
                            extent: extent,
                            imageName: $"{Name} uncovered diagnostic {part}",
                            extraUsage: 0,
                            format: UncoveredDiagnosticFormat
                        ),
                    ],
                },
                FilterInputs: FeedsFilter ? [CreateImage(extent, $"{Name} filter input {part}", 0)] : [],
                Traces: Instrumentation == HistoryInstrumentation.ControlTrace
                    ?
                    [
                        GpuImage.Create(
                            device: device,
                            allocator: allocator,
                            extent: extent,
                            format: TraceFormat,
                            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
                            mipLevels: 1,
                            name: $"{Name} accumulation trace {part}",
                            arrayLayers: TraceLayers
                        ),
                    ]
                    : []
            );
            for (int parity = 0; parity < 2; parity++)
                WriteSignalSet(set: sets[part, parity], images: parts[part], part: part, parity: parity);
        }
    }

    private void WriteSignalSet(DescriptorSet set, PartImages images, int part, int parity)
    {
        layouts.WriteStorageImage(set: set, binding: 0, image: images.Sample);
        layouts.WriteStorageImage(set: set, binding: 1, image: images.Slow);
        layouts.WriteStorageImage(set: set, binding: 2, image: images.LastFast(parity));
        layouts.WriteStorageImage(set: set, binding: 3, image: images.Fast(parity));
        layouts.WriteStorageImage(set: set, binding: 4, image: scratch.Part(part, HistoryMode));
        layouts.WriteStorageImage(set: set, binding: 5, image: surfaces.Test);
        layouts.WriteStorageImage(set: set, binding: 6, image: targets.Motion);
        layouts.WriteImage(set: set, binding: 7, arrayElement: 0, image: surfaces.Last(parity));
        layouts.WriteImage(set: set, binding: 8, arrayElement: 0, image: surfaces.LastDepth(parity));
        layouts.WriteStorageImage(
            set: set,
            binding: 9,
            image: reflectionDistanceHistory ? images.ReflectionGuide : scratch.Part(part)
        );
        layouts.WriteStorageImage(
            set: set,
            binding: 10,
            image: reflectionDistanceHistory ? images.ReflectionGuideScratch : scratch.Part(part)
        );
        // The filter's input, which only the spatial stage of a signal that feeds the filter uses: another signal's stages do
        // not use the binding, and it binds the scratch image, as bindings 9 and 10 do.
        layouts.WriteStorageImage(
            set: set,
            binding: 13,
            image: images.FilterInputs is [var input] ? input : scratch.Part(part)
        );
        // The sample age, which only the specular signal's temporal dispatch reads: another signal binds its sample.
        if (scratch.HistoryMode.KeepsLightAge)
            layouts.WriteStorageImage(set: set, binding: 11, image: images.Ages is [var age, ..] ? age : images.Sample);
        // The diagnostic of uncovered surfaces, which only the specular signal's temporal dispatch writes.
        if (scratch.Instrumentation == HistoryInstrumentation.LightAgeDiagnostic)
            layouts.WriteStorageImage(
                set: set,
                binding: 12,
                image: images.Ages is [_, var diagnostic] ? diagnostic : images.Sample
            );
        // The accumulation trace, which only the specular signal's dispatches write: another signal binds its sample.
        if (scratch.Instrumentation == HistoryInstrumentation.ControlTrace)
            layouts.WriteStorageImage(
                set: set,
                binding: 12,
                image: images.Traces is [var trace] ? trace : images.Sample
            );
    }

    private GpuImage CreateSampleAge(Extent2D extent, int part) =>
        CreateImage(extent: extent, imageName: $"{Name} sample age {part}", extraUsage: 0, format: SampleAgeFormat);

    /// <summary>
    /// An image of the signal at <paramref name="extent"/>: a storage image, which the read hooks copy out, plus
    /// <paramref name="extraUsage"/>.
    /// </summary>
    private GpuImage CreateImage(Extent2D extent, string imageName, ImageUsageFlags extraUsage) =>
        CreateImage(extent: extent, imageName: imageName, extraUsage: extraUsage, format: SignalFormat);

    private GpuImage CreateImage(Extent2D extent, string imageName, ImageUsageFlags extraUsage, Format format) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: format,
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit | extraUsage,
            mipLevels: 1,
            name: imageName
        );

    private static void CheckDevice(VulkanDevice device)
    {
        const FormatFeatureFlags needed = FormatFeatureFlags.StorageImageBit | FormatFeatureFlags.ColorAttachmentBit;
        if ((device.FormatProperties(SignalFormat).OptimalTilingFeatures & needed) != needed)
            throw new NotSupportedException(
                $"{SignalFormat} cannot be a color attachment and a storage image on {device.DeviceName}."
            );
    }

    private void DestroyImages()
    {
        foreach (PartImages images in parts)
        foreach (GpuImage image in images.All)
            image.Dispose();
    }

    // The pool of the layouts frees the sets.
    public void Dispose()
    {
        DestroyImages();
        device.Vk.DestroyPipeline(device.Device, temporal, null);
        device.Vk.DestroyPipeline(device.Device, spatial, null);
    }

    /// <summary>The images of one part.</summary>
    private sealed record PartImages(
        GpuImage Sample,
        GpuImage Slow,
        GpuImage[] Fasts,
        GpuImage[] Guides,
        GpuImage[] Ages,
        GpuImage[] FilterInputs,
        GpuImage[] Traces
    )
    {
        /// <summary>The fast history that the frame of <paramref name="parity"/> writes.</summary>
        public GpuImage Fast(int parity) => Fasts.Length == 0 ? Slow : Fasts[parity];

        /// <summary>The fast history that the last drawn frame wrote.</summary>
        public GpuImage LastFast(int parity) => Fasts.Length == 0 ? Slow : Fasts[1 - parity];

        public GpuImage ReflectionGuide => Guides.Length == 0 ? Sample : Guides[0];

        public GpuImage ReflectionGuideScratch => Guides[1];

        public IEnumerable<GpuImage> All => [Sample, Slow, .. Fasts, .. Guides, .. Ages, .. FilterInputs, .. Traces];

        /// <summary>Whether every image has <paramref name="extent"/>.</summary>
        public bool Fit(Extent2D extent) =>
            SameExtent(Sample.Extent, extent)
            && SameExtent(Slow.Extent, extent)
            && (Fasts.Length == 0 || SameExtent(Fasts[0].Extent, extent) && SameExtent(Fasts[1].Extent, extent))
            && (Guides.Length == 0 || SameExtent(Guides[0].Extent, extent) && SameExtent(Guides[1].Extent, extent))
            && Ages.All(age => SameExtent(age.Extent, extent))
            && FilterInputs.All(input => SameExtent(input.Extent, extent))
            && Traces.All(trace => SameExtent(trace.Extent, extent));
    }
}

/// <summary>
/// The accumulation of a signal as a pass of the frame graph, named "accumulate" and the signal's name: after its
/// producer, whose samples it reads.
/// </summary>
internal sealed class AccumulatePass(TemporalSignal signal) : RenderPass
{
    // Made once, because the timer reads the name in every frame, which allocates nothing.
    private readonly string name = $"accumulate {signal.Name}";

    public override string Name => name;

    public override void Record(FrameContext context) => signal.Accumulate(context);
}
