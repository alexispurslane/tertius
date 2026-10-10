using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Forward-shades transparent objects over the lit HDR image with the same lighting function, alpha-blended and
/// sorted back to front across all ranges (far range first). There is no depth buffer here: occlusion by opaque
/// surfaces in any range uses the G-buffer distance, and depth clamping keeps each object whole inside its range.
/// </summary>
internal sealed unsafe class TransparentPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;

    public TransparentPass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts)
    {
        this.device = device;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "transparent",
            VertexShader = "mesh.vert",
            FragmentShader = "transparent.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [RenderTargets.HdrFormat],
            VertexInput = VertexInput.Mesh,
            DepthClamp = true,
            AlphaBlend = true,
        }.Create(device, shaders);
    }

    public override string Name => "transparent";

    public override void Record(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        bool any = false;
        foreach (CommandRange range in scene.Transparent)
            any |= range.Count > 0;
        if (!any)
            return;

        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Materials.Buffer, BufferUse.ShaderRead);
        barriers.Image(targets.Distance, ImageUse.SampledFragment);
        barriers.Image(context.Shadows.Image, ImageUse.SampledFragment);
        barriers.Image(targets.Hdr, ImageUse.ColorAttachment);
        barriers.Flush(vk, commandBuffer);

        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = targets.Hdr.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D(new Offset2D(0, 0), targets.Extent),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        context.BindSceneSets();
        context.SetFullViewport();
        GeometryPass.BindGeometry(context);
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            CommandRange commands = scene.Transparent[range];
            if (commands.Count == 0)
                continue;
            GeometryPass.PushDraw(context, scene.RangeViewProjection[range]);
            GeometryPass.DrawIndirect(context, commands);
        }
        vk.CmdEndRendering(commandBuffer);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
