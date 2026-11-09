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
        var push = ScreenTracePushConstants.Of(setting, context.History.DrawIndex);
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
        // The light-change age: the plan reads the direct light and the last record, and writes every pixel of this one.
        int parity = context.History.Parity;
        if (images.LightAge(parity) is { } lightAge && images.LastLightAge(parity) is { } lastLightAge)
        {
            context.Barriers.Image(context.Targets.DirectDiffuse, ImageUse.StorageCompute);
            context.Barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeRead);
            context.Barriers.Image(lastLightAge, ImageUse.StorageCompute);
            context.Barriers.Image(lightAge, ImageUse.StorageCompute, discard: true);
        }
        foreach (GpuBuffer list in images.OwnRays)
            context.Barriers.Buffer(list, BufferUse.ComputeWrite);
        kernel.Record(context, side: 1);
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
        DepthPyramidImages pyramid = targets.Pyramid ?? throw new UnreachableException("The tracer reads the pyramid.");
        barriers.Image(pyramid.DepthImage, ImageUse.SampledComputeGeneral);
        barriers.Image(pyramid.DepthFinest, ImageUse.SampledComputeGeneral);
        barriers.Image(targets.DirectDiffuse, ImageUse.StorageCompute);
        barriers.Image(targets.DirectSpecular, ImageUse.StorageCompute);
        kernel.DeclareHistories(barriers, context.History.Parity, slow: false);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeRead);
        if (context.Environment is { } map)
        {
            barriers.Image(map.Specular, ImageUse.SampledCompute);
            barriers.Image(map.Irradiance, ImageUse.SampledCompute);
        }

        // Each kind writes its own layers, but the validation layers' hazard tracking sees one image.
        foreach (GpuImage image in (GpuImage[])[images.BlockLight, images.BlockRay, images.OwnLight, images.OwnRay])
            barriers.Image(image, ImageUse.StorageCompute);
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
        if (images.Specular.SampleAge(0) is { } age)
            context.Barriers.Image(age, ImageUse.StorageCompute, discard: true);
        if (images.Specular.UncoveredDiagnostic(0) is { } diagnostic)
            context.Barriers.Image(diagnostic, ImageUse.StorageCompute, discard: true);
        kernel.Record(context, side: 1);
    }

    public override void Dispose() => kernel.Dispose();
}
