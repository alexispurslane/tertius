namespace Tertius.Rendering;

/// <summary>
/// The occlusion depth pass, under <see cref="VisibilityBitmaskLight"/> (docs/plans/ambient-occlusion.md, "The occlusion
/// depth pass"): the view depth of each half-resolution texel, from the distance of its representative pixel, and copies of
/// that pixel's direct light and geometric normal for the near steps (docs/plans/screen-space-illumination.md, decision 25).
/// </summary>
internal sealed class OcclusionDepthPass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    OcclusionImages images,
    VisibilityBitmaskLight setting
) : RenderPass
{
    private readonly OcclusionPipeline pipeline = new(
        device: device,
        shaders: shaders,
        layouts: layouts,
        images: images,
        setting: setting,
        shader: "occlusion_depth"
    );

    public override string Name => "occlusion depth";

    public override void Record(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        RenderTargets targets = context.Targets;
        barriers.Image(targets.Distance, ImageUse.SampledCompute);
        barriers.Image(targets.Normal, ImageUse.SampledCompute);
        // The direct pass's color write, which the copy reads.
        barriers.Image(targets.Direct, ImageUse.SampledCompute);
        // Discarded, and still after the last frame's upsample read it.
        barriers.Image(images.ViewDepth, ImageUse.StorageCompute, discard: true);
        // Discarded, and after the last frame's sectors pass read them.
        barriers.Image(images.Direct, ImageUse.StorageCompute, discard: true);
        barriers.Image(images.GeometricNormal, ImageUse.StorageCompute, discard: true);
        barriers.Flush(context.Vk, context.CommandBuffer);
        pipeline.Dispatch(context, images.HalfExtent);
    }

    public override void Dispose() => pipeline.Dispose();
}
