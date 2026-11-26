namespace Tertius.Rendering;

/// <summary>
/// No screen tracer: the renderer records no ray plan, no rays and no resolve, and accumulates no indirect signal. Every
/// surface takes the environment's closed forms with every direction open (https://github.com/alexispurslane/tertius/wiki/temporal-renderer, section 8,
/// "The zero cases").
/// </summary>
public readonly record struct NoScreenTrace;

/// <summary>
/// Stochastic screen-space reflections from glossy surfaces.
/// </summary>
/// <param name="MaxRoughness">The perceptual roughness at which the environment fully supplies the reflection.</param>
/// <param name="ReflectionSteps">The most steps of a reflection ray's walk, 1 to <see cref="MaxStepsLimit"/>. A walk that takes them all misses.</param>
public readonly record struct ScreenTrace(float MaxRoughness, int ReflectionSteps)
{
    /// <summary>
    /// The width of the roughness band below <see cref="MaxRoughness"/> in which the specular sample blends the
    /// reflection ray's estimate into the environment reflection, linearly by the roughness, so that no edge shows.
    /// </summary>
    public const float HandOverWidth = 0.1f;

    /// <summary>The most steps of a walk, far past the caps of the design: it bounds the worst frame.</summary>
    public const int MaxStepsLimit = 4096;

    /// <summary>The design's setting: a MaxRoughness of 0.5, and a limit of 96 steps per reflection ray.</summary>
    public static ScreenTrace Default => new(MaxRoughness: 0.5f, ReflectionSteps: 96);

    /// <summary>The roughness below which the specular sample is the reflection ray's alone: the start of the hand-over band.</summary>
    internal float HandOverStart => MaxRoughness - HandOverWidth;
}

/// <summary>
/// Whether the screen tracer runs (https://github.com/alexispurslane/tertius/wiki/temporal-renderer, section 6): <see cref="NoScreenTrace"/> or
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
        return null;
    }
}
