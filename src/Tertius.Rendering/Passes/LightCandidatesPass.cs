using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// "Light candidates" under <see cref="ResampledLights"/> (docs/plans/temporal-renderer.md, section 7, "The two passes"):
/// one fullscreen pass that draws each lit pixel's candidates from the light table, reuses the last frame's reservoir of
/// R1 at the pixel's previous place, tests the chosen light's visibility and writes R0.
/// </summary>
internal sealed unsafe class LightCandidatesPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly LightReservoirs reservoirs;
    private readonly SurfaceHistory surfaces;
    private readonly Pipeline pipeline;

    public LightCandidatesPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants,
        LightReservoirs reservoirs,
        SurfaceHistory surfaces
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.reservoirs = reservoirs;
        this.surfaces = surfaces;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "light candidates",
            VertexShader = "lighting.vert",
            FragmentShader = "light_candidates.frag",
            Layout = layouts.ReservoirLayout,
            ColorFormats = [LightReservoirs.ReservoirFormat],
            CullMode = CullModeFlags.None,
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name => "light candidates";

    public override void Record(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        // Not the motion target, which the pass reads as a storage image below.
        foreach (GpuImage image in targets.GBuffer)
            if (image != targets.Motion)
                barriers.Image(image, ImageUse.SampledFragment);
        barriers.Image(context.Shadows.Image, ImageUse.SampledFragment);
        context.ShadowAtlas?.DeclareSampled(barriers, ImageUse.SampledFragment);
        barriers.Image(surfaces.Test, ImageUse.StorageFragment);
        barriers.Image(targets.Motion, ImageUse.StorageFragment);
        // The last frame's direct pass wrote it. A reset frame reads none of it, because the surface test fails every tap.
        barriers.Image(reservoirs.R1, ImageUse.SampledFragment);
        // Discarded, and still after the last frame's direct pass read it.
        barriers.Image(reservoirs.R0, ImageUse.ColorAttachment, discard: true);
        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(
            pipeline: pipeline,
            layout: layouts.ReservoirLayout,
            passSet: reservoirs.CandidatesSet,
            push: ResampledPush.Of(reservoirs.Setting, context.History.DrawIndex),
            targets: [(reservoirs.R0, AttachmentLoadOp.DontCare)]
        );
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
