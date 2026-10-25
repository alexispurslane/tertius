namespace Tertius.Rendering;

/// <summary>
/// The occlusion upsample pass, under <see cref="GroundTruthOcclusion"/> (docs/plans/ambient-occlusion.md, "The
/// upsample"): term A to the full-resolution occlusion target, weighted by each texel's distance from the pixel's plane.
/// </summary>
internal sealed class OcclusionUpsamplePass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    OcclusionImages images,
    GroundTruthOcclusion setting
) : RenderPass
{
    private readonly OcclusionPipeline pipeline = new(
        device: device,
        shaders: shaders,
        layouts: layouts,
        images: images,
        setting: setting,
        shader: "occlusion_upsample"
    );

    public override string Name => "occlusion upsample";

    public override void Record(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        RenderTargets targets = context.Targets;
        barriers.Image(images.TermA, ImageUse.StorageCompute);
        barriers.Image(images.ViewDepth, ImageUse.StorageCompute);
        barriers.Image(targets.Distance, ImageUse.SampledCompute);
        barriers.Image(targets.Normal, ImageUse.SampledCompute);
        // Discarded, and still after the last frame's lighting pass read it.
        barriers.Image(targets.Occlusion, ImageUse.StorageCompute, discard: true);
        barriers.Flush(context.Vk, context.CommandBuffer);
        pipeline.Dispatch(context, targets.Extent);
    }

    public override void Dispose() => pipeline.Dispose();
}
