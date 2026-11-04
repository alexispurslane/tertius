using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The composite (docs/plans/temporal-renderer.md, section 9, "The composite"): one fullscreen pass over all depth ranges
/// that writes the HDR image. At each lit pixel it multiplies the direct light's two images and the two indirect terms back
/// by their material factors (decision 18), adds the emission, and caps the sum at the surfaces' share. The indirect terms
/// are the screen tracer's slow histories, or under <see cref="NoScreenTrace"/> the environment's closed forms. Sky pixels get the sky and the stars, and an impostor's pixels its baked light.
/// Under the scatter it also writes the diffuse image, D and the skin mask at skin pixels and 0 elsewhere.
/// </summary>
internal sealed unsafe class CompositePass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly bool scatter;
    private readonly ScreenTraceImages? tracer;
    private readonly Pipeline pipeline;

    /// <param name="scatter">True under <see cref="SeparableSubsurface"/>, whose targets hold the diffuse image.</param>
    /// <param name="tracer">The screen tracer, whose signals' slow histories the composite reads, or null under <see cref="NoScreenTrace"/>.</param>
    public CompositePass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants,
        bool scatter,
        ScreenTraceImages? tracer
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.scatter = scatter;
        this.tracer = tracer;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "composite",
            VertexShader = "lighting.vert",
            FragmentShader = scatter ? "composite_scatter.frag" : "composite.frag",
            Layout = layouts.CompositeLayout,
            ColorFormats = scatter ? [RenderTargets.HdrFormat, RenderTargets.DiffuseFormat] : [RenderTargets.HdrFormat],
            CullMode = CullModeFlags.None,
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name => "composite";

    public override void Record(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        foreach (GpuImage image in targets.GBuffer)
            barriers.Image(image, ImageUse.SampledFragment);
        // After the direct pass's write, and the accumulations' writes of the slow histories.
        barriers.Image(targets.DirectDiffuse, ImageUse.StorageFragment);
        barriers.Image(targets.DirectSpecular, ImageUse.StorageFragment);
        if (tracer is not null)
        {
            barriers.Image(tracer.Diffuse.Slow(0), ImageUse.StorageFragment);
            barriers.Image(tracer.Specular.Slow(0), ImageUse.StorageFragment);
        }

        barriers.Image(targets.Hdr, ImageUse.ColorAttachment, discard: true);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        if (!scatter)
        {
            barriers.Flush(context.Vk, context.CommandBuffer);
            context.DrawSceneFullScreen(
                pipeline: pipeline,
                layout: layouts.CompositeLayout,
                passSet: targets.LightSet,
                targets: [(targets.Hdr, AttachmentLoadOp.DontCare)]
            );
            return;
        }

        GpuImage diffuse =
            targets.Diffuse ?? throw new UnreachableException("The targets of the scatter hold the diffuse image.");
        // Discarded, and still after the last frame's scatter passes read it: the pass writes every pixel.
        barriers.Image(diffuse, ImageUse.ColorAttachment, discard: true);
        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(
            pipeline: pipeline,
            layout: layouts.CompositeLayout,
            passSet: targets.LightSet,
            targets: [(targets.Hdr, AttachmentLoadOp.DontCare), (diffuse, AttachmentLoadOp.DontCare)]
        );
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
