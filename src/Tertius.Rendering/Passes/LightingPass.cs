using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One fullscreen pass over all depth ranges: reads the G-buffer, rebuilds each pixel's position from its view ray
/// and stored distance, and writes lit HDR color, with the sun shadowed by the cascades and the occluder spheres.
/// Sky pixels get the star field.
/// </summary>
internal sealed unsafe class LightingPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;

    public LightingPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants
    )
    {
        this.device = device;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "lighting",
            VertexShader = "lighting.vert",
            FragmentShader = "lighting.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [RenderTargets.HdrFormat],
            CullMode = CullModeFlags.None,
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name => "lighting";

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        foreach (GpuImage image in targets.GBuffer)
            barriers.Image(image, ImageUse.SampledFragment);
        // Always, even in a frame without shadow maps: the descriptor promises this layout whenever the shader runs.
        barriers.Image(context.Shadows.Image, ImageUse.SampledFragment);
        barriers.Image(targets.Hdr, ImageUse.ColorAttachment, discard: true);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        barriers.Flush(vk, commandBuffer);

        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = targets.Hdr.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            // Every pixel is written.
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), targets.Extent),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        context.BindSceneSets();
        context.SetFullViewport();
        vk.CmdDraw(commandBuffer: commandBuffer, vertexCount: 3, instanceCount: 1, firstVertex: 0, firstInstance: 0);
        vk.CmdEndRendering(commandBuffer);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
