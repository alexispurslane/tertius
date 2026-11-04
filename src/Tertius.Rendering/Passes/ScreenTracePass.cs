using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>The four passes of the screen tracer, in their order in a frame.</summary>
internal enum ScreenTraceStep
{
    /// <summary>The ray plan: the plan's bits at each pixel and the own-ray list of each kind.</summary>
    Plan,

    /// <summary>The reflection rays: each block's ray, then the listed pixels' own rays.</summary>
    ReflectionRays,

    /// <summary>The diffuse rays, as the reflection rays.</summary>
    DiffuseRays,

    /// <summary>The resolve: both signals' samples at each pixel.</summary>
    Resolve,
}

/// <summary>
/// One pass of the screen tracer (docs/plans/temporal-renderer.md, section 6): the ray plan, the rays of one kind, or the
/// resolve, each a pass of its own so that the GPU timings hold each one's time. All four bind
/// <see cref="ScreenTraceImages.Sets"/> of the draw's parity through <see cref="DescriptorLayouts.ScreenTraceLayout"/>.
/// The rays' passes run after the direct pass, whose images a hit reads, and the resolve writes the two signals' samples
/// before their accumulations.
/// </summary>
internal sealed unsafe class ScreenTracePass : RenderPass
{
    /// <summary>The width and the height of a workgroup: the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly ScreenTraceImages images;
    private readonly ScreenTrace setting;
    private readonly ScreenTraceStep step;

    // The plan's, the block rays' or the resolve's.
    private readonly Pipeline pipeline;

    // The own rays of the kind of a rays pass, or null.
    private readonly OwnRayDispatch? ownRays;

    /// <param name="constants">The renderer's constants: a ray's miss and a hit's closed form read the map while it is on.</param>
    public ScreenTracePass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ScreenTraceImages images,
        ScreenTrace setting,
        ShaderConstants constants,
        ScreenTraceStep step
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.images = images;
        this.setting = setting;
        this.step = step;
        (Name, string shader) = step switch
        {
            ScreenTraceStep.Plan => ("ray plan", "screen_trace_plan.comp"),
            ScreenTraceStep.ReflectionRays => ("reflection rays", "screen_trace_block_rays.comp"),
            ScreenTraceStep.DiffuseRays => ("diffuse rays", "screen_trace_block_rays.comp"),
            ScreenTraceStep.Resolve => ("screen trace resolve", "screen_trace_resolve.comp"),
        };
        ShaderConstants passConstants = constants with { RayKind = (uint)Kind };
        pipeline = new ComputePipelineDesc
        {
            Name = Name,
            Shader = shader,
            Layout = layouts.ScreenTraceLayout,
            Constants = passConstants,
        }.Create(device, shaders);
        if (TracesRays)
            ownRays = new OwnRayDispatch(
                device: device,
                shaders: shaders,
                pipeline: new ComputePipelineDesc
                {
                    Name = $"{Name}, own",
                    Shader = "screen_trace_own_rays.comp",
                    Layout = layouts.ScreenTraceLayout,
                    Constants = passConstants,
                }
            );
    }

    public override string Name { get; }

    /// <summary>Whether the pass traces the rays of a kind.</summary>
    private bool TracesRays => step is ScreenTraceStep.ReflectionRays or ScreenTraceStep.DiffuseRays;

    /// <summary>The ray kind of a rays pass, which indexes the record layers and the own-ray lists: 0 for the other passes.</summary>
    private int Kind => step == ScreenTraceStep.DiffuseRays ? 1 : 0;

    public override void Record(FrameContext context)
    {
        if (step == ScreenTraceStep.Plan)
            foreach (GpuBuffer list in images.OwnRays)
                OwnRayDispatch.Clear(context, list);
        DeclareUses(context);
        context.BeginComputePass(
            pipeline: pipeline,
            layout: layouts.ScreenTraceLayout,
            passSet: images.Sets[context.History.Parity]
        );
        var push = ScreenTracePushConstants.Of(setting, context.History.DrawIndex);
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layouts.ScreenTraceLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(ScreenTracePushConstants),
            pValues: &push
        );
        Extent2D extent = context.Extent;
        // A rays pass dispatches one invocation per 2 × 2 block, the others one per pixel.
        uint side = TracesRays ? 2u : 1u;
        uint width = (extent.Width + side - 1) / side,
            height = (extent.Height + side - 1) / side;
        context.Vk.CmdDispatch(
            commandBuffer: context.CommandBuffer,
            groupCountX: (width + GroupSize - 1) / GroupSize,
            groupCountY: (height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
        if (ownRays is null)
            return;

        // The own rays write the records of their own pixels, which no block ray writes. The own shader reads its kind's
        // list, but it binds both.
        foreach (GpuBuffer list in images.OwnRays)
            context.Barriers.Buffer(list, BufferUse.ComputeIndirectRead);
        ownRays.Trace(context, images.OwnRays[Kind]);
    }

    /// <summary>Declares the uses of the images and the lists that this pass reads and writes.</summary>
    private void DeclareUses(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        RenderTargets targets = context.Targets;
        foreach (GpuImage image in (GpuImage[])[targets.Distance, targets.Normal, targets.Specular, targets.BaseColor, targets.Emissive, targets.Shading])
            barriers.Image(image, ImageUse.SampledCompute);
        switch (step)
        {
            case ScreenTraceStep.Plan:
                DeclareHistories(barriers, context.History.Parity, slow: true);
                barriers.Image(images.Plan, ImageUse.StorageCompute, discard: true);
                foreach (GpuBuffer list in images.OwnRays)
                    barriers.Buffer(list, BufferUse.ComputeWrite);
                break;
            case ScreenTraceStep.ReflectionRays or ScreenTraceStep.DiffuseRays:
                DeclareRays(context);
                break;
            case ScreenTraceStep.Resolve:
                foreach (GpuImage image in images.Records)
                    barriers.Image(image, ImageUse.StorageCompute);
                // Discarded: the resolve writes every pixel of both.
                barriers.Image(images.Diffuse.Sample(0), ImageUse.StorageCompute, discard: true);
                barriers.Image(images.Specular.Sample(0), ImageUse.StorageCompute, discard: true);
                break;
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>
    /// Declares what the rays read and write: the pyramid, this frame's direct light, the last frame's fast histories and
    /// the surface test at a hit, the exposure and the map, and the records.
    /// </summary>
    private void DeclareRays(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        RenderTargets targets = context.Targets;
        ReflectionTargets pyramids = targets.Reflections ?? throw new UnreachableException("The tracer reads the pyramid.");
        barriers.Image(pyramids.DepthImage, ImageUse.SampledComputeGeneral);
        barriers.Image(pyramids.DepthFinest, ImageUse.SampledComputeGeneral);
        barriers.Image(targets.DirectDiffuse, ImageUse.StorageCompute);
        barriers.Image(targets.DirectSpecular, ImageUse.StorageCompute);
        DeclareHistories(barriers, context.History.Parity, slow: false);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeRead);
        if (context.Environment is { } map)
        {
            barriers.Image(map.Specular, ImageUse.SampledCompute);
            barriers.Image(map.Irradiance, ImageUse.SampledCompute);
        }

        // Each kind writes its own layers, but the validation layers' hazard tracking sees one image.
        foreach (GpuImage image in (GpuImage[])[images.BlockLight, images.BlockRay, images.OwnLight, images.OwnRay])
            barriers.Image(image, ImageUse.StorageCompute);
    }

    /// <summary>
    /// Declares the surface test image, the motion target, and the last frame's fast histories of both signals, or with
    /// <paramref name="slow"/> their slow histories, whose lengths the plan reads.
    /// </summary>
    private void DeclareHistories(BarrierBatch barriers, int parity, bool slow)
    {
        barriers.Image(images.SurfaceTest, ImageUse.StorageCompute);
        barriers.Image(images.Motion, ImageUse.StorageCompute);
        foreach (TemporalSignal signal in (TemporalSignal[])[images.Diffuse, images.Specular])
        {
            (_, GpuImage lastFast, GpuImage slowHistory) = signal.HitImages(part: 0, parity);
            barriers.Image(slow ? slowHistory : lastFast, ImageUse.StorageCompute);
        }
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, pipeline, null);
        ownRays?.Dispose();
    }
}
