using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A test pass after the composite that writes the cases of Shaders/store_rounding.glsl into two 16-bit float color
/// attachments, for <see cref="StoreRoundingTests"/>. The first image takes each case's value from a draw without a
/// blend. The second takes each case's base from a draw without a blend, then its step from an additive blend, so that
/// the blend's sum is the value. Pixel p holds case p.x mod 36 in its red channel.
/// </summary>
internal sealed unsafe class StoreRoundingProbe : RenderPass
{
    private readonly VulkanRenderer renderer;
    private VulkanDevice? device;
    private ShaderLibrary? shaders;
    private Pipeline value;
    private Pipeline baseValue;
    private Pipeline step;
    private GpuImage? written;
    private GpuImage? blended;

    private StoreRoundingProbe(VulkanRenderer renderer)
    {
        this.renderer = renderer;
    }

    public override string Name => "test store rounding probe";

    /// <summary>Adds a probe to <paramref name="renderer"/>'s graph, after its composite.</summary>
    public static StoreRoundingProbe On(VulkanRenderer renderer)
    {
        var probe = new StoreRoundingProbe(renderer);
        renderer.Graph.Add(FrameGraph.Stage.Composite, probe);
        return probe;
    }

    /// <summary>The image of the draw without a blend after the last frame, RGBA per pixel, row by row from the top.</summary>
    public float[] ReadWritten() =>
        renderer.ReadImage(written ?? throw new InvalidOperationException("The probe has drawn no frame."));

    /// <summary>The image of the additive blend after the last frame, RGBA per pixel, row by row from the top.</summary>
    public float[] ReadBlended() =>
        renderer.ReadImage(blended ?? throw new InvalidOperationException("The probe has drawn no frame."));

    public override void Prepare(FrameContext context)
    {
        if (written is not null)
            return;
        device = context.Device;
        shaders = new ShaderLibrary(context.Device, typeof(StoreRoundingProbe).Assembly);
        value = Pipeline(context, "store_rounding_value.frag", BlendMode.None);
        baseValue = Pipeline(context, "store_rounding_base.frag", BlendMode.None);
        step = Pipeline(context, "store_rounding_step.frag", BlendMode.Additive);
        written = Image(context, "test store rounding write");
        blended = Image(context, "test store rounding blend");
    }

    public override void Record(FrameContext context)
    {
        GpuImage writeTarget = written ?? throw new InvalidOperationException("Prepare makes the images.");
        GpuImage blendTarget = blended!;
        RenderTargets targets = context.Targets;
        foreach (GpuImage gbuffer in targets.GBuffer)
            context.Barriers.Image(gbuffer, ImageUse.SampledFragment);
        context.Barriers.Image(targets.DirectDiffuse, ImageUse.StorageFragment);
        context.Barriers.Image(targets.DirectSpecular, ImageUse.StorageFragment);
        context.Barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        context.Barriers.Image(writeTarget, ImageUse.ColorAttachment, discard: true);
        context.Barriers.Image(blendTarget, ImageUse.ColorAttachment, discard: true);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        Draw(context: context, pipeline: value, target: writeTarget, load: AttachmentLoadOp.DontCare);
        Draw(context: context, pipeline: baseValue, target: blendTarget, load: AttachmentLoadOp.DontCare);
        // The blend reads what the draw of the bases wrote, in a rendering of its own.
        context.Barriers.Image(blendTarget, ImageUse.ColorAttachment);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        Draw(context: context, pipeline: step, target: blendTarget, load: AttachmentLoadOp.Load);
    }

    public override void Dispose()
    {
        written?.Dispose();
        blended?.Dispose();
        if (device is not null)
            foreach (Pipeline pipeline in (Pipeline[])[value, baseValue, step])
                device.Vk.DestroyPipeline(device.Device, pipeline, null);
        shaders?.Dispose();
        base.Dispose();
    }

    private Pipeline Pipeline(FrameContext context, string fragmentShader, BlendMode blend) =>
        new GraphicsPipelineDesc
        {
            Name = Name,
            VertexShader = "lighting.vert",
            FragmentShader = fragmentShader,
            Layout = context.Layouts.CompositeLayout,
            ColorFormats = [RenderTargets.HdrFormat],
            CullMode = CullModeFlags.None,
            Blend = blend,
            Constants = renderer.ShadingConstants,
        }.Create(context.Device, shaders!);

    private GpuImage Image(FrameContext context, string name) =>
        GpuImage.Create(
            device: context.Device,
            allocator: renderer.Allocator,
            extent: context.Extent,
            format: RenderTargets.HdrFormat,
            usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: name
        );

    private void Draw(FrameContext context, Pipeline pipeline, GpuImage target, AttachmentLoadOp load) =>
        context.DrawSceneFullScreen(
            pipeline: pipeline,
            layout: context.Layouts.CompositeLayout,
            passSet: renderer.CompositeLightSet,
            targets: [(target, load)]
        );
}
