using System.Collections.Immutable;

namespace Tertius.Rendering;

/// <summary>One Gaussian of a diffusion profile: its variance, in square meters, and its weight in each color channel.</summary>
internal readonly record struct ProfileGaussian(double Variance, Double3 Weight);

/// <summary>
/// A diffusion profile R_c(r), a sum of Gaussians per color channel, and the kernel of taps made from its marginal
/// (docs/plans/skin-and-hair.md, "The profile and the kernel").
/// </summary>
internal sealed record SkinProfile(ImmutableArray<ProfileGaussian> Gaussians)
{
    /// <summary>The largest tap offset, in meters.</summary>
    public const double Reach = 0.008;

    private const double SquareMillimeter = 1e-6;

    // The depth of the continued fraction of erfc, which at 2 and above gives erfc within 1e-15 of its value.
    private const int ContinuedFractionDepth = 60;

    /// <summary>The profile of human skin of d'Eon, Luebke and Enderton 2007. Each channel's weights sum to 1.</summary>
    public static SkinProfile Human { get; } =
        new([
            new(Variance: 0.0064 * SquareMillimeter, Weight: new(0.233, 0.455, 0.649)),
            new(Variance: 0.0484 * SquareMillimeter, Weight: new(0.100, 0.336, 0.344)),
            new(Variance: 0.187 * SquareMillimeter, Weight: new(0.118, 0.198, 0)),
            new(Variance: 0.567 * SquareMillimeter, Weight: new(0.113, 0.007, 0.007)),
            new(Variance: 1.99 * SquareMillimeter, Weight: new(0.358, 0.004, 0)),
            new(Variance: 7.41 * SquareMillimeter, Weight: new(0.078, 0, 0)),
        ]);

    /// <summary>P_c(x), the profile's marginal along one axis at <paramref name="x"/> meters, per meter.</summary>
    public Double3 Marginal(double x)
    {
        Double3 sum = Double3.Zero;
        foreach (ProfileGaussian gaussian in Gaussians)
            sum +=
                gaussian.Weight
                * (Math.Exp(-x * x / (2 * gaussian.Variance)) / Math.Sqrt(2 * Math.PI * gaussian.Variance));
        return sum;
    }

    /// <summary>
    /// The 2n + 1 taps of the kernel, from −Reach to Reach, with n = <paramref name="tapsPerSide"/>: tap i at
    /// Reach · (i / n)², holding the marginal's integral between the midpoints to its neighbors.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tapsPerSide"/> is below 1.</exception>
    public ScatterKernel Kernel(int tapsPerSide)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tapsPerSide, 1);
        var taps = new ScatterTap[2 * tapsPerSide + 1];
        // The profile is even, so each tap after the center has its mirror before it. The weights are differences of the
        // tails beyond the bounds, so that each channel's weights sum to 1 up to rounding.
        Double3 tailBeyondInner = TailBeyond(Bound(1, tapsPerSide));
        taps[tapsPerSide] = new(Offset: 0, Weight: new Double3(1, 1, 1) - 2 * tailBeyondInner);
        for (int i = 1; i <= tapsPerSide; i++)
        {
            Double3 tailBeyondOuter = i < tapsPerSide ? TailBeyond(Bound(i + 1, tapsPerSide)) : Double3.Zero;
            Double3 weight = tailBeyondInner - tailBeyondOuter;
            double offset = Offset(i, tapsPerSide);
            taps[tapsPerSide + i] = new(Offset: offset, Weight: weight);
            taps[tapsPerSide - i] = new(Offset: -offset, Weight: weight);
            tailBeyondInner = tailBeyondOuter;
        }
        return new([.. taps]);
    }

    /// <summary>x_i, the offset of tap <paramref name="i"/> of 0 to n, in meters.</summary>
    private static double Offset(int i, int tapsPerSide)
    {
        double share = (double)i / tapsPerSide;
        return Reach * share * share;
    }

    /// <summary>b_i, the midpoint between taps i − 1 and i, for i of 1 to n.</summary>
    private static double Bound(int i, int tapsPerSide) => (Offset(i - 1, tapsPerSide) + Offset(i, tapsPerSide)) / 2;

    /// <summary>The marginal's integral from <paramref name="bound"/> ≥ 0 meters to infinity.</summary>
    private Double3 TailBeyond(double bound)
    {
        Double3 sum = Double3.Zero;
        foreach (ProfileGaussian gaussian in Gaussians)
            sum += gaussian.Weight * (0.5 * Erfc(bound / Math.Sqrt(2 * gaussian.Variance)));
        return sum;
    }

    /// <summary>
    /// The complementary error function at <paramref name="x"/> ≥ 0, within a few units in the last place below 2 and
    /// within 1e-15 of its value from 2 on.
    /// </summary>
    private static double Erfc(double x)
    {
        if (x >= 2)
        {
            // The continued fraction erfc(x) = e^(−x²) / √π · 1 / (x + (1/2) / (x + (2/2) / (x + (3/2) / ...))).
            double denominator = x;
            for (int k = ContinuedFractionDepth; k >= 1; k--)
                denominator = x + k / 2.0 / denominator;
            return Math.Exp(-x * x) / (Math.Sqrt(Math.PI) * denominator);
        }
        // The series erf(x) = 2 / √π · e^(−x²) · Σ (2x²)^k x / (1 · 3 ··· (2k + 1)), whose terms are all positive.
        double term = x;
        double series = 0;
        for (int k = 1; term > 1e-17 * series; k++)
        {
            series += term;
            term *= 2 * x * x / (2 * k + 1);
        }
        return 1 - 2 / Math.Sqrt(Math.PI) * Math.Exp(-x * x) * series;
    }
}

/// <summary>One tap of a scatter kernel: its offset from the center, in meters, and its weight in each color channel.</summary>
internal readonly record struct ScatterTap(double Offset, Double3 Weight);

/// <summary>
/// The taps of one pass of the separable scatter, in the order of their offsets, with the center in the middle. The
/// renderer uploads them as 2n + 1 vec4 (docs/plans/skin-and-hair.md, "The profile and the kernel").
/// </summary>
internal sealed record ScatterKernel(ImmutableArray<ScatterTap> Taps);
