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

    public void InvalidateImages()
    {
        kernel?.Dispose();
        kernel = null;
    }

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
        PyramidImages pyramid = context.Targets.Pyramid!;
        context.Barriers.Image(pyramid.Finest, ImageUse.SampledComputeGeneral);
        context.Barriers.Image(pyramid.Image, ImageUse.SampledComputeGeneral);
        foreach (GpuImage image in renderer.ScreenTraceImages.Records)
            context.Barriers.Image(image, ImageUse.StorageCompute);
        context.Barriers.Image(renderer.ScreenTraceImages.HitLight, ImageUse.StorageCompute);
        context.Barriers.Image(renderer.ScreenTraceSignals.Diffuse.Sample(0), ImageUse.StorageCompute);
        context.Barriers.Image(renderer.ScreenTraceSignals.Specular.Sample(0), ImageUse.StorageCompute);
        // A probe of the resolve writes FSR's mask from the records around each pixel, as the resolve does.
        if (renderer.ScreenTraceImages.LightAge(context.History.Parity) is { } lightAge)
            context.Barriers.Image(lightAge, ImageUse.StorageCompute);
        if (renderer.ScreenTraceImages.LightChangeMask is { } mask)
            context.Barriers.Image(mask, ImageUse.StorageCompute);
        kernel!.DeclareHistories(context.Barriers, context.History.Parity, slow: true);
        kernel!.DeclareHistories(context.Barriers, context.History.Parity, slow: false);
        kernel!.Record(context, side: 1);
    }

    public override void Dispose()
    {
        kernel?.Dispose();
        base.Dispose();
    }
}
