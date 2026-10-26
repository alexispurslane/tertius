namespace Tertius.Rendering;

/// <summary>
/// The occlusion sectors pass, under <see cref="VisibilityBitmaskLight"/> (docs/plans/screen-space-illumination.md, "A
/// near step"): the edges image, each slice's mask of the sectors that its near steps block, and term A, the near
/// estimate of the bounce and the occlusion.
/// </summary>
internal sealed class OcclusionSectorsPass(
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
        shader: "occlusion_sectors"
    );

    public override string Name => "occlusion sectors";

    public override void Record(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        RenderTargets targets = context.Targets;
        barriers.Image(images.ViewDepth, ImageUse.StorageCompute);
        barriers.Image(targets.Normal, ImageUse.SampledCompute);
        // The direct pass's color write, which the blockers' light reads.
        barriers.Image(targets.Direct, ImageUse.SampledCompute);
        barriers.Image(images.Edges, ImageUse.StorageCompute, discard: true);
        barriers.Image(images.TermA, ImageUse.StorageCompute, discard: true);
        // After the last frame's reads of the masks.
        barriers.Buffer(images.SectorMasks, BufferUse.ComputeWrite);
        barriers.Flush(context.Vk, context.CommandBuffer);
        pipeline.Dispatch(context, images.HalfExtent);
    }

    public override void Dispose() => pipeline.Dispose();
}
