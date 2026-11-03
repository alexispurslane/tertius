using System.Diagnostics.CodeAnalysis;
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
/// Each part owns a sample, a slow history, two fast histories, which alternate by the parity of the drawn frames
/// (<see cref="FrameHistory.PendingFrame.Parity"/>), and a scratch image, all RGBA16F at the targets' size. The slow
/// history holds the length in alpha. The sample is a color attachment of its producer, and every other image stays in
/// the General layout. The screen tracer's signals will share the scratch images (section 8, "Images"). With one signal,
/// each part keeps its own.
/// </para>
/// <para>Invariants, checked by <see cref="AssertInvariants"/>:</para>
/// <list type="bullet">
/// <item>Every image of every part has the targets' size.</item>
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
    private readonly string name;
    private readonly Pipeline temporal;
    private readonly Pipeline spatial;
    private readonly PartImages[] parts;

    /// <summary>The signal sets, [part, parity].</summary>
    private readonly DescriptorSet[,] sets;

    /// <param name="name">The signal's name in the images' names and the passes' names, such as "direct".</param>
    /// <param name="parts">The signal's parts: 2 for the direct signal, the diffuse and the specular.</param>
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
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.name = name;
        Parameters = parameters;
        CheckDevice(device);
        this.parts = new PartImages[parts];
        sets = new DescriptorSet[parts, 2];
        for (int part = 0; part < parts; part++)
            for (int parity = 0; parity < 2; parity++)
                sets[part, parity] = layouts.AllocateSignalSet();
        ReadSet = layouts.AllocateSignalReadSet();
        temporal = new ComputePipelineDesc
        {
            Name = $"accumulate {name} temporal",
            Shader = "accumulate_temporal.comp",
            Layout = layouts.AccumulateLayout,
        }.Create(device, shaders);
        spatial = new ComputePipelineDesc
        {
            Name = $"accumulate {name} spatial",
            Shader = "accumulate_spatial.comp",
            Layout = layouts.AccumulateLayout,
        }.Create(device, shaders);
        Create(targets, surfaces);
    }

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
    public void Accumulate(FrameContext context, SurfaceHistory surfaces)
    {
        Contract.Requires(context.Extent.Width == parts[0].Slow.Extent.Width);
        int parity = context.History.Parity;
        BarrierBatch barriers = context.Barriers;
        RenderTargets targets = context.Targets;
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
    /// Recreates the images at the targets' size and rewrites every set. The device must be idle, and the surface history
    /// resized first, because the sets point at its test image. A new size resets every history
    /// (<see cref="FrameHistory.Begin"/>), so the images' contents need not survive.
    /// </summary>
    public void Resize(RenderTargets targets, SurfaceHistory surfaces)
    {
        DestroyImages();
        Create(targets, surfaces);
    }

    /// <summary>Checks the invariants in the class remarks.</summary>
    internal void AssertInvariants()
    {
        Extent2D extent = parts[0].Slow.Extent;
        foreach (PartImages images in parts)
            foreach (GpuImage image in images.All)
                Contract.Invariant(image.Extent.Width == extent.Width && image.Extent.Height == extent.Height);
        Contract.Invariant(sets.GetLength(0) == parts.Length && sets.GetLength(1) == 2);
        Contract.Invariant(Parameters.Cap >= 1 && Parameters.FastCap >= 1);
    }

    private void Dispatch(FrameContext context, Pipeline pipeline, DescriptorSet set)
    {
        context.BeginComputePass(pipeline: pipeline, layout: layouts.AccumulateLayout, passSet: set);
        AccumulatePushConstants push = AccumulatePushConstants.Of(Parameters);
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

    private void Create(RenderTargets targets, SurfaceHistory surfaces)
    {
        Extent2D extent = targets.Extent;
        for (int part = 0; part < parts.Length; part++)
        {
            parts[part] = new PartImages(
                Sample: CreateImage(extent, $"{name} sample {part}", ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit),
                Slow: CreateImage(extent, $"{name} slow history {part}", 0),
                Fasts: [CreateImage(extent, $"{name} fast history {part}.0", 0), CreateImage(extent, $"{name} fast history {part}.1", 0)],
                Scratch: CreateImage(extent, $"{name} scratch {part}", 0)
            );
            for (int parity = 0; parity < 2; parity++)
                WriteSignalSet(set: sets[part, parity], images: parts[part], parity: parity, targets: targets, surfaces: surfaces);
            layouts.WriteStorageImage(set: ReadSet, binding: (uint)part, image: parts[part].Slow);
        }
    }

    private void WriteSignalSet(DescriptorSet set, PartImages images, int parity, RenderTargets targets, SurfaceHistory surfaces)
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
    }
}

/// <summary>
/// The accumulation of a signal as a pass of the frame graph, named "accumulate" and the signal's name: after its
/// producer, whose samples it reads.
/// </summary>
internal sealed class AccumulatePass(TemporalSignal signal, SurfaceHistory surfaces, string signalName) : RenderPass
{
    // Made once, because the timer reads the name in every frame, which allocates nothing.
    private readonly string name = $"accumulate {signalName}";

    public override string Name => name;

    public override void Record(FrameContext context) => signal.Accumulate(context, surfaces);
}
