using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Maps the HDR image to the output with exposure and a <see cref="ToneCurve"/>. With a <see cref="Bloom"/>, it first adds
/// the summed bloom levels by their strength. An sRGB output format applies the sRGB curve in hardware; for a UNORM output
/// the shader applies it.
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
            Bloom on => ("tonemap_bloom.frag", on),
            NoBloom => ("tonemap.frag", null),
            null => throw new ArgumentException("A bloom setting holds NoBloom or a Bloom.", nameof(bloom)),
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
        OutputFormat = outputFormat;
    }

    public Format OutputFormat { get; }

    public override string Name => "tone map";

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        barriers.Image(context.Targets.Hdr, ImageUse.SampledFragment);
        if (glow is not null)
            barriers.Image(context.Targets.Glow[0], ImageUse.SampledFragment);
        barriers.Image(context.Output, ImageUse.ColorAttachment, discard: true);
        barriers.Flush(vk, commandBuffer);

        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = context.Output.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), context.Output.Extent),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        DescriptorSet set = context.Targets.PostSet;
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Graphics,
            layout: layouts.PostLayout,
            firstSet: 0,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        context.SetFullViewport();
        var push = new ToneMapPushConstants
        {
            Exposure = context.Exposure,
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
