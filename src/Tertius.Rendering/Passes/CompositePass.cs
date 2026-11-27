using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The composite (https://github.com/alexispurslane/tertius/wiki/temporal-renderer, section 9, "The composite"): one fullscreen pass over all depth ranges
/// that writes scene radiance to the HDR image. It adds raw direct light and emission to the converted indirect estimates.
/// The indirect estimates come from the environment's closed forms and the tracer's specular history. Sky and impostor
/// pixels retain their scene radiance. Under skin scatter it also writes the skin's scene diffuse radiance and mask.
/// </summary>
internal sealed unsafe class CompositePass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly bool scatter;
    private readonly bool sharing;
    private readonly ScreenTraceImages? tracer;
    private readonly GpuBuffer? volumeBuffer;
    private readonly Pipeline pipeline;
    private readonly GpuImage emptyHistory;

    /// <param name="scatter">True under <see cref="SeparableSubsurface"/>, whose targets hold the diffuse image.</param>
    /// <param name="tracer">The screen tracer, whose signals' slow histories the composite reads, or null under <see cref="NoScreenTrace"/>.</param>
    public CompositePass(
        VulkanDevice device,
        DeviceAllocator allocator,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants,
        bool scatter,
        ScreenTraceImages? tracer,
        RenderTargets targets,
        LightVolumePass? volume = null
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.scatter = scatter;
        sharing = constants.ReflectionSharingPrototype;
        this.tracer = tracer;
        LightSet = layouts.AllocateLightSet();
        emptyHistory = GpuImage.Create(
            device: device,
            allocator: allocator,
            format: Format.R16G16B16A16Sfloat,
            extent: new(1, 1),
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferDstBit,
            mipLevels: 1,
            name: "unused indirect history"
        );
        try
        {
            Resize(targets);
            string fragmentShader = scatter ? "composite_scatter.frag" : "composite.frag";
            if (volume is not null)
            {
                volumeBuffer = volume.Buffer;
                layouts.WriteBuffer(
                    set: LightSet,
                    binding: 5,
                    type: DescriptorType.StorageBuffer,
                    buffer: volume.Buffer
                );
                fragmentShader = scatter ? "composite_volume_scatter.frag" : "composite_volume.frag";
            }
            pipeline = new GraphicsPipelineDesc
            {
                Name = "composite",
                VertexShader = "lighting.vert",
                FragmentShader = tracer is null ? fragmentShader : tracer.Specular.HistoryShader(fragmentShader),
                Layout = layouts.CompositeLayout,
                ColorFormats = scatter
                    ? [RenderTargets.HdrFormat, RenderTargets.DiffuseFormat]
                    : [RenderTargets.HdrFormat],
                CullMode = CullModeFlags.None,
                Constants = constants,
            }.Create(device, shaders);
        }
        catch
        {
            emptyHistory.Dispose();
            throw;
        }
    }

    public override string Name => "composite";

    internal DescriptorSet LightSet { get; }

    /// <summary>Rebinds the four light images and the existing reflection sample after resize, with the device idle.</summary>
    public void Resize(RenderTargets targets)
    {
        GpuImage[] light =
        [
            targets.DirectDiffuse,
            targets.DirectSpecular,
            tracer?.Specular.Slow(0) ?? emptyHistory,
            sharing && tracer is { } sampleTracer ? sampleTracer.Specular.Sample(0) : emptyHistory,
            sharing && tracer is { } guideTracer ? guideTracer.Specular.ReflectionGuide(0) : emptyHistory,
        ];
        for (int binding = 0; binding < light.Length; binding++)
            layouts.WriteStorageImage(set: LightSet, binding: (uint)binding, image: light[binding]);
    }

    /// <summary>Defines the zero history bound by disabled indirect paths, independently of mutable scene targets.</summary>
    public override void Prepare(FrameContext context)
    {
        if (emptyHistory.State.Layout != ImageLayout.Undefined)
            return;
        context.Barriers.Image(emptyHistory, ImageUse.TransferDestination);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        ClearColorValue zero = default;
        var range = new ImageSubresourceRange(
            aspectMask: ImageAspectFlags.ColorBit,
            baseMipLevel: 0,
            levelCount: 1,
            baseArrayLayer: 0,
            layerCount: 1
        );
        context.Vk.CmdClearColorImage(
            commandBuffer: context.CommandBuffer,
            image: emptyHistory.Handle,
            imageLayout: ImageLayout.TransferDstOptimal,
            pColor: &zero,
            rangeCount: 1,
            pRanges: &range
        );
        context.Barriers.Image(emptyHistory, ImageUse.StorageFragment);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
    }

    public override void Record(FrameContext context)
    {
        Prepare(context);
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        foreach (GpuImage image in targets.GBuffer)
            barriers.Image(image, ImageUse.SampledFragment);
        // After the direct pass's write, and the accumulations' writes of the slow histories.
        barriers.Image(targets.DirectDiffuse, ImageUse.StorageFragment);
        barriers.Image(targets.DirectSpecular, ImageUse.StorageFragment);
        if (tracer is not null)
        {
            barriers.Image(tracer.Specular.Slow(0), ImageUse.StorageFragment);
            if (sharing)
            {
                barriers.Image(tracer.Specular.Sample(0), ImageUse.StorageFragment);
                barriers.Image(tracer.Specular.ReflectionGuide(0), ImageUse.StorageFragment);
            }
        }

        barriers.Image(targets.Hdr, ImageUse.ColorAttachment, discard: true);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        if (volumeBuffer is not null)
            barriers.Buffer(volumeBuffer, BufferUse.ShaderRead);
        if (!scatter)
        {
            barriers.Flush(context.Vk, context.CommandBuffer);
            context.DrawSceneFullScreen(
                pipeline: pipeline,
                layout: layouts.CompositeLayout,
                passSet: LightSet,
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
            passSet: LightSet,
            targets: [(targets.Hdr, AttachmentLoadOp.DontCare), (diffuse, AttachmentLoadOp.DontCare)]
        );
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, pipeline, null);
        emptyHistory.Dispose();
    }
}
