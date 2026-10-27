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
    /// The joint bilateral upsample of the half-resolution trace, which adds the delta of each pixel: "reflections". Only
    /// over a <see cref="HalfTrace"/>.
    /// </summary>
    Upsample,
}

/// <summary>
/// One step of the reflections under <see cref="ConeTracedReflections"/> (docs/plans/screen-space-reflections.md, "The
/// passes"): the trace at the targets' resolution, or the half-resolution trace's upsample. One invocation per pixel or
/// texel, in 8 × 8 workgroups, over sets 0 to 2 of the scene layout and the reflection set.
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
    }

    public override string Name { get; }

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
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
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);

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
                // A pixel whose texels' rays disagree traces its own ray through the pyramids.
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
