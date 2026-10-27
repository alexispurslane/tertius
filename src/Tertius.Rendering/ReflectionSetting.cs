namespace Tertius.Rendering;

/// <summary>
/// No screen-space reflections: the renderer builds no pyramids and no reflection pass, and every glossy surface keeps
/// the ambient specular term.
/// </summary>
public readonly record struct NoReflections;

/// <summary>
/// Reflections of what is on the screen, traced through a min-max depth pyramid and blurred by a cone sized by the
/// roughness (docs/plans/screen-space-reflections.md). Where a ray finds nothing on the screen, the surface keeps the
/// ambient specular term.
/// </summary>
/// <param name="MaxRoughness">The roughness at which reflections have faded out, above 0.045 and at most 1. Surfaces up to half of it reflect in full.</param>
/// <param name="MaxSteps">The most cells that one ray examines, 1 to <see cref="MaxStepsLimit"/>. A ray that uses them all misses.</param>
/// <param name="Resolution">The pixels that start a ray: one of each block of 2 × 2, or every pixel.</param>
public readonly record struct ConeTracedReflections(float MaxRoughness, int MaxSteps, TraceResolution Resolution)
{
    /// <summary>The most steps a ray takes. 8 times the default, far past any measured need; it bounds the worst frame.</summary>
    public const int MaxStepsLimit = 1024;

    /// <summary>
    /// The renderer's default: reflections up to a roughness of 0.6, Unreal Engine 4's default, in 128 steps, traced at
    /// half resolution, which brings the reflections' passes to their share of the frame (ticket ter-ff6y).
    /// </summary>
    public static ConeTracedReflections Default =>
        new(MaxRoughness: 0.6f, MaxSteps: 128, Resolution: TraceResolution.Half);

    /// <summary>The weight of the reflection of a surface of this floored roughness: 1 up to half the maximum, 0 at it.</summary>
    internal float RoughnessFade(float roughness) => Math.Clamp(2 - 2 * roughness / MaxRoughness, 0, 1);
}

/// <summary>
/// The pixels of <see cref="ConeTracedReflections"/> that start a ray (docs/plans/screen-space-reflections.md, "The
/// half-resolution trace").
/// </summary>
public enum TraceResolution
{
    /// <summary>
    /// One ray for each block of 2 × 2 pixels, from its top-left pixel; an upsample that respects depth and normal edges
    /// spreads the rays. Where the blended rays hit different surfaces, at a reflected outline, a pixel traces its own ray.
    /// </summary>
    Half,

    /// <summary>One ray for every pixel.</summary>
    Full,
}

/// <summary>
/// Whether glossy surfaces reflect what is on the screen, and how (docs/plans/screen-space-reflections.md, "The
/// setting"): <see cref="NoReflections"/> or <see cref="ConeTracedReflections"/>. The default value is NoReflections,
/// the look from before the reflections. It is a generated union (see <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(NoReflections), typeof(ConeTracedReflections), ZeroCase = typeof(NoReflections))]
public readonly partial struct ReflectionSetting
{
    /// <summary>Why the renderer cannot use the setting, or null when it can.</summary>
    internal string? Problem =>
        this switch
        {
            NoReflections => null,
            ConeTracedReflections reflections => ProblemOf(reflections),
        };

    private static string? ProblemOf(ConeTracedReflections reflections)
    {
        // Below the floor no surface reflects, which is NoReflections. The negated comparison also refuses NaN.
        bool roughnessInRange = reflections.MaxRoughness is > ((float)GgxLobe.MinRoughness) and <= 1;
        if (!roughnessInRange)
            return $"ConeTracedReflections.MaxRoughness must lie above the roughness floor {GgxLobe.MinRoughness} and at most 1, not {reflections.MaxRoughness}.";
        if (reflections.MaxSteps is < 1 or > ConeTracedReflections.MaxStepsLimit)
            return $"ConeTracedReflections.MaxSteps must be 1 to {ConeTracedReflections.MaxStepsLimit}, not {reflections.MaxSteps}.";
        if (reflections.Resolution is not (TraceResolution.Half or TraceResolution.Full))
            return $"ConeTracedReflections.Resolution must be Half or Full, not {(int)reflections.Resolution}.";
        return null;
    }
}
