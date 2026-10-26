using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The depth pyramid pass, when <see cref="ConeTracedReflections"/> or <see cref="VisibilityBitmaskLight"/> reads the
/// pyramid (docs/plans/screen-space-reflections.md, "The depth pyramid"): levels 1 to L of the min-max inverse depth, from
/// the distance target, one dispatch per level.
/// </summary>
internal sealed class DepthPyramidPass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    DepthPyramidImage pyramid
) : RenderPass
{
    private readonly PyramidBuild build = new(
        device: device,
        shaders: shaders,
        layouts: layouts,
        name: "depth_pyramid"
    );

    public override string Name => "depth pyramid";

    public override void Record(FrameContext context)
    {
        context.Barriers.Image(context.Targets.Distance, ImageUse.SampledCompute);
        context.BindSceneSets(PipelineBindPoint.Compute, layouts.PyramidLayout);
        for (int level = 1; level <= pyramid.TopLevel; level++)
            build.Dispatch(context: context, image: pyramid.Image, mip: level - 1, levelSet: pyramid.LevelSet(level));
    }

    public override void Dispose() => build.Dispose();
}
