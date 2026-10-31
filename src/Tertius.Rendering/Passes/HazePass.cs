using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One fullscreen pass over all depth ranges that lays the atmosphere's haze over the opaque surfaces of the HDR image
/// (docs/plans/atmosphere.md, "The haze"), through a dual-source blend: the haze's light times (1 − T) plus the image times
/// T, where T is what the air between the eye and the surface lets through. A frame whose atmosphere has no haze records
/// nothing, so it costs nothing.
/// </summary>
internal sealed unsafe class HazePass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;

    public HazePass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts)
    {
        this.device = device;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "haze",
            VertexShader = "lighting.vert",
            FragmentShader = "haze.frag",
            Layout = layouts.SceneLayout,
            ColorAttachments = [new(RenderTargets.HdrFormat, BlendMode.Transmittance)],
            CullMode = CullModeFlags.None,
        }.Create(device, shaders);
    }

    public override string Name => "haze";

    public override void Record(FrameContext context)
    {
        if (!context.Scene.Constants.Atmosphere.HasHaze)
            return;
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        barriers.Image(targets.Distance, ImageUse.SampledFragment);
        // Kept: the blend dims and adds to the lit image.
        barriers.Image(targets.Hdr, ImageUse.ColorAttachment);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(pipeline, [(targets.Hdr, AttachmentLoadOp.Load)]);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
