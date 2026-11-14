using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// "Key and fill" (docs/plans/light-pairs.md, "Key and fill"), under <see cref="KeyAndFill"/>, right before the direct
/// pass. The corner pass draws one fragment per corner of the frame's tile grid and group of lights, which writes the
/// group's candidates for the corner's key lights, and the distance and the normal of the corner's surface
/// (Shaders/key_fill_corners.frag). The tile pass merges the candidates of each corner and gathers each tile's four
/// corners into its record (Shaders/key_fill_tiles.comp), which the direct pass reads.
/// </summary>
internal sealed unsafe class KeyFillPass : RenderPass
{
    /// <summary>The width and the height of a workgroup of key_fill_tiles.comp: its local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly KeyFillImages images;
    private readonly Pipeline corners;
    private readonly Pipeline tiles;

    public KeyFillPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants,
        KeyFillImages images
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.images = images;
        corners = new GraphicsPipelineDesc
        {
            Name = "key fill corners",
            VertexShader = "lighting.vert",
            FragmentShader = "key_fill_corners.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [.. images.Candidates.Select(image => image.Format)],
            CullMode = CullModeFlags.None,
            Constants = constants,
        }.Create(device, shaders);
        try
        {
            tiles = new ComputePipelineDesc
            {
                Name = "key fill tiles",
                Shader = "key_fill_tiles.comp",
                Layout = layouts.KeyFillTilesLayout,
            }.Create(device, shaders);
        }
        catch
        {
            device.Vk.DestroyPipeline(device.Device, corners, null);
            throw;
        }
    }

    public override string Name => "key and fill";

    public override void Record(FrameContext context)
    {
        RecordCorners(context);
        RecordTiles(context);
    }

    /// <summary>
    /// Draws the copies of the corner grid of the frame's render size into the first texels of the candidate images.
    /// </summary>
    private void RecordCorners(FrameContext context)
    {
        DeclareCornerReads(context);
        // Discarded: the tile pass reads only the texels that this draw writes.
        foreach (GpuImage image in images.Candidates)
            context.Barriers.Image(image, ImageUse.ColorAttachment, discard: true);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);

        context.BeginRenderingInto([.. images.Candidates.Select(image => (image, AttachmentLoadOp.DontCare))]);
        context.Vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Graphics, corners);
        context.BindSceneSets();
        context.SetViewport(KeyFillImages.CandidatesOf(context.Extent));
        context.Vk.CmdDraw(
            commandBuffer: context.CommandBuffer,
            vertexCount: 3,
            instanceCount: 1,
            firstVertex: 0,
            firstInstance: 0
        );
        context.Vk.CmdEndRendering(context.CommandBuffer);
    }

    /// <summary>Declares what the corner pass samples, as the direct pass declares it: a corner shades as a pixel does.</summary>
    private static void DeclareCornerReads(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        foreach (GpuImage image in targets.GBuffer)
            if (image != targets.Motion)
                context.Barriers.Image(image, ImageUse.SampledFragment);
        context.Barriers.Image(context.Shadows.Image, ImageUse.SampledFragment);
        context.ShadowAtlas?.DeclareSampled(context.Barriers, ImageUse.SampledFragment);
    }

    /// <summary>Dispatches one invocation per tile of the frame's render size.</summary>
    private void RecordTiles(FrameContext context)
    {
        foreach (GpuImage image in images.Candidates)
            context.Barriers.Image(image, ImageUse.SampledCompute);
        context.Barriers.Buffer(images.Tiles, BufferUse.ComputeWrite);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);

        context.Vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Compute, tiles);
        DescriptorSet set = images.Set;
        context.Vk.CmdBindDescriptorSets(
            commandBuffer: context.CommandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.KeyFillTilesLayout,
            firstSet: 0,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );

        Extent2D count = KeyFillImages.TilesOf(context.Extent);
        var push = new KeyFillTilesPushConstants
        {
            TilesWide = count.Width,
            TilesHigh = count.Height,
            KeyLights = (uint)images.Setting.KeyLights,
        };
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layouts.KeyFillTilesLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(KeyFillTilesPushConstants),
            pValues: &push
        );
        context.Vk.CmdDispatch(
            commandBuffer: context.CommandBuffer,
            groupCountX: (count.Width + GroupSize - 1) / GroupSize,
            groupCountY: (count.Height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, corners, null);
        device.Vk.DestroyPipeline(device.Device, tiles, null);
    }
}
