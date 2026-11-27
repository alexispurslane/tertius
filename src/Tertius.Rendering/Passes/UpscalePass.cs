using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Feeds the frame's jittered color, motion, depth, exposure, opaque color and the screen tracer's light-change mask to
/// the pinned FSR host.
/// </summary>
internal sealed unsafe class UpscalePass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly ShaderLibrary shaders;
    private readonly DescriptorLayouts layouts;
    private readonly Pipeline input;
    private readonly RenderTargets targets;
    private readonly SurfaceHistory surfaces;

    /// <summary>The screen tracer's images, whose light-change mask FSR reads, or null without the tracer.</summary>
    private readonly ScreenTraceImages? screenTrace;

    private readonly int framesInFlight;
    private readonly float sharpness;
    private Fsr3Upscaler upscaler;

    public UpscalePass(
        VulkanDevice device,
        DeviceAllocator allocator,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        RenderTargets targets,
        SurfaceHistory surfaces,
        ScreenTraceImages? screenTrace,
        int framesInFlight,
        float sharpness
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.shaders = shaders;
        this.layouts = layouts;
        this.targets = targets;
        this.surfaces = surfaces;
        this.screenTrace = screenTrace;
        this.framesInFlight = framesInFlight;
        this.sharpness = sharpness;

        input = new ComputePipelineDesc
        {
            Name = "FSR exposed input",
            Shader = "fsr_input.comp",
            Layout = layouts.FsrInputLayout,
        }.Create(device, shaders);
        upscaler = Create();
    }

    public override string Name => "FSR 3.1";

    internal GpuBuffer Constants => upscaler.Constants;

    public void Resize()
    {
        Fsr3Upscaler replacement = Create();
        upscaler.Dispose();
        upscaler = replacement;
    }

    private Fsr3Upscaler Create() =>
        new(
            device: device,
            allocator: allocator,
            shaders: shaders,
            maxRenderSize: Fsr3Upscaler.AllocationSize(targets.Extent),
            outputSize: targets.OutputExtent,
            framesInFlight: framesInFlight
        );

    public override void Record(FrameContext context)
    {
        if (targets.Upscale is not { } images)
            throw new InvalidOperationException("The upscale requires its targets.");
        ExposeInput(context, images);
        upscaler.Record(
            context.CommandBuffer,
            context.Barriers,
            new Fsr3Inputs
            {
                Color = images.Color,
                OpaqueOnly = images.OpaqueOnly,
                Reactive = images.Reactive,
                // The tracer remakes its images on a resize, so the pass reads the mask in each frame.
                TransparencyAndComposition = screenTrace?.LightChangeMask,
                Depth = surfaces.WrittenDepth(context.History.Parity),
                Motion = targets.Motion,
                Output = images.Output,
                Exposure = context.Exposure.Buffer,
                Frame = new Fsr3Frame
                {
                    Sharpness = sharpness,
                    RenderSize = context.Extent,
                    // The jitter that the frame's projection took (VulkanRenderer.ApplyRenderProjection), which the
                    // debug switch DebugJitter can turn off.
                    Jitter = new(context.Scene.Constants.RenderParams.Y, context.Scene.Constants.RenderParams.Z),
                    FrameMilliseconds = context.History.FsrFrameMilliseconds,
                    NearMeters = DepthRanges.NearPlane,
                    VerticalFov = context.Scene.Camera.VerticalFov,
                    Aspect = context.Scene.Camera.Aspect,
                    Reset = context.History.Reset,
                },
            }
        );
    }

    /// <summary>Applies display exposure to FSR's derived inputs while preserving the original scene radiance.</summary>
    private void ExposeInput(FrameContext context, UpscaleTargets images)
    {
        context.Barriers.Image(targets.Hdr, ImageUse.SampledCompute);
        context.Barriers.Image(images.Color, ImageUse.StorageCompute, discard: true);
        context.Barriers.Image(images.OpaqueOnly, ImageUse.StorageCompute);
        context.Barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeRead);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);

        context.Vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Compute, input);
        DescriptorSet* sets = stackalloc[] { context.Frame.FrameSet, targets.MeterSet };
        context.Vk.CmdBindDescriptorSets(
            commandBuffer: context.CommandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.FsrInputLayout,
            firstSet: 0,
            descriptorSetCount: 2,
            pDescriptorSets: sets,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );

        Extent2D extent = context.Extent;
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layouts.FsrInputLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(Extent2D),
            pValues: &extent
        );
        context.Vk.CmdDispatch(
            commandBuffer: context.CommandBuffer,
            groupCountX: (extent.Width + 7) / 8,
            groupCountY: (extent.Height + 7) / 8,
            groupCountZ: 1
        );
    }

    public override void Dispose()
    {
        upscaler.Dispose();
        device.Vk.DestroyPipeline(device.Device, input, null);
    }
}
