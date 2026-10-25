namespace Tertius.Rendering;

/// <summary>No glow: the renderer builds no bloom images and no bloom pass, and the tone map reads only the HDR image.</summary>
public readonly record struct NoBloom;

/// <summary>
/// A glow around light that is brighter than white after the exposure, added on top of the image before the tone curve.
/// </summary>
/// <param name="Threshold">
/// The brightness after the exposure, as the largest channel, at the center of the soft knee. 0 or more. Above the
/// knee's top, (1 + Knee) × Threshold, the glow is the light above the threshold.
/// </param>
/// <param name="Knee">
/// The half-width of the soft knee around <paramref name="Threshold"/>, as a fraction of it, from 0 to 1: light from
/// (1 − Knee) × Threshold glows a little, and the glow grows smoothly to the light above the threshold at (1 + Knee) ×
/// Threshold.
/// </param>
/// <param name="Strength">The glow's energy as a fraction of the light above the threshold. Positive.</param>
/// <param name="Levels">
/// How many half-size images the glow spreads through, 1 to <see cref="MaxLevels"/>. Each level doubles its reach.
/// </param>
public readonly record struct Bloom(float Threshold, float Knee, float Strength, int Levels)
{
    /// <summary>
    /// The most levels a bloom takes: enough to halve a 16,384 px side, the largest image, to 1 px. Past 1 px, each level
    /// would add only the image's mean glow everywhere, a flat veil.
    /// </summary>
    public const int MaxLevels = 14;

    /// <summary>The renderer's default: a threshold of 1, a knee of 0.5, a strength of 0.25 and 6 levels.</summary>
    public static Bloom Default => new(Threshold: 1, Knee: 0.5f, Strength: 0.25f, Levels: 6);
}

/// <summary>
/// Whether the renderer adds a glow around bright light, and its shape (ARCHITECTURE.md, "Bloom"): <see cref="NoBloom"/>
/// or <see cref="Bloom"/>. The default value is NoBloom, today's look without a glow. It is a generated union (see
/// <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(NoBloom), typeof(Bloom), ZeroCase = typeof(NoBloom))]
public readonly partial struct BloomSetting
{
    /// <summary>Why the renderer cannot use the setting, or null when it can.</summary>
    internal string? Problem =>
        this switch
        {
            NoBloom => null,
            Bloom bloom => ProblemOf(bloom),
        };

    private static string? ProblemOf(Bloom bloom)
    {
        if (!float.IsFinite(bloom.Threshold) || bloom.Threshold < 0)
            return $"Bloom.Threshold {bloom.Threshold} must be finite and 0 or more.";
        if (bloom.Knee is not (>= 0 and <= 1))
            return $"Bloom.Knee {bloom.Knee} is a fraction of the threshold, 0 to 1.";
        if (!float.IsFinite(bloom.Strength) || bloom.Strength <= 0)
            return $"Bloom.Strength {bloom.Strength} must be finite and positive.";
        if (bloom.Levels is < 1 or > Bloom.MaxLevels)
            return $"Bloom.Levels {bloom.Levels} must be 1 to {Bloom.MaxLevels}.";
        return null;
    }
}
