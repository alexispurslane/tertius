namespace Tertius.Rendering;

/// <summary>Feeds the frame's jittered color, motion, depth, exposure and opaque color to the pinned FSR host.</summary>
internal sealed class UpscalePass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly ShaderLibrary shaders;
    private readonly RenderTargets targets;
    private readonly SurfaceHistory surfaces;
    private readonly int framesInFlight;
    private readonly float sharpness;
    private Fsr3Upscaler upscaler;

    public UpscalePass(
        VulkanDevice device,
        DeviceAllocator allocator,
        ShaderLibrary shaders,
        RenderTargets targets,
        SurfaceHistory surfaces,
        int framesInFlight,
        float sharpness
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.shaders = shaders;
        this.targets = targets;
        this.surfaces = surfaces;
        this.framesInFlight = framesInFlight;
        this.sharpness = sharpness;
        upscaler = Create();
    }

    public override string Name => "FSR 3.1";

    internal GpuBuffer Constants => upscaler.Constants;

    public void Resize()
    {
        upscaler.Dispose();
        upscaler = Create();
    }

    private Fsr3Upscaler Create() =>
        new(
            device: device,
            allocator: allocator,
            shaders: shaders,
            maxRenderSize: targets.Extent,
            outputSize: targets.OutputExtent,
            framesInFlight: framesInFlight
        );

    public override void Record(FrameContext context)
    {
        if (targets.Upscale is not { } images)
            throw new InvalidOperationException("The upscale requires its targets.");
        upscaler.Record(
            context.CommandBuffer,
            context.Barriers,
            new Fsr3Inputs
            {
                Color = targets.Hdr,
                OpaqueOnly = images.OpaqueOnly,
                Reactive = images.Reactive,
                Depth = surfaces.WrittenDepth(context.History.Parity),
                Motion = targets.Motion,
                Output = images.Output,
                Exposure = context.Exposure.Buffer,
                Frame = new Fsr3Frame
                {
                    Sharpness = sharpness,
                    RenderSize = context.Extent,
                    Jitter = context.History.Jitter,
                    FrameMilliseconds = context.History.FsrFrameMilliseconds,
                    NearMeters = DepthRanges.NearPlane,
                    VerticalFov = context.Scene.Camera.VerticalFov,
                    Reset = context.History.Reset,
                },
            }
        );
    }

    public override void Dispose() => upscaler.Dispose();
}
