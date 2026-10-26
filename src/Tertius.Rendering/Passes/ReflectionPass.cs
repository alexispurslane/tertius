using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The reflections pass, under <see cref="ConeTracedReflections"/> (docs/plans/screen-space-reflections.md, "The
/// passes"): one ray per pixel through the depth pyramid and one read of the radiance pyramid at its hit, which adds
/// the reflection delta to the lit HDR image in place. One invocation per pixel, in 8 × 8 workgroups.
/// </summary>
internal sealed unsafe class ReflectionPass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    ReflectionTargets targets,
    ConeTracedReflections setting,
    ShaderConstants constants
) : RenderPass
{
    /// <summary>The width and the height of a workgroup, the shader's local size.</summary>
    private const uint GroupSize = 8;

    private readonly Pipeline pipeline = new ComputePipelineDesc
    {
        Name = "reflection trace",
        Shader = "reflection_trace.comp",
        Layout = layouts.ReflectionLayout,
        // The fallback reads the ambient light through ambientTerms, which reads the map while it is on.
        Constants = constants,
    }.Create(device, shaders);

    public override string Name => "reflections";

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        RenderTargets renderTargets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        barriers.Image(targets.DepthImage, ImageUse.SampledComputeGeneral);
        barriers.Image(targets.RadianceImage, ImageUse.SampledComputeGeneral);
        foreach (GpuImage image in renderTargets.GBuffer)
            barriers.Image(image, ImageUse.SampledCompute);
        barriers.Image(renderTargets.Occlusion, ImageUse.SampledCompute);
        barriers.Image(renderTargets.Hdr, ImageUse.StorageCompute);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeRead);
        // The fallback's ambient terms read the map while it is on.
        if (context.Environment is { } map)
        {
            barriers.Image(map.Specular, ImageUse.SampledCompute);
            barriers.Buffer(map.Irradiance, BufferUse.ComputeRead);
        }
        barriers.Flush(vk, commandBuffer);

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
        Extent2D extent = renderTargets.Extent;
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: (extent.Width + GroupSize - 1) / GroupSize,
            groupCountY: (extent.Height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
