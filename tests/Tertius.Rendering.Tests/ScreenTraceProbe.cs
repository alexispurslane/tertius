using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

internal sealed class ScreenTraceProbe(VulkanRenderer renderer, string shader) : RenderPass
{
    private ScreenTraceKernel? kernel;

    public override string Name => "test screen trace probe";

    public static ScreenTraceProbe On(VulkanRenderer renderer, string shader)
    {
        var probe = new ScreenTraceProbe(renderer, shader);
        renderer.Graph.Add(FrameGraph.Stage.ScreenTrace, probe);
        return probe;
    }

    public float[] Read() => renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));

    public override void Prepare(FrameContext context)
    {
        if (kernel is not null)
            return;
        using var shaders = new ShaderLibrary(context.Device, typeof(ScreenTraceProbe).Assembly);
        kernel = new(
            device: context.Device,
            shaders: shaders,
            layouts: context.Layouts,
            images: renderer.ScreenTraceImages,
            setting: ScreenTrace.Default,
            constants: renderer.ShadingConstants,
            name: Name,
            shader: shader
        );
    }

    public override void Record(FrameContext context)
    {
        ScreenTraceKernel.DeclareSurfaces(context);
        DepthPyramidImages pyramid = context.Targets.Pyramid!;
        context.Barriers.Image(pyramid.DepthFinest, ImageUse.SampledComputeGeneral);
        context.Barriers.Image(pyramid.DepthImage, ImageUse.SampledComputeGeneral);
        context.Barriers.Image(renderer.ScreenTraceSignals.Specular.Sample(0), ImageUse.StorageCompute, discard: true);
        kernel!.Record(context, side: 1);
    }

    public override void Dispose()
    {
        kernel?.Dispose();
        base.Dispose();
    }
}
