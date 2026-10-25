using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Draws the faces of the environment map that this frame draws (stage <see cref="FrameGraph.Stage.EnvironmentFaces"/>,
/// docs/plans/environment-map.md, "The capture"), then rebuilds each drawn face's mip chain in the radiance map.
/// </summary>
/// <remarks>
/// <para>A face is one rendering per depth range into level 0 of its layer, far to near, each with the face's own
/// projection of that range and a freshly cleared depth. The far range first draws the sky over every texel, with no depth
/// test, so the sky shows wherever no surface does. The surfaces are shaded forward, with the sun, the lamps and their
/// emission and no ambient light.</para>
/// <para>A face is a mirror image of a right-handed camera, so the surface pipeline's front faces are clockwise.</para>
/// </remarks>
internal sealed unsafe class EnvironmentFacePass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly EnvironmentMap map;
    private readonly Pipeline sky;
    private readonly Pipeline surfaces;

    public EnvironmentFacePass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        EnvironmentMap map
    )
    {
        this.device = device;
        this.map = map;
        sky = new GraphicsPipelineDesc
        {
            Name = "environment sky",
            VertexShader = "fullscreen.vert",
            FragmentShader = "environment_sky.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [EnvironmentMap.RadianceFormat],
            DepthFormat = EnvironmentMap.FaceDepthFormat,
            CullMode = CullModeFlags.None,
        }.Create(device, shaders);
        try
        {
            surfaces = new GraphicsPipelineDesc
            {
                Name = "environment faces",
                VertexShader = "mesh.vert",
                FragmentShader = "environment_face.frag",
                Layout = layouts.SceneLayout,
                ColorFormats = [EnvironmentMap.RadianceFormat],
                DepthFormat = EnvironmentMap.FaceDepthFormat,
                VertexInput = VertexInput.Mesh,
                DepthTest = true,
                DepthWrite = true,
                DepthCompare = CompareOp.Greater,
                FrontFace = FrontFace.Clockwise,
            }.Create(device, shaders);
        }
        catch
        {
            device.Vk.DestroyPipeline(device.Device, sky, null);
            throw;
        }
    }

    public override string Name => "environment faces";

    /// <summary>The texels of a face, W × W.</summary>
    private Extent2D FaceExtent => new((uint)map.Setting.FaceSize, (uint)map.Setting.FaceSize);

    public override void Record(FrameContext context)
    {
        List<EnvironmentFaceDraw> faces = context.Scene.EnvironmentFaces;
        if (faces.Count == 0)
            return;
        BarrierBatch barriers = context.Barriers;
        barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Materials.Buffer, BufferUse.ShaderRead);
        barriers.Image(context.Shadows.Image, ImageUse.SampledFragment);
        barriers.Flush(context.Vk, context.CommandBuffer);

        // The last frame's prefilter is the only reader of the radiance map, so each face's discard waits for it alone.
        PipelineStageFlags2 lastReaders = map.Radiance.State.Stages;
        for (int i = 0; i < faces.Count; i++)
            RecordFace(context: context, draw: faces[i], lastReaders: lastReaders, bindState: i == 0);
        // Every drawn face ended read-only for the prefilter, and the faces not drawn stayed so since the last frame.
        map.Radiance.State = ResourceUses.Describe(ImageUse.SampledCompute);
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, sky, null);
        device.Vk.DestroyPipeline(device.Device, surfaces, null);
    }

    /// <summary>
    /// Records one face: the sky and the three depth ranges into level 0 of its layer, then the blits of its mips. With
    /// <paramref name="bindState"/>, it first binds the sets, the viewport and the geometry, which later faces keep.
    /// </summary>
    private void RecordFace(
        FrameContext context,
        in EnvironmentFaceDraw draw,
        PipelineStageFlags2 lastReaders,
        bool bindState
    )
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        uint layer = (uint)draw.Face;
        DiscardFace(barriers: context.Barriers, layer: layer, lastReaders: lastReaders);
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            bool first = range == DepthRanges.Far;
            if (!first)
                AfterRange(context.Barriers, layer);
            context.Barriers.Image(map.FaceDepth, ImageUse.DepthAttachment, discard: true);
            context.Barriers.Flush(vk, commandBuffer);
            BeginRange(context: context, face: draw.Face, range: range);
            if (first)
            {
                if (bindState)
                    BindState(context);
                DrawSky(context, draw.Face);
                vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, surfaces);
            }

            CommandRange commands = draw[range];
            if (commands.Count > 0)
            {
                GeometryPass.PushDraw(context: context, viewProjection: CubeFaces.ViewProjection(draw.Face, range));
                GeometryPass.DrawIndirect(context, commands);
            }

            vk.CmdEndRendering(commandBuffer);
        }

        GenerateMips(context: context, layer: layer);
    }

    /// <summary>
    /// Discards the old content of one face, every level: level 0 for the color attachment and the others for the blits.
    /// The face is drawn anew, after the last frame's prefilter finished reading it.
    /// </summary>
    private void DiscardFace(BarrierBatch barriers, uint layer, PipelineStageFlags2 lastReaders)
    {
        barriers.ImageLevels(
            image: map.Radiance,
            baseMip: 0,
            mipCount: 1,
            oldLayout: ImageLayout.Undefined,
            newLayout: ImageLayout.ColorAttachmentOptimal,
            srcStages: lastReaders,
            srcAccess: AccessFlags2.None,
            dstStages: PipelineStageFlags2.ColorAttachmentOutputBit,
            dstAccess: AccessFlags2.ColorAttachmentReadBit | AccessFlags2.ColorAttachmentWriteBit,
            baseLayer: layer,
            layerCount: 1
        );
        barriers.ImageLevels(
            image: map.Radiance,
            baseMip: 1,
            mipCount: map.Radiance.MipLevels - 1,
            oldLayout: ImageLayout.Undefined,
            newLayout: ImageLayout.TransferDstOptimal,
            srcStages: lastReaders,
            srcAccess: AccessFlags2.None,
            dstStages: PipelineStageFlags2.AllTransferBit,
            dstAccess: AccessFlags2.TransferWriteBit,
            baseLayer: layer,
            layerCount: 1
        );
    }

    /// <summary>Makes one range's color writes visible to the next range, which loads and writes the same texels.</summary>
    private void AfterRange(BarrierBatch barriers, uint layer) =>
        barriers.ImageLevels(
            image: map.Radiance,
            baseMip: 0,
            mipCount: 1,
            oldLayout: ImageLayout.ColorAttachmentOptimal,
            newLayout: ImageLayout.ColorAttachmentOptimal,
            srcStages: PipelineStageFlags2.ColorAttachmentOutputBit,
            srcAccess: AccessFlags2.ColorAttachmentWriteBit,
            dstStages: PipelineStageFlags2.ColorAttachmentOutputBit,
            dstAccess: AccessFlags2.ColorAttachmentReadBit | AccessFlags2.ColorAttachmentWriteBit,
            baseLayer: layer,
            layerCount: 1
        );

    /// <summary>
    /// Begins the rendering of one range of one face: level 0 of its layer, loaded after the far range, which the sky
    /// covers, and the face depth cleared to the range's far boundary.
    /// </summary>
    private void BeginRange(FrameContext context, CubeFace face, int range)
    {
        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = map.FaceView(face),
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = range == DepthRanges.Far ? AttachmentLoadOp.DontCare : AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
        };
        var depth = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = map.FaceDepth.View,
            ImageLayout = ImageLayout.DepthAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            ClearValue = new(depthStencil: new ClearDepthStencilValue(DepthRanges.ClearDepth(range), 0)),
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), FaceExtent),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
            PDepthAttachment = &depth,
        };
        context.Vk.CmdBeginRendering(context.CommandBuffer, &rendering);
    }

    /// <summary>
    /// Binds sets 0 to 2 of the scene layout, the face's viewport and the geometry. Set 2 holds the cascades that the
    /// faces sample; its G-buffer bindings hold the targets of the frame before, which no face reads.
    /// </summary>
    private void BindState(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, sky);
        context.BindSceneSets();
        float size = map.Setting.FaceSize;
        var viewport = new Viewport(x: 0, y: 0, width: size, height: size, minDepth: 0, maxDepth: 1);
        var scissor = new Rect2D(new Offset2D(0, 0), FaceExtent);
        vk.CmdSetViewport(commandBuffer: commandBuffer, firstViewport: 0, viewportCount: 1, pViewports: &viewport);
        vk.CmdSetScissor(commandBuffer: commandBuffer, firstScissor: 0, scissorCount: 1, pScissors: &scissor);
        GeometryPass.BindGeometry(context);
    }

    /// <summary>Draws the sky over every texel of a face: one fullscreen triangle, with the face's basis in the push constants.</summary>
    private void DrawSky(FrameContext context, CubeFace face)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, sky);
        FaceBasis basis = CubeFaces.Basis(face);
        var push = new SkyFacePushConstants
        {
            Forward = new(basis.Forward, 1f / map.Setting.FaceSize),
            Right = new(basis.Right, 0),
            Up = new(basis.Up, 0),
        };
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: context.Layouts.SceneLayout,
            stageFlags: ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            offset: 0,
            size: (uint)sizeof(SkyFacePushConstants),
            pValues: &push
        );
        vk.CmdDraw(commandBuffer: commandBuffer, vertexCount: 3, instanceCount: 1, firstVertex: 0, firstInstance: 0);
    }

    /// <summary>
    /// Rebuilds one face's mip chain from level 0 with linear blits, a box filter of all four channels, so the alpha of a
    /// coarser level is the open-sky share of its larger texel. Then the whole face is read-only for the prefilter.
    /// </summary>
    private void GenerateMips(FrameContext context, uint layer)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        GpuImage radiance = map.Radiance;
        uint levels = radiance.MipLevels;
        LevelToSource(
            barriers: barriers,
            layer: layer,
            level: 0,
            from: ImageLayout.ColorAttachmentOptimal,
            stages: PipelineStageFlags2.ColorAttachmentOutputBit,
            access: AccessFlags2.ColorAttachmentWriteBit
        );
        barriers.Flush(vk, commandBuffer);
        int size = map.Setting.FaceSize;
        for (uint level = 1; level < levels; level++)
        {
            int next = Math.Max(1, size / 2);
            var blit = new ImageBlit
            {
                SrcSubresource = new(
                    aspectMask: ImageAspectFlags.ColorBit,
                    mipLevel: level - 1,
                    baseArrayLayer: layer,
                    layerCount: 1
                ),
                DstSubresource = new(
                    aspectMask: ImageAspectFlags.ColorBit,
                    mipLevel: level,
                    baseArrayLayer: layer,
                    layerCount: 1
                ),
            };
            blit.SrcOffsets[1] = new(size, size, 1);
            blit.DstOffsets[1] = new(next, next, 1);
            vk.CmdBlitImage(
                commandBuffer: commandBuffer,
                srcImage: radiance.Handle,
                srcImageLayout: ImageLayout.TransferSrcOptimal,
                dstImage: radiance.Handle,
                dstImageLayout: ImageLayout.TransferDstOptimal,
                regionCount: 1,
                pRegions: &blit,
                filter: Filter.Linear
            );
            // The level just written is the source of the next blit, and every level ends as a transfer source.
            LevelToSource(
                barriers: barriers,
                layer: layer,
                level: level,
                from: ImageLayout.TransferDstOptimal,
                stages: PipelineStageFlags2.AllTransferBit,
                access: AccessFlags2.TransferWriteBit
            );
            barriers.Flush(vk, commandBuffer);
            size = next;
        }

        barriers.ImageLevels(
            image: radiance,
            baseMip: 0,
            mipCount: levels,
            oldLayout: ImageLayout.TransferSrcOptimal,
            newLayout: ImageLayout.ShaderReadOnlyOptimal,
            srcStages: PipelineStageFlags2.AllTransferBit,
            srcAccess: AccessFlags2.None,
            dstStages: PipelineStageFlags2.ComputeShaderBit,
            dstAccess: AccessFlags2.ShaderSampledReadBit,
            baseLayer: layer,
            layerCount: 1
        );
        barriers.Flush(vk, commandBuffer);
    }

    /// <summary>Makes one level of a face a transfer source, after the writes of <paramref name="stages"/>.</summary>
    private void LevelToSource(
        BarrierBatch barriers,
        uint layer,
        uint level,
        ImageLayout from,
        PipelineStageFlags2 stages,
        AccessFlags2 access
    ) =>
        barriers.ImageLevels(
            image: map.Radiance,
            baseMip: level,
            mipCount: 1,
            oldLayout: from,
            newLayout: ImageLayout.TransferSrcOptimal,
            srcStages: stages,
            srcAccess: access,
            dstStages: PipelineStageFlags2.AllTransferBit,
            dstAccess: AccessFlags2.TransferReadBit,
            baseLayer: layer,
            layerCount: 1
        );
}
