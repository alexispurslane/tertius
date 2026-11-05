using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>No temporal anti-aliasing or upscale: every pass draws at the output size, without jitter.</summary>
public readonly record struct NoUpscaling;

/// <summary>A fixed ratio of render pixels to output pixels on each axis, from 0.5 to 1.</summary>
public readonly record struct FixedRenderScale(float Scale);

/// <summary>The GPU frame target in ms and the allowed render scales on each axis.</summary>
public readonly record struct FrameTimeTarget(float Milliseconds, float MinScale, float MaxScale)
{
    /// <summary>A 60 Hz frame with scales from half-size to native.</summary>
    public static FrameTimeTarget Default => new(1000f / 60, 0.5f, 1);
}

/// <summary>The source of the render scale: fixed for deterministic captures, measured for windows.</summary>
[GeneratedUnion(typeof(FixedRenderScale), typeof(FrameTimeTarget))]
public readonly partial struct RenderScaleSetting
{
    internal float Largest =>
        this switch
        {
            FixedRenderScale fixedScale => fixedScale.Scale,
            FrameTimeTarget target => target.MaxScale,
            null => 1,
        };

    internal string? Problem =>
        this switch
        {
            FixedRenderScale fixedScale when fixedScale.Scale is not (>= 0.5f and <= 1) =>
                "FixedRenderScale.Scale must be finite and from 0.5 to 1.",
            FixedRenderScale => null,
            FrameTimeTarget target => ProblemOf(target),
            null => "Fsr3Upscaling requires a render scale setting.",
        };

    internal static string? ProblemOf(FrameTimeTarget target)
    {
        if (!float.IsFinite(target.Milliseconds) || target.Milliseconds <= 0)
            return "FrameTimeTarget.Milliseconds must be finite and positive.";
        if (
            target.MinScale is not (>= 0.5f and <= 1)
            || target.MaxScale < target.MinScale
            || target.MaxScale is not (>= 0.5f and <= 1)
        )
            return "FrameTimeTarget scales must be finite and from 0.5 to 1, with MinScale no larger than MaxScale.";
        return null;
    }
}

/// <summary>FSR 3.1 temporal anti-aliasing and upscale, with sharpening from 0 (off) to 1 (strongest).</summary>
public readonly record struct Fsr3Upscaling(RenderScaleSetting RenderScale, float Sharpness)
{
    /// <summary>The window's measured scale, with sharpening off until explicitly requested.</summary>
    public static Fsr3Upscaling Default => new(FrameTimeTarget.Default, 0);
}

/// <summary>The one anti-aliasing and upscale setting (docs/plans/temporal-renderer.md, section 4).</summary>
[GeneratedUnion(typeof(NoUpscaling), typeof(Fsr3Upscaling), ZeroCase = typeof(NoUpscaling))]
public readonly partial struct UpscalingSetting
{
    internal string? Problem =>
        this switch
        {
            NoUpscaling => null,
            Fsr3Upscaling fsr when fsr.Sharpness is not (>= 0 and <= 1) =>
                "Fsr3Upscaling.Sharpness must be finite and from 0 to 1.",
            Fsr3Upscaling fsr => fsr.RenderScale.Problem,
        };

    internal float LargestScale =>
        this switch
        {
            NoUpscaling => 1,
            Fsr3Upscaling fsr => fsr.RenderScale.Largest,
        };

    internal Extent2D RenderExtent(Extent2D output, float scale) =>
        this switch
        {
            NoUpscaling => output,
            Fsr3Upscaling => new(Scaled(output.Width, scale), Scaled(output.Height, scale)),
        };

    private static uint Scaled(uint pixels, float scale) => Math.Max(8, (uint)(pixels * scale) / 8 * 8);

    internal UpscalingSetting ForHeadless() =>
        this switch
        {
            NoUpscaling => this,
            Fsr3Upscaling { RenderScale: FixedRenderScale } => this,
            Fsr3Upscaling fsr => fsr with { RenderScale = new FixedRenderScale(2f / 3) },
        };
}
