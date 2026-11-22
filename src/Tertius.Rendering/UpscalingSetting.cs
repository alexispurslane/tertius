using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>No temporal anti-aliasing or upscale: every pass draws at the output size, without jitter.</summary>
public readonly record struct NoUpscaling;

/// <summary>
/// The largest ratio of render pixels to output pixels on each axis, from 0.5 to 1. A draw takes the largest size of
/// exactly the output's aspect at or below it, and at least half the output (ARCHITECTURE.md, "Temporal upscaling and
/// render size").
/// </summary>
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

    /// <summary>
    /// The active size of <paramref name="output"/> at the requested <paramref name="scale"/>: a whole number of steps
    /// of the output's reduced ratio, so that it has exactly the output's aspect (ter-qt72). A step is the output divided
    /// by the greatest common divisor of its width and height, 16 × 9 px for 1920 × 1080. The size is the largest step
    /// count at or below the scale, inside the scale limits. Without upscaling, it is the output.
    /// </summary>
    /// <exception cref="ArgumentException">The output has no such size inside the limits (<see cref="Supports"/>).</exception>
    internal Extent2D RenderExtent(Extent2D output, float scale)
    {
        if (this is NoUpscaling)
            return output;
        if (OutputProblem(output) is { } problem)
            throw new ArgumentException(problem, nameof(output));
        uint divisor = Divisor(output);
        (uint first, uint last) = Steps(divisor);
        uint steps = Math.Clamp(StepsAtMost(scale, divisor), first, last);
        return new(output.Width / divisor * steps, output.Height / divisor * steps);
    }

    /// <summary>
    /// Whether the renderer can draw <paramref name="output"/>: it has an area, and it has as many active sizes inside
    /// the scale limits as the setting needs. A fixed scale needs one. A frame-time target with a range needs at least
    /// as many as the eighths of the output inside its limits, and at least two, so that the controller's choice is no
    /// coarser than an eighth: five for 0.5 to 1. So 1366 × 768, whose greatest common divisor 2 gives only 0.5 and 1,
    /// is not drawable under the default target, and 1680 × 1050, with 106 sizes from 0.5 to 1, is. Every positive
    /// output has the native size, so a native-only setting draws all.
    /// </summary>
    internal bool Supports(Extent2D output) => OutputProblem(output) == null;

    /// <summary>Why the renderer cannot draw <paramref name="output"/>, or null when it can (<see cref="Supports"/>).</summary>
    internal string? OutputProblem(Extent2D output)
    {
        if (output.Width == 0 || output.Height == 0)
            return $"The output {output.Width} × {output.Height} has no area.";
        if (HasSizes(Divisor(output)))
            return null;
        (float smallest, float largest) = ScaleLimits;
        return $"The output {output.Width} × {output.Height} has fewer than {SizesNeeded} sizes of exactly its aspect "
            + $"from {smallest} to {largest} of it: its width and height have the greatest common divisor "
            + $"{Divisor(output)}.";
    }

    /// <summary>
    /// The window output nearest to <paramref name="output"/> that <see cref="Supports"/>: the output itself when the
    /// renderer can draw it or it has no area, else each axis rounded to the nearest multiple of the window grid, ties
    /// up. The grid is the smallest number of pixels from 8 up whose square has the sizes that the setting needs. A
    /// multiple of the grid on both axes has them too, because its greatest common divisor is a multiple of the grid.
    /// The grid starts at 8, the grid of the eighths that a frame-time target needs, and an axis moves by at most half
    /// the grid.
    /// </summary>
    /// <exception cref="NotSupportedException">No grid up to 65,536 px gives the sizes: the scale limits lie too close.</exception>
    internal Extent2D SnapOutput(Extent2D output)
    {
        if (output.Width == 0 || output.Height == 0 || Supports(output))
            return output;
        uint grid = 8;
        while (!HasSizes(grid))
        {
            if (grid == 65536)
                throw new NotSupportedException(
                    $"No window of up to 65,536 px has {SizesNeeded} sizes of exactly its aspect from "
                        + $"{ScaleLimits.Smallest} to {ScaleLimits.Largest} of it."
                );
            grid++;
        }
        return new(Snapped(output.Width, grid), Snapped(output.Height, grid));
    }

    /// <summary>
    /// The smallest and the largest scale that a draw may realize: native without upscaling, from 0.5 to a fixed scale,
    /// and a frame-time target's own limits. They are hard limits: no size outside them is drawn.
    /// </summary>
    private (float Smallest, float Largest) ScaleLimits =>
        this switch
        {
            NoUpscaling => (1, 1),
            Fsr3Upscaling { RenderScale: FixedRenderScale fixedScale } => (0.5f, fixedScale.Scale),
            Fsr3Upscaling { RenderScale: FrameTimeTarget target } => (target.MinScale, target.MaxScale),
            Fsr3Upscaling => throw new InvalidOperationException("Fsr3Upscaling requires a render scale setting."),
        };

    /// <summary>Whether an output whose width and height have the greatest common divisor <paramref name="divisor"/> has
    /// the active sizes that the setting needs inside its limits (<see cref="Supports"/>).</summary>
    private bool HasSizes(uint divisor) => SizesInside(divisor) >= SizesNeeded;

    /// <summary>How many active sizes an output needs inside the limits (<see cref="Supports"/>).</summary>
    private uint SizesNeeded =>
        this is Fsr3Upscaling { RenderScale: FrameTimeTarget target } && target.MinScale < target.MaxScale
            ? Math.Max(2, SizesInside(8))
            : 1;

    /// <summary>How many step counts of an output with <paramref name="divisor"/> steps lie inside the limits.</summary>
    private uint SizesInside(uint divisor)
    {
        (uint first, uint last) = Steps(divisor);
        return last + 1 - first;
    }

    /// <summary>
    /// The fewest and the most steps of an output with <paramref name="divisor"/> steps whose realized scale lies
    /// inside the limits. The first exceeds the last, by one, when none does.
    /// </summary>
    private (uint First, uint Last) Steps(uint divisor)
    {
        (float smallest, float largest) = ScaleLimits;
        uint last = StepsAtMost(largest, divisor);
        // The product of a float and a divisor below 2^24 is exact in double, so the ceiling's ratio is at least the
        // limit and rounds to a float at least as large. A ratio just below the limit can round up to it, as 72 of 120
        // does to 0.6f, which lies above 0.6: such a step also counts.
        uint first = Math.Max(1, (uint)Math.Ceiling(smallest * (double)divisor));
        while (first > 1 && Realized(first - 1, divisor) >= smallest)
            first--;
        return (first, last);
    }

    /// <summary>The most steps of <paramref name="divisor"/>, at least 0, whose realized scale is at most
    /// <paramref name="scale"/>.</summary>
    private static uint StepsAtMost(float scale, uint divisor)
    {
        // As in Steps: the floor's ratio rounds to at most the scale, and a ratio just above the scale can round down
        // to it, as 84 of 120 does to 0.7f, which lies below 0.7: such a step also counts.
        uint steps = (uint)Math.Clamp(Math.Floor(scale * (double)divisor), 0, divisor);
        while (steps < divisor && Realized(steps + 1, divisor) <= scale)
            steps++;
        return steps;
    }

    /// <summary>
    /// The scale that <paramref name="steps"/> of <paramref name="divisor"/> realize, as the renderer computes it from
    /// the drawn width over the output width. Both divisions round the same ratio, so they give the same float.
    /// </summary>
    private static float Realized(uint steps, uint divisor) => steps / (float)divisor;

    private static uint Divisor(Extent2D output)
    {
        uint width = output.Width,
            height = output.Height;
        while (height != 0)
            (width, height) = (height, width % height);
        return width;
    }

    private static uint Snapped(uint pixels, uint grid) => checked(Math.Max(1, (pixels + grid / 2) / grid) * grid);

    internal UpscalingSetting ForHeadless() =>
        this switch
        {
            NoUpscaling => this,
            Fsr3Upscaling { RenderScale: FixedRenderScale } => this,
            Fsr3Upscaling fsr => fsr with { RenderScale = new FixedRenderScale(2f / 3) },
        };
}
