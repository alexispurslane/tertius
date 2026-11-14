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
/// farther ones. Every range draws its solid runs and then its cutout runs, each one vkCmdDrawIndexedIndirect, followed
/// by the <see cref="RangeDrawers"/>. A range's depth is cleared to the depth of its far boundary, which cuts off what lies
/// beyond it (see <see cref="DepthRanges"/>).
/// </summary>
internal sealed unsafe class GeometryPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly KeptPositions? kept;
    private readonly SidedPipeline pipeline;
    private readonly SidedPipeline cutoutPipeline;

    /// <param name="kept">The kept deformed positions, whose last frame's buffer the motion reads, or null without the deformation.</param>
    public GeometryPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants,
        KeptPositions? kept
    )
    {
        this.device = device;
        this.kept = kept;
        ShaderConstants commonConstants = constants with { WritesMotion = true, MainView = true };
        var desc = new GraphicsPipelineDesc
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
            Constants = commonConstants,
        };
        pipeline = SidedPipeline.Create(device: device, shaders: shaders, desc: desc);
        try
        {
            cutoutPipeline = SidedPipeline.Create(
                device: device,
                shaders: shaders,
                desc: desc with
                {
                    Name = "g-buffer cutout",
                    Constants = commonConstants with { CutoutAlpha = true },
                }
            );
        }
        catch
        {
            pipeline.Destroy(device);
            throw;
        }
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
        // After the last drawn frame's deform pass wrote them, which may have no job this frame.
        if (kept?.Read(context.History.Parity) is { } lastPositions)
            barriers.Buffer(lastPositions, BufferUse.ShaderRead);

        RenderingAttachmentInfo* colors = stackalloc RenderingAttachmentInfo[targets.GBuffer.Length];
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            bool first = range == DepthRanges.Far;
            BeginRange(context: context, range: range, colors: colors);
            // Sets and dynamic state stay bound across the ranges, and the runs bind their own pipelines, as a range
            // drawer may.
            if (first)
            {
                context.BindSceneSets();
                context.SetFullViewport();
                BindGeometry(context);
            }

            SidedRuns runs = scene.Opaque[range];
            if (runs.Count > 0)
            {
                PushDraw(context: context, viewProjection: scene.RangeViewProjection[range]);
                DrawRuns(context: context, runs: runs, solid: pipeline, cutout: cutoutPipeline);
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
                // Zero everywhere: distance 0 marks sky for the direct and ambient passes, and the shading target's code 0
                // the standard model. A float 0 has the bits of an integer 0, which the integer target takes.
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
    /// </summary>
    public static void PushDraw(
        FrameContext context,
        Matrix4x4 viewProjection,
        Vector3 eye = default,
        PipelineLayout layout = default
    )
    {
        var push = new DrawPushConstants { ViewProj = viewProjection, Eye = new(eye, 1f) };
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layout.Handle != 0 ? layout : context.Layouts.SceneLayout,
            stageFlags: ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            offset: 0,
            size: (uint)sizeof(DrawPushConstants),
            pValues: &push
        );
    }

    /// <summary>
    /// Draws one view's opaque runs: the solid run with <paramref name="solid"/>, then the cutout run with
    /// <paramref name="cutout"/>, binding each pipeline before its run when the run has commands. Both pipelines share the
    /// scene layout, so the sets and the push constants stay bound.
    /// </summary>
    public static void DrawRuns(FrameContext context, OpaqueRuns runs, Pipeline solid, Pipeline cutout)
    {
        // The solid run first, so that its early depth writes hide the cutout fragments behind it before their shader
        // runs (docs/plans/skin-and-hair.md, decision 25).
        DrawRun(context: context, commands: runs.Solid, pipeline: solid);
        DrawRun(context: context, commands: runs.Cutout, pipeline: cutout);
    }

    /// <summary>
    /// Draws one view's opaque runs of a pass that shades: both solid runs with the variants of <paramref name="solid"/>,
    /// then both cutout runs with those of <paramref name="cutout"/>, as <see cref="DrawRuns(FrameContext, OpaqueRuns, Pipeline, Pipeline)"/> does.
    /// </summary>
    public static void DrawRuns(FrameContext context, SidedRuns runs, SidedPipeline solid, SidedPipeline cutout)
    {
        // Both solid runs first, for the early depth writes of decision 25.
        DrawRun(context: context, commands: runs.OneSided.Solid, pipeline: solid.OneSided);
        DrawRun(context: context, commands: runs.TwoSided.Solid, pipeline: solid.TwoSided);
        DrawRun(context: context, commands: runs.OneSided.Cutout, pipeline: cutout.OneSided);
        DrawRun(context: context, commands: runs.TwoSided.Cutout, pipeline: cutout.TwoSided);
    }

    /// <summary>Binds <paramref name="pipeline"/> and draws <paramref name="commands"/>, when they are any.</summary>
    private static void DrawRun(FrameContext context, CommandRange commands, Pipeline pipeline)
    {
        if (commands.Count == 0)
            return;
        context.Vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Graphics, pipeline);
        DrawIndirect(context, commands);
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

    public override void Dispose()
    {
        pipeline.Destroy(device);
        cutoutPipeline.Destroy(device);
    }
}
