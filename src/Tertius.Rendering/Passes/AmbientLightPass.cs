using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One fullscreen pass over all depth ranges that writes the HDR image: the direct image plus the ambient light, which
/// the estimate target dims and brightens, capped at the surfaces' share (docs/plans/screen-space-illumination.md, "The
/// ambient pass"). Sky pixels get the sky and the stars, and an impostor's pixels its baked light from the direct image.
/// Under the scatter it also adds D's ambient part to the scatter image, through an additive blend on that attachment
/// alone (docs/plans/skin-and-hair.md, decision 14).
/// </summary>
internal sealed unsafe class AmbientLightPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly ScatterImages? scatter;
    private readonly Pipeline pipeline;

    /// <param name="scatter">The scatter images under <see cref="SeparableSubsurface"/>, or null without the scatter.</param>
    public AmbientLightPass(
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
            Name = "ambient light",
            VertexShader = "lighting.vert",
            FragmentShader = scatter is null ? "ambient.frag" : "ambient_scatter.frag",
            Layout = layouts.SceneLayout,
            // The HDR image takes no blend and the scatter image adds: two blend states, which independentBlend allows.
            ColorAttachments = scatter is null
                ? [new(RenderTargets.HdrFormat)]
                : [new(RenderTargets.HdrFormat), new(ScatterImages.ScatterFormat, BlendMode.Additive)],
            CullMode = CullModeFlags.None,
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name => "ambient light";

    public override void Record(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        foreach (GpuImage image in targets.GBuffer)
            barriers.Image(image, ImageUse.SampledFragment);
        // After the upsample's write, or the clear's under NoScreenSpaceLight.
        barriers.Image(targets.Estimate, ImageUse.SampledFragment);
        barriers.Image(targets.Direct, ImageUse.SampledFragment);
        barriers.Image(targets.Hdr, ImageUse.ColorAttachment, discard: true);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        if (scatter is { } images)
        {
            // Kept: the blend adds to what the direct pass wrote.
            barriers.Image(images.Scatter, ImageUse.ColorAttachment);
            barriers.Flush(context.Vk, context.CommandBuffer);
            context.DrawSceneFullScreen(
                pipeline,
                [(targets.Hdr, AttachmentLoadOp.DontCare), (images.Scatter, AttachmentLoadOp.Load)]
            );
            return;
        }

        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(pipeline, [(targets.Hdr, AttachmentLoadOp.DontCare)]);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
