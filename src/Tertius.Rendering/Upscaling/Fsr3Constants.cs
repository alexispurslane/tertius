using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The constant buffer <c>cbFSR3Upscaler</c> of AMD's FSR 3.1.4 passes, in the layout of AMD's
/// <c>Fsr3UpscalerConstants</c> (ffx_fsr3upscaler_private.h), which is also the buffer's std140 layout. The CPU writes
/// every field as AMD's host does (<see cref="Next"/>), except <see cref="DeltaPreExposure"/>, which the exposure step
/// writes on the GPU (docs/plans/temporal-renderer.md, decision 31).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Fsr3Constants
{
    // C's FLT_EPSILON, 2^-23, which AMD's host adds to an element of the inverse projection. It is not .NET's
    // float.Epsilon, the smallest subnormal float.
    private const float FltEpsilon = 1.1920929e-7f;

    public int RenderSizeX;
    public int RenderSizeY;
    public int PreviousFrameRenderSizeX;
    public int PreviousFrameRenderSizeY;
    public int UpscaleSizeX;
    public int UpscaleSizeY;
    public int PreviousFrameUpscaleSizeX;
    public int PreviousFrameUpscaleSizeY;
    public int MaxRenderSizeX;
    public int MaxRenderSizeY;
    public int MaxUpscaleSizeX;
    public int MaxUpscaleSizeY;
    public float DeviceToViewDepth0;
    public float DeviceToViewDepth1;
    public float DeviceToViewDepth2;
    public float DeviceToViewDepth3;
    public float JitterOffsetX;
    public float JitterOffsetY;
    public float PreviousFrameJitterOffsetX;
    public float PreviousFrameJitterOffsetY;
    public float MotionVectorScaleX;
    public float MotionVectorScaleY;
    public float DownscaleFactorX;
    public float DownscaleFactorY;
    public float MotionVectorJitterCancellationX;
    public float MotionVectorJitterCancellationY;
    public float TanHalfFov;
    public float JitterPhaseCount;
    public float DeltaTime;
    public float DeltaPreExposure;
    public float ViewSpaceToMetersFactor;
    public float FrameIndex;
    public float VelocityFactor;
    public float ReactivenessScale;
    public float ShadingChangeScale;
    public float AccumulationAddedPerFrame;
    public float MinDisocclusionAccumulation;

    /// <summary>
    /// The constants of a new context, as AMD's <c>fsr3upscalerCreate</c> sets them. The largest render size stays for the
    /// context's life: AMD's dispatch sets it again in every frame to the same value.
    /// </summary>
    public static Fsr3Constants Created(Extent2D maxRenderSize, Extent2D outputSize) =>
        new()
        {
            MaxRenderSizeX = (int)maxRenderSize.Width,
            MaxRenderSizeY = (int)maxRenderSize.Height,
            MaxUpscaleSizeX = (int)outputSize.Width,
            MaxUpscaleSizeY = (int)outputSize.Height,
            VelocityFactor = 1f,
            ReactivenessScale = 1f,
            ShadingChangeScale = 1f,
            AccumulationAddedPerFrame = 1f / 3f,
            MinDisocclusionAccumulation = -1f / 3f,
        };

    /// <summary>
    /// The constants of the frame after <paramref name="last"/>, as AMD's <c>fsr3upscalerDispatch</c> computes them from
    /// the engine's inputs: reversed infinite depth, motion vectors at the render size in units of its width and height,
    /// no jitter cancellation, an output of the context's output size, and 1 m per view unit. AMD's host computes
    /// <see cref="DeltaPreExposure"/> on the CPU, and the engine's exposure step on the GPU, so the result keeps the value
    /// of <paramref name="last"/>. <paramref name="resetAccumulation"/> is the frame's reset or the context's first frame.
    /// </summary>
    public static Fsr3Constants Next(in Fsr3Constants last, in Fsr3Frame frame, bool resetAccumulation)
    {
        Fsr3Constants next = last;
        next.PreviousFrameJitterOffsetX = last.JitterOffsetX;
        next.PreviousFrameJitterOffsetY = last.JitterOffsetY;
        next.JitterOffsetX = frame.Jitter.X;
        next.JitterOffsetY = frame.Jitter.Y;
        next.PreviousFrameRenderSizeX = last.RenderSizeX;
        next.PreviousFrameRenderSizeY = last.RenderSizeY;
        next.RenderSizeX = (int)frame.RenderSize.Width;
        next.RenderSizeY = (int)frame.RenderSize.Height;

        // AMD derives the horizontal field of view from the vertical one, in single floats. Each tangent and arctangent
        // runs in double and rounds to float, which gives the correctly rounded float on every machine: the float
        // functions of the C libraries round differently, and x86-64's tanHalfFOV was 2 ulps from the Mac's (ter-qjyo).
        float aspect = frame.RenderSize.Width / (float)frame.RenderSize.Height;
        float horizontalFov = (float)Math.Atan((float)Math.Tan(frame.VerticalFov / 2) * aspect) * 2;
        next.TanHalfFov = (float)Math.Tan(horizontalFov * 0.5f);
        next.ViewSpaceToMetersFactor = 1f;
        next.SetDeviceToViewDepth(frame);

        next.PreviousFrameUpscaleSizeX = last.UpscaleSizeX;
        next.PreviousFrameUpscaleSizeY = last.UpscaleSizeY;
        next.UpscaleSizeX = last.MaxUpscaleSizeX;
        next.UpscaleSizeY = last.MaxUpscaleSizeY;
        next.DownscaleFactorX = next.RenderSizeX / (float)next.UpscaleSizeX;
        next.DownscaleFactorY = next.RenderSizeY / (float)next.UpscaleSizeY;

        // The motion vectors' scale is the render size, divided by the render size that they live in.
        next.MotionVectorScaleX = frame.RenderSize.Width / (float)next.RenderSizeX;
        next.MotionVectorScaleY = frame.RenderSize.Height / (float)next.RenderSizeY;

        // AMD moves the phase count used by the shaders one step per frame toward the count of this render size.
        int phaseCount = Fsr3Upscaler.JitterPhaseCount(frame.RenderSize.Width, (uint)next.UpscaleSizeX);
        if (resetAccumulation || last.JitterPhaseCount == 0)
            next.JitterPhaseCount = phaseCount;
        else if (phaseCount > (int)last.JitterPhaseCount)
            next.JitterPhaseCount = last.JitterPhaseCount + 1;
        else if (phaseCount < (int)last.JitterPhaseCount)
            next.JitterPhaseCount = last.JitterPhaseCount - 1;

        next.DeltaTime = Math.Clamp(frame.FrameMilliseconds / 1000f, 0f, 1f);
        next.FrameIndex = resetAccumulation ? 0f : last.FrameIndex + 1f;
        return next;
    }

    /// <summary>
    /// AMD's <c>setupDeviceDepthToViewSpaceDepthParams</c> for reversed infinite depth, with AMD's sample's planes: the
    /// near plane FLT_MAX and the far plane the camera's near plane.
    /// </summary>
    private void SetDeviceToViewDepth(in Fsr3Frame frame)
    {
        // After the swap of reversed depth, the far plane is the camera's near plane. AMD's elements of the inverse
        // projection for reversed infinite depth are c = FLT_EPSILON and e = that plane, and d = -1.
        DeviceToViewDepth0 = -(0f + FltEpsilon);
        DeviceToViewDepth1 = frame.NearMeters;
        float aspect = frame.RenderSize.Width / (float)frame.RenderSize.Height;
        float cotHalfFovY = (float)Math.Cos(0.5f * frame.VerticalFov) / (float)Math.Sin(0.5f * frame.VerticalFov);
        DeviceToViewDepth2 = 1f / (cotHalfFovY / aspect);
        DeviceToViewDepth3 = 1f / cotHalfFovY;
    }
}

/// <summary>This frame's values, from which FSR's constants derive.</summary>
internal readonly record struct Fsr3Frame
{
    /// <summary>RCAS sharpening from 0 (off) to 1 (strongest).</summary>
    public float Sharpness { get; init; }

    /// <summary>The size in pixels that this frame drew at, within the largest render size.</summary>
    public required Extent2D RenderSize { get; init; }

    /// <summary>
    /// AMD's jitter offset in render pixels: the negation of the offset that the frame's projection moved by, which is
    /// <see cref="Fsr3Upscaler.JitterOffset"/> of the draw.
    /// </summary>
    public required Vector2 Jitter { get; init; }

    /// <summary>The time from the last frame in ms, which AMD clamps to 0 to 1,000.</summary>
    public required float FrameMilliseconds { get; init; }

    /// <summary>The camera's near plane in meters, the far plane of AMD's reversed infinite depth.</summary>
    public required float NearMeters { get; init; }

    /// <summary>The vertical field of view of the projection in radians.</summary>
    public required float VerticalFov { get; init; }

    /// <summary>This frame reads no history, as after a cut.</summary>
    public required bool Reset { get; init; }
}
