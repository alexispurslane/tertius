using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The history of one signal: demodulated, pre-exposed light accumulated over frames at each pixel of the render size
/// (docs/plans/temporal-renderer.md, section 8, "The accumulator"). Its producer writes the sample of each part in each
/// frame. <see cref="Accumulate"/> then reprojects the last frame's history through the surface test, rescales it by the
/// exposure ratio, blends the sample in, fixes short histories from their neighbors and clamps the slow history into the
/// fast history's box. A pixel whose sample is not finite keeps its history. In a reset frame, the surface test fails
/// every tap, so every pixel's history starts from its sample.
/// </summary>
/// <remarks>
/// <para>
/// No signal of the renderer accumulates today: the direct light keeps one frame (docs/plans/temporal-renderer.md, decision
/// 29), so the composite reads the direct pass's images. The screen tracer's signals (ter-gayt) are the next users, and
/// until then only the accumulator's tests make a signal (<see cref="VulkanRenderer.CreateSignal"/>).
/// </para>
/// <para>
/// Each part owns a sample, a slow history, two fast histories, which alternate by the parity of the drawn frames
/// (<see cref="FrameHistory.PendingFrame.Parity"/>), and a scratch image, all RGBA16F at the targets' size. The slow
/// history holds the length in alpha. The sample is a color attachment of its producer, and every other image stays in
/// the General layout. The screen tracer's signals will share the scratch images (section 8, "Images"). With one signal,
/// each part keeps its own.
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
    /// <summary>The format of every image of a signal.</summary>
    public const Format SignalFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The width and the height of a workgroup of both dispatches: the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly RenderTargets targets;
    private readonly SurfaceHistory surfaces;
    private readonly Pipeline temporal;
    private readonly Pipeline spatial;
    private readonly PartImages[] parts;

    /// <summary>The signal sets, [part, parity].</summary>
    private readonly DescriptorSet[,] sets;

    /// <param name="targets">The render targets, whose size the images take, and whose motion target the sets bind.</param>
    /// <param name="surfaces">The surface history, whose test image the sets bind.</param>
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
        SignalParameters parameters,
        string name,
        int parts
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parts, DescriptorLayouts.SignalReadParts);
        CheckDevice(device);
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.targets = targets;
        this.surfaces = surfaces;
        Name = name;
        Parameters = parameters;

        this.parts = new PartImages[parts];
        sets = new DescriptorSet[parts, 2];
        for (int part = 0; part < parts; part++)
        for (int parity = 0; parity < 2; parity++)
            sets[part, parity] = layouts.AllocateSignalSet();
        ReadSet = layouts.AllocateSignalReadSet();

        temporal = CreatePipeline(shaders, "temporal");
        spatial = CreatePipeline(shaders, "spatial");
        Create();
    }

    /// <summary>The pipeline of the dispatch of <paramref name="kind"/>, "temporal" or "spatial": accumulate_kind.comp.</summary>
    private Pipeline CreatePipeline(ShaderLibrary shaders, string kind) =>
        new ComputePipelineDesc
        {
            Name = $"accumulate {Name} {kind}",
            Shader = $"accumulate_{kind}.comp",
            Layout = layouts.AccumulateLayout,
        }.Create(device, shaders);

    /// <summary>The signal's name in the images' names and the passes' names, such as "indirect diffuse".</summary>
    public string Name { get; }

    /// <summary>The signal's parts.</summary>
    public int Parts => parts.Length;

    /// <summary>The signal's parameter set.</summary>
    public SignalParameters Parameters { get; }

    /// <summary>
    /// Set 3 of <see cref="DescriptorLayouts.CompositeLayout"/>: the slow history of each part as a storage image for the
    /// fragment stage, which the composite reads.
    /// </summary>
    public DescriptorSet ReadSet { get; }

    /// <summary>The sample of <paramref name="part"/>, which the producer writes in each frame: rgb the light, a 1.</summary>
    public GpuImage Sample(int part) => parts[part].Sample;

    /// <summary>The slow history of <paramref name="part"/> after the last accumulation: rgb the light, a the length.</summary>
    public GpuImage Slow(int part) => parts[part].Slow;

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
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        for (int part = 0; part < parts.Length; part++)
        {
            PartImages images = parts[part];
            barriers.Image(images.Sample, ImageUse.StorageCompute);
            barriers.Image(images.Slow, ImageUse.StorageCompute);
            barriers.Image(images.LastFast(parity), ImageUse.StorageCompute);
            // Discarded: the temporal dispatch writes every pixel of each.
            barriers.Image(images.Fast(parity), ImageUse.StorageCompute, discard: true);
            barriers.Image(images.Scratch, ImageUse.StorageCompute, discard: true);
            Dispatch(context: context, pipeline: temporal, set: sets[part, parity]);

            // The spatial dispatch reads the neighbors' results of the temporal one.
            barriers.Image(images.Scratch, ImageUse.StorageCompute);
            barriers.Image(images.Fast(parity), ImageUse.StorageCompute);
            barriers.Image(images.Slow, ImageUse.StorageCompute);
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
        var push = AccumulatePushConstants.Of(Parameters);
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
                Slow: CreateImage(extent, $"{Name} slow history {part}", 0),
                Fasts:
                [
                    CreateImage(extent, $"{Name} fast history {part}.0", 0),
                    CreateImage(extent, $"{Name} fast history {part}.1", 0),
                ],
                Scratch: CreateImage(extent, $"{Name} scratch {part}", 0)
            );
            for (int parity = 0; parity < 2; parity++)
                WriteSignalSet(set: sets[part, parity], images: parts[part], parity: parity);
            layouts.WriteStorageImage(set: ReadSet, binding: (uint)part, image: parts[part].Slow);
        }
    }

    private void WriteSignalSet(DescriptorSet set, PartImages images, int parity)
    {
        layouts.WriteStorageImage(set: set, binding: 0, image: images.Sample);
        layouts.WriteStorageImage(set: set, binding: 1, image: images.Slow);
        layouts.WriteStorageImage(set: set, binding: 2, image: images.LastFast(parity));
        layouts.WriteStorageImage(set: set, binding: 3, image: images.Fast(parity));
        layouts.WriteStorageImage(set: set, binding: 4, image: images.Scratch);
        layouts.WriteStorageImage(set: set, binding: 5, image: surfaces.Test);
        layouts.WriteStorageImage(set: set, binding: 6, image: targets.Motion);
    }

    /// <summary>
    /// An image of the signal at <paramref name="extent"/>: a storage image, which the read hooks copy out, plus
    /// <paramref name="extraUsage"/>.
    /// </summary>
    private GpuImage CreateImage(Extent2D extent, string imageName, ImageUsageFlags extraUsage) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: SignalFormat,
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
    private sealed record PartImages(GpuImage Sample, GpuImage Slow, GpuImage[] Fasts, GpuImage Scratch)
    {
        /// <summary>The fast history that the frame of <paramref name="parity"/> writes.</summary>
        public GpuImage Fast(int parity) => Fasts[parity];

        /// <summary>The fast history that the last drawn frame wrote.</summary>
        public GpuImage LastFast(int parity) => Fasts[1 - parity];

        public IEnumerable<GpuImage> All => [Sample, Slow, Fasts[0], Fasts[1], Scratch];

        /// <summary>Whether every image has <paramref name="extent"/>.</summary>
        public bool Fit(Extent2D extent) =>
            SameExtent(Sample.Extent, extent)
            && SameExtent(Slow.Extent, extent)
            && SameExtent(Fasts[0].Extent, extent)
            && SameExtent(Fasts[1].Extent, extent)
            && SameExtent(Scratch.Extent, extent);
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
