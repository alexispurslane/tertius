namespace Tertius.Rendering;

/// <summary>
/// The occlusion denoise pass, under <see cref="VisibilityBitmaskLight"/> (docs/plans/ambient-occlusion.md, "The denoise"):
/// XeGTAO's 3 × 3 edge-aware blur, run twice, from term A to term B and back, so that term A holds the result.
/// </summary>
internal sealed class OcclusionDenoisePass(
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
        shader: "occlusion_denoise",
        constants: null
    );

    public override string Name => "occlusion denoise";

    public override void Record(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        barriers.Image(images.Edges, ImageUse.StorageCompute);
        barriers.Image(images.TermA, ImageUse.StorageCompute);
        barriers.Image(images.TermB, ImageUse.StorageCompute, discard: true);
        barriers.Flush(context.Vk, context.CommandBuffer);
        pipeline.DispatchDenoise(context: context, set: images.SetAToB, last: false);

        // The last dispatch reads what the first wrote, and writes term A only after the first has read it.
        barriers.Image(images.TermB, ImageUse.StorageCompute);
        barriers.Image(images.TermA, ImageUse.StorageCompute);
        barriers.Flush(context.Vk, context.CommandBuffer);
        pipeline.DispatchDenoise(context: context, set: images.SetBToA, last: true);
    }

    public override void Dispose() => pipeline.Dispose();
}
