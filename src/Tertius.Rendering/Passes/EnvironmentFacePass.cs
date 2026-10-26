using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Draws the faces of the environment maps that this frame draws (stage <see cref="FrameGraph.Stage.EnvironmentFaces"/>,
/// docs/plans/environment-map.md, "The capture"), map by map, into level 0 of each face's layer of the radiance maps. The
/// prefilter pass derives the mips.
/// </summary>
/// <remarks>
/// <para>A face is one rendering per depth range into level 0 of its layer, far to near, each with the face's own
/// projection of that range and a freshly cleared depth. The far range first draws the sky over every texel, with no depth
/// test, so the sky shows wherever no surface does. The surfaces are shaded forward, with every light and their
/// emission and no ambient light.</para>
/// <para>A face looks along its map's axes (docs/plans/environment-maps-per-frame.md, decision 9), and keeps the lights of
/// its map and the maps outside it (decision 13).</para>
/// <para>A face is a mirror image of a right-handed camera, so the surface pipelines' front faces are clockwise. Each range
/// draws its solid run, then its cutout run, whose pipeline discards a texel below its material's cutoff: a face has no
/// transparent pass, so it sees the core of a fringed material alone (docs/plans/skin-and-hair.md).</para>
/// </remarks>
internal sealed unsafe class EnvironmentFacePass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly EnvironmentMap map;
    private readonly Pipeline sky;
    private readonly Pipeline surfaces;
    private readonly Pipeline cutoutSurfaces;

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
        var desc = new GraphicsPipelineDesc
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
        };
        try
        {
            surfaces = desc.Create(device, shaders);
        }
        catch
        {
            device.Vk.DestroyPipeline(device.Device, sky, null);
            throw;
        }

        try
        {
            // A face reads no ambient light, so its pipelines keep every other constant at its default, as the solid one does.
            cutoutSurfaces = (
                desc with
                {
                    Name = "environment faces cutout",
                    Constants = new ShaderConstants(EnvironmentMap: false) { CutoutAlpha = true },
                }
            ).Create(device, shaders);
        }
        catch
        {
            device.Vk.DestroyPipeline(device.Device, sky, null);
            device.Vk.DestroyPipeline(device.Device, surfaces, null);
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
        if (context.ShadowAtlas is { } atlas)
            barriers.Image(atlas.Image, ImageUse.SampledFragment);
        barriers.Flush(context.Vk, context.CommandBuffer);

        // The last frame's prefilter is the only reader of the radiance map, so each face's discard waits for it alone.
        PipelineStageFlags2 lastReaders = map.Radiance.State.Stages;
        for (int i = 0; i < faces.Count; i++)
            RecordFace(context: context, draw: faces[i], lastReaders: lastReaders, bindState: i == 0);
        // Level 0 of every drawn face ended read-only for the prefilter, and the rest of the map stayed so since the last
        // frame's prefilter.
        map.Radiance.State = ResourceUses.Describe(ImageUse.SampledCompute);
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, sky, null);
        device.Vk.DestroyPipeline(device.Device, surfaces, null);
        device.Vk.DestroyPipeline(device.Device, cutoutSurfaces, null);
    }

    /// <summary>
    /// Records one face: the sky and the three depth ranges into level 0 of its layer, which ends read-only. With
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
        uint layer = (uint)(CubeFaces.Count * draw.Map + (int)draw.Face);
        DiscardFace(barriers: context.Barriers, layer: layer, lastReaders: lastReaders);
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            bool first = range == DepthRanges.Far;
            if (!first)
                AfterRange(context.Barriers, layer);
            context.Barriers.Image(map.FaceDepth, ImageUse.DepthAttachment, discard: true);
            context.Barriers.Flush(vk, commandBuffer);
            BeginRange(context: context, draw: draw, range: range);
            if (first)
            {
                if (bindState)
                    BindState(context);
                DrawSky(context, draw.Basis);
            }

            OpaqueRuns runs = draw[range];
            if (runs.Count > 0)
            {
                GeometryPass.PushDraw(
                    context: context,
                    viewProjection: CubeFaces.ViewProjection(draw.Basis, range),
                    map: draw.Map
                );
                GeometryPass.DrawRuns(context: context, runs: runs, solid: surfaces, cutout: cutoutSurfaces);
            }

            vk.CmdEndRendering(commandBuffer);
        }

        ToPrefilter(context.Barriers, layer);
        context.Barriers.Flush(vk, commandBuffer);
    }

    /// <summary>
    /// Discards the old content of level 0 of one face, for the color attachment. The face is drawn anew, after the last
    /// frame's prefilter finished reading it.
    /// </summary>
    private void DiscardFace(BarrierBatch barriers, uint layer, PipelineStageFlags2 lastReaders) =>
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
    private void BeginRange(FrameContext context, in EnvironmentFaceDraw draw, int range)
    {
        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = map.FaceView(draw.Map, draw.Face),
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
        context.SetViewport(FaceExtent);
        GeometryPass.BindGeometry(context);
    }

    /// <summary>Draws the sky over every texel of a face: one fullscreen triangle, with the face's basis in the push constants.</summary>
    private void DrawSky(FrameContext context, FaceBasis basis)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, sky);
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

    /// <summary>Makes level 0 of one face read-only for the prefilter, after the last range's color writes.</summary>
    private void ToPrefilter(BarrierBatch barriers, uint layer) =>
        barriers.ImageLevels(
            image: map.Radiance,
            baseMip: 0,
            mipCount: 1,
            oldLayout: ImageLayout.ColorAttachmentOptimal,
            newLayout: ImageLayout.ShaderReadOnlyOptimal,
            srcStages: PipelineStageFlags2.ColorAttachmentOutputBit,
            srcAccess: AccessFlags2.ColorAttachmentWriteBit,
            dstStages: PipelineStageFlags2.ComputeShaderBit,
            dstAccess: AccessFlags2.ShaderSampledReadBit | AccessFlags2.ShaderStorageReadBit,
            baseLayer: layer,
            layerCount: 1
        );
}
