using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Renders the impostor captures that <see cref="ImpostorManager"/> chose this frame (stage
/// <see cref="FrameGraph.Stage.ImpostorCaptures"/>), and draws the impostor billboards inside the G-buffer pass's
/// depth ranges (as an <see cref="IRangeDrawer"/> of <see cref="GeometryPass"/>).
/// </summary>
/// <remarks>
/// <para>A capture is one object alone, drawn with <c>mesh.vert</c> through the same instance and indirect buffers
/// as the G-buffer pass: <see cref="Prepare"/> appends its GPU instance and its one indirect command. The camera is
/// the main view's eye, looking at the center of the bounding sphere with the main view's up axis, and its square
/// frustum is fitted to the sphere's tangent cone plus two texels of margin (<see cref="CaptureProjection"/>).
/// Perspective, not orthographic: at the 200 px switch an object can be only a few radii away, where an
/// orthographic capture misses the front-to-back perspective by many pixels; the manager recaptures when the eye's
/// distance changes the perspective by about a pixel. The fragment shader lights the object with the lighting
/// pass's BRDF, <c>surfaceRadiance</c> with the shadow maps off (ambient with its specular part, the sun with the
/// eclipse term, and the point and spot lights) and writes premultiplied color, coverage and the relative depth into level 0 of the tile. Blits then rebuild the tile's mip
/// chain.</para>
/// <para>A billboard is a quad in the plane of the bounding sphere's center, spanned by the capture's axes as the
/// object's current rotation turns them (so an object rolling about the view axis needs no recapture), with one
/// GPU instance per depth range it is drawn in. Seen from the capture's eye, the quad shows exactly the captured
/// perspective image. Its fragment shader alpha-tests the coverage at 0.5, rebuilds the surface point on the ray
/// from the capture's eye through the quad, writes that point's depth (<c>gl_FragDepth</c>) and distance, and
/// writes the captured color as emission with base color alpha 0, so the lighting pass adds no light of its own.</para>
/// </remarks>
internal sealed unsafe class ImpostorPass : RenderPass, IRangeDrawer
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly ImpostorAtlas atlas;
    private readonly ImpostorManager manager;
    private readonly Pipeline capturePipeline;
    private readonly Pipeline billboardPipeline;
    private readonly PreparedCapture[] prepared = new PreparedCapture[ImpostorManager.CapturesPerFrame];
    private int preparedCount;

    public ImpostorPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ImpostorAtlas atlas,
        ImpostorManager manager,
        ShaderConstants constants
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.atlas = atlas;
        this.manager = manager;
        capturePipeline = new GraphicsPipelineDesc
        {
            Name = "impostor capture",
            VertexShader = "mesh.vert",
            FragmentShader = "impostor_capture.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [ImpostorAtlas.ColorFormat, ImpostorAtlas.DepthOffsetFormat],
            DepthFormat = ImpostorAtlas.CaptureDepthFormat,
            VertexInput = VertexInput.Mesh,
            DepthTest = true,
            DepthWrite = true,
            DepthCompare = CompareOp.Greater,
            Constants = constants,
        }.Create(device, shaders);
        try
        {
            billboardPipeline = new GraphicsPipelineDesc
            {
                Name = "impostor billboards",
                VertexShader = "impostor.vert",
                FragmentShader = "impostor.frag",
                Layout = layouts.ImpostorLayout,
                ColorFormats = RenderTargets.GBufferFormats,
                DepthFormat = RenderTargets.DepthFormat,
                VertexInput = VertexInput.None,
                DepthTest = true,
                DepthWrite = true,
                DepthCompare = CompareOp.Greater,
                CullMode = CullModeFlags.None,
            }.Create(device, shaders);
        }
        catch
        {
            device.Vk.DestroyPipeline(device.Device, capturePipeline, null);
            throw;
        }
    }

    public override string Name => "impostor captures";

    public override void Prepare(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        ReadOnlySpan<MeshInfo> meshes = context.Geometry.Meshes;
        preparedCount = 0;
        foreach (ref readonly ImpostorCapture capture in manager.Captures)
        {
            MeshInfo mesh = meshes[capture.Mesh];
            int instance = scene.AppendInstance(
                GpuInstance.From(
                    position: capture.Position,
                    rotation: capture.Rotation,
                    scale: capture.Scale,
                    material: capture.Material,
                    map: capture.Map,
                    hemisphere: capture.Hemisphere
                )
            );
            int command = scene.AppendCommand(
                new()
                {
                    IndexCount = mesh.IndexCount,
                    InstanceCount = 1,
                    FirstIndex = mesh.FirstIndex,
                    VertexOffset = mesh.VertexOffset,
                    FirstInstance = (uint)instance,
                }
            );
            prepared[preparedCount++] = new(
                capture.Tile,
                command,
                CaptureProjection(
                    right: capture.Right,
                    up: capture.Up,
                    toward: capture.Toward,
                    distance: capture.Distance,
                    radius: capture.Radius,
                    tanHalfAngle: capture.TanHalfAngle
                )
            );
        }

        ReadOnlySpan<ImpostorDraw> draws = manager.Draws;
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            int first = scene.InstanceCount;
            int bit = 1 << range;
            foreach (ref readonly ImpostorDraw draw in draws)
                if ((draw.Ranges & bit) != 0)
                    scene.AppendInstance(
                        GpuInstance.FromAxes(
                            axisX: draw.Right,
                            axisY: draw.Up,
                            axisZ: draw.Toward,
                            origin: draw.Center,
                            material: (uint)draw.Tile
                        )
                    );
            scene.Impostors[range] = new(first, scene.InstanceCount - first);
        }
    }

    /// <summary>
    /// The capture camera, in System.Numerics row-vector form: a perspective view from the eye (the origin of
    /// camera-relative world space) along −<paramref name="toward"/>, with x along <paramref name="right"/> and y
    /// along <paramref name="up"/> (flipped for Vulkan's y-down clip space, like the main view), and a square frustum
    /// of half-angle tangent <paramref name="tanHalfAngle"/>. Depth is reversed over the bounding sphere's depth: 1 at
    /// d − r, 0 at d + r, with a 1% margin. The whole clip vector is divided by d, which changes nothing on screen
    /// but makes clip w, and so 1 / gl_FragCoord.w, the depth relative to the sphere's center. The axes are
    /// right-handed like view space (toward = right × up), so front faces stay counterclockwise.
    /// </summary>
    internal static Matrix4x4 CaptureProjection(
        Vector3 right,
        Vector3 up,
        Vector3 toward,
        float distance,
        float radius,
        float tanHalfAngle
    )
    {
        float near = distance - 1.01f * radius;
        float far = distance + 1.01f * radius;
        float lateral = 1f / (tanHalfAngle * distance);
        // View depth z = dot(p, −toward). Reversed depth z_ndc = near (far − z) / (z (far − near)) comes from
        // clip z = near (far − z) / (far − near) and clip w = z, both divided by d here.
        float a = near * far / (far - near) / distance;
        float b = -near / (far - near) / distance;
        float w = 1f / distance;
        Vector3 forward = -toward;
        return new(
            m11: right.X * lateral,
            m12: -up.X * lateral,
            m13: forward.X * b,
            m14: forward.X * w,
            m21: right.Y * lateral,
            m22: -up.Y * lateral,
            m23: forward.Y * b,
            m24: forward.Y * w,
            m31: right.Z * lateral,
            m32: -up.Z * lateral,
            m33: forward.Z * b,
            m34: forward.Z * w,
            m41: 0,
            m42: 0,
            m43: a,
            m44: 0
        );
    }

    public override void Record(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        bool billboards =
            scene.Impostors[DepthRanges.Far].Count
                + scene.Impostors[DepthRanges.Middle].Count
                + scene.Impostors[DepthRanges.Near].Count
            > 0;
        if (preparedCount == 0 && !billboards)
            return;

        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        // The first use moves every tile into the layout the atlas descriptor promises; afterwards this is free.
        barriers.Image(atlas.Color, ImageUse.SampledFragment);
        barriers.Image(atlas.DepthOffset, ImageUse.SampledFragment);
        if (preparedCount > 0)
        {
            barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
            barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
            barriers.Buffer(context.Materials.Buffer, BufferUse.ShaderRead);
        }

        barriers.Flush(vk, commandBuffer);
        if (preparedCount == 0)
            return;

        for (int i = 0; i < preparedCount; i++)
            RecordCapture(context, prepared[i], bindState: i == 0);
        // Each capture ended with its tile read-only again and its writes visible to fragment shaders.
        atlas.Color.State = ResourceUses.Describe(ImageUse.SampledFragment);
        atlas.DepthOffset.State = ResourceUses.Describe(ImageUse.SampledFragment);
    }

    private void RecordCapture(FrameContext context, in PreparedCapture capture, bool bindState)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        uint layer = (uint)capture.Tile;
        // The tile's old capture is discarded. Earlier frames only sampled it in fragment shaders, and every earlier
        // capture of it ended with a barrier into that stage, so waiting for it orders after all of them.
        DiscardTile(barriers, atlas.Color, layer);
        DiscardTile(barriers, atlas.DepthOffset, layer);
        barriers.Image(atlas.CaptureDepth, ImageUse.DepthAttachment, discard: true);
        barriers.Flush(vk, commandBuffer);

        RenderingAttachmentInfo* colors = stackalloc RenderingAttachmentInfo[2];
        colors[0] = TileAttachment(atlas.ColorView(capture.Tile));
        colors[1] = TileAttachment(atlas.DepthOffsetView(capture.Tile));
        var depth = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = atlas.CaptureDepth.View,
            ImageLayout = ImageLayout.DepthAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            // Reversed depth: 0 is the far side of the capture box.
            ClearValue = new(depthStencil: new ClearDepthStencilValue(0f, 0)),
        };
        var extent = new Extent2D(ImpostorAtlas.TileSize, ImpostorAtlas.TileSize);
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), extent),
            LayerCount = 1,
            ColorAttachmentCount = 2,
            PColorAttachments = colors,
            PDepthAttachment = &depth,
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);
        if (bindState)
        {
            vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, capturePipeline);
            // Frame constants, instances and materials, and the textures. The G-buffer set is not used here.
            DescriptorSet* sets = stackalloc[] { context.Frame.FrameSet, context.Frame.TextureSet };
            vk.CmdBindDescriptorSets(
                commandBuffer: commandBuffer,
                pipelineBindPoint: PipelineBindPoint.Graphics,
                layout: layouts.SceneLayout,
                firstSet: 0,
                descriptorSetCount: 2,
                pDescriptorSets: sets,
                dynamicOffsetCount: 0,
                pDynamicOffsets: null
            );
            var viewport = new Viewport(
                x: 0,
                y: 0,
                width: ImpostorAtlas.TileSize,
                height: ImpostorAtlas.TileSize,
                minDepth: 0,
                maxDepth: 1
            );
            var scissor = new Rect2D(new Offset2D(0, 0), extent);
            vk.CmdSetViewport(commandBuffer: commandBuffer, firstViewport: 0, viewportCount: 1, pViewports: &viewport);
            vk.CmdSetScissor(commandBuffer: commandBuffer, firstScissor: 0, scissorCount: 1, pScissors: &scissor);
            GeometryPass.BindGeometry(context);
        }

        // The main view's eye (the origin), so that specular highlights are where the camera would see them.
        GeometryPass.PushDraw(context: context, viewProjection: capture.ViewProjection);
        GeometryPass.DrawIndirect(context, new(capture.Command, 1));
        vk.CmdEndRendering(commandBuffer);

        GenerateMips(vk: vk, commandBuffer: commandBuffer, barriers: barriers, layer: layer);
    }

    private static RenderingAttachmentInfo TileAttachment(ImageView view) =>
        new()
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = view,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            // No coverage, no color, no depth offset.
            ClearValue = new(new ClearColorValue(float32_0: 0f, float32_1: 0f, float32_2: 0f, float32_3: 0f)),
        };

    private static void DiscardTile(BarrierBatch barriers, GpuImage image, uint layer)
    {
        barriers.ImageLevels(
            image: image,
            baseMip: 0,
            mipCount: 1,
            oldLayout: ImageLayout.Undefined,
            newLayout: ImageLayout.ColorAttachmentOptimal,
            srcStages: PipelineStageFlags2.FragmentShaderBit,
            srcAccess: AccessFlags2.None,
            dstStages: PipelineStageFlags2.ColorAttachmentOutputBit,
            dstAccess: AccessFlags2.ColorAttachmentReadBit | AccessFlags2.ColorAttachmentWriteBit,
            baseLayer: layer,
            layerCount: 1
        );
        barriers.ImageLevels(
            image: image,
            baseMip: 1,
            mipCount: ImpostorAtlas.MipLevels - 1,
            oldLayout: ImageLayout.Undefined,
            newLayout: ImageLayout.TransferDstOptimal,
            srcStages: PipelineStageFlags2.FragmentShaderBit,
            srcAccess: AccessFlags2.None,
            dstStages: PipelineStageFlags2.AllTransferBit,
            dstAccess: AccessFlags2.TransferWriteBit,
            baseLayer: layer,
            layerCount: 1
        );
    }

    /// <summary>
    /// Rebuilds one tile's mip chain in both arrays from level 0 with linear blits (a box filter of the premultiplied
    /// values), then returns the whole tile to the read-only layout for fragment shaders.
    /// </summary>
    private void GenerateMips(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, uint layer)
    {
        const uint levels = ImpostorAtlas.MipLevels;
        foreach (GpuImage image in Images())
            barriers.ImageLevels(
                image: image,
                baseMip: 0,
                mipCount: 1,
                oldLayout: ImageLayout.ColorAttachmentOptimal,
                newLayout: ImageLayout.TransferSrcOptimal,
                srcStages: PipelineStageFlags2.ColorAttachmentOutputBit,
                srcAccess: AccessFlags2.ColorAttachmentWriteBit,
                dstStages: PipelineStageFlags2.AllTransferBit,
                dstAccess: AccessFlags2.TransferReadBit,
                baseLayer: layer,
                layerCount: 1
            );
        barriers.Flush(vk, commandBuffer);

        int size = ImpostorAtlas.TileSize;
        for (uint level = 1; level < levels; level++)
        {
            if (level > 1)
            {
                foreach (GpuImage image in Images())
                    barriers.ImageLevels(
                        image: image,
                        baseMip: level - 1,
                        mipCount: 1,
                        oldLayout: ImageLayout.TransferDstOptimal,
                        newLayout: ImageLayout.TransferSrcOptimal,
                        srcStages: PipelineStageFlags2.AllTransferBit,
                        srcAccess: AccessFlags2.TransferWriteBit,
                        dstStages: PipelineStageFlags2.AllTransferBit,
                        dstAccess: AccessFlags2.TransferReadBit,
                        baseLayer: layer,
                        layerCount: 1
                    );
                barriers.Flush(vk, commandBuffer);
            }

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
            foreach (GpuImage image in Images())
                vk.CmdBlitImage(
                    commandBuffer: commandBuffer,
                    srcImage: image.Handle,
                    srcImageLayout: ImageLayout.TransferSrcOptimal,
                    dstImage: image.Handle,
                    dstImageLayout: ImageLayout.TransferDstOptimal,
                    regionCount: 1,
                    pRegions: &blit,
                    filter: Filter.Linear
                );
            size = next;
        }

        // Every level but the last is a transfer source now; the last is still a transfer destination.
        foreach (GpuImage image in Images())
        {
            barriers.ImageLevels(
                image: image,
                baseMip: 0,
                mipCount: levels - 1,
                oldLayout: ImageLayout.TransferSrcOptimal,
                newLayout: ImageLayout.ShaderReadOnlyOptimal,
                srcStages: PipelineStageFlags2.AllTransferBit,
                srcAccess: AccessFlags2.TransferReadBit,
                dstStages: PipelineStageFlags2.FragmentShaderBit,
                dstAccess: AccessFlags2.ShaderSampledReadBit,
                baseLayer: layer,
                layerCount: 1
            );
            barriers.ImageLevels(
                image: image,
                baseMip: levels - 1,
                mipCount: 1,
                oldLayout: ImageLayout.TransferDstOptimal,
                newLayout: ImageLayout.ShaderReadOnlyOptimal,
                srcStages: PipelineStageFlags2.AllTransferBit,
                srcAccess: AccessFlags2.TransferWriteBit,
                dstStages: PipelineStageFlags2.FragmentShaderBit,
                dstAccess: AccessFlags2.ShaderSampledReadBit,
                baseLayer: layer,
                layerCount: 1
            );
        }

        barriers.Flush(vk, commandBuffer);
    }

    private AtlasImages Images() => new(atlas);

    public void DrawRange(FrameContext context, int range)
    {
        InstanceRange instances = context.Scene.Impostors[range];
        if (instances.Count == 0)
            return;
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, billboardPipeline);
        // Compatible with the scene layout for sets 0 to 2, so those stay bound.
        DescriptorSet set = atlas.Set;
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Graphics,
            layout: layouts.ImpostorLayout,
            firstSet: 3,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        GeometryPass.PushDraw(
            context: context,
            viewProjection: context.Scene.RangeViewProjection[range],
            layout: layouts.ImpostorLayout
        );
        vk.CmdDraw(
            commandBuffer: commandBuffer,
            vertexCount: 6,
            instanceCount: (uint)instances.Count,
            firstVertex: 0,
            firstInstance: (uint)instances.First
        );
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, capturePipeline, null);
        device.Vk.DestroyPipeline(device.Device, billboardPipeline, null);
    }

    /// <summary>A capture ready to record: its tile, its indirect command and its view-projection matrix.</summary>
    private readonly record struct PreparedCapture(int Tile, int Command, Matrix4x4 ViewProjection);

    /// <summary>Enumerates the two atlas arrays without allocating.</summary>
    private readonly struct AtlasImages(ImpostorAtlas atlas)
    {
        public Enumerator GetEnumerator() => new(atlas);

        public struct Enumerator(ImpostorAtlas atlas)
        {
            private int index = -1;

            public readonly GpuImage Current => index == 0 ? atlas.Color : atlas.DepthOffset;

            public bool MoveNext() => ++index < 2;
        }
    }
}
