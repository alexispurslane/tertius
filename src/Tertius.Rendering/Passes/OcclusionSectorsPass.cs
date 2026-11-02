namespace Tertius.Rendering;

/// <summary>
/// The occlusion sectors pass, under <see cref="VisibilityBitmaskLight"/> (docs/plans/screen-space-illumination.md, "A
/// near step"): the edges image and term A, the estimate of the net bounce and the occlusion. While the environment map
/// is on, it reads the map along the blocked directions (decision 41).
/// </summary>
internal sealed class OcclusionSectorsPass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    OcclusionImages images,
    VisibilityBitmaskLight setting,
    ShaderConstants constants
) : RenderPass
{
    private readonly OcclusionPipeline pipeline = new(
        device: device,
        shaders: shaders,
        layouts: layouts,
        images: images,
        setting: setting,
        shader: "occlusion_sectors",
        constants: constants
    );

    public override string Name => "occlusion sectors";

    public override void Record(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        RenderTargets targets = context.Targets;
        barriers.Image(images.ViewDepth, ImageUse.StorageCompute);
        // The texel setup's normals. The steps read the depth pass's copies.
        barriers.Image(targets.Normal, ImageUse.SampledCompute);
        // A receiver's model code: a hair receiver's own card blocks nothing.
        barriers.Image(targets.Shading, ImageUse.SampledCompute);
        barriers.Image(images.Source, ImageUse.StorageCompute);
        barriers.Image(images.DepthAndNormal, ImageUse.StorageCompute);
        barriers.Image(images.Edges, ImageUse.StorageCompute, discard: true);
        barriers.Image(images.TermA, ImageUse.StorageCompute, discard: true);
        // The map code of each texel names its map, whose specular levels every piece reads. S is a ratio of those reads,
        // so the pass reads no exposure.
        if (context.Environment is { } map)
        {
            barriers.Image(targets.BaseColor, ImageUse.SampledCompute);
            barriers.Image(map.Specular, ImageUse.SampledCompute);
        }
        barriers.Flush(context.Vk, context.CommandBuffer);
        pipeline.Dispatch(context, images.HalfExtent);
    }

    public override void Dispose() => pipeline.Dispose();
}
