using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One fullscreen pass over all depth ranges that writes the HDR image: the direct image plus the ambient light, which
/// the estimate target dims and brightens, capped at the surfaces' share (docs/plans/screen-space-illumination.md, "The
/// ambient pass"). Sky pixels get the sky and the stars, and an impostor's pixels its baked light from the direct image.
/// </summary>
internal sealed unsafe class AmbientLightPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;

    public AmbientLightPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants
    )
    {
        this.device = device;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "ambient light",
            VertexShader = "lighting.vert",
            FragmentShader = "ambient.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [RenderTargets.HdrFormat],
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
        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(targets.Hdr, pipeline);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
