using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Maps the HDR image to the output with exposure and a <see cref="ToneCurve"/>. An sRGB output format applies
/// the sRGB curve in hardware; for a UNORM output the shader applies it. With SMAA on, it writes the SMAA tone-mapped
/// image instead, which is sRGB, and SMAA writes the output.
/// </summary>
internal sealed unsafe class ToneMapPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly Pipeline pipeline;
    private readonly ToneCurve curve;
    private readonly SmaaTargets? smaa;

    /// <param name="smaa">The SMAA images, whose tone-mapped image this pass writes, or null to write the output.</param>
    public ToneMapPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        Format outputFormat,
        ToneCurve curve,
        SmaaTargets? smaa
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.curve = curve;
        this.smaa = smaa;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "tone map",
            VertexShader = "fullscreen.vert",
            FragmentShader = "tonemap.frag",
            Layout = layouts.PostLayout,
            ColorFormats = [smaa is null ? outputFormat : SmaaTargets.ToneMappedFormat],
            CullMode = CullModeFlags.None,
        }.Create(device, shaders);
    }

    public override string Name => "tone map";

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        GpuImage target = smaa?.ToneMapped ?? context.Output;
        barriers.Image(context.Targets.Hdr, ImageUse.SampledFragment);
        barriers.Image(target, ImageUse.ColorAttachment, discard: true);
        barriers.Flush(vk, commandBuffer);

        context.BeginFullScreen(
            target: target,
            loadOp: AttachmentLoadOp.DontCare,
            pipeline: pipeline,
            layout: layouts.PostLayout,
            set: context.Targets.PostSet
        );
        var push = new ToneMapPushConstants
        {
            Exposure = context.Exposure,
            // The SMAA tone-mapped image is sRGB, so the hardware applies the curve.
            EncodeSrgb = smaa is null && context.EncodeSrgb ? 1u : 0u,
            ToneCurve = (uint)curve,
        };
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.PostLayout,
            stageFlags: ShaderStageFlags.FragmentBit,
            offset: 0,
            size: (uint)sizeof(ToneMapPushConstants),
            pValues: &push
        );
        vk.CmdDraw(commandBuffer: commandBuffer, vertexCount: 3, instanceCount: 1, firstVertex: 0, firstInstance: 0);
        vk.CmdEndRendering(commandBuffer);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
