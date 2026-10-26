using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Tertius.Rendering;

/// <summary>
/// Draws that another pass adds to every depth range of the G-buffer pass, inside the range's rendering instance and
/// after its meshes, such as impostor billboards. The drawer may bind its own pipeline.
/// </summary>
internal interface IRangeDrawer
{
    void DrawRange(FrameContext context, int range);
}

/// <summary>
/// Fills the G-buffer: the far, middle and near depth ranges in that order, each with its own projection and a
/// freshly cleared depth buffer. The G-buffer is cleared once, before the far range, so nearer ranges draw over
/// farther ones. Every range is one vkCmdDrawIndexedIndirect over that range's commands, followed by the
/// <see cref="RangeDrawers"/>. A range's depth is cleared to the depth of its far boundary, which cuts off what lies
/// beyond it (see <see cref="DepthRanges"/>).
/// </summary>
internal sealed unsafe class GeometryPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;

    public GeometryPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants
    )
    {
        this.device = device;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "g-buffer",
            VertexShader = "mesh.vert",
            FragmentShader = "gbuffer.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = RenderTargets.GBufferFormats,
            DepthFormat = RenderTargets.DepthFormat,
            VertexInput = VertexInput.Mesh,
            DepthTest = true,
            DepthWrite = true,
            DepthCompare = CompareOp.Greater,
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name => "G-buffer";

    /// <summary>Called in every range after the meshes, in order. See <see cref="IRangeDrawer"/>.</summary>
    public List<IRangeDrawer> RangeDrawers { get; } = [];

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        PreparedScene scene = context.Scene;

        barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Materials.Buffer, BufferUse.ShaderRead);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);

        RenderingAttachmentInfo* colors = stackalloc RenderingAttachmentInfo[targets.GBuffer.Length];
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            bool first = range == DepthRanges.Far;
            BeginRange(context: context, range: range, colors: colors);
            // Every range: a range drawer may have bound its own pipeline. Sets and dynamic state stay.
            vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
            if (first)
            {
                context.BindSceneSets();
                context.SetFullViewport();
                BindGeometry(context);
            }

            CommandRange commands = scene.Opaque[range];
            if (commands.Count > 0)
            {
                PushDraw(context: context, viewProjection: scene.RangeViewProjection[range]);
                DrawIndirect(context, commands);
            }

            foreach (IRangeDrawer drawer in RangeDrawers)
                drawer.DrawRange(context, range);
            vk.CmdEndRendering(commandBuffer);
        }
    }

    /// <summary>
    /// Begins the rendering instance of <paramref name="range"/>: clears the G-buffer before the far range and the depth
    /// before every range. <paramref name="colors"/> has room for one attachment per G-buffer target.
    /// </summary>
    private static void BeginRange(FrameContext context, int range, RenderingAttachmentInfo* colors)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        GpuImage[] gbuffer = targets.GBuffer;
        bool first = range == DepthRanges.Far;
        foreach (GpuImage image in gbuffer)
            barriers.Image(image, ImageUse.ColorAttachment, discard: first);
        barriers.Image(targets.Depth, ImageUse.DepthAttachment, discard: true);
        barriers.Flush(vk, commandBuffer);

        for (int i = 0; i < gbuffer.Length; i++)
            colors[i] = new()
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = gbuffer[i].View,
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = first ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                StoreOp = AttachmentStoreOp.Store,
                // Zero everywhere: distance 0 marks sky for the lighting pass.
                ClearValue = new(new ClearColorValue(float32_0: 0f, float32_1: 0f, float32_2: 0f, float32_3: 0f)),
            };
        var depth = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = targets.Depth.View,
            ImageLayout = ImageLayout.DepthAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            // The next range clears depth again and later passes use the G-buffer distance instead.
            StoreOp = AttachmentStoreOp.DontCare,
            ClearValue = new(depthStencil: new ClearDepthStencilValue(DepthRanges.ClearDepth(range), 0)),
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), targets.Extent),
            LayerCount = 1,
            ColorAttachmentCount = (uint)gbuffer.Length,
            PColorAttachments = colors,
            PDepthAttachment = &depth,
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);
    }

    /// <summary>Binds the global vertex and index buffers. Pipeline and binding state survive across rendering instances.</summary>
    public static void BindGeometry(FrameContext context)
    {
        ulong offset = 0;
        Buffer vertexBuffer = context.Geometry.VertexBuffer.Handle;
        context.Vk.CmdBindVertexBuffers(
            commandBuffer: context.CommandBuffer,
            firstBinding: 0,
            bindingCount: 1,
            pBuffers: &vertexBuffer,
            pOffsets: &offset
        );
        context.Vk.CmdBindIndexBuffer(
            commandBuffer: context.CommandBuffer,
            buffer: context.Geometry.IndexBuffer.Handle,
            offset: 0,
            indexType: IndexType.Uint32
        );
    }

    /// <summary>
    /// Sets the mesh-pass push constants. The main view has its eye at the origin; a shadow cascade or an impostor
    /// capture passes its own matrix. <paramref name="layout"/> defaults to <see cref="DescriptorLayouts.SceneLayout"/>.
    /// An environment face passes its <paramref name="map"/>, whose lights it keeps; no other pass reads it.
    /// </summary>
    public static void PushDraw(
        FrameContext context,
        Matrix4x4 viewProjection,
        Vector3 eye = default,
        PipelineLayout layout = default,
        int map = 0
    )
    {
        var push = new DrawPushConstants
        {
            ViewProj = viewProjection,
            Eye = new(eye, 1f),
            Map = (uint)map,
        };
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layout.Handle != 0 ? layout : context.Layouts.SceneLayout,
            stageFlags: ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            offset: 0,
            size: (uint)sizeof(DrawPushConstants),
            pValues: &push
        );
    }

    /// <summary>One multi-draw over a run of this frame's indirect commands.</summary>
    public static void DrawIndirect(FrameContext context, CommandRange commands)
    {
        uint stride = (uint)Unsafe.SizeOf<DrawIndexedIndirectCommand>();
        context.Vk.CmdDrawIndexedIndirect(
            commandBuffer: context.CommandBuffer,
            buffer: context.Frame.Commands.Buffer.Handle,
            offset: (ulong)commands.First * stride,
            drawCount: (uint)commands.Count,
            stride: stride
        );
        context.IndirectDrawCalls++;
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
