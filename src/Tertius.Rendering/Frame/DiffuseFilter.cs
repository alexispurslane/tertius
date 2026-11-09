using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The spatial filter of the indirect diffuse light (ticket ter-9371): a variance-guided à-trous wavelet filter after
/// Schied et al. 2017, "Spatiotemporal Variance-Guided Filtering", which runs after the diffuse signal's accumulation, at
/// the render size. It spreads the rare bright samples of a small, bright light source, such as a sunlit strip, over
/// their neighbors on the same surface, and keeps their light.
/// </summary>
/// <remarks>
/// <para>
/// The variance dispatch pairs the signal's slow history with the standard deviation of its samples' luminance. Then
/// <see cref="Dispatches"/> wavelet dispatches each take a 5 × 5 B-spline kernel at taps 2^i pixels apart, stopped by the
/// pixels' planes, normals and luminance, and filter the deviation too. Unlike the paper, which divides each pixel's
/// weighted sum by its own sum of weights, a wavelet dispatch moves light between two pixels by a share that is the same
/// in both directions, so it neither makes nor loses light (diffuse_filter_wavelet.comp). The light of dispatch
/// <see cref="FeedbackDispatch"/> replaces the slow history's light, so that the next frame's history holds the filtered
/// light, as the paper feeds back its first step. The last dispatch writes <see cref="Filtered"/>, which the composite
/// shows and the next frame's rays read at their hits.
/// </para>
/// <para>
/// The filter owns two images that the wavelet dispatches alternate and <see cref="Filtered"/>, each RGBA16F at the
/// targets' size in the General layout: rgb the demodulated, pre-exposed light, a the deviation of its luminance.
/// </para>
/// <para>Invariants, checked by <see cref="AssertInvariants"/>:</para>
/// <list type="bullet">
/// <item>Every image has the size of the targets.</item>
/// <item>The filter holds, per parity, one variance set and one wavelet set per wavelet dispatch.</item>
/// </list>
/// </remarks>
internal sealed unsafe class DiffuseFilter : IDisposable
{
    /// <summary>The wavelet dispatches: the paper's five, whose last takes taps 16 pixels apart.</summary>
    public const int Dispatches = 5;

    /// <summary>The dispatch whose light the slow history takes: the paper's first.</summary>
    public const int FeedbackDispatch = 0;

    /// <summary>The filter sets of one filter: per parity, a variance set and a wavelet set per dispatch.</summary>
    public const uint Sets = 2 * (1 + Dispatches);

    /// <summary>The width and the height of a workgroup: the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly RenderTargets targets;
    private readonly TemporalSignal signal;
    private readonly Pipeline variance;
    private readonly Pipeline wavelet;
    /// <summary>The filter sets, [parity, dispatch]: dispatch 0 the variance dispatch, 1 + i wavelet dispatch i.</summary>
    private readonly DescriptorSet[,] sets = new DescriptorSet[2, 1 + Dispatches];
    private GpuImage[] passes;

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
        for (int parity = 0; parity < sets.GetLength(0); parity++)
        for (int dispatch = 0; dispatch < sets.GetLength(1); dispatch++)
            sets[parity, dispatch] = layouts.AllocateFilterSet();
        variance = CreatePipeline(shaders, "variance");
        wavelet = CreatePipeline(shaders, "wavelet");
        (passes, Filtered) = CreateImages();
        WriteSets();
    }

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
            var value => throw new ArgumentException(
                $"Unknown TERTIUS_DIFFUSE_FILTER value '{value}': use on or off."
            ),
        };

    /// <summary>
    /// The filtered light after the last frame's filter: rgb the demodulated, pre-exposed light, a the deviation of its
    /// luminance. The composite reads it, and the next frame's rays read it at their hits.
    /// </summary>
    public GpuImage Filtered { get; private set; }

    /// <summary>
    /// Records the variance dispatch and the wavelet dispatches, after the signal's accumulation, whose slow history and
    /// fast history they read.
    /// </summary>
    public void Filter(FrameContext context)
    {
        Contract.Requires(context.Targets == targets && FitsTargets());
        int parity = context.History.Parity;
        BarrierBatch barriers = context.Barriers;
        barriers.Image(targets.Distance, ImageUse.SampledCompute);
        barriers.Image(targets.Normal, ImageUse.SampledCompute);
        barriers.Image(targets.BaseColor, ImageUse.SampledCompute);
        barriers.Image(targets.Shading, ImageUse.SampledCompute);
        barriers.Image(signal.Slow(0), ImageUse.StorageCompute);
        barriers.Image(signal.Fast(0, parity), ImageUse.StorageCompute);
        barriers.Image(passes[0], ImageUse.StorageCompute, discard: true);
        Dispatch(context: context, pipeline: variance, set: sets[parity, 0], push: default);
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
            Dispatch(context: context, pipeline: wavelet, set: sets[parity, 1 + dispatch], push: push);
        }
    }

    /// <summary>
    /// Recreates the images at the targets' size and rewrites every set. The device must be idle, and the signal resized
    /// first, because the sets point at its images.
    /// </summary>
    public void Resize()
    {
        DestroyImages();
        (passes, Filtered) = CreateImages();
        WriteSets();
        Contract.Ensures(FitsTargets());
    }

    /// <summary>Checks the invariants in the class remarks.</summary>
    internal void AssertInvariants()
    {
        Contract.Invariant(FitsTargets());
        Contract.Invariant(sets.GetLength(0) == 2 && sets.GetLength(1) == 1 + Dispatches);
    }

    /// <summary>The image that wavelet dispatch <paramref name="dispatch"/> reads: the variance dispatch's output first.</summary>
    private GpuImage InputOf(int dispatch) => passes[dispatch % 2];

    /// <summary>The image that wavelet dispatch <paramref name="dispatch"/> writes: <see cref="Filtered"/> for the last.</summary>
    private GpuImage OutputOf(int dispatch) => dispatch == Dispatches - 1 ? Filtered : passes[(dispatch + 1) % 2];

    private bool FitsTargets() =>
        SameExtent(Filtered.Extent)
        && SameExtent(passes[0].Extent)
        && SameExtent(passes[1].Extent);

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

    private (GpuImage[] Passes, GpuImage Filtered) CreateImages() =>
        ([CreateImage("diffuse filter pass 0"), CreateImage("diffuse filter pass 1")], CreateImage("diffuse filter output"));

    private void WriteSets()
    {
        for (int parity = 0; parity < sets.GetLength(0); parity++)
        {
            WriteSet(set: sets[parity, 0], input: passes[1], output: passes[0], parity: parity);
            for (int dispatch = 0; dispatch < Dispatches; dispatch++)
                WriteSet(
                    set: sets[parity, 1 + dispatch],
                    input: InputOf(dispatch),
                    output: OutputOf(dispatch),
                    parity: parity
                );
        }
    }

    /// <summary>
    /// Writes a filter set: the dispatch's input and output, the slow history, and the fast history that a frame of
    /// <paramref name="parity"/> writes, whose deviation the variance dispatch reads.
    /// </summary>
    private void WriteSet(DescriptorSet set, GpuImage input, GpuImage output, int parity)
    {
        layouts.WriteStorageImage(set: set, binding: 0, image: input);
        layouts.WriteStorageImage(set: set, binding: 1, image: output);
        layouts.WriteStorageImage(set: set, binding: 2, image: signal.Slow(0));
        layouts.WriteStorageImage(set: set, binding: 3, image: signal.Fast(0, parity));
    }

    private GpuImage CreateImage(string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: targets.Extent,
            format: TemporalSignal.SignalFormat,
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: name
        );

    private void DestroyImages()
    {
        foreach (GpuImage image in passes)
            image.Dispose();
        Filtered.Dispose();
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
