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

    /// <summary>The shadow cascades of every cascade set. Passes that shade with the far lights sample them.</summary>
    public required ShadowMap Shadows { get; init; }

    /// <summary>The local shadow atlas, or null with <see cref="NoLocalShadows"/>. Passes that shade with the near lights sample it.</summary>
    public required ShadowAtlas? ShadowAtlas { get; init; }

    /// <summary>The timestamps around the passes. Only <see cref="FrameGraph.Record"/> writes them.</summary>
    public required GpuTimer Timer { get; init; }

    /// <summary>The exposure buffer, which the passes that write the HDR image or the emissive target read.</summary>
    public required ExposureResources Exposure { get; init; }

    /// <summary>The scene environment map, or null with <see cref="NoEnvironmentMap"/>.</summary>
    public required EnvironmentMap? Environment { get; init; }

    public long Number { get; set; }

    /// <summary>
    /// The output format is UNORM, so the tone map applies the sRGB curve.
    /// </summary>
    public bool EncodeSrgb { get; set; }

    /// <summary>Set when this frame must also be copied to the capture buffer.</summary>
    public GpuBuffer? CaptureBuffer { get; set; }

    /// <summary>vkCmdDrawIndexedIndirect calls recorded this frame.</summary>
    public int IndirectDrawCalls { get; set; }

    public CommandBuffer CommandBuffer { get; set; }
    public Vk Vk => Device.Vk;
    public Extent2D Extent => History.RenderSize;

    /// <summary>Binds sets 0 to 2 of <see cref="DescriptorLayouts.SceneLayout"/> for this frame.</summary>
    public void BindSceneSets() => BindSceneSets(PipelineBindPoint.Graphics, Layouts.SceneLayout);

    /// <summary>
    /// Binds this frame's sets 0 to 2 at <paramref name="bindPoint"/> through <paramref name="layout"/>, whose first three
    /// sets must be those of <see cref="DescriptorLayouts.SceneLayout"/>.
    /// </summary>
    public unsafe void BindSceneSets(PipelineBindPoint bindPoint, PipelineLayout layout)
    {
        DescriptorSet* sets = stackalloc[] { Frame.FrameSet, Frame.TextureSet, Targets.GBufferSet };
        Vk.CmdBindDescriptorSets(
            commandBuffer: CommandBuffer,
            pipelineBindPoint: bindPoint,
            layout: layout,
            firstSet: 0,
            descriptorSetCount: 3,
            pDescriptorSets: sets,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
    }

    /// <summary>
    /// Flushes the barriers that the pass declared, then binds the compute <paramref name="pipeline"/>, this frame's sets
    /// 0 to 2 and the pass's own <paramref name="passSet"/> as set 3, through <paramref name="layout"/>, whose first three
    /// sets must be those of <see cref="DescriptorLayouts.SceneLayout"/>.
    /// </summary>
    public void BeginComputePass(Pipeline pipeline, PipelineLayout layout, DescriptorSet passSet)
    {
        Barriers.Flush(Vk, CommandBuffer);
        Vk.CmdBindPipeline(CommandBuffer, PipelineBindPoint.Compute, pipeline);
        BindSceneSets(PipelineBindPoint.Compute, layout);
        BindPassSet(PipelineBindPoint.Compute, layout, passSet);
    }

    /// <summary>Binds a pass's own <paramref name="passSet"/> as set 3 at <paramref name="bindPoint"/> through <paramref name="layout"/>.</summary>
    private unsafe void BindPassSet(PipelineBindPoint bindPoint, PipelineLayout layout, DescriptorSet passSet) =>
        Vk.CmdBindDescriptorSets(
            commandBuffer: CommandBuffer,
            pipelineBindPoint: bindPoint,
            layout: layout,
            firstSet: 3,
            descriptorSetCount: 1,
            pDescriptorSets: &passSet,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );

    /// <summary>
    /// Begins rendering into <paramref name="target"/> alone, binds a full-screen <paramref name="pipeline"/> with its one
    /// descriptor set, and sets the full viewport. The target starts at zero when <paramref name="loadOp"/> is Clear;
    /// DontCare leaves it undefined, for a pass that writes every pixel.
    /// </summary>
    public unsafe void BeginFullScreen(
        GpuImage target,
        AttachmentLoadOp loadOp,
        Pipeline pipeline,
        PipelineLayout layout,
        DescriptorSet set
    )
    {
        BeginRenderingInto([(target, loadOp)]);
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
        SetViewport(target.Extent);
    }

    /// <summary>
    /// Draws one full-screen triangle of <paramref name="pipeline"/>, whose layout is
    /// <see cref="DescriptorLayouts.SceneLayout"/>, into <paramref name="targets"/> in the order of its color attachments,
    /// with this frame's sets 0 to 2. Each target starts as its load op says: DontCare for a target whose every pixel the
    /// pipeline replaces, Load for one that it blends onto.
    /// </summary>
    public void DrawSceneFullScreen(Pipeline pipeline, ReadOnlySpan<(GpuImage Image, AttachmentLoadOp LoadOp)> targets)
    {
        BeginRenderingInto(targets);
        Vk.CmdBindPipeline(CommandBuffer, PipelineBindPoint.Graphics, pipeline);
        BindSceneSets();
        SetFullViewport();
        Vk.CmdDraw(commandBuffer: CommandBuffer, vertexCount: 3, instanceCount: 1, firstVertex: 0, firstInstance: 0);
        Vk.CmdEndRendering(CommandBuffer);
    }

    /// <summary>
    /// Draws one full-screen triangle of <paramref name="pipeline"/> as the overload above does, through
    /// <paramref name="layout"/>, whose first three sets are those of <see cref="DescriptorLayouts.SceneLayout"/>, with the
    /// pass's own <paramref name="passSet"/> as set 3.
    /// </summary>
    public void DrawSceneFullScreen(
        Pipeline pipeline,
        PipelineLayout layout,
        DescriptorSet passSet,
        ReadOnlySpan<(GpuImage Image, AttachmentLoadOp LoadOp)> targets
    )
    {
        BeginRenderingInto(targets);
        Vk.CmdBindPipeline(CommandBuffer, PipelineBindPoint.Graphics, pipeline);
        BindSceneSets(PipelineBindPoint.Graphics, layout);
        BindPassSet(PipelineBindPoint.Graphics, layout, passSet);
        SetFullViewport();
        Vk.CmdDraw(commandBuffer: CommandBuffer, vertexCount: 3, instanceCount: 1, firstVertex: 0, firstInstance: 0);
        Vk.CmdEndRendering(CommandBuffer);
    }

    /// <summary>
    /// Draws one full-screen triangle as the overload above does, after it pushes <paramref name="push"/> for the fragment
    /// stage, at offset 0 of <paramref name="layout"/>'s push range.
    /// </summary>
    public unsafe void DrawSceneFullScreen<TPush>(
        Pipeline pipeline,
        PipelineLayout layout,
        DescriptorSet passSet,
        TPush push,
        ReadOnlySpan<(GpuImage Image, AttachmentLoadOp LoadOp)> targets
    )
        where TPush : unmanaged
    {
        BeginRenderingInto(targets);
        Vk.CmdBindPipeline(CommandBuffer, PipelineBindPoint.Graphics, pipeline);
        BindSceneSets(PipelineBindPoint.Graphics, layout);
        BindPassSet(PipelineBindPoint.Graphics, layout, passSet);
        Vk.CmdPushConstants(
            commandBuffer: CommandBuffer,
            layout: layout,
            stageFlags: ShaderStageFlags.FragmentBit,
            offset: 0,
            size: (uint)sizeof(TPush),
            pValues: &push
        );
        SetFullViewport();
        Vk.CmdDraw(commandBuffer: CommandBuffer, vertexCount: 3, instanceCount: 1, firstVertex: 0, firstInstance: 0);
        Vk.CmdEndRendering(CommandBuffer);
    }

    /// <summary>
    /// Begins rendering into <paramref name="targets"/>, one to four color attachments of one size, their whole extent,
    /// each stored at the end.
    /// </summary>
    public unsafe void BeginRenderingInto(ReadOnlySpan<(GpuImage Image, AttachmentLoadOp LoadOp)> targets)
    {
        const int mostTargets = 4;
        RenderingAttachmentInfo* colors = stackalloc RenderingAttachmentInfo[mostTargets];
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targets.Length, mostTargets);
        for (int i = 0; i < targets.Length; i++)
            colors[i] = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = targets[i].Image.View,
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = targets[i].LoadOp,
                StoreOp = AttachmentStoreOp.Store,
                ClearValue = default,
            };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), targets[0].Image.Extent),
            LayerCount = 1,
            ColorAttachmentCount = (uint)targets.Length,
            PColorAttachments = colors,
        };
        Vk.CmdBeginRendering(CommandBuffer, &rendering);
    }

    /// <summary>Sets a viewport and scissor that cover the whole render target.</summary>
    public void SetFullViewport() => SetViewport(Extent);

    /// <summary>Sets a viewport, of depth 0 to 1, and a scissor that cover a target of <paramref name="extent"/> from its corner.</summary>
    public unsafe void SetViewport(Extent2D extent)
    {
        var viewport = new Viewport(x: 0, y: 0, width: extent.Width, height: extent.Height, minDepth: 0, maxDepth: 1);
        var scissor = new Rect2D(new Offset2D(0, 0), extent);
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

    /// <summary>
    /// The frame's pending state of the frame history (<see cref="FrameHistory.Begin"/>): its reset, and the parity of the
    /// drawn frames, which picks the kept positions and the surface history that the frame writes.
    /// </summary>
    public FrameHistory.PendingFrame History { get; set; } = null!;

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
/// they were added. The renderer fills <see cref="Stage.Deformation"/> (<see cref="DeformPass"/>, under
/// <see cref="RendererOptions.Deformation"/>), <see cref="Stage.ShadowMaps"/> (<see cref="ShadowPass"/>, then
/// <see cref="LocalShadowPass"/> under <see cref="LocalShadowAtlas"/>), the environment map's
/// stages (<see cref="EnvironmentFacePass"/> and <see cref="EnvironmentPrefilterPass"/>, under
/// <see cref="SceneEnvironmentMap"/>), <see cref="Stage.ImpostorCaptures"/> (<see cref="ImpostorPass"/>, when impostors are on) and
/// <see cref="Stage.GBuffer"/> through <see cref="Stage.Readback"/>, the screen tracer's passes and the depth pyramid
/// (under <see cref="ScreenTrace"/>), the skin scatter (<see cref="SkinScatterPass"/>, under
/// <see cref="SeparableSubsurface"/>), the exposure pass
/// (<see cref="ExposurePass"/>, under <see cref="AutoExposure"/>), FSR and the overlay (<see cref="OutlinePass"/>) included. Draws
/// that belong inside the G-buffer pass's depth ranges, such as impostor billboards, go through
/// <see cref="GeometryPass.RangeDrawers"/>.
/// </summary>
internal sealed class FrameGraph : IDisposable
{
    private readonly List<(Stage Stage, int Order, RenderPass Pass)> passes = [];

    public IEnumerable<RenderPass> Passes => passes.Select(p => p.Pass);

    /// <summary>
    /// The names of the passes that <see cref="Record"/> leaves out. A test hook: a test reads an image as an earlier pass
    /// left it, such as the near estimate before the denoise.
    /// </summary>
    public HashSet<string> Skipped { get; } = [];

    public enum Stage
    {
        /// <summary>
        /// The deform pass, under <see cref="RendererOptions.Deformation"/>: first, because every pass that draws a mesh
        /// draws the deformed copies that it writes (docs/plans/skinning-and-morphs.md, "Barriers and the frame graph").
        /// </summary>
        Deformation,
        ShadowMaps,

        /// <summary>The environment map's faces of this frame, after the shadow maps, because a face samples this frame's cascades.</summary>
        EnvironmentFaces,

        /// <summary>
        /// The specular map and the irradiance cube, after the faces, because they derive from the faces of this frame, and
        /// before every reader of the map, the impostor captures first.
        /// </summary>
        EnvironmentPrefilter,
        ImpostorCaptures,
        GBuffer,

        /// <summary>
        /// The surface test: right after the G-buffer pass, whose distance, normals, roughness and motion target it reads,
        /// and before the temporal passes, which read its pass bits (docs/plans/temporal-renderer.md, "The frame's order").
        /// </summary>
        SurfaceTest,

        /// <summary>
        /// The depth pyramid, under <see cref="ScreenTrace"/>: right after the G-buffer pass, because it describes the
        /// G-buffer's distance, so every screen-space reader after it can use it.
        /// </summary>
        DepthPyramid,

        /// <summary>
        /// The direct pass: after the G-buffer pass and the shadow maps, which it reads. It writes the direct light's two
        /// images (docs/plans/temporal-renderer.md, "The frame's order").
        /// </summary>
        DirectLight,

        /// <summary>
        /// The screen tracer's four passes and the two indirect accumulations, under <see cref="ScreenTrace"/>: after the
        /// direct pass, whose images a hit reads, and after the surface test, whose pass bits the plan and a hit's history
        /// read. Each accumulation follows the resolve, which writes its sample (docs/plans/temporal-renderer.md, "The
        /// frame's order").
        /// </summary>
        ScreenTrace,

        /// <summary>
        /// The composite: after the direct pass and the screen tracer's accumulations, whose images it reads, into the HDR
        /// image.
        /// </summary>
        Composite,

        /// <summary>
        /// The skin scatter's two passes, under <see cref="SeparableSubsurface"/>: after the composite, so that the
        /// scattered light holds the indirect light (docs/plans/skin-and-hair.md, decision 18). A tracer hit reads the
        /// histories before this scatter, so reflected skin has no scatter yet (ter-x8bz).
        /// </summary>
        SkinScatter,

        /// <summary>
        /// The haze of the atmosphere over the opaque surfaces: after the composite and the scatter, so that it covers
        /// each surface's whole light, and before the transparent pass, whose panes and puffs take their own haze over the hazed image
        /// (docs/plans/atmosphere.md, "The haze").
        /// </summary>
        Haze,
        Transparent,

        /// <summary>The exposure's meter and adapt step, under <see cref="AutoExposure"/>: after every writer of the HDR image, before the tone map reads it.</summary>
        Exposure,
        Upscale,
        ExposureAdapt,

        /// <summary>
        /// After every pass that writes the HDR image, so that all bright light glows, and before the tone map, which adds the
        /// glow. After the exposure pass, because both only read the HDR image, and this order changes its use from the
        /// meter's compute read to the fragment reads of the bloom and the tone map once.
        /// </summary>
        Bloom,
        ToneMap,

        /// <summary>Debug drawing over the output image, such as frame outlines, before the readback so a capture holds it.</summary>
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

    /// <summary>
    /// Records every pass in order, with a GPU timestamp before the first pass and after each pass, or without pass
    /// timestamps after the last pass of each cost group of the render scale.
    /// </summary>
    public void Record(FrameContext context)
    {
        int slot = context.Frame.Index;
        context.Timer.Begin(context.CommandBuffer, slot);
        bool output = false;
        for (int index = 0; index < passes.Count; index++)
        {
            (Stage stage, _, RenderPass pass) = passes[index];
            if (!output && stage >= Stage.ToneMap)
            {
                BeginOutput(context);
                output = true;
            }
            if (Skipped.Contains(pass.Name))
                continue;
            context.Device.BeginLabel(context.CommandBuffer, pass.Name);
            pass.Record(context);
            context.Device.EndLabel(context.CommandBuffer);
            RenderScaleController.CostGroup group = RenderScaleController.CostGroupOf(stage);
            if (context.Timer.EachPass)
                context.Timer.After(
                    commandBuffer: context.CommandBuffer,
                    slot: slot,
                    pass: new(pass.Name, 0) { Stage = stage }
                );
            else if (NextRecordedGroup(index) != group)
                context.Timer.After(
                    commandBuffer: context.CommandBuffer,
                    slot: slot,
                    pass: new(RenderScaleController.NameOf(group), 0) { Stage = stage }
                );
        }
    }

    /// <summary>The cost group of the first pass after <paramref name="index"/> that records, or null after the last.</summary>
    private RenderScaleController.CostGroup? NextRecordedGroup(int index)
    {
        for (int next = index + 1; next < passes.Count; next++)
            if (!Skipped.Contains(passes[next].Pass.Name))
                return RenderScaleController.CostGroupOf(passes[next].Stage);
        return null;
    }

    private static unsafe void BeginOutput(FrameContext context)
    {
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        context.Vk.EndCommandBuffer(context.CommandBuffer).Check("vkEndCommandBuffer");
        context.CommandBuffer = context.Frame.OutputCommandBuffer;
        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        context.Vk.BeginCommandBuffer(context.CommandBuffer, &begin).Check("vkBeginCommandBuffer");
    }

    public void Dispose()
    {
        foreach ((_, _, RenderPass pass) in passes)
            pass.Dispose();
        passes.Clear();
    }
}
