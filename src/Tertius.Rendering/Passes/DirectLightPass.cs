using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One fullscreen pass over all depth ranges that writes the direct image: the light of each light, shadowed by its slot's
/// cascades and occluder spheres or by its atlas tiles, plus the emission, pre-exposed and capped. No ambient light
/// (docs/plans/screen-space-illumination.md, "The direct pass"). Sky pixels get 0. When the targets hold the diffuse image,
/// it also writes that: the diffuse part and the skin mask (docs/plans/skin-and-hair.md, "The diffuse light D").
/// </summary>
internal sealed unsafe class DirectLightPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;

    /// <param name="diffuse">True when the targets hold the diffuse image.</param>
    public DirectLightPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants,
        bool diffuse
    )
    {
        this.device = device;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "direct light",
            VertexShader = "lighting.vert",
            FragmentShader = diffuse ? "direct_diffuse.frag" : "direct.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = diffuse
                ? [RenderTargets.DirectFormat, RenderTargets.DiffuseFormat]
                : [RenderTargets.DirectFormat],
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
        context.ShadowAtlas?.DeclareSampled(barriers, ImageUse.SampledFragment);
        // Discarded, and still after the last frame's ambient pass read it.
        barriers.Image(targets.Direct, ImageUse.ColorAttachment, discard: true);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        // The renderer makes the pass and the targets from the same options, so the image is there exactly when the
        // pipeline draws it.
        if (targets.Diffuse is { } diffuse)
        {
            // Discarded, and still after the last frame's readers, the copy and the scatter passes: the pass writes every
            // pixel.
            barriers.Image(diffuse, ImageUse.ColorAttachment, discard: true);
            barriers.Flush(context.Vk, context.CommandBuffer);
            context.DrawSceneFullScreen(
                pipeline,
                [(targets.Direct, AttachmentLoadOp.DontCare), (diffuse, AttachmentLoadOp.DontCare)]
            );
            return;
        }

        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(pipeline, [(targets.Direct, AttachmentLoadOp.DontCare)]);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
