using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The radiance pyramid pass, under <see cref="ConeTracedReflections"/> (docs/plans/screen-space-reflections.md, "The
/// radiance pyramid"): level 0 is the lit HDR image times the surface coverage, and each next level is the 13-tap
/// downsample of the one below, one dispatch per level.
/// </summary>
internal sealed class RadiancePyramidPass(
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
        name: "radiance_pyramid"
    );

    public override string Name => "radiance pyramid";

    public override void Record(FrameContext context)
    {
        context.Barriers.Image(context.Targets.Hdr, ImageUse.SampledCompute);
        context.Barriers.Image(context.Targets.Distance, ImageUse.SampledCompute);
        context.BindSceneSets(PipelineBindPoint.Compute, layouts.PyramidLayout);
        for (int level = 0; level <= targets.RadianceTopLevel; level++)
            build.Dispatch(
                context: context,
                image: targets.RadianceImage,
                mip: level,
                levelSet: targets.RadianceLevelSet(level)
            );
    }

    public override void Dispose() => build.Dispose();
}
