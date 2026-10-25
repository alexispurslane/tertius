using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Builds the glow of the bright light in the bloom levels of <see cref="RenderTargets.Glow"/> (ARCHITECTURE.md, "Bloom").
/// Down: the HDR image's light above the soft threshold into level 0, then each level into the next, with Jimenez's
/// 13-tap filter. Up: each level, from the smallest, added into the one before with a 3x3 tent filter, so level 0 ends
/// holding every level summed. The tone map pass adds it to the image.
/// </summary>
internal sealed unsafe class BloomPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly Bloom bloom;
    private readonly Pipeline prefilter;
    private readonly Pipeline down;
    private readonly Pipeline up;

    public BloomPass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, Bloom bloom)
    {
        this.device = device;
        this.layouts = layouts;
        this.bloom = bloom;
        prefilter = Fullscreen(shaders, "bloom_prefilter.frag", BlendMode.None);
        down = Fullscreen(shaders, "bloom_down.frag", BlendMode.None);
        up = Fullscreen(shaders, "bloom_up.frag", BlendMode.Additive);
    }

    public override string Name => "bloom";

    public override void Record(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        GpuImage[] levels = targets.Glow;
        // The exposure that the threshold applies to, in its one place. When every shading pass writes light that is
        // already exposed (ticket ter-zzyr), this factor becomes 1.
        var push = new BloomPushConstants
        {
            Threshold = bloom.Threshold,
            Knee = bloom.Knee,
            Exposure = context.Exposure,
        };
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layouts.BloomLayout,
            stageFlags: ShaderStageFlags.FragmentBit,
            offset: 0,
            size: (uint)sizeof(BloomPushConstants),
            pValues: &push
        );

        Draw(
            context: context,
            pipeline: prefilter,
            source: targets.Hdr,
            sourceSet: targets.GlowSources[0],
            target: levels[0],
            add: false
        );
        for (int i = 1; i < levels.Length; i++)
            Draw(
                context: context,
                pipeline: down,
                source: levels[i - 1],
                sourceSet: targets.GlowSources[i],
                target: levels[i],
                add: false
            );
        // From the smallest level up, so that each level adds a level that already holds every smaller one.
        for (int i = levels.Length - 2; i >= 0; i--)
            Draw(
                context: context,
                pipeline: up,
                source: levels[i + 1],
                sourceSet: targets.GlowSources[i + 2],
                target: levels[i],
                add: true
            );
    }

    /// <summary>
    /// One fullscreen draw into <paramref name="target"/> that reads <paramref name="source"/> through
    /// <paramref name="sourceSet"/>. With <paramref name="add"/>, the draw blends into what the target holds; otherwise it
    /// replaces every pixel.
    /// </summary>
    private void Draw(
        FrameContext context,
        Pipeline pipeline,
        GpuImage source,
        DescriptorSet sourceSet,
        GpuImage target,
        bool add
    )
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        context.Barriers.Image(source, ImageUse.SampledFragment);
        context.Barriers.Image(target, ImageUse.ColorAttachment, discard: !add);
        context.Barriers.Flush(vk, commandBuffer);

        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = target.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = add ? AttachmentLoadOp.Load : AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), target.Extent),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Graphics,
            layout: layouts.BloomLayout,
            firstSet: 0,
            descriptorSetCount: 1,
            pDescriptorSets: &sourceSet,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        var viewport = new Viewport(
            x: 0,
            y: 0,
            width: target.Extent.Width,
            height: target.Extent.Height,
            minDepth: 0,
            maxDepth: 1
        );
        var scissor = new Rect2D(new Offset2D(0, 0), target.Extent);
        vk.CmdSetViewport(commandBuffer: commandBuffer, firstViewport: 0, viewportCount: 1, pViewports: &viewport);
        vk.CmdSetScissor(commandBuffer: commandBuffer, firstScissor: 0, scissorCount: 1, pScissors: &scissor);
        vk.CmdDraw(commandBuffer: commandBuffer, vertexCount: 3, instanceCount: 1, firstVertex: 0, firstInstance: 0);
        vk.CmdEndRendering(commandBuffer);
    }

    /// <summary>A pipeline of one fullscreen triangle into an HDR image, named after its fragment shader.</summary>
    private Pipeline Fullscreen(ShaderLibrary shaders, string fragmentShader, BlendMode blend) =>
        new GraphicsPipelineDesc
        {
            Name = fragmentShader,
            VertexShader = "fullscreen.vert",
            FragmentShader = fragmentShader,
            Layout = layouts.BloomLayout,
            ColorFormats = [RenderTargets.HdrFormat],
            CullMode = CullModeFlags.None,
            Blend = blend,
        }.Create(device, shaders);

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, prefilter, null);
        device.Vk.DestroyPipeline(device.Device, down, null);
        device.Vk.DestroyPipeline(device.Device, up, null);
    }
}
