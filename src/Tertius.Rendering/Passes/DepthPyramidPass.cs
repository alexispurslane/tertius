using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The depth pyramid pass, under <see cref="ScreenTrace"/> (docs/plans/screen-space-reflections.md, "The depth
/// pyramid"): levels 1 to L of the min-max inverse depth, from the surfaces of the pixels, which the distance target and
/// the normals target's geometric normals give, one dispatch per level below the tail and one for the tail. Then the
/// finest level, each pixel's center w, in one more dispatch (docs/plans/temporal-renderer.md, decision 40).
/// </summary>
internal sealed unsafe class DepthPyramidPass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    DepthPyramidImages pyramid
) : RenderPass
{
    private readonly PyramidBuild build = new(
        device: device,
        shaders: shaders,
        layouts: layouts,
        name: "depth_pyramid"
    );

    private readonly DepthPyramidTail tail = new(device: device, shaders: shaders, layouts: layouts);

    private readonly Pipeline finest = new ComputePipelineDesc
    {
        Name = "depth pyramid finest",
        Shader = "depth_pyramid_finest.comp",
        Layout = layouts.PyramidLayout,
    }.Create(device, shaders);

    public override string Name => "depth pyramid";

    public override void Record(FrameContext context)
    {
        context.Barriers.Image(context.Targets.Distance, ImageUse.SampledCompute);
        context.Barriers.Image(context.Targets.Normal, ImageUse.SampledCompute);
        context.BindSceneSets(PipelineBindPoint.Compute, layouts.PyramidLayout);
        // Mip m holds level m + 1, so the dispatches write the mips below the tail's first.
        int tailStart = DepthPyramidTail.FirstMip(pyramid.DepthImage);
        for (int level = 1; level <= tailStart; level++)
            build.Dispatch(
                context: context,
                image: pyramid.DepthImage,
                mip: level - 1,
                levelSet: pyramid.DepthLevelSet(level)
            );
        tail.Dispatch(context: context, image: pyramid.DepthImage, tailSet: pyramid.DepthTailSet);
        DispatchFinest(context);
    }

    public override void Dispose()
    {
        build.Dispose();
        tail.Dispose();
        device.Vk.DestroyPipeline(device.Device, finest, null);
    }

    /// <summary>Writes the finest level, after the last frame's reads of it. The scene sets stay bound from the levels.
    /// </summary>
    private void DispatchFinest(FrameContext context)
    {
        Vk vk = context.Vk;
        GpuImage image = pyramid.DepthFinest;
        context.Barriers.Image(image, ImageUse.StorageCompute, discard: true);
        context.Barriers.Flush(vk, context.CommandBuffer);
        vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Compute, finest);
        DescriptorSet set = pyramid.DepthFinestSet;
        vk.CmdBindDescriptorSets(
            commandBuffer: context.CommandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.PyramidLayout,
            firstSet: 3,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        vk.CmdDispatch(
            commandBuffer: context.CommandBuffer,
            groupCountX: (image.Extent.Width + 7) / 8,
            groupCountY: (image.Extent.Height + 7) / 8,
            groupCountZ: 1
        );
    }
}
