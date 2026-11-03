using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// "Direct light" (docs/plans/temporal-renderer.md, section 7, "The two passes"): one fullscreen pass over all depth
/// ranges that writes the direct signal's sample of each lit pixel in its two parts, the diffuse and the specular light,
/// each divided by its material factor and pre-exposed. Sky and impostor pixels get 0. Under <see cref="EveryLight"/>,
/// which keeps no reservoirs, it takes every light with its shadows. Under <see cref="ResampledLights"/> it reuses the
/// neighbors' reservoirs of R0, shades the chosen light and every exact light, and writes R1.
/// </summary>
internal sealed unsafe class DirectLightPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly TemporalSignal signal;
    private readonly LightReservoirs? reservoirs;
    private readonly Pipeline pipeline;

    /// <param name="reservoirs">The reservoirs, which hold the setting of <see cref="ResampledLights"/>, or null under
    /// <see cref="EveryLight"/>.</param>
    public DirectLightPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants,
        TemporalSignal signal,
        LightReservoirs? reservoirs
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.signal = signal;
        this.reservoirs = reservoirs;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "direct light",
            VertexShader = "lighting.vert",
            FragmentShader = reservoirs is null ? "direct_every.frag" : "direct_resampled.frag",
            Layout = reservoirs is null ? layouts.SceneLayout : layouts.ReservoirLayout,
            ColorFormats = reservoirs is null
                ? [TemporalSignal.SignalFormat, TemporalSignal.SignalFormat]
                : [TemporalSignal.SignalFormat, TemporalSignal.SignalFormat, LightReservoirs.ReservoirFormat],
            CullMode = CullModeFlags.None,
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name => "direct light";

    public override void Record(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        // Not the motion target, which the pass does not read: the accumulation reads it next, as a storage image.
        foreach (GpuImage image in targets.GBuffer)
            if (image != targets.Motion)
                barriers.Image(image, ImageUse.SampledFragment);
        // Always, even in a frame without shadow maps: the descriptor promises this layout whenever the shader runs.
        barriers.Image(context.Shadows.Image, ImageUse.SampledFragment);
        context.ShadowAtlas?.DeclareSampled(barriers, ImageUse.SampledFragment);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        // Discarded, and still after the last frame's accumulation read them: the pass writes every pixel.
        barriers.Image(signal.Sample(0), ImageUse.ColorAttachment, discard: true);
        barriers.Image(signal.Sample(1), ImageUse.ColorAttachment, discard: true);
        if (reservoirs is null)
        {
            barriers.Flush(context.Vk, context.CommandBuffer);
            context.DrawSceneFullScreen(
                pipeline,
                [(signal.Sample(0), AttachmentLoadOp.DontCare), (signal.Sample(1), AttachmentLoadOp.DontCare)]
            );
            return;
        }

        barriers.Image(reservoirs.R0, ImageUse.SampledFragment);
        // Discarded, and still after this frame's candidates read it.
        barriers.Image(reservoirs.R1, ImageUse.ColorAttachment, discard: true);
        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(
            pipeline: pipeline,
            layout: layouts.ReservoirLayout,
            passSet: reservoirs.DirectSet,
            push: ResampledPush.Of(reservoirs.Setting, context.History.DrawIndex),
            targets:
            [
                (signal.Sample(0), AttachmentLoadOp.DontCare),
                (signal.Sample(1), AttachmentLoadOp.DontCare),
                (reservoirs.R1, AttachmentLoadOp.DontCare),
            ]
        );
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}

/// <summary>The push constants of ReSTIR's passes for one draw.</summary>
internal static class ResampledPush
{
    public static ResampledPushConstants Of(ResampledLights setting, long drawIndex) =>
        new()
        {
            DrawIndex = unchecked((uint)drawIndex),
            Candidates = (uint)setting.Candidates,
            Neighbors = (uint)setting.SpatialNeighbors,
        };
}
