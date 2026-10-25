namespace Tertius.Rendering;

/// <summary>
/// The occlusion horizons pass, under <see cref="GroundTruthOcclusion"/> (docs/plans/ambient-occlusion.md, "The occlusion
/// horizons pass"): the edges image, and occlusion term A from the slices and the steps of GTAO.
/// </summary>
internal sealed class OcclusionHorizonsPass(
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
        shader: "occlusion_horizons"
    );

    public override string Name => "occlusion horizons";

    public override void Record(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        barriers.Image(images.ViewDepth, ImageUse.StorageCompute);
        barriers.Image(context.Targets.Normal, ImageUse.SampledCompute);
        barriers.Image(images.Edges, ImageUse.StorageCompute, discard: true);
        barriers.Image(images.TermA, ImageUse.StorageCompute, discard: true);
        barriers.Flush(context.Vk, context.CommandBuffer);
        pipeline.Dispatch(context, images.HalfExtent);
    }

    public override void Dispose() => pipeline.Dispose();
}
