using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>The compute steps of the reflections after the pyramids (docs/plans/screen-space-reflections.md, "The passes").</summary>
internal enum ReflectionStep
{
    /// <summary>
    /// The trace of the targets' <see cref="ReflectionTrace"/>: one ray per pixel whose hit adds the delta to the HDR image
    /// in place, "reflections", or one ray per block of 2 × 2 pixels into the half-resolution trace, "reflection trace".
    /// </summary>
    Trace,

    /// <summary>
    /// The joint bilateral upsample of the half-resolution trace, which adds the delta of each pixel, and the own rays of
    /// the pixels whose texels' rays disagree, in a second, indirect dispatch: "reflections". Only over a
    /// <see cref="HalfTrace"/>.
    /// </summary>
    Upsample,
}

/// <summary>
/// One step of the reflections under <see cref="ConeTracedReflections"/> (docs/plans/screen-space-reflections.md, "The
/// passes"): the trace at the targets' resolution, or the half-resolution trace's upsample. One invocation per pixel or
/// texel, in 8 × 8 workgroups, over sets 0 to 2 of the scene layout and the reflection set. The upsample's own rays run
/// after it in workgroups of 64 invocations, one per listed pixel.
/// </summary>
internal sealed unsafe class ReflectionPass : RenderPass
{
    /// <summary>The width and the height of a workgroup, the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly ReflectionTargets targets;
    private readonly ConeTracedReflections setting;
    private readonly ReflectionStep step;
    private readonly Pipeline pipeline;

    /// <summary>The upsample's own-ray dispatch, reflection_own_rays.comp, or the null handle for the trace.</summary>
    private readonly Pipeline ownRayPipeline;

    /// <exception cref="ArgumentException"><paramref name="step"/> is the upsample, and the targets trace at full resolution.</exception>
    public ReflectionPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ReflectionTargets targets,
        ConeTracedReflections setting,
        ShaderConstants constants,
        ReflectionStep step
    )
    {
        // The targets keep the case of their trace on a resize, so the shader and the name hold for the pass's life.
        (string name, string shader, string pipelineName) = step switch
        {
            ReflectionStep.Trace => targets.Trace switch
            {
                FullTrace => (Name: "reflections", Shader: "reflection_trace.comp", PipelineName: "reflection trace"),
                HalfTrace => (
                    Name: "reflection trace",
                    Shader: "reflection_half_trace.comp",
                    PipelineName: "reflection half trace"
                ),
                null => throw new UnreachableException("The targets always hold a trace."),
            },
            ReflectionStep.Upsample => targets.Trace switch
            {
                HalfTrace => (
                    Name: "reflections",
                    Shader: "reflection_upsample.comp",
                    PipelineName: "reflection upsample"
                ),
                FullTrace => throw new ArgumentException(
                    "The upsample needs targets that trace at half resolution.",
                    nameof(step)
                ),
                null => throw new UnreachableException("The targets always hold a trace."),
            },
        };
        this.device = device;
        this.layouts = layouts;
        this.targets = targets;
        this.setting = setting;
        this.step = step;
        Name = name;
        pipeline = new ComputePipelineDesc
        {
            Name = pipelineName,
            Shader = shader,
            Layout = layouts.ReflectionLayout,
            // The step that adds the delta, the full-resolution trace or the upsample, reads its fallback through
            // ambientTerms, which reads the map while it is on. The half-resolution trace reads no ambient light, so its
            // compiled module holds no constant and ignores them (ComputePipelineDesc.Constants).
            Constants = constants,
        }.Create(device, shaders);
        if (step == ReflectionStep.Upsample)
            ownRayPipeline = new ComputePipelineDesc
            {
                Name = "reflection own rays",
                Shader = "reflection_own_rays.comp",
                Layout = layouts.ReflectionLayout,
                // The own rays add their deltas, whose fallback reads the map while it is on.
                Constants = constants,
            }.Create(device, shaders);
    }

    public override string Name { get; }

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        if (step == ReflectionStep.Upsample)
            ClearOwnRays(context);
        DeclareUses(context);
        context.BeginComputePass(pipeline: pipeline, layout: layouts.ReflectionLayout, passSet: targets.TraceSet);
        var push = ReflectionPushConstants.For(setting);
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.ReflectionLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(ReflectionPushConstants),
            pValues: &push
        );
        Extent2D extent = InvocationExtent(context.Targets.Extent);
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: (extent.Width + GroupSize - 1) / GroupSize,
            groupCountY: (extent.Height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
        if (step == ReflectionStep.Upsample)
            TraceOwnRays(context);
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, pipeline, null);
        if (ownRayPipeline.Handle != 0)
            device.Vk.DestroyPipeline(device.Device, ownRayPipeline, null);
    }

    /// <summary>
    /// Sets the own-ray list's header to an empty list whose dispatch has no workgroup, (0, 1, 1) and a count of 0, before
    /// the upsample appends to it.
    /// </summary>
    private void ClearOwnRays(FrameContext context)
    {
        GpuBuffer ownRays = UpsampledTrace().OwnRays;
        context.Barriers.Buffer(ownRays, BufferUse.TransferDestination);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        uint* empty = stackalloc uint[] { 0, 1, 1, 0 };
        context.Vk.CmdUpdateBuffer(
            commandBuffer: context.CommandBuffer,
            dstBuffer: ownRays.Handle,
            dstOffset: 0,
            dataSize: ReflectionTargets.OwnRayHeaderBytes,
            pData: empty
        );
    }

    /// <summary>
    /// Traces the own rays that the upsample listed, by an indirect dispatch whose command the upsample counted. The bound
    /// sets and push constants stay, because the pipeline has the upsample's layout.
    /// </summary>
    private void TraceOwnRays(FrameContext context)
    {
        GpuBuffer ownRays = UpsampledTrace().OwnRays;
        context.Barriers.Buffer(ownRays, BufferUse.ComputeIndirectRead);
        // The own rays write only pixels that the upsample left, but the validation layers' hazard tracking sees one image,
        // so the HDR image takes a barrier in the same flush, which drains nothing more than the list's barrier.
        context.Barriers.Image(context.Targets.Hdr, ImageUse.StorageCompute);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        context.Vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Compute, ownRayPipeline);
        context.Vk.CmdDispatchIndirect(commandBuffer: context.CommandBuffer, buffer: ownRays.Handle, offset: 0);
    }

    /// <summary>The images and the buffer that the step reads and writes, and how.</summary>
    private void DeclareUses(FrameContext context)
    {
        RenderTargets renderTargets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        foreach (GpuImage image in renderTargets.GBuffer)
            barriers.Image(image, ImageUse.SampledCompute);
        barriers.Image(renderTargets.Estimate, ImageUse.SampledCompute);
        switch (step)
        {
            case ReflectionStep.Trace:
                DeclarePyramids(barriers);
                switch (targets.Trace)
                {
                    case HalfTrace half:
                        // Every texel is written in each frame, after the last frame's upsample read it.
                        barriers.Image(half.Traced, ImageUse.StorageCompute, discard: true);
                        barriers.Image(half.HitDepths, ImageUse.StorageCompute, discard: true);
                        return;
                    case FullTrace:
                        break;
                    case null:
                    default:
                        throw new UnreachableException("The targets always hold a trace.");
                }

                break;
            case ReflectionStep.Upsample:
                HalfTrace upsampled = UpsampledTrace();
                barriers.Image(upsampled.Traced, ImageUse.StorageCompute);
                barriers.Image(upsampled.HitDepths, ImageUse.StorageCompute);
                // The upsample appends to the own-ray list by atomic adds, after its header's clear.
                barriers.Buffer(upsampled.OwnRays, BufferUse.ComputeWrite);
                // A pixel whose texels' rays disagree traces its own ray through the pyramids in the second dispatch.
                DeclarePyramids(barriers);
                break;
            default:
                throw new UnreachableException();
        }

        // The full-resolution trace and the upsample add the deltas to the lit image in the exposure's scale.
        barriers.Image(renderTargets.Hdr, ImageUse.StorageCompute);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeRead);
        // Their fallback's ambient terms read the map while it is on.
        if (context.Environment is { } map)
        {
            barriers.Image(map.Specular, ImageUse.SampledCompute);
            barriers.Buffer(map.Irradiance, BufferUse.ComputeRead);
        }
    }

    private void DeclarePyramids(BarrierBatch barriers)
    {
        barriers.Image(targets.DepthImage, ImageUse.SampledComputeGeneral);
        barriers.Image(targets.RadianceImage, ImageUse.SampledComputeGeneral);
    }

    /// <summary>The images that the upsample reads: the half-resolution trace's, which the constructor makes sure of.</summary>
    private HalfTrace UpsampledTrace() =>
        targets.Trace switch
        {
            HalfTrace half => half,
            FullTrace or null => throw new UnreachableException("The constructor refuses a full-resolution trace."),
        };

    /// <summary>The pixels or the texels of the step: the screen, or the half-resolution trace's texels.</summary>
    private Extent2D InvocationExtent(Extent2D screen) =>
        step switch
        {
            ReflectionStep.Trace => targets.Trace switch
            {
                HalfTrace half => half.Traced.Extent,
                FullTrace => screen,
                null => throw new UnreachableException("The targets always hold a trace."),
            },
            ReflectionStep.Upsample => screen,
        };
}
