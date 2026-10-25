namespace Tertius.Rendering;

/// <summary>
/// Whether the renderer adds a glow around bright light, and its shape (ARCHITECTURE.md, "Bloom"). A built-in C# 15 union
/// of two class cases, so it allocates nothing beyond its case. The default value holds no case, and the renderer
/// refuses it.
/// </summary>
public union BloomSetting(NoBloom, Bloom)
{
    // The built-in union's ToString gives only the name of the union.
    public override readonly string ToString() => Value?.ToString() ?? "BloomSetting(none)";
}

/// <summary>No glow: the renderer builds no bloom images and no bloom pass, and the tone map reads only the HDR image.</summary>
public sealed record NoBloom;

/// <summary>
/// A glow around light that is brighter than white after the exposure, added on top of the image before the tone curve.
/// </summary>
public sealed record Bloom
{
    /// <summary>The most levels a bloom takes. A 16,384 px side halves to 1 px in 14 levels, so more add nothing.</summary>
    public const int MaxLevels = 16;

    /// <param name="threshold">The brightness after the exposure, as the largest channel, from which light glows in full. 0 or more.</param>
    /// <param name="knee">The width of the soft knee below the threshold, as a fraction of it, from 0 to 1.</param>
    /// <param name="strength">The glow's energy as a fraction of the light above the threshold. Positive.</param>
    /// <param name="levels">How many half-size images the glow spreads through, 1 to <see cref="MaxLevels"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside its range, or not finite.</exception>
    public Bloom(float threshold, float knee, float strength, int levels)
    {
        if (!float.IsFinite(threshold) || threshold < 0)
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "A threshold is finite and 0 or more.");
        if (knee is not (>= 0 and <= 1))
            throw new ArgumentOutOfRangeException(nameof(knee), knee, "A knee is a fraction of the threshold, 0 to 1.");
        if (!float.IsFinite(strength) || strength <= 0)
            throw new ArgumentOutOfRangeException(nameof(strength), strength, "A strength is finite and positive.");
        if (levels is < 1 or > MaxLevels)
            throw new ArgumentOutOfRangeException(nameof(levels), levels, $"A bloom has 1 to {MaxLevels} levels.");
        Threshold = threshold;
        Knee = knee;
        Strength = strength;
        Levels = levels;
    }

    /// <summary>The brightness after the exposure, as the largest channel, from which light glows in full.</summary>
    public float Threshold { get; }

    /// <summary>The width of the soft knee below <see cref="Threshold"/>, as a fraction of it: light from (1 − Knee) × Threshold glows a little.</summary>
    public float Knee { get; }

    /// <summary>The glow's energy as a fraction of the light above the threshold.</summary>
    public float Strength { get; }

    /// <summary>How many half-size images the glow spreads through. Each level doubles its reach.</summary>
    public int Levels { get; }
}
