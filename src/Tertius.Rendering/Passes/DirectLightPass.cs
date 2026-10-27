using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One fullscreen pass over all depth ranges that writes the direct image: the light of each light, shadowed by its slot's
/// cascades and occluder spheres or by its atlas tiles, plus the emission, pre-exposed and capped. No ambient light
/// (docs/plans/screen-space-illumination.md, "The direct pass"). Sky pixels get 0. Under the scatter it also writes the
/// scatter image: D's direct part and the mask at skin pixels, 0 elsewhere (docs/plans/skin-and-hair.md, "The diffuse
/// light D").
/// </summary>
internal sealed unsafe class DirectLightPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly ScatterImages? scatter;
    private readonly Pipeline pipeline;

    /// <param name="scatter">The scatter images under <see cref="SeparableSubsurface"/>, or null without the scatter.</param>
    public DirectLightPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants,
        ScatterImages? scatter
    )
    {
        this.device = device;
        this.scatter = scatter;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "direct light",
            VertexShader = "lighting.vert",
            FragmentShader = scatter is null ? "direct.frag" : "direct_scatter.frag",
            Layout = layouts.SceneLayout,
            ColorAttachments = scatter is null
                ? [new(RenderTargets.DirectFormat)]
                : [new(RenderTargets.DirectFormat), new(ScatterImages.ScatterFormat)],
            CullMode = CullModeFlags.None,
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name => "direct light";

    public override void Record(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        foreach (GpuImage image in targets.GBuffer)
            barriers.Image(image, ImageUse.SampledFragment);
        // Always, even in a frame without shadow maps: the descriptor promises this layout whenever the shader runs.
        barriers.Image(context.Shadows.Image, ImageUse.SampledFragment);
        if (context.ShadowAtlas is { } atlas)
            barriers.Image(atlas.Image, ImageUse.SampledFragment);
        // Discarded, and still after the last frame's ambient pass read it.
        barriers.Image(targets.Direct, ImageUse.ColorAttachment, discard: true);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        if (scatter is { } images)
        {
            // Discarded, and still after the last frame's columns pass read it: the pass writes every pixel.
            barriers.Image(images.Scatter, ImageUse.ColorAttachment, discard: true);
            barriers.Flush(context.Vk, context.CommandBuffer);
            context.DrawSceneFullScreen(
                pipeline,
                [(targets.Direct, AttachmentLoadOp.DontCare), (images.Scatter, AttachmentLoadOp.DontCare)]
            );
            return;
        }

        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(pipeline, [(targets.Direct, AttachmentLoadOp.DontCare)]);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
