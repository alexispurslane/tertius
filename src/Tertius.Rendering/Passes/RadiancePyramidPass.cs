using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The radiance pyramid pass, under <see cref="ConeTracedReflections"/> (docs/plans/screen-space-reflections.md, "The
/// radiance pyramid"): level 0 is the lit HDR image without the glow of an entity that holds a light, times the surface
/// coverage (docs/plans/screen-space-reflections.md, "A lamp's glow"), and each next level is the 13-tap downsample of the
/// one below, one dispatch per level.
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
        // Level 0 reads the G-buffer through set 2: the distance for the coverage, and the emissive and shading targets
        // for the glow that it leaves out. The whole G-buffer is declared, as the reflections' steps declare it, so that
        // a new read of set 2 needs no new barrier here.
        foreach (GpuImage image in context.Targets.GBuffer)
            context.Barriers.Image(image, ImageUse.SampledCompute);
        context.Barriers.Image(context.Targets.Hdr, ImageUse.SampledCompute);
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
