using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// "Direct light" (docs/plans/temporal-renderer.md, section 7): one fullscreen pass over all depth ranges that takes every
/// light of the main view with its shadows at each lit pixel, and writes its two parts, the diffuse and the specular light,
/// each divided by its material factor and pre-exposed, into <see cref="RenderTargets.DirectDiffuse"/> and
/// <see cref="RenderTargets.DirectSpecular"/>. Sky and impostor pixels get 0. Under <see cref="KeyAndFill"/>, each lit
/// pixel takes the key lights and the fill of its tile's record instead, which <see cref="KeyFillPass"/> wrote
/// (docs/plans/light-pairs.md, "Key and fill").
/// </summary>
internal sealed unsafe class DirectLightPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly KeyFillImages? keyFill;
    private readonly Pipeline pipeline;

    /// <param name="keyFill">The tile records under <see cref="KeyAndFill"/>, or null under <see cref="EveryLightPair"/>.</param>
    public DirectLightPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants,
        KeyFillImages? keyFill
    )
    {
        this.device = device;
        this.keyFill = keyFill;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "direct light",
            VertexShader = "lighting.vert",
            FragmentShader = keyFill is null ? "direct.frag" : "direct_key_fill.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [RenderTargets.DirectFormat, RenderTargets.DirectFormat],
            CullMode = CullModeFlags.None,
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name => "direct light";

    public override void Record(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        // Not the motion target, which the pass does not read.
        foreach (GpuImage image in targets.GBuffer)
            if (image != targets.Motion)
                barriers.Image(image, ImageUse.SampledFragment);
        // Always, even in a frame without shadow maps: the descriptor promises this layout whenever the shader runs.
        barriers.Image(context.Shadows.Image, ImageUse.SampledFragment);
        context.ShadowAtlas?.DeclareSampled(barriers, ImageUse.SampledFragment);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        if (keyFill is not null)
            barriers.Buffer(keyFill.Tiles, BufferUse.ShaderRead);
        // Discarded, and still after the last frame's composite and occlusion depth pass read them: the pass writes every
        // pixel.
        barriers.Image(targets.DirectDiffuse, ImageUse.ColorAttachment, discard: true);
        barriers.Image(targets.DirectSpecular, ImageUse.ColorAttachment, discard: true);
        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(
            pipeline,
            [(targets.DirectDiffuse, AttachmentLoadOp.DontCare), (targets.DirectSpecular, AttachmentLoadOp.DontCare)]
        );
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
