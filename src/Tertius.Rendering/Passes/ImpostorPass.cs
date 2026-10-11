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
/// pass's Blinn-Phong (ambient, the sun with the eclipse term, and the point and spot lights, but not the shadow
/// maps) and writes premultiplied color, coverage and the relative depth into level 0 of the tile. Blits then rebuild the tile's mip
/// chain.</para>
/// <para>A billboard is a quad in the plane of the bounding sphere's center, spanned by the capture's axes as the
/// object's current rotation turns them (so an object rolling about the view axis needs no recapture), with one
/// GPU instance per depth range it is drawn in. Seen from the capture's eye, the quad shows exactly the captured
/// perspective image. Its fragment shader alpha-tests the coverage at 0.5, rebuilds the surface point on the ray
/// from the capture's eye through the quad, writes that point's depth (<c>gl_FragDepth</c>) and distance, and
/// writes the captured color as emission with albedo alpha 0, so the lighting pass adds no light of its own.</para>
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

    public ImpostorPass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, ImpostorAtlas atlas, ImpostorManager manager)
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
            int instance = scene.AppendInstance(GpuInstance.From(capture.Position, capture.Rotation, capture.Scale, capture.Material, capture.Hemisphere));
            int command = scene.AppendCommand(new DrawIndexedIndirectCommand
            {
                IndexCount = mesh.IndexCount,
                InstanceCount = 1,
                FirstIndex = mesh.FirstIndex,
                VertexOffset = mesh.VertexOffset,
                FirstInstance = (uint)instance,
            });
            prepared[preparedCount++] = new PreparedCapture(capture.Tile, command,
                CaptureProjection(capture.Right, capture.Up, capture.Toward, capture.Distance, capture.Radius, capture.TanHalfAngle));
        }

        ReadOnlySpan<ImpostorDraw> draws = manager.Draws;
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            int first = scene.InstanceCount;
            int bit = 1 << range;
            foreach (ref readonly ImpostorDraw draw in draws)
            {
                if ((draw.Ranges & bit) != 0)
                    scene.AppendInstance(GpuInstance.FromAxes(draw.Right, draw.Up, draw.Toward, draw.Center, (uint)draw.Tile));
            }
            scene.Impostors[range] = new InstanceRange(first, scene.InstanceCount - first);
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
    internal static Matrix4x4 CaptureProjection(Vector3 right, Vector3 up, Vector3 toward, float distance, float radius, float tanHalfAngle)
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
        return new Matrix4x4(
            right.X * lateral, -up.X * lateral, forward.X * b, forward.X * w,
            right.Y * lateral, -up.Y * lateral, forward.Y * b, forward.Y * w,
            right.Z * lateral, -up.Z * lateral, forward.Z * b, forward.Z * w,
            0, 0, a, 0);
    }

    public override void Record(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        bool billboards = scene.Impostors[DepthRanges.Far].Count + scene.Impostors[DepthRanges.Middle].Count + scene.Impostors[DepthRanges.Near].Count > 0;
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
            ClearValue = new ClearValue(depthStencil: new ClearDepthStencilValue(0f, 0)),
        };
        var extent = new Extent2D(ImpostorAtlas.TileSize, ImpostorAtlas.TileSize);
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D(new Offset2D(0, 0), extent),
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
            vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, layouts.SceneLayout, 0, 2, sets, 0, null);
            var viewport = new Viewport(0, 0, ImpostorAtlas.TileSize, ImpostorAtlas.TileSize, 0, 1);
            var scissor = new Rect2D(new Offset2D(0, 0), extent);
            vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
            vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
            GeometryPass.BindGeometry(context);
        }
        // The main view's eye (the origin), so that specular highlights are where the camera would see them.
        GeometryPass.PushDraw(context, capture.ViewProjection);
        GeometryPass.DrawIndirect(context, new CommandRange(capture.Command, 1));
        vk.CmdEndRendering(commandBuffer);

        GenerateMips(vk, commandBuffer, barriers, layer);
    }

    private static RenderingAttachmentInfo TileAttachment(ImageView view) => new()
    {
        SType = StructureType.RenderingAttachmentInfo,
        ImageView = view,
        ImageLayout = ImageLayout.ColorAttachmentOptimal,
        LoadOp = AttachmentLoadOp.Clear,
        StoreOp = AttachmentStoreOp.Store,
        // No coverage, no color, no depth offset.
        ClearValue = new ClearValue(new ClearColorValue(0f, 0f, 0f, 0f)),
    };

    private static void DiscardTile(BarrierBatch barriers, GpuImage image, uint layer)
    {
        barriers.ImageLevels(image, 0, 1, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlags2.FragmentShaderBit, AccessFlags2.None,
            PipelineStageFlags2.ColorAttachmentOutputBit, AccessFlags2.ColorAttachmentReadBit | AccessFlags2.ColorAttachmentWriteBit, layer, 1);
        barriers.ImageLevels(image, 1, ImpostorAtlas.MipLevels - 1, ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
            PipelineStageFlags2.FragmentShaderBit, AccessFlags2.None, PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit, layer, 1);
    }

    /// <summary>
    /// Rebuilds one tile's mip chain in both arrays from level 0 with linear blits (a box filter of the premultiplied
    /// values), then returns the whole tile to the read-only layout for fragment shaders.
    /// </summary>
    private void GenerateMips(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, uint layer)
    {
        const uint levels = ImpostorAtlas.MipLevels;
        foreach (GpuImage image in Images())
        {
            barriers.ImageLevels(image, 0, 1, ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal,
                PipelineStageFlags2.ColorAttachmentOutputBit, AccessFlags2.ColorAttachmentWriteBit,
                PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferReadBit, layer, 1);
        }
        barriers.Flush(vk, commandBuffer);

        int size = ImpostorAtlas.TileSize;
        for (uint level = 1; level < levels; level++)
        {
            if (level > 1)
            {
                foreach (GpuImage image in Images())
                {
                    barriers.ImageLevels(image, level - 1, 1, ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal,
                        PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit,
                        PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferReadBit, layer, 1);
                }
                barriers.Flush(vk, commandBuffer);
            }
            int next = Math.Max(1, size / 2);
            var blit = new ImageBlit
            {
                SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level - 1, layer, 1),
                DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level, layer, 1),
            };
            blit.SrcOffsets[1] = new Offset3D(size, size, 1);
            blit.DstOffsets[1] = new Offset3D(next, next, 1);
            foreach (GpuImage image in Images())
                vk.CmdBlitImage(commandBuffer, image.Handle, ImageLayout.TransferSrcOptimal, image.Handle, ImageLayout.TransferDstOptimal, 1, &blit, Filter.Linear);
            size = next;
        }

        // Every level but the last is a transfer source now; the last is still a transfer destination.
        foreach (GpuImage image in Images())
        {
            barriers.ImageLevels(image, 0, levels - 1, ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferReadBit,
                PipelineStageFlags2.FragmentShaderBit, AccessFlags2.ShaderSampledReadBit, layer, 1);
            barriers.ImageLevels(image, levels - 1, 1, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit,
                PipelineStageFlags2.FragmentShaderBit, AccessFlags2.ShaderSampledReadBit, layer, 1);
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
        vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, layouts.ImpostorLayout, 3, 1, &set, 0, null);
        GeometryPass.PushDraw(context, context.Scene.RangeViewProjection[range], layout: layouts.ImpostorLayout);
        vk.CmdDraw(commandBuffer, 6, (uint)instances.Count, 0, (uint)instances.First);
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
