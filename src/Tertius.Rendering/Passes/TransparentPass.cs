using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Forward-shades glass and the fringes of fringed materials over the lit HDR image with the same lighting function, and
/// adds the puffs to it, sorted together back to front across all ranges (far range first). Glass and a fringe add their
/// light and multiply what lies behind by the share that they let through (<see cref="BlendMode.Transmittance"/>). Each
/// range draws its runs in order: a <see cref="GlassCommands"/> run with the glass pipeline, a <see cref="FringeCommands"/>
/// run with the fringe pipeline, which blends a texel below the cutoff by its coverage (docs/plans/skin-and-hair.md, "The
/// fringe"), each in the variant that draws back faces for a two-sided run, and a <see cref="PuffSprites"/> run with the
/// puff pipeline, one instanced draw of 6 vertices per puff. So
/// glass dims a puff behind it and leaves one in front of it as it is. There is no depth buffer here: occlusion by opaque surfaces in any range uses the G-buffer distance, and depth
/// clamping keeps each object and each puff whole inside its range.
/// </summary>
internal sealed unsafe class TransparentPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly SidedPipeline pipeline;
    private readonly SidedPipeline fringePipeline;
    private readonly Pipeline puffPipeline;

    public TransparentPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants
    )
    {
        this.device = device;
        var glass = new GraphicsPipelineDesc
        {
            Name = "transparent",
            VertexShader = "mesh.vert",
            FragmentShader = "transparent.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [RenderTargets.HdrFormat],
            Blend = BlendMode.Transmittance,
            VertexInput = VertexInput.Mesh,
            DepthClamp = true,
            Constants = constants,
        };
        pipeline = SidedPipeline.Create(device: device, shaders: shaders, desc: glass);
        try
        {
            fringePipeline = SidedPipeline.Create(
                device: device,
                shaders: shaders,
                desc: glass with
                {
                    Name = "fringes",
                    FragmentShader = "fringe.frag",
                }
            );
        }
        catch
        {
            pipeline.Destroy(device);
            throw;
        }

        try
        {
            puffPipeline = new GraphicsPipelineDesc
            {
                Name = "puffs",
                VertexShader = "puff.vert",
                FragmentShader = "puff.frag",
                Layout = layouts.SceneLayout,
                ColorFormats = [RenderTargets.HdrFormat],
                Blend = BlendMode.Additive,
                VertexInput = VertexInput.None,
                // The square faces the eye, and its winding does not matter.
                CullMode = CullModeFlags.None,
                DepthClamp = true,
                Constants = constants,
            }.Create(device, shaders);
        }
        catch
        {
            pipeline.Destroy(device);
            fringePipeline.Destroy(device);
            throw;
        }
    }

    public override string Name => "transparent";

    public override void Record(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        bool any = false;
        foreach (List<TransparentRun> runs in scene.Transparent)
            any |= runs.Count > 0;
        if (!any)
            return;

        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Materials.Buffer, BufferUse.ShaderRead);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        barriers.Image(targets.Distance, ImageUse.SampledFragment);
        // The vertex shader of the lit puffs samples the cascades at each puff's center.
        barriers.Image(context.Shadows.Image, ImageUse.SampledVertexFragment);
        if (context.ShadowAtlas is { } atlas)
            barriers.Image(atlas.Image, ImageUse.SampledVertexFragment);
        barriers.Image(targets.Hdr, ImageUse.ColorAttachment);
        barriers.Flush(vk, commandBuffer);

        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = targets.Hdr.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), targets.Extent),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);
        DrawRuns(context: context, scene: scene, vk: vk, commandBuffer: commandBuffer);
        vk.CmdEndRendering(commandBuffer);
    }

    /// <summary>Draws the transparent runs of each depth range, from far to near, in the order of the scene.</summary>
    private void DrawRuns(FrameContext context, PreparedScene scene, Vk vk, CommandBuffer commandBuffer)
    {
        // Every pipeline has the scene layout, so the sets and the push constants stay bound when the pipeline changes.
        Pipeline bound = pipeline.OneSided;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, bound);
        context.BindSceneSets();
        context.SetFullViewport();
        GeometryPass.BindGeometry(context);
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            List<TransparentRun> runs = scene.Transparent[range];
            if (runs.Count == 0)
                continue;
            GeometryPass.PushDraw(context: context, viewProjection: scene.RangeViewProjection[range]);
            foreach (TransparentRun run in runs)
                switch (run)
                {
                    case FringeCommands { Commands: var fringes, TwoSided: var twoSided }:
                        Bind(
                            vk: vk,
                            commandBuffer: commandBuffer,
                            bound: ref bound,
                            wanted: fringePipeline.For(twoSided)
                        );
                        GeometryPass.DrawIndirect(context, fringes);
                        break;
                    case GlassCommands { Commands: var commands, TwoSided: var twoSided }:
                        Bind(vk: vk, commandBuffer: commandBuffer, bound: ref bound, wanted: pipeline.For(twoSided));
                        GeometryPass.DrawIndirect(context, commands);
                        break;
                    case PuffSprites { First: var first, Count: var count }:
                        Bind(vk: vk, commandBuffer: commandBuffer, bound: ref bound, wanted: puffPipeline);
                        vk.CmdDraw(
                            commandBuffer: commandBuffer,
                            vertexCount: 6,
                            instanceCount: (uint)count,
                            firstVertex: 0,
                            firstInstance: (uint)first
                        );
                        break;
                    case null:
                        // The scene batcher adds a run of glass, of fringes or of puffs, never an empty run.
                        throw new UnreachableException("A transparent run is empty.");
                    default:
                        throw new UnreachableException();
                }
        }
    }

    private static void Bind(Vk vk, CommandBuffer commandBuffer, ref Pipeline bound, Pipeline wanted)
    {
        if (bound.Handle == wanted.Handle)
            return;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, wanted);
        bound = wanted;
    }

    public override void Dispose()
    {
        pipeline.Destroy(device);
        fringePipeline.Destroy(device);
        device.Vk.DestroyPipeline(device.Device, puffPipeline, null);
    }
}
