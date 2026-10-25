using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>The three passes of SMAA 1x, in the order that they run (docs/plans/anti-aliasing.md, "Design").</summary>
internal enum SmaaStep
{
    /// <summary>Luma edge detection: the tone-mapped image's sRGB-encoded values to the edge image.</summary>
    Edges,

    /// <summary>Blending-weight calculation: the edge image and the two tables to the weight image.</summary>
    Weights,

    /// <summary>Neighborhood blending: the tone-mapped image's linear values and the weight image to the output.</summary>
    Blending,
}

/// <summary>
/// One full-screen pass of SMAA 1x, with the reference header's vertex and pixel functions (Shaders/smaa.glsl). The edge
/// and weight passes clear their image to zero first: the edge shader discards the pixels with no edge, and the header
/// asks for both clears so that a later stencil mask can skip pixels in the weight pass.
/// </summary>
internal sealed unsafe class SmaaPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly SmaaTargets targets;
    private readonly SmaaStep step;
    private readonly Pipeline pipeline;

    public SmaaPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        SmaaTargets targets,
        SmaaStep step,
        Format outputFormat
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.targets = targets;
        this.step = step;
        string shader = step switch
        {
            SmaaStep.Edges => "smaa_edges",
            SmaaStep.Weights => "smaa_weights",
            SmaaStep.Blending => "smaa_blending",
        };
        Format format = step switch
        {
            SmaaStep.Edges => SmaaTargets.EdgesFormat,
            SmaaStep.Weights => SmaaTargets.WeightsFormat,
            SmaaStep.Blending => outputFormat,
        };
        pipeline = new GraphicsPipelineDesc
        {
            Name = Name,
            VertexShader = shader + ".vert",
            FragmentShader = shader + ".frag",
            Layout = layouts.SmaaLayout,
            ColorFormats = [format],
            CullMode = CullModeFlags.None,
        }.Create(device, shaders);
    }

    public override string Name =>
        step switch
        {
            SmaaStep.Edges => "SMAA edges",
            SmaaStep.Weights => "SMAA weights",
            SmaaStep.Blending => "SMAA blending",
        };

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        GpuImage target = DeclareUses(context.Barriers, context.Output);
        context.Barriers.Flush(vk, commandBuffer);

        // Zero means no edge and no weight. The edge shader discards the pixels with no edge, so its image needs the
        // clear. The weight shader writes every pixel, but the header's step 2 clears its image too, for the stencil mask
        // that may come later. The blending pass writes every pixel of the output.
        context.BeginFullScreen(
            target: target,
            loadOp: step == SmaaStep.Blending ? AttachmentLoadOp.DontCare : AttachmentLoadOp.Clear,
            pipeline: pipeline,
            layout: layouts.SmaaLayout,
            set: targets.Set
        );
        Extent2D extent = target.Extent;
        var push = new SmaaPushConstants
        {
            Metrics = new(x: 1f / extent.Width, y: 1f / extent.Height, z: extent.Width, w: extent.Height),
            EncodeSrgb = step == SmaaStep.Blending && context.EncodeSrgb ? 1u : 0u,
        };
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.SmaaLayout,
            stageFlags: ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            offset: 0,
            size: (uint)sizeof(SmaaPushConstants),
            pValues: &push
        );
        vk.CmdDraw(commandBuffer: commandBuffer, vertexCount: 3, instanceCount: 1, firstVertex: 0, firstInstance: 0);
        vk.CmdEndRendering(commandBuffer);
    }

    /// <summary>Declares the images that this step reads and writes, and returns the one that it writes.</summary>
    private GpuImage DeclareUses(BarrierBatch barriers, GpuImage output)
    {
        switch (step)
        {
            case SmaaStep.Edges:
                barriers.Image(targets.ToneMapped, ImageUse.SampledFragment);
                barriers.Image(targets.Edges, ImageUse.ColorAttachment, discard: true);
                return targets.Edges;
            case SmaaStep.Weights:
                barriers.Image(targets.Edges, ImageUse.SampledFragment);
                barriers.Image(targets.Tables.Area, ImageUse.SampledFragment);
                barriers.Image(targets.Tables.Search, ImageUse.SampledFragment);
                barriers.Image(targets.Weights, ImageUse.ColorAttachment, discard: true);
                return targets.Weights;
            case SmaaStep.Blending:
                barriers.Image(targets.ToneMapped, ImageUse.SampledFragment);
                barriers.Image(targets.Weights, ImageUse.SampledFragment);
                barriers.Image(output, ImageUse.ColorAttachment, discard: true);
                return output;
            default:
                throw new UnreachableException();
        }
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
