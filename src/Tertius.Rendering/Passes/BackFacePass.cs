using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// THROWAWAY (ter-kh4g, stage 1): the cost probe of a back-face depth layer. After the G-buffer pass, it draws the back
/// faces of the one-sided opaque runs of each depth range into <see cref="RenderTargets.BackDistance"/>, with the
/// G-buffer's depth image and with front faces culled. It keeps the nearest back face behind each pixel's front surface,
/// and writes the front surface's distance over the back face's. Nothing reads the image. The cutout run draws as solid,
/// so the probe runs no alpha test. Only <c>TERTIUS_BACK_FACE_PROBE=on</c> records the pass.
/// </summary>
internal sealed unsafe class BackFacePass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;

    public BackFacePass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts)
    {
        this.device = device;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "back faces (throwaway)",
            VertexShader = "back_face.vert",
            FragmentShader = "back_face.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [RenderTargets.DistanceFormat],
            DepthFormat = RenderTargets.DepthFormat,
            VertexInput = VertexInput.PositionOnly,
            DepthTest = true,
            DepthWrite = true,
            DepthCompare = CompareOp.Greater,
            CullMode = CullModeFlags.FrontBit,
        }.Create(device, shaders);
    }

    public override string Name => "back faces";

    /// <summary>The probe's mode without <c>TERTIUS_BACK_FACE_PROBE</c>. A timing job on a cloud worker flips it in a
    /// scratch commit, because the job's runs share one environment.</summary>
    private const string DefaultMode = "off";

    /// <summary>Whether the probe records its pass: under <c>TERTIUS_BACK_FACE_PROBE=on</c>, and off without it, so
    /// that every test and every other run draws main's frame.</summary>
    /// <exception cref="ArgumentException">The variable holds another value.</exception>
    public static bool SwitchedOn() =>
        (Environment.GetEnvironmentVariable("TERTIUS_BACK_FACE_PROBE") is { Length: > 0 } mode ? mode : DefaultMode) switch
        {
            "on" => true,
            "off" => false,
            var value => throw new ArgumentException(
                $"Unknown TERTIUS_BACK_FACE_PROBE value '{value}': use on or off."
            ),
        };

    public override void Record(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            BeginRange(context, range);
            if (range == DepthRanges.Far)
            {
                context.BindSceneSets();
                context.SetFullViewport();
                GeometryPass.BindGeometry(context);
                context.Vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Graphics, pipeline);
            }

            OpaqueRuns runs = scene.Opaque[range].OneSided;
            if (runs.Count > 0)
            {
                GeometryPass.PushDraw(context: context, viewProjection: scene.RangeViewProjection[range]);
                if (runs.Solid.Count > 0)
                    GeometryPass.DrawIndirect(context, runs.Solid);
                if (runs.Cutout.Count > 0)
                    GeometryPass.DrawIndirect(context, runs.Cutout);
            }

            context.Vk.CmdEndRendering(context.CommandBuffer);
        }
    }

    /// <summary>
    /// Begins the rendering instance of <paramref name="range"/>: clears the target before the far range and the depth
    /// before every range, as the G-buffer pass does, so that a nearer range draws over a farther one.
    /// </summary>
    private static void BeginRange(FrameContext context, int range)
    {
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        bool first = range == DepthRanges.Far;
        barriers.Image(targets.BackDistance, ImageUse.ColorAttachment, discard: first);
        barriers.Image(targets.Depth, ImageUse.DepthAttachment, discard: true);
        barriers.Image(targets.Distance, ImageUse.SampledFragment);
        barriers.Flush(context.Vk, context.CommandBuffer);
        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = targets.BackDistance.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = first ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            // Share 0: no back face.
            ClearValue = new(new ClearColorValue(float32_0: 0f, float32_1: 0f, float32_2: 0f, float32_3: 0f)),
        };
        var depth = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = targets.Depth.View,
            ImageLayout = ImageLayout.DepthAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            ClearValue = new(depthStencil: new ClearDepthStencilValue(DepthRanges.ClearDepth(range), 0)),
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), targets.Extent),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
            PDepthAttachment = &depth,
        };
        context.Vk.CmdBeginRendering(context.CommandBuffer, &rendering);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
