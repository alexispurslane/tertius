using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The light pyramid pass, under <see cref="ScreenTrace"/> with the guided rays on (ARCHITECTURE.md, "Guided diffuse
/// rays"): the finest level, each pixel's guide light from this frame's direct light and its surface share, then levels 1
/// to L of its sums, one dispatch per level below the tail and one for the tail. It runs after the direct pass and the
/// surface test, whose images it reads, and before the rays, which read it.
/// </summary>
internal sealed class LightPyramidPass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    ScreenTraceImages images
) : RenderPass
{
    private readonly PyramidBuild build = new(
        device: device,
        shaders: shaders,
        layouts: layouts,
        name: "light_pyramid",
        firstShader: null
    );

    public override string Name => "light pyramid";

    /// <summary>
    /// Whether the debug switch TERTIUS_GUIDED_RAYS turns the guided diffuse rays on: unset, empty and "on" turn them on,
    /// "off" turns them off, so that every diffuse ray comes from the cosine lobe, as before the guided rays.
    /// </summary>
    /// <exception cref="ArgumentException">The variable holds another value.</exception>
    public static bool SwitchedOn() =>
        Environment.GetEnvironmentVariable("TERTIUS_GUIDED_RAYS") switch
        {
            null or "" or "on" => true,
            "off" => false,
            var value => throw new ArgumentException($"Unknown TERTIUS_GUIDED_RAYS value '{value}': use on or off."),
        };

    public override void Record(FrameContext context)
    {
        ScreenTraceKernel.DeclareSurfaces(context);
        context.Barriers.Image(context.Targets.DirectDiffuse, ImageUse.SampledComputeGeneral);
        // Read at each record: a resize recreates the surface test image and the motion target.
        context.Barriers.Image(images.SurfaceTest, ImageUse.StorageCompute);
        context.Barriers.Image(images.Motion, ImageUse.StorageCompute);
        context.BindSceneSets(PipelineBindPoint.Compute, layouts.PyramidLayout);
        PyramidImages pyramid = images.LightPyramidImages;
        build.DispatchFinest(context, pyramid);
        // Level 1 sums the finest level that the dispatch above wrote.
        context.Barriers.Image(pyramid.Finest, ImageUse.SampledComputeGeneral);
        build.DispatchLevels(context, pyramid);
    }

    public override void Dispose() => build.Dispose();
}
