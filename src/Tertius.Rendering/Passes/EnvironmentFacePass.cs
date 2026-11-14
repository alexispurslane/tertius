using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Draws the faces of the environment maps that this frame draws (stage <see cref="FrameGraph.Stage.EnvironmentFaces"/>,
/// docs/plans/environment-map.md, "The capture"), map by map, into level 0 of each face's layer of the radiance maps and of
/// the distance cubes (docs/plans/environment-map-distances.md, "How the capture writes it"). The prefilter pass derives
/// the mips.
/// </summary>
/// <remarks>
/// <para>A face lights each texel once. Its surfaces light nothing: they write the face's surface record
/// (<see cref="FaceSurface"/>), one rendering per depth range, far to near, each with the face's own projection of that
/// range and a freshly cleared depth. So the record of a texel is the nearest surface of the nearest range that covers
/// it. One fullscreen triangle then lights every texel from the record, with every light and no ambient light, and
/// writes the sky where the record holds no surface. A forward face ran the light loop for every fragment of every
/// small triangle, which cost the deck 0.11 ms more of its mean frame (ticket t2-zcr4).</para>
/// <para>A surface adds its emission only when its entity holds no light: the glow of a lamp is its light's face, and the
/// lamp's power reaches the room once, through its light (docs/plans/environment-map.md, "How a face is lit").</para>
/// <para>The light pass writes a texel's radiance and its inverse distance from the eye together, as two color
/// attachments. So each texel of the distance cube describes the surface that the radiance map's texel shows: the sky's
/// 0, or the surface of the record.</para>
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
    private readonly SidedPipeline surfaces;
    private readonly SidedPipeline cutoutSurfaces;
    private readonly Pipeline light;

    public EnvironmentFacePass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        EnvironmentMap map,
        ShaderConstants constants
    )
    {
        this.device = device;
        this.map = map;
        var desc = new GraphicsPipelineDesc
        {
            Name = "environment face surfaces",
            VertexShader = "mesh.vert",
            FragmentShader = "environment_face.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = FaceSurface.Formats,
            DepthFormat = EnvironmentMap.FaceDepthFormat,
            VertexInput = VertexInput.Mesh,
            Constants = constants,
            DepthTest = true,
            DepthWrite = true,
            DepthCompare = CompareOp.Greater,
            FrontFace = FrontFace.Clockwise,
        };
        try
        {
            surfaces = SidedPipeline.Create(device: device, shaders: shaders, desc: desc);
            cutoutSurfaces = SidedPipeline.Create(
                device: device,
                shaders: shaders,
                desc: desc with
                {
                    Name = "environment face surfaces cutout",
                    Constants = constants with { CutoutAlpha = true },
                }
            );
            light = LightDesc(layouts: layouts, map: map, constants: constants).Create(device, shaders);
        }
        catch
        {
            // A pipeline that the constructor did not reach is a null handle, which vkDestroyPipeline ignores.
            Dispose();
            throw;
        }
    }

    /// <summary>The desc of the light pipeline: one color output per drawn cube, in the order of the light shader's outputs.</summary>
    private static GraphicsPipelineDesc LightDesc(
        DescriptorLayouts layouts,
        EnvironmentMap map,
        ShaderConstants constants
    )
    {
        var colorFormats = new Format[map.DrawnCubes.Length];
        for (int output = 0; output < colorFormats.Length; output++)
            colorFormats[output] = map.DrawnCubes[output].Image.Format;
        return new()
        {
            Name = "environment face light",
            VertexShader = "fullscreen.vert",
            FragmentShader = "environment_face_light.frag",
            Layout = layouts.FaceLightLayout,
            ColorFormats = colorFormats,
            Constants = constants,
            CullMode = CullModeFlags.None,
        };
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
        context.ShadowAtlas?.DeclareSampled(barriers, ImageUse.SampledFragment);
        barriers.Flush(context.Vk, context.CommandBuffer);

        // Sets 0 to 2 of the scene layout, the face's viewport and the geometry, which every face keeps. Set 2 holds the
        // cascades that the light pass samples; its G-buffer bindings hold the targets of the frame before, which no face
        // reads.
        context.BindSceneSets();
        context.SetViewport(FaceExtent);
        GeometryPass.BindGeometry(context);
        foreach (EnvironmentFaceDraw face in faces)
        {
            RecordSurfaces(context, face);
            RecordLight(context, face);
        }

        // Level 0 of every drawn face ended read-only for the prefilter, within its readers' use, and the rest of each
        // image stayed in that use since the last frame's prefilter.
        foreach (DrawnCube cube in map.DrawnCubes)
            cube.ReturnToReaders();
    }

    public override void Dispose()
    {
        surfaces.Destroy(device);
        cutoutSurfaces.Destroy(device);
        device.Vk.DestroyPipeline(device.Device, light, null);
    }

    /// <summary>
    /// Records the surfaces of one face into the surface record: the three depth ranges, far to near. The far range
    /// starts the record anew, and each later range draws over the ranges behind it.
    /// </summary>
    private void RecordSurfaces(FrameContext context, in EnvironmentFaceDraw draw)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        Span<RenderingAttachmentInfo> record = stackalloc RenderingAttachmentInfo[FaceSurface.Count];
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            map.FaceSurface.DrawnBy(barriers: context.Barriers, attachments: record, anew: range == DepthRanges.Far);
            context.Barriers.Image(map.FaceDepth, ImageUse.DepthAttachment, discard: true);
            context.Barriers.Flush(vk, commandBuffer);
            BeginRange(context: context, range: range, record: record);
            SidedRuns runs = draw[range];
            if (runs.Count > 0)
            {
                GeometryPass.PushDraw(context: context, viewProjection: CubeFaces.ViewProjection(draw.Basis, range));
                GeometryPass.DrawRuns(context: context, runs: runs, solid: surfaces, cutout: cutoutSurfaces);
            }

            vk.CmdEndRendering(commandBuffer);
        }
    }

    /// <summary>
    /// Begins the rendering of one range of a face into the attachments of the surface record,
    /// <paramref name="record"/>, with the face depth cleared to the range's far boundary.
    /// </summary>
    private void BeginRange(FrameContext context, int range, Span<RenderingAttachmentInfo> record)
    {
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
            ColorAttachmentCount = (uint)record.Length,
            PDepthAttachment = &depth,
        };
        fixed (RenderingAttachmentInfo* colors = record)
        {
            rendering.PColorAttachments = colors;
            context.Vk.CmdBeginRendering(context.CommandBuffer, &rendering);
        }
    }

    /// <summary>
    /// Lights one face from the finished surface record: one fullscreen triangle into level 0 of the face's layer of each
    /// drawn cube, which ends read-only for the prefilter.
    /// </summary>
    private void RecordLight(FrameContext context, in EnvironmentFaceDraw draw)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        uint layer = (uint)(CubeFaces.Count * draw.Map + (int)draw.Face);
        map.FaceSurface.ReadBy(context.Barriers);
        DiscardFace(barriers: context.Barriers, layer: layer);
        context.Barriers.Flush(vk, commandBuffer);

        BeginLight(context, layer);
        DrawLight(context, draw);
        vk.CmdEndRendering(commandBuffer);

        ToPrefilter(context.Barriers, layer);
        context.Barriers.Flush(vk, commandBuffer);
    }

    /// <summary>
    /// Draws the fullscreen triangle of one face's light pass, inside its rendering: the light pipeline, the face's
    /// surface record as set 3, and the face's basis and map as push constants.
    /// </summary>
    private void DrawLight(FrameContext context, in EnvironmentFaceDraw draw)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, light);
        // Compatible with the scene layout for sets 0 to 2, so those stay bound.
        DescriptorSet record = map.FaceSurface.Set;
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Graphics,
            layout: context.Layouts.FaceLightLayout,
            firstSet: 3,
            descriptorSetCount: 1,
            pDescriptorSets: &record,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        var push = new FaceLightPushConstants
        {
            Forward = new(draw.Basis.Forward, 1f / map.Setting.FaceSize),
            Right = new(draw.Basis.Right, 0),
            Up = new(draw.Basis.Up, 0),
            Map = (uint)draw.Map,
        };
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: context.Layouts.FaceLightLayout,
            stageFlags: ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            offset: 0,
            size: (uint)sizeof(FaceLightPushConstants),
            pValues: &push
        );
        vk.CmdDraw(commandBuffer: commandBuffer, vertexCount: 3, instanceCount: 1, firstVertex: 0, firstInstance: 0);
    }

    /// <summary>
    /// Discards the old content of level 0 of one face in each drawn cube, for the color attachments. The face is drawn
    /// anew, after the last frame's readers of each image finished reading it: their recorded stages, which change only
    /// after the last face.
    /// </summary>
    private void DiscardFace(BarrierBatch barriers, uint layer)
    {
        foreach (DrawnCube cube in map.DrawnCubes)
            barriers.ImageLevels(
                image: cube.Image,
                baseMip: 0,
                mipCount: 1,
                oldLayout: ImageLayout.Undefined,
                newLayout: ImageLayout.ColorAttachmentOptimal,
                srcStages: cube.Image.State.Stages,
                srcAccess: AccessFlags2.None,
                dstStages: PipelineStageFlags2.ColorAttachmentOutputBit,
                dstAccess: AccessFlags2.ColorAttachmentReadBit | AccessFlags2.ColorAttachmentWriteBit,
                baseLayer: layer,
                layerCount: 1
            );
    }

    /// <summary>
    /// Begins the light rendering of one face: level 0 of its layer in each drawn cube, whose every texel the fullscreen
    /// triangle writes.
    /// </summary>
    private void BeginLight(FrameContext context, uint layer)
    {
        ReadOnlySpan<DrawnCube> cubes = map.DrawnCubes;
        RenderingAttachmentInfo* colors = stackalloc RenderingAttachmentInfo[cubes.Length];
        for (int output = 0; output < cubes.Length; output++)
            colors[output] = new()
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = cubes[output].FaceView((int)layer),
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = AttachmentLoadOp.DontCare,
                StoreOp = AttachmentStoreOp.Store,
            };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), FaceExtent),
            LayerCount = 1,
            ColorAttachmentCount = (uint)cubes.Length,
            PColorAttachments = colors,
        };
        context.Vk.CmdBeginRendering(context.CommandBuffer, &rendering);
    }

    /// <summary>
    /// Makes level 0 of one face of each drawn cube read-only for the prefilter, after the light pass's color writes.
    /// </summary>
    private void ToPrefilter(BarrierBatch barriers, uint layer)
    {
        foreach (DrawnCube cube in map.DrawnCubes)
            barriers.ImageLevels(
                image: cube.Image,
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
}
