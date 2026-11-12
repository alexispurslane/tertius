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
/// (<see cref="VulkanRenderer.CreateSignal(SignalParameters, string, int)"/>).
/// </para>
/// <para>
/// Each part owns a sample, a slow history and two fast histories, which alternate by the parity of the drawn frames
/// (<see cref="FrameHistory.PendingFrame.Parity"/>), RGBA16F at the targets' size except the experiment's RGBA32F slow history. The slow history holds the length
/// in alpha, the specular signal's sample and fast history the hit distance, 0 for a miss, and the diffuse signal's fast
/// history its retained deviation, which the clamp's box and the diffuse filter read (<see cref="HistoryConfidence"/>).
/// The sample is a color attachment of its producer, and every other image stays in the General layout. The signals share
/// the scratch images (<see cref="SignalScratch"/>). Beside a precise specular history, each part also owns its sample's
/// light age, which the resolve writes and the temporal dispatch reads at binding 11 (the light-change age, ticket
/// tiunr-7haj): the temporal dispatch of both signals cuts its history by it (ticket ter-wkvw for the diffuse signal). The
/// diffuse signal keeps its half-precision history and takes the light-age variant of its temporal dispatch
/// (<see cref="ShaderVariant.LightAge"/>).
/// </para>
/// <para>
/// Each part also owns its block sums (<see cref="BlockSums"/>), one texel per block of 8 × 8 pixels, which the temporal
/// dispatch writes and the spatial dispatch reads within one accumulation: the block test's evidence of a change
/// (docs/plans/bounce-light-confidence.md, section 8), where the signal's <see cref="HistoryConfidence"/> runs it. Beside a
/// precise history, a reflection part also owns its retained deviation, two R16F images by parity
/// (<see cref="DeviationImage"/>), which its block test of drops pools (section 6).
/// </para>
/// <para>Invariants, checked by <see cref="AssertInvariants"/>:</para>
/// <list type="bullet">
/// <item>Every image of every part has the size of the targets that the signal binds, and its block sums one texel per
/// block of those targets.</item>
/// <item>The signal sets hold one set per part and parity.</item>
/// <item>The parameter set has a cap and a fast cap of at least 1.</item>
/// </list>
/// </remarks>
internal sealed unsafe class TemporalSignal : IDisposable
{
    /// <summary>The sample, fast-history and guide format, also used by the original slow history.</summary>
    public const Format SignalFormat = Format.R16G16B16A16Sfloat;

    /// <summary>
    /// The format of a sample's light age beside a precise specular history: 0 for no evidence, else the age in frames plus
    /// 1.
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

    /// <summary>
    /// The format of the block sums: per block of 8 × 8 pixels the sums over its streams of the difference of the fast and
    /// the slow history's luminance, of its predicted variance, the count of streams, and the sum of the slow history's
    /// luminance (accumulate_blocks.glsl). Single floats, because a window adds up to 49 texels of up to 16 streams each.
    /// </summary>
    public const Format BlockSumsFormat = Format.R32G32B32A32Sfloat;

    /// <summary>
    /// The format of the reflections' retained deviation beside a precise history, in pre-exposed luminance: their fast
    /// history's alpha holds the hit distance.
    /// </summary>
    public const Format DeviationFormat = Format.R16Sfloat;

    /// <summary>
    /// The width and the height of a workgroup of both dispatches, the shaders' local size, and of a block of the block
    /// test.
    /// </summary>
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
        int parts
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parts, MostParts);
        if (!ConfidenceFitsLight(parameters))
            throw new ArgumentException(
                "The widened box keeps the deviation of the diffuse light, and the drop evidence that of the reflections.",
                nameof(parameters)
            );
        CheckDevice(device);

        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.targets = targets;
        this.surfaces = surfaces;
        this.scratch = scratch;

        Name = name;
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
        Parameters = parameters with
        {
            Confidence = ConfidenceOf(parameters.Confidence, singleDiffuseHistory, scratch.HistoryMode),
        };
        reflectionDistanceHistory =
            parameters.Light == SignalLight.Specular
            && Environment.GetEnvironmentVariable("TERTIUS_REFLECTION_SHARING") == "1";
        temporal = CreatePipeline(
            shaders,
            singleDiffuseHistory ? "diffuse_single_temporal" : "temporal",
            prototype.LimitIncoming
        );
        spatial = CreatePipeline(shaders, singleDiffuseHistory ? "diffuse_single_spatial" : "spatial");
        Create();
    }

    /// <summary>
    /// Whether the confidence rules of <paramref name="parameters"/> keep their deviation where its signal's shaders look
    /// for it: the diffuse light's in its fast history's alpha, the reflections' in an image of their own.
    /// </summary>
    private static bool ConfidenceFitsLight(SignalParameters parameters) =>
        parameters.Confidence switch
        {
            WidenedBox => parameters.Light == SignalLight.Diffuse,
            DropEvidence => parameters.Light == SignalLight.Specular,
            NarrowBox => true,
            null => throw new ArgumentException("A signal's parameters need confidence rules.", nameof(parameters)),
        };

    /// <summary>
    /// The rules that a signal of <paramref name="confidence"/> follows: the narrow box where its storage keeps no deviation
    /// for them. The single history keeps no fast history to widen a box by, and the original history no image of the
    /// reflections' deviation to pool.
    /// </summary>
    private static HistoryConfidence ConfidenceOf(
        HistoryConfidence confidence,
        bool singleHistory,
        SpecularHistoryMode mode
    ) => singleHistory || confidence is DropEvidence && !mode.KeepsRetainedDeviation ? new NarrowBox() : confidence;

    /// <summary>The pipeline of the dispatch of <paramref name="kind"/>, "temporal" or "spatial": accumulate_kind.comp.</summary>
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

    /// <summary>The signal's parts.</summary>
    public int Parts => parts.Length;

    /// <summary>The signal's parameter set.</summary>
    public SignalParameters Parameters { get; }

    /// <summary>
    /// Whether the signal keeps its retained deviation in an image pair of its own: the reflections beside a precise
    /// history, whose shaders declare it and whose fast history's alpha holds the hit distance.
    /// </summary>
    private bool KeepsDeviationImage => Parameters.Light == SignalLight.Specular && HistoryMode.KeepsRetainedDeviation;

    /// <summary>
    /// The slow history's storage: the specular history's mode, or original storage for other signals. Whether the signal's
    /// samples have a light age is <see cref="KeepsLightAge"/>.
    /// </summary>
    public SpecularHistoryMode HistoryMode { get; }

    /// <summary>The instrumentation of the specular history, or none for other signals.</summary>
    public HistoryInstrumentation Instrumentation { get; }

    /// <summary>
    /// Whether the signal's samples have a light age (<see cref="SampleAge(int)"/>), the evidence of a light change by which
    /// its history is cut: every signal beside a precise specular history, whose screen tracer records the light-change age
    /// of each pixel.
    /// </summary>
    public bool KeepsLightAge => scratch.HistoryMode.KeepsLightAge;

    /// <summary>
    /// The shader variant whose declared slow-history format matches this signal's storage (<see cref="HistoryShader(string,
    /// SpecularHistoryMode, HistoryInstrumentation)"/>), and which reads the light age where the signal keeps it
    /// (<see cref="LightAgeShader(string)"/>).
    /// </summary>
    public string HistoryShader(string shader) =>
        HistoryMode == SpecularHistoryMode.Original && KeepsLightAge
            ? LightAgeShader(shader)
            : HistoryShader(shader, HistoryMode, Instrumentation);

    /// <summary>
    /// The variant of <paramref name="shader"/> for a half-precision history whose samples have a light age: the
    /// light-age variant for the shaders that it holds, the temporal dispatch, and the shader itself otherwise.
    /// </summary>
    internal static string LightAgeShader(string shader) =>
        ShaderVariant.LightAge.Shaders.Contains(shader)
            ? ShaderVariant.Of(shader, ShaderVariant.LightAge.Suffix)
            : shader;

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
    /// The light age of the sample of <paramref name="part"/>, which the resolve writes beside a precise specular history
    /// (<see cref="SampleAgeFormat"/>, <see cref="KeepsLightAge"/>), or null without it.
    /// </summary>
    public GpuImage? SampleAge(int part) => parts[part].SampleAge;

    /// <summary>
    /// The block sums of <paramref name="part"/> after the last accumulation (<see cref="BlockSumsFormat"/>), one texel per
    /// block of 8 × 8 pixels, which only a signal with a block test writes. The tests and the survey's probe read it.
    /// </summary>
    public GpuImage BlockSums(int part) => parts[part].BlockSums;

    /// <summary>
    /// The reflections' retained deviation of <paramref name="part"/> that the frame of <paramref name="parity"/> writes
    /// beside a precise history (<see cref="DeviationFormat"/>), which <see cref="DropEvidence"/> pools, or null without it.
    /// </summary>
    public GpuImage? DeviationImage(int part, int parity) => parts[part].Deviation(parity);

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
            BeforeTemporal(barriers: barriers, part: part, parity: parity);
            Dispatch(context: context, pipeline: temporal, set: sets[part, parity]);
            BeforeSpatial(barriers: barriers, part: part, parity: parity);
            Dispatch(context: context, pipeline: spatial, set: sets[part, parity]);
        }
    }

    /// <summary>The barriers of <paramref name="part"/>'s images before its temporal dispatch.</summary>
    private void BeforeTemporal(BarrierBatch barriers, int part, int parity)
    {
        PartImages images = parts[part];
        if (!reflectionDistanceHistory && HistoryMode != SpecularHistoryMode.Original)
            barriers.Image(scratch.Part(part), ImageUse.StorageCompute);
        barriers.Image(images.Sample, ImageUse.StorageCompute);
        foreach (GpuImage age in images.Ages)
            barriers.Image(age, ImageUse.StorageCompute);
        foreach (GpuImage trace in images.Traces)
            barriers.Image(trace, ImageUse.StorageCompute);
        barriers.Image(images.Slow, ImageUse.StorageCompute);
        if (!singleDiffuseHistory)
        {
            barriers.Image(images.LastFast(parity), ImageUse.StorageCompute);
            // Discarded: the temporal dispatch writes every pixel of it.
            barriers.Image(images.Fast(parity), ImageUse.StorageCompute, discard: true);
        }
        if (reflectionDistanceHistory)
        {
            barriers.Image(images.ReflectionGuide, ImageUse.StorageCompute);
            barriers.Image(images.ReflectionGuideScratch, ImageUse.StorageCompute, discard: true);
        }
        // After the last signal's spatial dispatch, which read it.
        barriers.Image(scratch.Part(part, HistoryMode), ImageUse.StorageCompute, discard: true);
        // Discarded: with the block test, the temporal dispatch writes every texel, and without it no dispatch reads one.
        barriers.Image(images.BlockSums, ImageUse.StorageCompute, discard: true);
        if (images.Deviation(1 - parity) is { } lastDeviation)
            barriers.Image(lastDeviation, ImageUse.StorageCompute);
        // Discarded: the temporal dispatch writes every pixel of it.
        if (images.Deviation(parity) is { } deviation)
            barriers.Image(deviation, ImageUse.StorageCompute, discard: true);
    }

    /// <summary>
    /// The barriers of <paramref name="part"/>'s images before its spatial dispatch, which reads the neighbors' results of
    /// the temporal one and the block sums of its windows.
    /// </summary>
    private void BeforeSpatial(BarrierBatch barriers, int part, int parity)
    {
        PartImages images = parts[part];
        barriers.Image(scratch.Part(part, HistoryMode), ImageUse.StorageCompute);
        barriers.Image(images.BlockSums, ImageUse.StorageCompute);
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

    /// <summary>The blocks of 8 × 8 pixels that cover <paramref name="extent"/>: one per workgroup of each dispatch.</summary>
    private static Extent2D BlocksOf(Extent2D extent) =>
        new((extent.Width + GroupSize - 1) / GroupSize, (extent.Height + GroupSize - 1) / GroupSize);

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
        Extent2D groups = BlocksOf(context.Extent);
        context.Vk.CmdDispatch(
            commandBuffer: context.CommandBuffer,
            groupCountX: groups.Width,
            groupCountY: groups.Height,
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
                Ages: CreateAges(extent, part),
                Deviations: KeepsDeviationImage
                    ? [CreateDeviation(extent, part, 0), CreateDeviation(extent, part, 1)]
                    : [],
                BlockSums: CreateImage(
                    extent: BlocksOf(extent),
                    imageName: $"{Name} block sums {part}",
                    extraUsage: 0,
                    format: BlockSumsFormat
                ),
                Traces: Instrumentation == HistoryInstrumentation.ControlTrace ? [CreateTrace(extent, part)] : []
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
        // The sample age, which the temporal dispatch of both signals reads.
        if (images.SampleAge is { } age)
            layouts.WriteStorageImage(set: set, binding: 11, image: age);
        // The diagnostic of uncovered surfaces, which only the specular signal's temporal dispatch writes.
        if (scratch.Instrumentation == HistoryInstrumentation.LightAgeDiagnostic)
            layouts.WriteStorageImage(
                set: set,
                binding: 12,
                image: images.Ages is [_, var diagnostic] ? diagnostic : images.Sample
            );
        layouts.WriteStorageImage(set: set, binding: DescriptorLayouts.BlockSumsBinding, image: images.BlockSums);
        // The reflections' retained deviation, which only their precise temporal dispatch reads and writes: another signal
        // binds its sample.
        if (layouts.HistoryMode.KeepsRetainedDeviation)
        {
            layouts.WriteStorageImage(
                set: set,
                binding: DescriptorLayouts.LastDeviationBinding,
                image: images.Deviation(1 - parity) ?? images.Sample
            );
            layouts.WriteStorageImage(
                set: set,
                binding: DescriptorLayouts.DeviationBinding,
                image: images.Deviation(parity) ?? images.Sample
            );
        }
        // The accumulation trace, which only the specular signal's dispatches write: another signal binds its sample.
        if (scratch.Instrumentation == HistoryInstrumentation.ControlTrace)
            layouts.WriteStorageImage(
                set: set,
                binding: 12,
                image: images.Traces is [var trace] ? trace : images.Sample
            );
    }

    /// <summary>
    /// The light-age images of <paramref name="part"/>: none without the light age, else the sample age, and under the
    /// diagnostic the diagnostic of uncovered surfaces after it.
    /// </summary>
    private GpuImage[] CreateAges(Extent2D extent, int part) =>
        (KeepsLightAge, Instrumentation) switch
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
        };

    /// <summary>The reflections' retained deviation of <paramref name="part"/> that the frame of <paramref name="parity"/> writes.</summary>
    private GpuImage CreateDeviation(Extent2D extent, int part, int parity) =>
        CreateImage(
            extent: extent,
            imageName: $"{Name} retained deviation {part}.{parity}",
            extraUsage: 0,
            format: DeviationFormat
        );

    /// <summary>The sample age of <paramref name="part"/>, which its producer writes as it writes the sample.</summary>
    private GpuImage CreateSampleAge(Extent2D extent, int part) =>
        CreateImage(
            extent: extent,
            imageName: $"{Name} sample age {part}",
            extraUsage: ImageUsageFlags.TransferDstBit,
            format: SampleAgeFormat
        );

    /// <summary>The accumulation trace of <paramref name="part"/> (<see cref="TraceLayers"/>).</summary>
    private GpuImage CreateTrace(Extent2D extent, int part) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: TraceFormat,
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: $"{Name} accumulation trace {part}",
            arrayLayers: TraceLayers
        );

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
        GpuImage[] Deviations,
        GpuImage BlockSums,
        GpuImage[] Traces
    )
    {
        /// <summary>The fast history that the frame of <paramref name="parity"/> writes.</summary>
        public GpuImage Fast(int parity) => Fasts.Length == 0 ? Slow : Fasts[parity];

        /// <summary>The fast history that the last drawn frame wrote.</summary>
        public GpuImage LastFast(int parity) => Fasts.Length == 0 ? Slow : Fasts[1 - parity];

        public GpuImage ReflectionGuide => Guides.Length == 0 ? Sample : Guides[0];

        public GpuImage ReflectionGuideScratch => Guides[1];

        public GpuImage? SampleAge => Ages is [var age, ..] ? age : null;

        /// <summary>The reflections' retained deviation that the frame of <paramref name="parity"/> writes, if any.</summary>
        public GpuImage? Deviation(int parity) => Deviations.Length == 0 ? null : Deviations[parity];

        public IEnumerable<GpuImage> All =>
            [Sample, Slow, .. Fasts, .. Guides, .. Ages, .. Deviations, BlockSums, .. Traces];

        /// <summary>Whether every image has <paramref name="extent"/>.</summary>
        public bool Fit(Extent2D extent) =>
            SameExtent(Sample.Extent, extent)
            && SameExtent(Slow.Extent, extent)
            && AllFit(Fasts, extent)
            && AllFit(Guides, extent)
            && AllFit(Ages, extent)
            && AllFit(Deviations, extent)
            && AllFit(Traces, extent)
            && SameExtent(BlockSums.Extent, BlocksOf(extent));

        /// <summary>Whether every image of <paramref name="images"/> has <paramref name="extent"/>.</summary>
        private static bool AllFit(GpuImage[] images, Extent2D extent)
        {
            foreach (GpuImage image in images)
                if (!SameExtent(image.Extent, extent))
                    return false;
            return true;
        }
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
