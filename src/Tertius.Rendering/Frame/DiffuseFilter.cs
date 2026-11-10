using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The spatial filter of the indirect diffuse light, after Schied et al. 2017, "Spatiotemporal Variance-Guided
/// Filtering". It spreads rare bright samples over their neighbors on the same surface, and keeps their light.
/// </summary>
/// <remarks>
/// <para>
/// The variance dispatch pairs the signal's slow history with the standard deviation of its samples' luminance. Then
/// <see cref="Dispatches"/> wavelet dispatches each take a 5 × 5 B-spline kernel at taps 2^i pixels apart, stopped by the
/// pixels' planes, normals and luminance. Unlike the paper's normalized mean, a dispatch moves light between two pixels
/// by a share that is the same in both directions, so it neither makes nor loses light (diffuse_filter_wavelet.comp).
/// </para>
/// <para>
/// The light of dispatch <see cref="FeedbackDispatch"/> replaces the slow history's light, as the paper feeds back its
/// first step: the filter is the slow history's second writer, after the accumulation. The last dispatch writes
/// <see cref="Filtered"/>, which the composite shows and the next frame's rays read at their hits.
/// </para>
/// <para>
/// The filter owns two images that the wavelet dispatches alternate and <see cref="Filtered"/>, each RGBA16F at the
/// targets' size in the General layout: rgb the demodulated, pre-exposed light, a the deviation of its luminance. It
/// also owns the guide, RGBA32F at the same size: each pixel's point and view depth, which the variance dispatch writes
/// once so that no tap of a wavelet dispatch rebuilds them from the G-buffer (diffuse_filter.glsl, guideAt).
/// </para>
/// <para>Invariants, checked by <see cref="AssertInvariants"/>:</para>
/// <list type="bullet">
/// <item>Every image, the guide too, has the size of the targets.</item>
/// <item>The wavelet dispatches' two alternating images and <see cref="Filtered"/> are three different images.</item>
/// </list>
/// </remarks>
internal sealed unsafe class DiffuseFilter : IDisposable
{
    /// <summary>The wavelet dispatches: the paper's five, whose last takes taps 16 pixels apart.</summary>
    public const int Dispatches = 5;

    /// <summary>The dispatch whose light the slow history takes: the paper's first.</summary>
    public const int FeedbackDispatch = 0;

    /// <summary>
    /// The filter sets of one filter: a variance set per parity, whose fast history differs, and a wavelet set per
    /// dispatch, which reads no fast history.
    /// </summary>
    public const uint Sets = 2 + Dispatches;

    /// <summary>The width and the height of a workgroup: the shaders' local size.</summary>
    private const uint GroupSize = 8;

    /// <summary>The guide's format: a point and a depth in single floats, the same bits as the taps rebuilt before.</summary>
    private const Format GuideFormat = Format.R32G32B32A32Sfloat;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly RenderTargets targets;
    private readonly TemporalSignal signal;
    private readonly Pipeline variance;
    private readonly Pipeline wavelet;

    /// <summary>The variance dispatch's set of each parity.</summary>
    private readonly DescriptorSet[] varianceSets = new DescriptorSet[2];

    /// <summary>The set of each wavelet dispatch.</summary>
    private readonly DescriptorSet[] waveletSets = new DescriptorSet[Dispatches];
    private GpuImage[] passes;
    private GpuImage guide;

    /// <param name="signal">The diffuse signal, whose part 0 the filter reads and whose slow history it writes.</param>
    public DiffuseFilter(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        ShaderLibrary shaders,
        RenderTargets targets,
        TemporalSignal signal
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.targets = targets;
        this.signal = signal;
        foreach (DescriptorSet[] sets in (DescriptorSet[][])[varianceSets, waveletSets])
            for (int index = 0; index < sets.Length; index++)
                sets[index] = layouts.AllocateFilterSet();
        variance = CreatePipeline(shaders, "variance");
        wavelet = CreatePipeline(shaders, "wavelet");
        (passes, Filtered, guide) = CreateImages();
        WriteSets();
    }

    /// <summary>
    /// The filtered light after the last frame's filter: rgb the demodulated, pre-exposed light, a the deviation of its
    /// luminance. The composite reads it, and the next frame's rays read it at their hits.
    /// </summary>
    public GpuImage Filtered { get; private set; }

    /// <summary>
    /// Whether the debug switch TERTIUS_DIFFUSE_FILTER turns the filter on: unset, empty and "on" turn it on, "off" turns it
    /// off, so that the composite shows the slow history and the rays read the fast history, as before the filter.
    /// </summary>
    /// <exception cref="ArgumentException">The variable holds another value.</exception>
    public static bool SwitchedOn() =>
        Environment.GetEnvironmentVariable("TERTIUS_DIFFUSE_FILTER") switch
        {
            null or "" or "on" => true,
            "off" => false,
            var value => throw new ArgumentException($"Unknown TERTIUS_DIFFUSE_FILTER value '{value}': use on or off."),
        };

    /// <summary>
    /// Records the variance dispatch and the wavelet dispatches, after the signal's accumulation, whose slow history and
    /// fast history they read.
    /// </summary>
    public void Filter(FrameContext context)
    {
        Contract.Requires(context.Targets == targets && FitsTargets());
        int parity = context.History.Parity;
        BarrierBatch barriers = context.Barriers;

        // The surfaces that the edge stops compare, and the material that tells a lit surface.
        foreach (GpuImage surface in (GpuImage[])[targets.Distance, targets.Normal, targets.BaseColor, targets.Shading])
            barriers.Image(surface, ImageUse.SampledCompute);

        barriers.Image(signal.Slow(0), ImageUse.StorageCompute);
        barriers.Image(signal.Fast(0, parity), ImageUse.StorageCompute);
        barriers.Image(passes[0], ImageUse.StorageCompute, discard: true);
        barriers.Image(guide, ImageUse.StorageCompute, discard: true);
        Dispatch(context: context, pipeline: variance, set: varianceSets[parity], push: default);
        // Every wavelet dispatch only reads the guide, so one barrier after its write serves them all.
        barriers.Image(guide, ImageUse.StorageCompute);

        for (int dispatch = 0; dispatch < Dispatches; dispatch++)
        {
            var push = new DiffuseFilterPushConstants
            {
                Step = 1 << dispatch,
                Feedback = dispatch == FeedbackDispatch ? 1u : 0u,
            };
            barriers.Image(InputOf(dispatch), ImageUse.StorageCompute);
            barriers.Image(OutputOf(dispatch), ImageUse.StorageCompute, discard: true);
            barriers.Image(signal.Slow(0), ImageUse.StorageCompute);
            Dispatch(context: context, pipeline: wavelet, set: waveletSets[dispatch], push: push);
        }
    }

    /// <summary>
    /// Recreates the images at the targets' size and rewrites every set. The device must be idle, and the signal resized
    /// first, because the sets point at its images.
    /// </summary>
    public void Resize()
    {
        DestroyImages();
        (passes, Filtered, guide) = CreateImages();
        WriteSets();
        Contract.Ensures(FitsTargets());
    }

    /// <summary>Checks the invariants in the class remarks.</summary>
    internal void AssertInvariants()
    {
        Contract.Invariant(FitsTargets());
        Contract.Invariant(passes[0] != passes[1] && passes[0] != Filtered && passes[1] != Filtered);
    }

    /// <summary>The image that wavelet dispatch <paramref name="dispatch"/> reads: the variance dispatch's output first.</summary>
    private GpuImage InputOf(int dispatch) => passes[dispatch % 2];

    /// <summary>The image that wavelet dispatch <paramref name="dispatch"/> writes: <see cref="Filtered"/> for the last.</summary>
    private GpuImage OutputOf(int dispatch) => dispatch == Dispatches - 1 ? Filtered : passes[(dispatch + 1) % 2];

    private bool FitsTargets() =>
        SameExtent(Filtered.Extent)
        && SameExtent(passes[0].Extent)
        && SameExtent(passes[1].Extent)
        && SameExtent(guide.Extent);

    private bool SameExtent(Extent2D extent) =>
        extent.Width == targets.Extent.Width && extent.Height == targets.Extent.Height;

    private Pipeline CreatePipeline(ShaderLibrary shaders, string kind) =>
        new ComputePipelineDesc
        {
            Name = $"diffuse filter {kind}",
            Shader = $"diffuse_filter_{kind}.comp",
            Layout = layouts.DiffuseFilterLayout,
        }.Create(device, shaders);

    private void Dispatch(FrameContext context, Pipeline pipeline, DescriptorSet set, DiffuseFilterPushConstants push)
    {
        context.BeginComputePass(pipeline: pipeline, layout: layouts.DiffuseFilterLayout, passSet: set);
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layouts.DiffuseFilterLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(DiffuseFilterPushConstants),
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

    private (GpuImage[] Passes, GpuImage Filtered, GpuImage Guide) CreateImages() =>
        (
            [CreateImage("diffuse filter pass 0"), CreateImage("diffuse filter pass 1")],
            CreateImage("diffuse filter output"),
            CreateImage("diffuse filter guide", GuideFormat)
        );

    private void WriteSets()
    {
        // The variance dispatch reads no input: binding 0 holds an image of the right format for the layout.
        for (int parity = 0; parity < varianceSets.Length; parity++)
            WriteSet(set: varianceSets[parity], input: passes[1], output: passes[0], parity: parity);

        // The wavelet dispatches read no fast history: binding 3 holds parity 0's for the layout.
        for (int dispatch = 0; dispatch < Dispatches; dispatch++)
            WriteSet(set: waveletSets[dispatch], input: InputOf(dispatch), output: OutputOf(dispatch), parity: 0);
    }

    /// <summary>
    /// Writes a filter set: the dispatch's input and output, the slow history, the fast history that a frame of
    /// <paramref name="parity"/> writes, whose deviation the variance dispatch reads, and the guide. Every binding holds an
    /// image, because the set layout declares five.
    /// </summary>
    private void WriteSet(DescriptorSet set, GpuImage input, GpuImage output, int parity)
    {
        layouts.WriteStorageImage(set: set, binding: 0, image: input);
        layouts.WriteStorageImage(set: set, binding: 1, image: output);
        layouts.WriteStorageImage(set: set, binding: 2, image: signal.Slow(0));
        layouts.WriteStorageImage(set: set, binding: 3, image: signal.Fast(0, parity));
        layouts.WriteStorageImage(set: set, binding: 4, image: guide);
    }

    private GpuImage CreateImage(string name, Format format = TemporalSignal.SignalFormat) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: targets.Extent,
            format: format,
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: name
        );

    private void DestroyImages()
    {
        foreach (GpuImage image in passes)
            image.Dispose();
        Filtered.Dispose();
        guide.Dispose();
    }

    // The pool of the layouts frees the sets.
    public void Dispose()
    {
        DestroyImages();
        device.Vk.DestroyPipeline(device.Device, variance, null);
        device.Vk.DestroyPipeline(device.Device, wavelet, null);
    }
}

/// <summary>The diffuse filter as a pass of the frame graph: after the diffuse signal's accumulation.</summary>
internal sealed class DiffuseFilterPass(DiffuseFilter filter) : RenderPass
{
    public override string Name => "filter indirect diffuse";

    public override void Record(FrameContext context) => filter.Filter(context);
}
