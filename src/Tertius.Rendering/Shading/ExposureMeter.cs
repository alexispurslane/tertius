using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The C# copy of the exposure's meter and approach (docs/plans/auto-exposure.md, "The math"). meter.glsl,
/// exposure_meter.comp and exposure_adapt.comp compute the same, and ExposureShaderTests pins them to it.
/// </summary>
internal static class ExposureMeter
{
    public const int Bins = 256;
    public const int BinsPerStop = 8;
    public const float LowestStops = -16;
    public const float MiddleGray = 0.18f;
    public static readonly Vector3 LuminanceWeights = new(0.2126f, 0.7152f, 0.0722f);

    /// <summary>The luminance of linear Rec. 709 radiance.</summary>
    public static float Luminance(Vector3 radiance) => Vector3.Dot(radiance, LuminanceWeights);

    /// <summary>The bin of a pre-exposed luminance: the first for 2^LowestStops or less, the last for 2^16 or more.</summary>
    public static int BinOf(float luminance)
    {
        float stops = MathF.Log2(MathF.Max(luminance, MathF.Pow(2, LowestStops)));
        return Math.Clamp((int)MathF.Floor((stops - LowestStops) * BinsPerStop), 0, Bins - 1);
    }

    /// <summary>The log2 of the pre-exposed luminance at the center of <paramref name="bin"/>.</summary>
    public static float BinCenterStops(int bin) => LowestStops + (bin + 0.5f) / BinsPerStop;

    /// <summary>The trimmed mean in stops of pre-exposed luminance, or null when the histogram holds no count.</summary>
    public static float? TrimmedMeanStops(ReadOnlySpan<uint> bins, float darkShare, float brightShare)
    {
        double total = 0;
        foreach (uint count in bins)
            total += count;
        if (total == 0)
            return null;

        double low = darkShare * total;
        double high = (1 - brightShare) * total;
        double end = 0;
        double weightedStops = 0;
        double kept = 0;
        for (int bin = 0; bin < bins.Length; bin++)
        {
            double start = end;
            end += bins[bin];
            double weight = Math.Max(0, Math.Min(end, high) - Math.Max(start, low));
            weightedStops += weight * BinCenterStops(bin);
            kept += weight;
        }

        return (float)(weightedStops / kept);
    }

    /// <summary>The target: middle gray for the trimmed mean, with the compensation, inside the clamp; the used stops when there is no mean.</summary>
    public static float TargetStops(
        float? trimmedMeanStops,
        float usedStops,
        float compensationStops,
        float minStops,
        float maxStops
    )
    {
        if (trimmedMeanStops is not { } mean)
            return usedStops;
        // The frame shaded with usedStops, so the radiance's mean is the metered mean minus them.
        float radianceMean = mean - usedStops;
        return Math.Clamp(MathF.Log2(MiddleGray) - radianceMean + compensationStops, minStops, maxStops);
    }

    /// <summary>1 − exp(−seconds / timeConstant), in doubles: 0 for a step of 0 or less, 1 for an endless step or a time constant of 0.</summary>
    public static float ApproachFactor(float seconds, float timeConstant) =>
        seconds > 0 ? (float)(1 - Math.Exp(-(double)seconds / timeConstant)) : 0;

    /// <summary>The next exposure: stops + (target − stops) times the factor of the direction.</summary>
    public static float Approach(float stops, float target, float intoDarkFactor, float intoLightFactor)
    {
        // A target above the exposure means the view got darker, so the exposure rises.
        float factor = target > stops ? intoDarkFactor : intoLightFactor;
        return stops + (target - stops) * factor;
    }
}
