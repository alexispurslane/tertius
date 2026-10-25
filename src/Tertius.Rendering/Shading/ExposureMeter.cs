using System.Diagnostics;
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

    /// <summary>
    /// The bright edge: the pre-exposed luminance in stops at the (1 − brightShare) quantile of the counts, inside its
    /// bin by the share of the bin's count below it; null when the histogram holds no count.
    /// </summary>
    public static float? BrightEdgeStops(ReadOnlySpan<uint> bins, float brightShare)
    {
        double total = 0;
        foreach (uint count in bins)
            total += count;
        if (total == 0)
            return null;

        double high = (1 - brightShare) * total;
        double end = 0;
        for (int bin = 0; bin < bins.Length; bin++)
        {
            double start = end;
            end += bins[bin];
            // high lies above 0 and at most total, so exactly one bin with a count holds it.
            bool holdsTheEdge = start < high && high <= end;
            if (holdsTheEdge)
                return (float)(LowestStops + (bin + (high - start) / bins[bin]) / BinsPerStop);
        }

        throw new UnreachableException();
    }

    /// <summary>The trimmed mean and the bright edge of a histogram, or null when it holds no count.</summary>
    public static (float TrimmedMeanStops, float BrightEdgeStops)? Reading(
        ReadOnlySpan<uint> bins,
        float darkShare,
        float brightShare
    )
    {
        if (TrimmedMeanStops(bins, darkShare, brightShare) is not { } mean)
            return null;
        if (BrightEdgeStops(bins, brightShare) is not { } edge)
            return null;
        return (mean, edge);
    }

    /// <summary>
    /// The target: the lower of the exposure that maps the trimmed mean to middle gray and the one that maps the bright
    /// edge to 1, plus the compensation, inside the clamp; the used stops when there is no reading.
    /// </summary>
    public static float TargetStops(
        (float TrimmedMeanStops, float BrightEdgeStops)? reading,
        float usedStops,
        float compensationStops,
        float minStops,
        float maxStops
    )
    {
        if (reading is not { TrimmedMeanStops: var mean, BrightEdgeStops: var edge })
            return usedStops;
        // The frame shaded with usedStops, so the radiance's stops are the metered stops minus them.
        float averageTarget = MathF.Log2(MiddleGray) - (mean - usedStops);
        float highlightTarget = -(edge - usedStops);
        return Math.Clamp(MathF.Min(averageTarget, highlightTarget) + compensationStops, minStops, maxStops);
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
