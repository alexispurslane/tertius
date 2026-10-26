using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The compute pipeline of one occlusion shader, and the dispatch that the occlusion passes record with it: the scene's
/// sets 0 to 2, an occlusion set, the push constants of the setting, and one invocation per texel or pixel in 8 × 8
/// workgroups (docs/plans/screen-space-illumination.md, "Push constants and sets"). A pass declares its barriers before
/// it dispatches.
/// </summary>
internal sealed unsafe class OcclusionPipeline : IDisposable
{
    /// <summary>The width and the height of a workgroup: XeGTAO's XE_GTAO_NUMTHREADS, and the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly OcclusionImages images;
    private readonly GroundTruthOcclusion setting;
    private readonly Pipeline pipeline;

    /// <param name="shader">The shader's name without its extension, such as <c>occlusion_depth</c>.</param>
    public OcclusionPipeline(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        OcclusionImages images,
        GroundTruthOcclusion setting,
        string shader
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.images = images;
        this.setting = setting;
        pipeline = new ComputePipelineDesc
        {
            Name = shader,
            Shader = shader + ".comp",
            Layout = layouts.OcclusionLayout,
        }.Create(device, shaders);
    }

    /// <summary>Dispatches one invocation per texel or pixel of <paramref name="extent"/>, with <see cref="OcclusionImages.SetAToB"/>.</summary>
    public void Dispatch(FrameContext context, Extent2D extent) =>
        Record(context: context, extent: extent, set: images.SetAToB, lastDenoise: false);

    /// <summary>
    /// Dispatches one invocation per half-resolution texel, with <paramref name="set"/>, an occlusion set of
    /// <see cref="OcclusionImages"/>. <paramref name="last"/> marks the second dispatch.
    /// </summary>
    public void DispatchDenoise(FrameContext context, DescriptorSet set, bool last) =>
        Record(context: context, extent: images.HalfExtent, set: set, lastDenoise: last);

    public void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);

    private void Record(FrameContext context, Extent2D extent, DescriptorSet set, bool lastDenoise)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipeline);
        context.BindSceneSets(PipelineBindPoint.Compute, layouts.OcclusionLayout);
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.OcclusionLayout,
            firstSet: 3,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        var push = OcclusionPushConstants.For(setting: setting, lastDenoise: lastDenoise);
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.OcclusionLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(OcclusionPushConstants),
            pValues: &push
        );
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: (extent.Width + GroupSize - 1) / GroupSize,
            groupCountY: (extent.Height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
    }
}
