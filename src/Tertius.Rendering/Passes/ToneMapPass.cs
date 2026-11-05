using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Maps the pre-exposed HDR image to the output with a <see cref="ToneCurve"/>. With a <see cref="Bloom"/>, it first adds
/// the summed bloom levels by their strength. An sRGB output format applies the sRGB curve in hardware; for a UNORM output
/// the shader applies it. Under FSR it reads the upscaled HDR image, otherwise the render-size HDR image.
/// </summary>
internal sealed unsafe class ToneMapPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly Pipeline pipeline;
    private readonly ToneCurve curve;

    // The bloom whose glow the pass adds, or null with NoBloom: the one record of the setting, which chose the shader.
    private readonly Bloom? glow;

    public ToneMapPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        Format outputFormat,
        ToneCurve curve,
        BloomSetting bloom
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.curve = curve;
        (string shader, glow) = bloom switch
        {
            Bloom on => ("tonemap_bloom.frag", (Bloom?)on),
            NoBloom => ("tonemap.frag", (Bloom?)null),
        };
        pipeline = new GraphicsPipelineDesc
        {
            Name = "tone map",
            VertexShader = "fullscreen.vert",
            FragmentShader = shader,
            Layout = layouts.PostLayout,
            ColorFormats = [outputFormat],
            CullMode = CullModeFlags.None,
        }.Create(device, shaders);
    }

    public override string Name => "tone map";

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        GpuImage target = context.Output;
        barriers.Image(context.Targets.PostHdr, ImageUse.SampledFragment);
        if (glow is not null)
            barriers.Image(context.Targets.Glow[0], ImageUse.SampledFragment);
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
            EncodeSrgb = context.EncodeSrgb ? 1u : 0u,
            ToneCurve = (uint)curve,
            GlowStrength = glow?.Strength ?? 0,
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
