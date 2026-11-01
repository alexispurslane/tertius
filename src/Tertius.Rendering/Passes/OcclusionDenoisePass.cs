namespace Tertius.Rendering;

/// <summary>
/// The occlusion denoise pass, under <see cref="VisibilityBitmaskLight"/> (docs/plans/ambient-occlusion.md, "The denoise"):
/// XeGTAO's 3 × 3 edge-aware blur, run <see cref="Dispatches"/> times, from term A to term B and back, so that term A
/// holds the result.
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

    /// <summary>
    /// How many blurs the pass runs: four, which with the default 4 slices leave noise in S and G within 10 % of a
    /// 16-slice reference (docs/plans/screen-space-illumination.md, decision 43). Six blurs spread a crease's darkening
    /// past the tests' tolerance. Even, so that the last writes term A, which the upsample reads.
    /// </summary>
    public const int Dispatches = 4;

    public override string Name => "occlusion denoise";

    public override void Record(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        barriers.Image(images.Edges, ImageUse.StorageCompute);
        barriers.Image(images.TermA, ImageUse.StorageCompute);
        barriers.Image(images.TermB, ImageUse.StorageCompute, discard: true);
        for (int dispatch = 0; dispatch < Dispatches; dispatch++)
        {
            // Each later dispatch reads what the one before wrote, and writes its image only after that one read it.
            if (dispatch > 0)
            {
                barriers.Image(images.TermA, ImageUse.StorageCompute);
                barriers.Image(images.TermB, ImageUse.StorageCompute);
            }
            barriers.Flush(context.Vk, context.CommandBuffer);
            pipeline.DispatchDenoise(
                context: context,
                set: dispatch % 2 == 0 ? images.SetAToB : images.SetBToA,
                last: dispatch == Dispatches - 1
            );
        }
    }

    public override void Dispose() => pipeline.Dispose();
}
