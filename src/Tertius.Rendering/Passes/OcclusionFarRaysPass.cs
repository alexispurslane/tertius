namespace Tertius.Rendering;

/// <summary>
/// The occlusion far rays pass, under <see cref="VisibilityBitmaskLight"/> (docs/plans/screen-space-illumination.md,
/// "The far ray of a slice"): one ray per slice beyond the radius through the depth pyramid, which moves the slice's
/// open share of term A to blocked, with the light of its hit.
/// </summary>
internal sealed class OcclusionFarRaysPass(
    VulkanDevice device,
    ShaderLibrary shaders,
    DescriptorLayouts layouts,
    OcclusionImages images,
    DepthPyramidImage depthPyramid,
    VisibilityBitmaskLight setting
) : RenderPass
{
    private readonly OcclusionPipeline pipeline = new(
        device: device,
        shaders: shaders,
        layouts: layouts,
        images: images,
        setting: setting,
        shader: "occlusion_far_rays"
    );

    public override string Name => "occlusion far rays";

    public override void Record(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        RenderTargets targets = context.Targets;
        // After the sectors pass's writes of the masks and of term A, which this pass reads and moves in place.
        barriers.Buffer(images.SectorMasks, BufferUse.ComputeRead);
        barriers.Image(images.TermA, ImageUse.StorageCompute);
        barriers.Image(images.ViewDepth, ImageUse.StorageCompute);
        // The hits' normals and light, and the distance of the pyramid's level 0.
        barriers.Image(targets.Normal, ImageUse.SampledCompute);
        barriers.Image(targets.Direct, ImageUse.SampledCompute);
        barriers.Image(targets.Distance, ImageUse.SampledCompute);
        // After the depth pyramid pass's last write.
        barriers.Image(depthPyramid.Image, ImageUse.SampledComputeGeneral);
        barriers.Flush(context.Vk, context.CommandBuffer);
        pipeline.Dispatch(context, images.HalfExtent);
    }

    public override void Dispose() => pipeline.Dispose();
}
