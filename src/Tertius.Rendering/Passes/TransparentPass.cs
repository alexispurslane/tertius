using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Forward-shades transparent objects over the lit HDR image with the same lighting function, alpha-blended, and adds the
/// puffs to it, sorted together back to front across all ranges (far range first). Each range draws its runs in order:
/// a <see cref="MeshCommands"/> run with the transparent pipeline, and a <see cref="PuffSprites"/> run with the puff
/// pipeline, one instanced draw of 6 vertices per puff. So glass dims a puff behind it and leaves one in front of it as
/// it is. There is no depth buffer here: occlusion by opaque surfaces in any range uses the G-buffer distance, and depth
/// clamping keeps each object and each puff whole inside its range.
/// </summary>
internal sealed unsafe class TransparentPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;
    private readonly Pipeline puffPipeline;

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
            Blend = BlendMode.Alpha
        }.Create(device, shaders);
        puffPipeline = new GraphicsPipelineDesc
        {
            Name = "puffs",
            VertexShader = "puff.vert",
            FragmentShader = "puff.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [RenderTargets.HdrFormat],
            VertexInput = VertexInput.None,
            // The square faces the eye, and its winding does not matter.
            CullMode = CullModeFlags.None,
            DepthClamp = true,
            Blend = BlendMode.Additive
        }.Create(device, shaders);
    }

    public override string Name => "transparent";

    public override void Record(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        bool any = false;
        foreach (List<TransparentRun> runs in scene.Transparent)
            any |= runs.Count > 0;
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
        // The vertex shader of the lit puffs samples the cascades at each puff's center.
        barriers.Image(context.Shadows.Image, ImageUse.SampledVertexFragment);
        barriers.Image(targets.Hdr, ImageUse.ColorAttachment);
        barriers.Flush(vk, commandBuffer);

        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = targets.Hdr.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), targets.Extent),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &color
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);
        // Both pipelines have the scene layout, so the sets and the push constants stay bound when the pipeline changes.
        Pipeline bound = pipeline;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, bound);
        context.BindSceneSets();
        context.SetFullViewport();
        GeometryPass.BindGeometry(context);
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            List<TransparentRun> runs = scene.Transparent[range];
            if (runs.Count == 0)
                continue;
            GeometryPass.PushDraw(context: context, viewProjection: scene.RangeViewProjection[range]);
            foreach (TransparentRun run in runs)
                switch (run)
                {
                    case MeshCommands(var commands):
                        Bind(vk: vk, commandBuffer: commandBuffer, bound: ref bound, wanted: pipeline);
                        GeometryPass.DrawIndirect(context, commands);
                        break;
                    case PuffSprites { First: var first, Count: var count }:
                        Bind(vk: vk, commandBuffer: commandBuffer, bound: ref bound, wanted: puffPipeline);
                        vk.CmdDraw(commandBuffer: commandBuffer, vertexCount: 6, instanceCount: (uint)count,
                            firstVertex: 0, firstInstance: (uint)first);
                        break;
                    case null:
                        // The scene batcher adds a run of meshes or of puffs, never an empty run.
                        throw new UnreachableException("A transparent run is empty.");
                    default:
                        throw new UnreachableException();
                }
        }

        vk.CmdEndRendering(commandBuffer);
    }

    private static void Bind(Vk vk, CommandBuffer commandBuffer, ref Pipeline bound, Pipeline wanted)
    {
        if (bound.Handle == wanted.Handle)
            return;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, wanted);
        bound = wanted;
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, pipeline, null);
        device.Vk.DestroyPipeline(device.Device, puffPipeline, null);
    }
}
