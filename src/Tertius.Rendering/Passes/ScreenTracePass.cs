using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

internal sealed unsafe class ScreenTraceKernel : IDisposable
{
    private const uint GroupSize = 8;
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly ScreenTraceImages images;
    private readonly ScreenTrace setting;
    private readonly ShaderConstants constants;
    private readonly Pipeline pipeline;

    public ScreenTraceKernel(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ScreenTraceImages images,
        ScreenTrace setting,
        ShaderConstants constants,
        string name,
        string shader
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.images = images;
        this.setting = setting;
        this.constants = constants;
        Name = name;
        pipeline = new ComputePipelineDesc
        {
            Name = name,
            Shader = images.Specular.HistoryShader(shader),
            Layout = layouts.ScreenTraceLayout,
            Constants = constants,
        }.Create(device, shaders);
    }

    public string Name { get; }

    public OwnRayDispatch CreateOwnRays(ShaderLibrary shaders) =>
        new(
            device: device,
            shaders: shaders,
            pipeline: new ComputePipelineDesc
            {
                Name = $"{Name}, own",
                Shader = images.Specular.HistoryShader("screen_trace_own_rays.comp"),
                Layout = layouts.ScreenTraceLayout,
                Constants = constants,
            }
        );

    public void Record(FrameContext context, uint side)
    {
        context.BeginComputePass(
            pipeline: pipeline,
            layout: layouts.ScreenTraceLayout,
            passSet: images.Sets[context.History.Parity]
        );
        var push = ScreenTracePushConstants.Of(setting, context.History.DrawIndex, images.Guided);
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layouts.ScreenTraceLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(ScreenTracePushConstants),
            pValues: &push
        );
        Extent2D extent = context.Extent;
        uint width = (extent.Width + side - 1) / side,
            height = (extent.Height + side - 1) / side;
        context.Vk.CmdDispatch(
            commandBuffer: context.CommandBuffer,
            groupCountX: (width + GroupSize - 1) / GroupSize,
            groupCountY: (height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
    }

    public static void DeclareSurfaces(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        foreach (
            GpuImage image in (GpuImage[])
                [
                    targets.Distance,
                    targets.Normal,
                    targets.Specular,
                    targets.BaseColor,
                    targets.Emissive,
                    targets.Shading,
                ]
        )
            context.Barriers.Image(image, ImageUse.SampledCompute);
    }

    /// <summary>The environment map's two images, which a miss and the closed form read, under <see cref="SceneEnvironmentMap"/>.</summary>
    public static void DeclareEnvironment(FrameContext context)
    {
        if (context.Environment is not { } map)
            return;
        context.Barriers.Image(map.Specular, ImageUse.SampledCompute);
        context.Barriers.Image(map.Irradiance, ImageUse.SampledCompute);
    }

    public void DeclareHistories(BarrierBatch barriers, int parity, bool slow)
    {
        barriers.Image(images.SurfaceTest, ImageUse.StorageCompute);
        barriers.Image(images.Motion, ImageUse.StorageCompute);
        barriers.Image(slow ? images.Diffuse.Slow(0) : images.HitDiffuse(parity), ImageUse.StorageCompute);
        (_, GpuImage lastFast, GpuImage slowSpecular) = images.Specular.HitImages(part: 0, parity);
        barriers.Image(slow ? slowSpecular : lastFast, ImageUse.StorageCompute);
    }

    public void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}

internal sealed class ScreenTracePlanPass(ScreenTraceKernel kernel, ScreenTraceImages images) : RenderPass
{
    public override string Name => kernel.Name;

    public override void Record(FrameContext context)
    {
        foreach (GpuBuffer list in images.OwnRays)
            OwnRayDispatch.Clear(context, list);
        ScreenTraceKernel.DeclareSurfaces(context);
        kernel.DeclareHistories(context.Barriers, context.History.Parity, slow: true);
        context.Barriers.Image(images.Plan, ImageUse.StorageCompute, discard: true);

        DeclareHitLight(context);

        // The light-change age: the plan reads the last record, and writes every pixel of this one.
        int parity = context.History.Parity;
        if (images.LightAge(parity) is { } lightAge && images.LastLightAge(parity) is { } lastLightAge)
        {
            context.Barriers.Image(lastLightAge, ImageUse.StorageCompute);
            context.Barriers.Image(lightAge, ImageUse.StorageCompute, discard: true);
        }
        foreach (GpuBuffer list in images.OwnRays)
            context.Barriers.Buffer(list, BufferUse.ComputeWrite);
        kernel.Record(context, side: 1);
    }

    /// <summary>
    /// The hit light's resources: the plan writes it at every pixel that a walk can hit, by the pyramid's finest level,
    /// from this frame's direct light, the last frame's light of both signals and the environment's closed form.
    /// </summary>
    private void DeclareHitLight(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        barriers.Image(
            context.Targets.Pyramid?.Finest ?? throw new UnreachableException("The tracer reads the pyramid."),
            ImageUse.SampledComputeGeneral
        );
        kernel.DeclareHistories(barriers, context.History.Parity, slow: false);
        barriers.Image(context.Targets.DirectDiffuse, ImageUse.StorageCompute);
        barriers.Image(context.Targets.DirectSpecular, ImageUse.StorageCompute);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeRead);
        ScreenTraceKernel.DeclareEnvironment(context);
        barriers.Image(images.HitLight, ImageUse.StorageCompute, discard: true);
        // PROBE: the last draw's diffuse own-sample mean (TERTIUS_PROBE_HIT_LIGHT=own).
        if (images.Diffuse.OwnMean(0, 1 - context.History.Parity) is { } ownMean)
            barriers.Image(ownMean, ImageUse.StorageCompute);
    }

    public override void Dispose() => kernel.Dispose();
}

internal sealed class ScreenTraceRaysPass(
    ScreenTraceKernel kernel,
    ScreenTraceImages images,
    OwnRayDispatch ownRays,
    int kind
) : RenderPass
{
    public override string Name => kernel.Name;

    public override void Record(FrameContext context)
    {
        ScreenTraceKernel.DeclareSurfaces(context);
        DeclareRays(context);
        kernel.Record(context, side: 2);
        foreach (GpuBuffer list in images.OwnRays)
            context.Barriers.Buffer(list, BufferUse.ComputeIndirectRead);
        ownRays.Trace(context, images.OwnRays[kind]);
    }

    private void DeclareRays(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        RenderTargets targets = context.Targets;
        PyramidImages pyramid = targets.Pyramid ?? throw new UnreachableException("The tracer reads the pyramid.");
        barriers.Image(pyramid.Image, ImageUse.SampledComputeGeneral);
        barriers.Image(pyramid.Finest, ImageUse.SampledComputeGeneral);

        // Without the guiding, no pass writes the light pyramid, and the rays never read it: the barrier only gives it
        // the layout of the set's descriptors.
        barriers.Image(images.LightPyramidImages.Image, ImageUse.SampledComputeGeneral);
        barriers.Image(images.LightPyramidImages.Finest, ImageUse.SampledComputeGeneral);

        // A miss reads the exposure and the environment map.
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeRead);
        ScreenTraceKernel.DeclareEnvironment(context);

        // Each kind writes its own layers, but the validation layers' hazard tracking sees one image.
        foreach (GpuImage image in (GpuImage[])[images.BlockLight, images.BlockRay, images.OwnLight, images.OwnRay])
            barriers.Image(image, ImageUse.StorageCompute);
        // A hit reads the light that the plan wrote at its pixel.
        barriers.Image(images.HitLight, ImageUse.StorageCompute);
        // A hit reads the age that the plan wrote at its pixel.
        if (images.LightAge(context.History.Parity) is { } lightAge)
            barriers.Image(lightAge, ImageUse.StorageCompute);
    }

    public override void Dispose()
    {
        kernel.Dispose();
        ownRays.Dispose();
    }
}

internal sealed class ScreenTraceResolvePass(ScreenTraceKernel kernel, ScreenTraceImages images) : RenderPass
{
    public override string Name => kernel.Name;

    public override void Record(FrameContext context)
    {
        ScreenTraceKernel.DeclareSurfaces(context);
        foreach (GpuImage image in images.Records)
            context.Barriers.Image(image, ImageUse.StorageCompute);
        context.Barriers.Image(images.Diffuse.Sample(0), ImageUse.StorageCompute, discard: true);
        context.Barriers.Image(images.Specular.Sample(0), ImageUse.StorageCompute, discard: true);
        foreach (TemporalSignal signal in (TemporalSignal[])[images.Diffuse, images.Specular])
            if (signal.SampleAge(0) is { } age)
                context.Barriers.Image(age, ImageUse.StorageCompute, discard: true);
        if (images.Specular.UncoveredDiagnostic(0) is { } diagnostic)
            context.Barriers.Image(diagnostic, ImageUse.StorageCompute, discard: true);
        // FSR's mask reads the records that the plan wrote around each pixel.
        if (images.LightAge(context.History.Parity) is { } lightAge)
            context.Barriers.Image(lightAge, ImageUse.StorageCompute);
        if (images.LightChangeMask is { } mask)
            context.Barriers.Image(mask, ImageUse.StorageCompute, discard: true);
        kernel.Record(context, side: 1);
    }

    public override void Dispose() => kernel.Dispose();
}
