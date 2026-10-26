namespace Tertius.Rendering;

/// <summary>A fixed exposure: every pass multiplies radiance by 2^<paramref name="Stops"/>.</summary>
/// <param name="Stops">The exposure in stops, from −64 to 64. One stop doubles the image's brightness. 0 keeps radiance as it is.</param>
public readonly record struct FixedExposure(float Stops);

/// <summary>
/// An exposure that follows the brightness of the surfaces in view over time (docs/plans/auto-exposure.md). The meter
/// maps the surface pixels' trimmed mean to middle gray, or their bright edge to 1 when that is lower, and the exposure
/// approaches that target with a time constant per direction.
/// </summary>
/// <param name="MinStops">The lowest exposure, in stops, from −64 to <paramref name="MaxStops"/>.</param>
/// <param name="MaxStops">The highest exposure, in stops, up to 64.</param>
/// <param name="CompensationStops">Stops added to the target before the clamp, from −64 to 64. +1 makes the settled image twice as bright.</param>
/// <param name="DarkShare">The share of the surface pixels that the meter drops from the dark end, 0 or more.</param>
/// <param name="BrightShare">The share that the meter drops from the bright end, 0 or more. The two shares add up to at most 0.99.</param>
/// <param name="IntoDarkSeconds">The time constant, in s, 0 or more, while the exposure rises because the view got darker.</param>
/// <param name="IntoLightSeconds">The time constant, in s, 0 or more, while the exposure falls because the view got brighter.</param>
public readonly record struct AutoExposure(
    float MinStops,
    float MaxStops,
    float CompensationStops,
    float DarkShare,
    float BrightShare,
    float IntoDarkSeconds,
    float IntoLightSeconds
)
{
    /// <summary>
    /// The renderer's default: −14 to +10 stops, no compensation, a tenth dropped at each end, 3 s into the dark and 1 s
    /// into the light. The clamp keeps 3 stops beyond a sunlit hull at 1 AU and beyond a lamp-lit cabin
    /// (docs/plans/local-light-shadows.md, decision 42).
    /// </summary>
    public static AutoExposure Default =>
        new(
            MinStops: -14,
            MaxStops: 10,
            CompensationStops: 0,
            DarkShare: 0.1f,
            BrightShare: 0.1f,
            IntoDarkSeconds: 3,
            IntoLightSeconds: 1
        );
}

/// <summary>
/// How the renderer chooses the exposure: <see cref="FixedExposure"/> or <see cref="AutoExposure"/>. The default value is
/// FixedExposure(0), today's look. It is a generated union (see <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(FixedExposure), typeof(AutoExposure), ZeroCase = typeof(FixedExposure))]
public readonly partial struct ExposureSetting
{
    /// <summary>The largest magnitude of a stops value in a setting.</summary>
    public const float StopsLimit = 64;

    /// <summary>Why the renderer cannot use the setting, or null when it can.</summary>
    internal string? Problem =>
        this switch
        {
            FixedExposure(var stops) => StopsProblem("FixedExposure.Stops", stops),
            AutoExposure auto => ProblemOf(auto),
        };

    /// <summary>The exposure in stops that the first frame seeds: the fixed stops, or 0 inside the clamp.</summary>
    internal float InitialStops =>
        this switch
        {
            FixedExposure(var stops) => stops,
            AutoExposure auto => Math.Clamp(0f, auto.MinStops, auto.MaxStops),
        };

    private static string? ProblemOf(AutoExposure auto)
    {
        if (StopsProblem("AutoExposure.MinStops", auto.MinStops) is { } min)
            return min;
        if (StopsProblem("AutoExposure.MaxStops", auto.MaxStops) is { } max)
            return max;
        if (StopsProblem("AutoExposure.CompensationStops", auto.CompensationStops) is { } compensation)
            return compensation;
        if (auto.MinStops > auto.MaxStops)
            return $"AutoExposure.MinStops {auto.MinStops} must not lie above MaxStops {auto.MaxStops}.";
        if (NonNegativeProblem("AutoExposure.DarkShare", auto.DarkShare) is { } dark)
            return dark;
        if (NonNegativeProblem("AutoExposure.BrightShare", auto.BrightShare) is { } bright)
            return bright;
        // The meter must keep some pixels, or the trimmed mean divides 0 by 0. Shares that add up to just below 1 can keep
        // none, because the adapt step computes the trim's two ends in single floats, which can round to one value.
        if (auto.DarkShare + auto.BrightShare > 0.99f)
            return $"AutoExposure.DarkShare {auto.DarkShare} and BrightShare {auto.BrightShare} must add up to at most 0.99.";
        if (NonNegativeProblem("AutoExposure.IntoDarkSeconds", auto.IntoDarkSeconds) is { } intoDark)
            return intoDark;
        return NonNegativeProblem("AutoExposure.IntoLightSeconds", auto.IntoLightSeconds);
    }

    private static string? StopsProblem(string name, float stops) =>
        float.IsFinite(stops) && Math.Abs(stops) <= StopsLimit
            ? null
            : $"{name} {stops} must be finite and from -{StopsLimit} to {StopsLimit}.";

    private static string? NonNegativeProblem(string name, float value) =>
        float.IsFinite(value) && value >= 0 ? null : $"{name} {value} must be finite and 0 or more.";
}
