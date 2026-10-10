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

    public FrameResources Frame { get; set; } = null!;
    /// <summary>The snapshot being drawn, unculled. A shadow pass selects casters from it, including ones outside the view.</summary>
    public RenderSnapshot Snapshot { get; set; } = null!;
    /// <summary>The interpolation factor of this frame.</summary>
    public float Alpha { get; set; }
    public PreparedScene Scene { get; set; } = null!;
    /// <summary>The final image: a swapchain image or the headless output.</summary>
    public GpuImage Output { get; set; } = null!;
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
        Vk.CmdBindDescriptorSets(CommandBuffer, PipelineBindPoint.Graphics, Layouts.SceneLayout, 0, 3, sets, 0, null);
    }

    /// <summary>Sets a viewport and scissor that cover the whole render target.</summary>
    public unsafe void SetFullViewport()
    {
        var viewport = new Viewport(0, 0, Extent.Width, Extent.Height, 0, 1);
        var scissor = new Rect2D(new Offset2D(0, 0), Extent);
        Vk.CmdSetViewport(CommandBuffer, 0, 1, &viewport);
        Vk.CmdSetScissor(CommandBuffer, 0, 1, &scissor);
    }
}

/// <summary>
/// One step of the frame, in two phases. <see cref="Prepare"/> runs on the CPU after batching and before the frame's
/// buffers are written, so a pass can append its own GPU instances and indirect commands to
/// <see cref="FrameContext.Scene"/> (<see cref="PreparedScene.AppendInstance"/>, <see cref="PreparedScene.AppendCommand"/>).
/// <see cref="Record"/> then records GPU work. Passes declare the images and buffers they touch through
/// <see cref="FrameContext.Barriers"/> before they record, so the barriers between passes come from the uses.
/// </summary>
internal abstract class RenderPass : IDisposable
{
    public abstract string Name { get; }

    /// <summary>CPU work before the frame's instance and indirect buffers are written. Optional.</summary>
    public virtual void Prepare(FrameContext context)
    {
    }

    /// <summary>Records this pass into <see cref="FrameContext.CommandBuffer"/>.</summary>
    public abstract void Record(FrameContext context);

    public virtual void Dispose()
    {
    }
}

/// <summary>
/// The passes of a frame in a fixed order of stages. Each stage holds any number of passes, recorded in the order
/// they were added. The renderer fills <see cref="Stage.ShadowMaps"/> (<see cref="ShadowPass"/>),
/// <see cref="Stage.ImpostorCaptures"/> (<see cref="ImpostorPass"/>, when impostors are on) and
/// <see cref="Stage.GBuffer"/> through <see cref="Stage.Readback"/>. Draws that belong inside the G-buffer pass's
/// depth ranges, such as impostor billboards, go through <see cref="GeometryPass.RangeDrawers"/>.
/// </summary>
internal sealed class FrameGraph : IDisposable
{
    public enum Stage
    {
        ShadowMaps,
        ImpostorCaptures,
        GBuffer,
        Lighting,
        Transparent,
        ToneMap,
        Readback,
    }

    private readonly List<(Stage Stage, int Order, RenderPass Pass)> passes = new();

    public IEnumerable<RenderPass> Passes => passes.Select(p => p.Pass);

    public void Add(Stage stage, RenderPass pass)
    {
        passes.Add((stage, passes.Count, pass));
        passes.Sort((a, b) => a.Stage != b.Stage ? a.Stage.CompareTo(b.Stage) : a.Order.CompareTo(b.Order));
    }

    public void Prepare(FrameContext context)
    {
        foreach ((_, _, RenderPass pass) in passes)
            pass.Prepare(context);
    }

    public void Record(FrameContext context)
    {
        foreach ((_, _, RenderPass pass) in passes)
        {
            context.Device.BeginLabel(context.CommandBuffer, pass.Name);
            pass.Record(context);
            context.Device.EndLabel(context.CommandBuffer);
        }
    }

    public void Dispose()
    {
        foreach ((_, _, RenderPass pass) in passes)
            pass.Dispose();
        passes.Clear();
    }
}
