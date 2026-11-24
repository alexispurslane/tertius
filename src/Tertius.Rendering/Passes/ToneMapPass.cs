using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Maps the pre-exposed HDR image to the output with a <see cref="ToneCurve"/>. With a <see cref="Bloom"/>, it first adds
/// the summed bloom levels by their strength. An sRGB output format applies the sRGB curve in hardware; for a UNORM output
/// the shader applies it. Under FSR it reads the upscaled HDR image, otherwise the render-size HDR image. On an 8-bit output
/// it dithers the encoded color by at most one code, from the blue-noise mask at the frame's draw index (ARCHITECTURE.md,
/// "Tone mapping").
/// </summary>
internal sealed unsafe class ToneMapPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly Pipeline pipeline;
    private readonly ToneCurve curve;

    // One code of the output's encoding as a share of 1, the dither's size, or 0 for no dither.
    private readonly float code;

    // The bloom whose glow the pass adds, or null with NoBloom: the one record of the setting, which chose the shader.
    private readonly Bloom? glow;

    public ToneMapPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        Format outputFormat,
        ToneCurve curve,
        BloomSetting bloom,
        bool dither
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.curve = curve;
        code = dither ? DitherCodeOf(outputFormat) : 0;
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

    /// <summary>
    /// One code of <paramref name="format"/>'s encoding as a share of 1, the size of the output's dither: 1/255 for the
    /// 8-bit formats that the outputs take, and 0, no dither, for every other format. A 10-bit output's dither is not
    /// built: its steps are a quarter as wide.
    /// </summary>
    internal static float DitherCodeOf(Format format) =>
        format is Format.R8G8B8A8Srgb or Format.B8G8R8A8Srgb or Format.R8G8B8A8Unorm or Format.B8G8R8A8Unorm
            ? 1 / 255f
            : 0;

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
            DrawIndex = (uint)context.History.DrawIndex,
            Code = code,
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
