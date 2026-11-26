using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The depth pyramid pass, under <see cref="ScreenTrace"/> (https://github.com/alexispurslane/tertius/wiki/screen-space-reflections, "The depth
/// pyramid"): levels 1 to L of the slabs, one dispatch per level below the tail and one for the tail. Then the finest
/// level, each pixel's slab, in one more dispatch (https://github.com/alexispurslane/tertius/wiki/back-face-depth-layer, "The model"). Level 1 and the
/// finest level read the pixels' surfaces, which the distance target and the normals target's geometric normals give,
/// and the back share of <see cref="BackFacePass"/>.
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
        context.Barriers.Image(
            context.Targets.BackShare
                ?? throw new UnreachableException("The targets of the screen tracer hold the back share."),
            ImageUse.SampledCompute
        );
        context.BindSceneSets(PipelineBindPoint.Compute, layouts.PyramidLayout);
        build.DispatchLevels(context, pyramid);
        build.DispatchFinest(context, pyramid);
    }

    public override void Dispose() => build.Dispose();
}
