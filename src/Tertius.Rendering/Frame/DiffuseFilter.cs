using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The spatial filter of the indirect diffuse light, after Schied et al. 2017, "Spatiotemporal Variance-Guided
/// Filtering". It spreads rare bright samples over their neighbors on the same surface, and keeps their light.
/// </summary>
/// <remarks>
/// <para>
/// The signal's accumulation writes the filter's input (<see cref="TemporalSignal.FilterInput"/>). The kernel's wavelet
/// dispatches each take its B-spline kernel at taps 2^i pixels apart, stopped by the pixels' planes, normals and luminance
/// (<see cref="DiffuseFilterKernel"/>). A dispatch moves light between two pixels by a share that is the same in both
/// directions, so it neither makes nor loses light (diffuse_filter_wavelet.glsl).
/// </para>
/// <para>
/// The light of dispatch <see cref="FeedbackDispatch"/> replaces the slow history's light, as the paper feeds back its
/// first step: the filter is the slow history's second writer, after the accumulation. The last dispatch writes
/// <see cref="Filtered"/>, which the composite shows and the next frame's rays read at their hits.
/// </para>
/// <para>
/// The filter owns two images that the wavelet dispatches alternate and <see cref="Filtered"/>, each RGBA16F at the
/// targets' size in the General layout: rgb the demodulated, pre-exposed light, a the deviation of its luminance.
/// </para>
/// <para>Invariants, checked by <see cref="AssertInvariants"/>:</para>
/// <list type="bullet">
/// <item>Every image has the size of the targets.</item>
/// <item>The wavelet dispatches' two alternating images and <see cref="Filtered"/> are three different images.</item>
/// </list>
/// </remarks>
internal sealed unsafe class DiffuseFilter : IDisposable
{
    /// <summary>The dispatch whose light the slow history takes: the paper's first.</summary>
    public const int FeedbackDispatch = 0;

    /// <summary>The filter sets of one filter: a set per wavelet dispatch of the kernel with the most.</summary>
    public const uint Sets = DiffuseFilterKernel.MostDispatches;

    /// <summary>The width and the height of a workgroup: the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly RenderTargets targets;
    private readonly TemporalSignal signal;
    private readonly Pipeline wavelet;

    /// <summary>The set of each wavelet dispatch.</summary>
    private readonly DescriptorSet[] sets;
    private GpuImage[] passes;

    /// <param name="signal">The diffuse signal, whose part 0 the filter filters and whose slow history it writes.</param>
    /// <param name="kernel">The wavelet kernel: <see cref="DiffuseFilterKernel.Thin"/> or <see cref="DiffuseFilterKernel.Wide"/>.</param>
    /// <exception cref="ArgumentException">The signal feeds no filter.</exception>
    public DiffuseFilter(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        ShaderLibrary shaders,
        RenderTargets targets,
        TemporalSignal signal,
        DiffuseFilterKernel kernel
    )
    {
        if (!signal.FeedsFilter)
            throw new ArgumentException($"The signal {signal.Name} feeds no diffuse filter.", nameof(signal));
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.targets = targets;
        this.signal = signal;
        Kernel = kernel;
        sets = new DescriptorSet[kernel.Dispatches];
        for (int index = 0; index < sets.Length; index++)
            sets[index] = layouts.AllocateFilterSet();
        wavelet = new ComputePipelineDesc
        {
            Name = "diffuse filter wavelet",
            Shader = kernel.WaveletShader,
            Layout = layouts.DiffuseFilterLayout,
        }.Create(device, shaders);
        (passes, Filtered) = CreateImages();
        WriteSets();
    }

    /// <summary>The filter's wavelet kernel.</summary>
    public DiffuseFilterKernel Kernel { get; }

    /// <summary>
    /// The filtered light after the last frame's filter: rgb the demodulated, pre-exposed light, a the deviation of its
    /// luminance. The composite reads it, and the next frame's rays read it at their hits.
    /// </summary>
    public GpuImage Filtered { get; private set; }

    /// <summary>
    /// The kernel that the debug switch TERTIUS_DIFFUSE_FILTER selects, or null for no filter: unset, empty and "on" select
    /// the thin kernel, "wide" the paper's kernel, and "off" no filter, so that the composite shows the slow history and the
    /// rays read the fast history, as before the filter.
    /// </summary>
    /// <exception cref="ArgumentException">The variable holds another value.</exception>
    public static DiffuseFilterKernel? Selected() =>
        Environment.GetEnvironmentVariable("TERTIUS_DIFFUSE_FILTER") switch
        {
            null or "" or "on" => DiffuseFilterKernel.Thin,
            "wide" => DiffuseFilterKernel.Wide,
            "off" => null,
            var value => throw new ArgumentException(
                $"Unknown TERTIUS_DIFFUSE_FILTER value '{value}': use on, wide or off."
            ),
        };

    /// <summary>Records the wavelet dispatches, after the signal's accumulation, whose filter input they read.</summary>
    public void Filter(FrameContext context)
    {
        Contract.Requires(context.Targets == targets && FitsTargets());
        BarrierBatch barriers = context.Barriers;

        // The surfaces that the edge stops compare, and the material that tells a lit surface.
        foreach (GpuImage surface in (GpuImage[])[targets.Distance, targets.Normal, targets.BaseColor])
            barriers.Image(surface, ImageUse.SampledCompute);

        for (int dispatch = 0; dispatch < sets.Length; dispatch++)
        {
            var push = new DiffuseFilterPushConstants
            {
                Step = 1 << dispatch,
                Feedback = dispatch == FeedbackDispatch ? 1u : 0u,
            };
            barriers.Image(InputOf(dispatch), ImageUse.StorageCompute);
            barriers.Image(OutputOf(dispatch), ImageUse.StorageCompute, discard: true);
            barriers.Image(signal.Slow(0), ImageUse.StorageCompute);
            Dispatch(context: context, set: sets[dispatch], push: push);
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
        Contract.Invariant(passes[0] != passes[1] && passes[0] != Filtered && passes[1] != Filtered);
    }

    /// <summary>The image that wavelet dispatch <paramref name="dispatch"/> reads: the signal's filter input first.</summary>
    private GpuImage InputOf(int dispatch) => dispatch == 0 ? signal.FilterInput(0) : passes[(dispatch - 1) % 2];

    /// <summary>The image that wavelet dispatch <paramref name="dispatch"/> writes: <see cref="Filtered"/> for the last.</summary>
    private GpuImage OutputOf(int dispatch) => dispatch == sets.Length - 1 ? Filtered : passes[dispatch % 2];

    private bool FitsTargets() =>
        SameExtent(Filtered.Extent) && SameExtent(passes[0].Extent) && SameExtent(passes[1].Extent);

    private bool SameExtent(Extent2D extent) =>
        extent.Width == targets.Extent.Width && extent.Height == targets.Extent.Height;

    private void Dispatch(FrameContext context, DescriptorSet set, DiffuseFilterPushConstants push)
    {
        context.BeginComputePass(pipeline: wavelet, layout: layouts.DiffuseFilterLayout, passSet: set);
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
        (
            [CreateImage("diffuse filter pass 0"), CreateImage("diffuse filter pass 1")],
            CreateImage("diffuse filter output")
        );

    /// <summary>Writes each dispatch's set: its input, its output and the slow history, which the feedback step writes.</summary>
    private void WriteSets()
    {
        for (int dispatch = 0; dispatch < sets.Length; dispatch++)
        {
            layouts.WriteStorageImage(set: sets[dispatch], binding: 0, image: InputOf(dispatch));
            layouts.WriteStorageImage(set: sets[dispatch], binding: 1, image: OutputOf(dispatch));
            layouts.WriteStorageImage(set: sets[dispatch], binding: 2, image: signal.Slow(0));
        }
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
        device.Vk.DestroyPipeline(device.Device, wavelet, null);
    }
}

/// <summary>
/// A wavelet kernel of the diffuse filter (diffuse_filter_wavelet.glsl). Each reaches 63 pixels from a pixel along each
/// axis, and its steps together cover every pixel inside that reach.
/// </summary>
internal sealed record DiffuseFilterKernel
{
    /// <summary>The most dispatches of a kernel: the thin kernel's.</summary>
    public const int MostDispatches = 6;

    /// <summary>
    /// The default: six dispatches of a 3 × 3 kernel, (1, 2, 1) / 4 along each axis, at taps 1 to 32 pixels apart. That is
    /// 48 taps per pixel (ticket ter-hxhl).
    /// </summary>
    public static readonly DiffuseFilterKernel Thin = new(MostDispatches, "diffuse_filter_wavelet.comp");

    /// <summary>
    /// The paper's five dispatches of a 5 × 5 kernel, (1, 4, 6, 4, 1) / 16 along each axis, at taps 1 to 16 pixels apart.
    /// That is 120 taps per pixel.
    /// </summary>
    public static readonly DiffuseFilterKernel Wide = new(5, "diffuse_filter_wavelet_wide.comp");

    private DiffuseFilterKernel(int dispatches, string waveletShader)
    {
        Dispatches = dispatches;
        WaveletShader = waveletShader;
    }

    /// <summary>The wavelet dispatches, whose taps lie 2^i pixels apart in dispatch i.</summary>
    public int Dispatches { get; }

    /// <summary>The wavelet dispatch's shader.</summary>
    public string WaveletShader { get; }
}

/// <summary>The diffuse filter as a pass of the frame graph: after the diffuse signal's accumulation.</summary>
internal sealed class DiffuseFilterPass(DiffuseFilter filter) : RenderPass
{
    public override string Name => "filter indirect diffuse";

    public override void Record(FrameContext context) => filter.Filter(context);
}
