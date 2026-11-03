using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A test pass after the composite that writes each pixel's direct light and emission, pre-exposed and capped, into an
/// image of its own: the direct signal's slow histories times their material factors, as the direct image held the light
/// before the direct signal. Sky pixels get 0. It draws Shaders/direct_light_probe.frag, the composite without the ambient
/// light. A probe of the diffuse part draws Shaders/direct_diffuse_probe.frag: the diffuse factor times the diffuse history
/// alone, as the diffuse image held it, the bounce source's light.
/// </summary>
internal sealed unsafe class DirectLightProbe : RenderPass
{
    private readonly VulkanRenderer renderer;
    private readonly bool diffuseOnly;
    private VulkanDevice? device;
    private ShaderLibrary? shaders;
    private Pipeline pipeline;
    private GpuImage? image;

    private DirectLightProbe(VulkanRenderer renderer, bool diffuseOnly)
    {
        this.renderer = renderer;
        this.diffuseOnly = diffuseOnly;
    }

    public override string Name => "test direct light probe";

    /// <summary>
    /// Adds a probe to <paramref name="renderer"/>'s graph, after its composite: of the diffuse part alone if
    /// <paramref name="diffuseOnly"/>, else of both parts and the emission.
    /// </summary>
    public static DirectLightProbe On(VulkanRenderer renderer, bool diffuseOnly = false)
    {
        var probe = new DirectLightProbe(renderer, diffuseOnly);
        renderer.Graph.Add(FrameGraph.Stage.Composite, probe);
        return probe;
    }

    /// <summary>The probe's image after the last frame, RGBA per pixel, row by row from the top.</summary>
    public float[] Read() =>
        renderer.ReadImage(image ?? throw new InvalidOperationException("The probe has drawn no frame."));

    public override void Prepare(FrameContext context)
    {
        if (image is not null)
            return;
        device = context.Device;
        shaders = new ShaderLibrary(context.Device, typeof(DirectLightProbe).Assembly);
        pipeline = new GraphicsPipelineDesc
        {
            Name = "test direct light probe",
            VertexShader = "lighting.vert",
            FragmentShader = diffuseOnly ? "direct_diffuse_probe.frag" : "direct_light_probe.frag",
            Layout = context.Layouts.CompositeLayout,
            ColorFormats = [RenderTargets.HdrFormat],
            CullMode = CullModeFlags.None,
            Constants = renderer.ShadingConstants,
        }.Create(context.Device, shaders);
        image = GpuImage.Create(
            device: context.Device,
            allocator: renderer.Allocator,
            extent: context.Extent,
            format: RenderTargets.HdrFormat,
            usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: "test direct light probe"
        );
    }

    public override void Record(FrameContext context)
    {
        GpuImage target = image ?? throw new InvalidOperationException("Prepare makes the image.");
        TemporalSignal direct = renderer.DirectSignal;
        foreach (GpuImage gbuffer in context.Targets.GBuffer)
            context.Barriers.Image(gbuffer, ImageUse.SampledFragment);
        for (int part = 0; part < direct.Parts; part++)
            context.Barriers.Image(direct.Slow(part), ImageUse.StorageFragment);
        context.Barriers.Image(target, ImageUse.ColorAttachment, discard: true);
        context.Barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(
            pipeline: pipeline,
            layout: context.Layouts.CompositeLayout,
            passSet: direct.ReadSet,
            targets: [(target, AttachmentLoadOp.DontCare)]
        );
    }

    public override void Dispose()
    {
        image?.Dispose();
        if (device is not null)
            device.Vk.DestroyPipeline(device.Device, pipeline, null);
        shaders?.Dispose();
        base.Dispose();
    }
}
