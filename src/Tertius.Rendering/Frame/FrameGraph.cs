using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>Everything a pass needs to record one frame.</summary>
internal sealed class FrameContext
{
    public required VulkanDevice Device { get; init; }
    public required DescriptorLayouts Layouts { get; init; }
    public required RenderTargets Targets { get; init; }
    public required GeometryStore Geometry { get; init; }
    public required MaterialStore Materials { get; init; }
    public required BarrierBatch Barriers { get; init; }

    /// <summary>The sun shadow cascades. Passes that shade with the sun sample them.</summary>
    public required ShadowMap Shadows { get; init; }

    /// <summary>The timestamps around the passes. Only <see cref="FrameGraph.Record"/> writes them.</summary>
    public required GpuTimer Timer { get; init; }

    public long Number { get; set; }
    public float Exposure { get; set; } = 1f;

    /// <summary>The output format stores linear values and the tone mapper must apply the sRGB curve.</summary>
    public bool EncodeSrgb { get; set; }

    /// <summary>Set when this frame must also be copied to the capture buffer.</summary>
    public GpuBuffer? CaptureBuffer { get; set; }

    /// <summary>vkCmdDrawIndexedIndirect calls recorded this frame.</summary>
    public int IndirectDrawCalls { get; set; }

    public CommandBuffer CommandBuffer => Frame.CommandBuffer;
    public Vk Vk => Device.Vk;
    public Extent2D Extent => Targets.Extent;

    /// <summary>Binds sets 0 to 2 of <see cref="DescriptorLayouts.SceneLayout"/> for this frame.</summary>
    public unsafe void BindSceneSets()
    {
        DescriptorSet* sets = stackalloc[] { Frame.FrameSet, Frame.TextureSet, Targets.GBufferSet };
        Vk.CmdBindDescriptorSets(
            commandBuffer: CommandBuffer,
            pipelineBindPoint: PipelineBindPoint.Graphics,
            layout: Layouts.SceneLayout,
            firstSet: 0,
            descriptorSetCount: 3,
            pDescriptorSets: sets,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
    }

    /// <summary>
    /// Begins rendering into <paramref name="target"/> alone, which a clear sets to zero, binds a full-screen
    /// <paramref name="pipeline"/> with its one descriptor set, and sets the full viewport.
    /// </summary>
    public unsafe void BeginFullScreen(
        GpuImage target,
        AttachmentLoadOp loadOp,
        Pipeline pipeline,
        PipelineLayout layout,
        DescriptorSet set
    )
    {
        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = target.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = loadOp,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = default,
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), target.Extent),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
        };
        Vk.CmdBeginRendering(CommandBuffer, &rendering);
        Vk.CmdBindPipeline(CommandBuffer, PipelineBindPoint.Graphics, pipeline);
        Vk.CmdBindDescriptorSets(
            commandBuffer: CommandBuffer,
            pipelineBindPoint: PipelineBindPoint.Graphics,
            layout: layout,
            firstSet: 0,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        SetFullViewport();
    }

    /// <summary>Sets a viewport and scissor that cover the whole render target.</summary>
    public unsafe void SetFullViewport()
    {
        var viewport = new Viewport(x: 0, y: 0, width: Extent.Width, height: Extent.Height, minDepth: 0, maxDepth: 1);
        var scissor = new Rect2D(new Offset2D(0, 0), Extent);
        Vk.CmdSetViewport(commandBuffer: CommandBuffer, firstViewport: 0, viewportCount: 1, pViewports: &viewport);
        Vk.CmdSetScissor(commandBuffer: CommandBuffer, firstScissor: 0, scissorCount: 1, pScissors: &scissor);
    }

    // The renderer keeps one context for its life and sets the values of each frame before the frame graph runs, so no
    // pass reads them before the first frame. A frame has no values to give at the creation of the context, and a new
    // context per frame would allocate on the hot path.
#pragma warning disable TS0003
    public FrameResources Frame { get; set; } = null!;

    /// <summary>The snapshot being drawn, unculled. A shadow pass selects casters from it, including ones outside the view.</summary>
    public RenderSnapshot Snapshot { get; set; } = null!;

    /// <summary>The interpolation factor of this frame.</summary>
    public float Alpha { get; set; }

    public PreparedScene Scene { get; set; } = null!;

    /// <summary>The final image: a swapchain image or the headless output.</summary>
    public GpuImage Output { get; set; } = null!;
#pragma warning restore TS0003
}

/// <summary>
/// One step of the frame, in two phases. <see cref="Prepare"/> runs on the CPU after batching and before the frame's
/// buffers are written, so a pass can append its own GPU instances and indirect commands to
/// <see cref="FrameContext.Scene"/> (<see cref="PreparedScene.AppendInstance"/>, <see cref="PreparedScene.AppendCommand"/>
/// ).
/// <see cref="Record"/> then records GPU work. Passes declare the images and buffers they touch through
/// <see cref="FrameContext.Barriers"/> before they record, so the barriers between passes come from the uses.
/// </summary>
internal abstract class RenderPass : IDisposable
{
    public abstract string Name { get; }

    /// <summary>CPU work before the frame's instance and indirect buffers are written. Optional.</summary>
    public virtual void Prepare(FrameContext context) { }

    /// <summary>Records this pass into <see cref="FrameContext.CommandBuffer"/>.</summary>
    public abstract void Record(FrameContext context);

    public virtual void Dispose() { }
}

/// <summary>
/// The passes of a frame in a fixed order of stages. Each stage holds any number of passes, recorded in the order
/// they were added. The renderer fills <see cref="Stage.ShadowMaps"/> (<see cref="ShadowPass"/>),
/// <see cref="Stage.ImpostorCaptures"/> (<see cref="ImpostorPass"/>, when impostors are on) and
/// <see cref="Stage.GBuffer"/> through <see cref="Stage.Readback"/>, SMAA (<see cref="SmaaPass"/>, when it is on) and the
/// overlay (<see cref="OutlinePass"/>) included. Draws
/// that belong inside the G-buffer pass's depth ranges, such as impostor billboards, go through
/// <see cref="GeometryPass.RangeDrawers"/>.
/// </summary>
internal sealed class FrameGraph : IDisposable
{
    private readonly List<(Stage Stage, int Order, RenderPass Pass)> passes = [];

    public IEnumerable<RenderPass> Passes => passes.Select(p => p.Pass);

    public enum Stage
    {
        ShadowMaps,
        ImpostorCaptures,
        GBuffer,
        Lighting,
        Transparent,
        ToneMap,

        /// <summary>
        /// The three SMAA passes, when SMAA is on. They follow the tone map, because SMAA finds edges in the display-encoded
        /// image, and precede the overlay, so that they never blend its outlines.
        /// </summary>
        AntiAliasing,

        /// <summary>Debug drawing over the tone-mapped image, such as frame outlines, before the readback so a capture holds it.</summary>
        Overlay,
        Readback,
    }

    /// <exception cref="InvalidOperationException">The graph already holds <see cref="GpuTimer.MaxPasses"/> passes.</exception>
    public void Add(Stage stage, RenderPass pass)
    {
        if (passes.Count == GpuTimer.MaxPasses)
            throw new InvalidOperationException($"A frame graph holds at most {GpuTimer.MaxPasses} passes.");
        passes.Add((stage, passes.Count, pass));
        passes.Sort((a, b) => a.Stage != b.Stage ? a.Stage.CompareTo(b.Stage) : a.Order.CompareTo(b.Order));
    }

    public void Prepare(FrameContext context)
    {
        foreach ((_, _, RenderPass pass) in passes)
            pass.Prepare(context);
    }

    /// <summary>Records every pass in order, with a GPU timestamp before the first pass and after each pass.</summary>
    public void Record(FrameContext context)
    {
        int slot = context.Frame.Index;
        context.Timer.Begin(context.CommandBuffer, slot);
        foreach ((_, _, RenderPass pass) in passes)
        {
            context.Device.BeginLabel(context.CommandBuffer, pass.Name);
            pass.Record(context);
            context.Device.EndLabel(context.CommandBuffer);
            context.Timer.After(commandBuffer: context.CommandBuffer, slot: slot, pass: pass.Name);
        }
    }

    public void Dispose()
    {
        foreach ((_, _, RenderPass pass) in passes)
            pass.Dispose();
        passes.Clear();
    }
}
