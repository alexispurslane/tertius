using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// After the G-buffer pass and before the depth pyramid: writes the back share of each pixel into
/// <see cref="RenderTargets.BackShare"/> (docs/plans/back-face-depth-layer.md, decision 4). It draws the G-buffer's
/// indirect commands of each depth range, far to near, with the range's jittered matrix and a cleared depth, and keeps
/// the nearest fragment that counts behind the pixel's front surface.
/// </summary>
internal sealed unsafe class BackFacePass : RenderPass
{
    private readonly VulkanDevice device;

    /// <summary>The pipelines of the closed one-sided runs, which cull front faces.</summary>
    private readonly RunPipelines closedOneSided;

    /// <summary>The pipelines of the runs whose both faces the pass draws, which cull no face.</summary>
    private readonly RunPipelines bothFaces;

    public BackFacePass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts)
    {
        this.device = device;
        // The main view's mip bias, so that the cutout test keeps the texels that the G-buffer keeps.
        var constants = new ShaderConstants(EnvironmentMap: false) { MainView = true };
        var desc = new GraphicsPipelineDesc
        {
            Name = "back faces, closed one-sided",
            VertexShader = "back_face.vert",
            FragmentShader = "back_face.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [RenderTargets.BackShareFormat],
            DepthFormat = RenderTargets.DepthFormat,
            VertexInput = VertexInput.PositionOnly,
            DepthTest = true,
            DepthWrite = true,
            DepthCompare = CompareOp.Greater,
            CullMode = CullModeFlags.FrontBit,
            Constants = constants,
        };
        closedOneSided = RunPipelines.Create(device: device, shaders: shaders, desc: desc);
        try
        {
            bothFaces = RunPipelines.Create(
                device: device,
                shaders: shaders,
                desc: desc with
                {
                    Name = "back faces, both faces",
                    CullMode = CullModeFlags.None,
                    Constants = constants with { BothFaces = true },
                }
            );
        }
        catch
        {
            closedOneSided.Destroy(device);
            throw;
        }
    }

    public override string Name => "back faces";

    public override void Record(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        BarrierBatch barriers = context.Barriers;
        barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Materials.Buffer, BufferUse.ShaderRead);
        bool cleared = false;
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            BackFaceRuns runs = scene.Opaque[range].BackFace;
            // A rendering instance with no draw costs about 0.025 ms on the M1 Max (t1-s961), so such a range begins none.
            if (runs.ClosedOneSided.Count + runs.BothFaces.Count == 0)
                continue;
            BeginRendering(context: context, range: range, clear: !cleared);
            // Sets and dynamic state stay bound across the rendering instances.
            if (!cleared)
            {
                context.BindSceneSets();
                context.SetFullViewport();
                GeometryPass.BindGeometry(context);
            }

            GeometryPass.PushDraw(context: context, viewProjection: scene.RangeViewProjection[range]);
            // Both solid runs first, so that their depth hides the cutout fragments behind them before their texture
            // sample (docs/plans/skin-and-hair.md, decision 25).
            GeometryPass.DrawRun(context: context, commands: runs.ClosedOneSided.Solid, pipeline: closedOneSided.Solid);
            GeometryPass.DrawRun(context: context, commands: runs.BothFaces.Solid, pipeline: bothFaces.Solid);
            GeometryPass.DrawRun(context: context, commands: runs.ClosedOneSided.Cutout, pipeline: closedOneSided.Cutout);
            GeometryPass.DrawRun(context: context, commands: runs.BothFaces.Cutout, pipeline: bothFaces.Cutout);
            context.Vk.CmdEndRendering(context.CommandBuffer);
            cleared = true;
        }

        // A frame with no draw of the pass still clears the share once.
        if (!cleared)
        {
            BeginRendering(context: context, range: DepthRanges.Far, clear: true);
            context.Vk.CmdEndRendering(context.CommandBuffer);
        }
    }

    /// <summary>
    /// Begins the rendering instance of <paramref name="range"/> with a cleared depth, as the G-buffer pass does, so that a
    /// nearer range draws over a farther one. With <paramref name="clear"/>, it clears the share to 0 too: no fragment.
    /// </summary>
    private static void BeginRendering(FrameContext context, int range, bool clear)
    {
        RenderTargets targets = context.Targets;
        GpuImage share =
            targets.BackShare ?? throw new UnreachableException("The targets of the screen tracer hold the back share.");
        BarrierBatch barriers = context.Barriers;
        barriers.Image(share, ImageUse.ColorAttachment, discard: clear);
        barriers.Image(targets.Depth, ImageUse.DepthAttachment, discard: true);
        barriers.Image(targets.Distance, ImageUse.SampledFragment);
        barriers.Flush(context.Vk, context.CommandBuffer);
        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = share.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = clear ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new(new ClearColorValue(float32_0: 0f, float32_1: 0f, float32_2: 0f, float32_3: 0f)),
        };
        var depth = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = targets.Depth.View,
            ImageLayout = ImageLayout.DepthAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            // The next range clears the depth again, and no later pass reads it.
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

    public override void Dispose()
    {
        closedOneSided.Destroy(device);
        bothFaces.Destroy(device);
    }

    /// <summary>The pipelines of one kind of run: its solid run's and its cutout run's, which runs the cutout test.</summary>
    private readonly record struct RunPipelines(Pipeline Solid, Pipeline Cutout)
    {
        /// <summary>
        /// The solid pipeline of <paramref name="desc"/> and its cutout pipeline: the same, with the texture coordinate
        /// and the cutout shaders. Destroys the first when the second fails.
        /// </summary>
        public static RunPipelines Create(VulkanDevice device, ShaderLibrary shaders, GraphicsPipelineDesc desc)
        {
            Pipeline solid = desc.Create(device, shaders);
            try
            {
                Pipeline cutout = (
                    desc with
                    {
                        Name = desc.Name + " cutout",
                        VertexShader = "back_face_cutout.vert",
                        FragmentShader = "back_face_cutout.frag",
                        VertexInput = VertexInput.PositionAndTexCoord,
                    }
                ).Create(device, shaders);
                return new(Solid: solid, Cutout: cutout);
            }
            catch
            {
                device.Vk.DestroyPipeline(device.Device, solid, null);
                throw;
            }
        }

        public void Destroy(VulkanDevice device)
        {
            device.Vk.DestroyPipeline(device.Device, Solid, null);
            device.Vk.DestroyPipeline(device.Device, Cutout, null);
        }
    }
}
