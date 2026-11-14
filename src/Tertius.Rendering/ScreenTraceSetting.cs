namespace Tertius.Rendering;

/// <summary>
/// No screen tracer: the renderer records no ray plan, no rays and no resolve, and accumulates no indirect signal. Every
/// surface takes the environment's closed forms with every direction open (docs/plans/temporal-renderer.md, section 8,
/// "The zero cases").
/// </summary>
public readonly record struct NoScreenTrace;

/// <summary>
/// The screen tracer (docs/plans/temporal-renderer.md, section 6): each lit pixel gets a diffuse and a specular sample in
/// every frame, from rays that walk the depth pyramid's two depths. The two indirect signals accumulate them.
/// </summary>
/// <param name="MaxRoughness">
/// The roughness above which a pixel traces no reflection ray, and its specular sample comes from the diffuse rays alone:
/// <see cref="HandOverWidth"/> to 1.
/// </param>
/// <param name="ReflectionSteps">The most steps of a reflection ray's walk, 1 to <see cref="MaxStepsLimit"/>. A walk that takes them all misses.</param>
/// <param name="DiffuseSteps">The most steps of a diffuse ray's walk, 1 to <see cref="MaxStepsLimit"/>.</param>
public readonly record struct ScreenTrace(float MaxRoughness, int ReflectionSteps, int DiffuseSteps)
{
    /// <summary>
    /// The width of the roughness band below <see cref="MaxRoughness"/> in which the specular sample blends the
    /// reflection ray's estimate into the diffuse rays' estimate, linearly by the roughness, so that no edge shows.
    /// </summary>
    public const float HandOverWidth = 0.1f;

    /// <summary>The most steps of a walk, far past the caps of the design: it bounds the worst frame.</summary>
    public const int MaxStepsLimit = 4096;

    /// <summary>The design's setting: a MaxRoughness of 0.5, and the caps of decision 41, 96 and 62 steps.</summary>
    public static ScreenTrace Default => new(MaxRoughness: 0.5f, ReflectionSteps: 96, DiffuseSteps: 62);

    /// <summary>The roughness below which the specular sample is the reflection ray's alone: the start of the hand-over band.</summary>
    internal float HandOverStart => MaxRoughness - HandOverWidth;
}

/// <summary>
/// Whether the screen tracer runs (docs/plans/temporal-renderer.md, section 6): <see cref="NoScreenTrace"/> or
/// <see cref="ScreenTrace"/>. The default value is NoScreenTrace. It is a generated union (see
/// <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(NoScreenTrace), typeof(ScreenTrace), ZeroCase = typeof(NoScreenTrace))]
public readonly partial struct ScreenTraceSetting
{
    /// <summary>Why the renderer cannot use the setting, or null when it can.</summary>
    internal string? Problem =>
        this switch
        {
            NoScreenTrace => null,
            ScreenTrace trace => ProblemOf(trace),
        };

    private static string? ProblemOf(ScreenTrace trace)
    {
        // The band must lie inside the roughness range. The negated comparison also refuses NaN.
        if (trace.MaxRoughness is not (>= ScreenTrace.HandOverWidth and <= 1))
            return $"ScreenTrace.MaxRoughness must lie from {ScreenTrace.HandOverWidth} to 1, not {trace.MaxRoughness}.";
        if (trace.ReflectionSteps is < 1 or > ScreenTrace.MaxStepsLimit)
            return $"ScreenTrace.ReflectionSteps must be 1 to {ScreenTrace.MaxStepsLimit}, not {trace.ReflectionSteps}.";
        if (trace.DiffuseSteps is < 1 or > ScreenTrace.MaxStepsLimit)
            return $"ScreenTrace.DiffuseSteps must be 1 to {ScreenTrace.MaxStepsLimit}, not {trace.DiffuseSteps}.";
        return null;
    }
}
