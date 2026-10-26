using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The depth pyramid pass, under <see cref="ConeTracedReflections"/> (docs/plans/screen-space-reflections.md, "The depth
/// pyramid"): levels 1 to L of the min-max inverse depth, from the distance target, one dispatch per level below the
/// tail and one for the tail.
/// </summary>
internal sealed class DepthPyramidPass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    ReflectionTargets targets
) : RenderPass
{
    private readonly PyramidBuild build = new(
        device: device,
        shaders: shaders,
        layouts: layouts,
        name: "depth_pyramid"
    );

    private readonly DepthPyramidTail tail = new(device: device, shaders: shaders, layouts: layouts);

    public override string Name => "depth pyramid";

    public override void Record(FrameContext context)
    {
        context.Barriers.Image(context.Targets.Distance, ImageUse.SampledCompute);
        context.BindSceneSets(PipelineBindPoint.Compute, layouts.PyramidLayout);
        // Mip m holds level m + 1, so the dispatches write the mips below the tail's first.
        int tailStart = DepthPyramidTail.FirstMip(targets.DepthImage);
        for (int level = 1; level <= tailStart; level++)
            build.Dispatch(
                context: context,
                image: targets.DepthImage,
                mip: level - 1,
                levelSet: targets.DepthLevelSet(level)
            );
        tail.Dispatch(context: context, image: targets.DepthImage, tailSet: targets.DepthTailSet);
    }

    public override void Dispose()
    {
        build.Dispose();
        tail.Dispose();
    }
}
