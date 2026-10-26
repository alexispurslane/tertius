using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>The compute steps of the reflections after the pyramids, by the trace's resolution (docs/plans/screen-space-reflections.md, "The passes").</summary>
internal enum ReflectionStep
{
    /// <summary>One ray per pixel, whose hit adds the reflection delta to the lit HDR image in place: "reflections".</summary>
    FullResolutionTrace,

    /// <summary>One ray per block of 2 × 2 pixels, from its top-left pixel, into the half-resolution trace: "reflection trace".</summary>
    HalfResolutionTrace,

    /// <summary>The joint bilateral upsample of the half-resolution trace, which adds the delta of each pixel: "reflections".</summary>
    Upsample,
}

/// <summary>
/// One step of the reflections under <see cref="ConeTracedReflections"/> (docs/plans/screen-space-reflections.md, "The
/// passes"): the full-resolution trace, or the half-resolution trace and its upsample. One invocation per pixel or
/// texel, in 8 × 8 workgroups, over sets 0 to 2 of the scene layout and the reflection set.
/// </summary>
internal sealed unsafe class ReflectionPass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    ReflectionTargets targets,
    ConeTracedReflections setting,
    ReflectionStep step
) : RenderPass
{
    /// <summary>The width and the height of a workgroup, the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly Pipeline pipeline = new ComputePipelineDesc
    {
        Name = step switch
        {
            ReflectionStep.FullResolutionTrace => "reflection trace",
            ReflectionStep.HalfResolutionTrace => "reflection half trace",
            ReflectionStep.Upsample => "reflection upsample",
        },
        Shader = step switch
        {
            ReflectionStep.FullResolutionTrace => "reflection_trace.comp",
            ReflectionStep.HalfResolutionTrace => "reflection_half_trace.comp",
            ReflectionStep.Upsample => "reflection_upsample.comp",
        },
        Layout = layouts.ReflectionLayout,
    }.Create(device, shaders);

    public override string Name =>
        step switch
        {
            ReflectionStep.FullResolutionTrace or ReflectionStep.Upsample => "reflections",
            ReflectionStep.HalfResolutionTrace => "reflection trace",
        };

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        DeclareUses(context);
        context.Barriers.Flush(vk, commandBuffer);

        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipeline);
        context.BindSceneSets(PipelineBindPoint.Compute, layouts.ReflectionLayout);
        DescriptorSet traceSet = targets.TraceSet;
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.ReflectionLayout,
            firstSet: 3,
            descriptorSetCount: 1,
            pDescriptorSets: &traceSet,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
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
        barriers.Image(renderTargets.Occlusion, ImageUse.SampledCompute);
        switch (step)
        {
            case ReflectionStep.FullResolutionTrace:
                DeclarePyramids(barriers);
                barriers.Image(renderTargets.Hdr, ImageUse.StorageCompute);
                barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeRead);
                break;
            case ReflectionStep.HalfResolutionTrace:
                DeclarePyramids(barriers);
                // Every texel is written in each frame, after the last frame's upsample read it.
                barriers.Image(TracedImage(), ImageUse.StorageCompute, discard: true);
                break;
            case ReflectionStep.Upsample:
                barriers.Image(TracedImage(), ImageUse.StorageCompute);
                barriers.Image(renderTargets.Hdr, ImageUse.StorageCompute);
                barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeRead);
                break;
            default:
                throw new UnreachableException();
        }
    }

    private void DeclarePyramids(BarrierBatch barriers)
    {
        barriers.Image(targets.DepthImage, ImageUse.SampledComputeGeneral);
        barriers.Image(targets.RadianceImage, ImageUse.SampledComputeGeneral);
    }

    /// <summary>The half-resolution trace, which the targets make for the steps of <see cref="TraceResolution.Half"/>.</summary>
    private GpuImage TracedImage()
    {
        // VulkanRenderer adds the half-resolution steps only over the targets of a half-resolution trace.
        Contract.Assert(targets.TracedImage is not null);
        return targets.TracedImage;
    }

    /// <summary>The pixels or the texels of the step: the screen, or ⌈W / 2⌉ × ⌈H / 2⌉ for the half-resolution trace.</summary>
    private Extent2D InvocationExtent(Extent2D screen) =>
        step switch
        {
            ReflectionStep.FullResolutionTrace or ReflectionStep.Upsample => screen,
            ReflectionStep.HalfResolutionTrace => new((screen.Width + 1) / 2, (screen.Height + 1) / 2),
        };
}
