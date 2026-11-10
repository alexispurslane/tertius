using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The depth pyramid pass, under <see cref="ScreenTrace"/> (docs/plans/screen-space-reflections.md, "The depth
/// pyramid"): levels 1 to L of the min-max inverse depth, from the surfaces of the pixels, which the distance target and
/// the normals target's geometric normals give, one dispatch per level below the tail and one for the tail. Then the
/// finest level, each pixel's center w, in one more dispatch (docs/plans/temporal-renderer.md, decision 40).
/// </summary>
internal sealed class DepthPyramidPass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    PyramidImages pyramid
) : RenderPass
{
    private readonly PyramidBuild build = new(
        device: device,
        shaders: shaders,
        layouts: layouts,
        name: "depth_pyramid",
        firstShader: "depth_pyramid_first.comp"
    );

    public override string Name => "depth pyramid";

    public override void Record(FrameContext context)
    {
        context.Barriers.Image(context.Targets.Distance, ImageUse.SampledCompute);
        context.Barriers.Image(context.Targets.Normal, ImageUse.SampledCompute);
        context.BindSceneSets(PipelineBindPoint.Compute, layouts.PyramidLayout);
        build.DispatchLevels(context, pyramid);
        build.DispatchFinest(context, pyramid);
    }

    public override void Dispose() => build.Dispose();
}
