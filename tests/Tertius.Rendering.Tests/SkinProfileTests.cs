namespace Tertius.Rendering.Tests;

/// <summary>
/// The diffusion profile of human skin and the kernel of taps made from it (docs/plans/skin-and-hair.md, "The profile
/// and the kernel"). Integrals use Simpson's rule on steps far below the narrowest Gaussian's width of 0.08 mm.
/// </summary>
public sealed class SkinProfileTests
{
    private const int TapsPerSide = 8;

    // Far past the widest Gaussian, whose width is 2.72 mm: the profile beyond it is below 1e-100.
    private const double Far = 0.1;

    public static TheoryData<int> TapCounts => [with(Enumerable.Range(1, 16))];

    /// <summary>Simpson's rule of <paramref name="integrand"/> from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static Double3 Integral(Func<double, Double3> integrand, double from, double to, int intervals)
    {
        double step = (to - from) / intervals;
        Double3 sum = integrand(from) + integrand(to);
        for (int i = 1; i < intervals; i++)
            sum += integrand(from + i * step) * (i % 2 == 0 ? 2 : 4);
        return sum * (step / 3);
    }

    private static Double3 SecondMomentOfTheMarginal() =>
        Integral(integrand: x => SkinProfile.Human.Marginal(x) * (x * x), from: -Far, to: Far, intervals: 200_000);

    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance, string because)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance, because);
        actual.Y.Should().BeApproximately(expected.Y, tolerance, because);
        actual.Z.Should().BeApproximately(expected.Z, tolerance, because);
    }

    [Fact]
    public void EachChannelOfTheHumanProfileSumsToOne()
    {
        Double3 total = Integral(integrand: SkinProfile.Human.Marginal, from: -Far, to: Far, intervals: 200_000);

        ShouldBeNear(
            actual: total,
            expected: new(1, 1, 1),
            tolerance: 1e-9,
            because: "the skin keeps the light that enters it, and only moves it"
        );
    }

    [Fact]
    public void RedScattersFartherThanGreenAndGreenFartherThanBlue()
    {
        Double3 variance = SecondMomentOfTheMarginal();

        variance.X.Should().BeGreaterThan(variance.Y, "red light travels farthest under the skin");
        variance.Y.Should().BeGreaterThan(variance.Z, "blue light is absorbed soonest");
    }

    [Theory]
    [MemberData(nameof(TapCounts))]
    public void TheKernelsWeightsSumToOneInEachChannel(int tapsPerSide)
    {
        ScatterKernel kernel = SkinProfile.Human.Kernel(tapsPerSide);

        Double3 total = kernel.Taps.Aggregate(Double3.Zero, (sum, tap) => sum + tap.Weight);

        ShouldBeNear(actual: total, expected: new(1, 1, 1), tolerance: 1e-14, because: "the taps split the whole line");
    }

    [Fact]
    public void TheKernelIsSymmetric()
    {
        ScatterKernel kernel = SkinProfile.Human.Kernel(TapsPerSide);

        kernel.Taps.Should().HaveCount(2 * TapsPerSide + 1);
        kernel.Taps[TapsPerSide].Offset.Should().Be(0, "the middle tap is the center");
        for (int i = 1; i <= TapsPerSide; i++)
        {
            ScatterTap after = kernel.Taps[TapsPerSide + i];
            ScatterTap before = kernel.Taps[TapsPerSide - i];
            before.Offset.Should().Be(-after.Offset);
            before.Weight.Should().Be(after.Weight, "the profile is the same on both sides of a point");
        }
    }

    [Fact]
    public void TheTapsCrowdTowardTheCenter()
    {
        ScatterKernel kernel = SkinProfile.Human.Kernel(TapsPerSide);

        kernel
            .Taps[TapsPerSide + 1]
            .Offset.Should()
            .BeApproximately(SkinProfile.Reach / (TapsPerSide * TapsPerSide), 1e-18);
        kernel.Taps[2 * TapsPerSide].Offset.Should().Be(SkinProfile.Reach);
        for (int i = 1; i < 2 * TapsPerSide; i++)
            kernel
                .Taps[i]
                .Offset.Should()
                .BeGreaterThan(kernel.Taps[i - 1].Offset, "the taps run from −Reach to Reach");
    }

    [Fact]
    public void TheOutermostTapsHoldTheTails()
    {
        ScatterKernel kernel = SkinProfile.Human.Kernel(TapsPerSide);
        double lastBound = (kernel.Taps[2 * TapsPerSide - 1].Offset + kernel.Taps[2 * TapsPerSide].Offset) / 2;

        Double3 tail = Integral(integrand: SkinProfile.Human.Marginal, from: lastBound, to: Far, intervals: 200_000);

        ShouldBeNear(
            actual: kernel.Taps[2 * TapsPerSide].Weight,
            expected: tail,
            tolerance: 1e-12,
            because: "the outermost tap holds all the light beyond the midpoint to its neighbor"
        );
        kernel.Taps[0].Weight.Should().Be(kernel.Taps[2 * TapsPerSide].Weight);
    }

    [Fact]
    public void TheKernelsSecondMomentIsTheProfilesVariance()
    {
        ScatterKernel kernel = SkinProfile.Human.Kernel(TapsPerSide);
        Double3 profileVariance = SkinProfile.Human.Gaussians.Aggregate(
            Double3.Zero,
            (sum, gaussian) => sum + gaussian.Weight * gaussian.Variance
        );

        Double3 kernelVariance = kernel.Taps.Aggregate(
            Double3.Zero,
            (sum, tap) => sum + tap.Weight * (tap.Offset * tap.Offset)
        );

        // Measured on 2026-10-26: −0.23 % in red, +0.16 % in green, +1.6 % in blue.
        Double3 error = (kernelVariance - profileVariance) / profileVariance;
        Math.Abs(error.X).Should().BeLessThan(0.05, "the red light's spread survives the taps");
        Math.Abs(error.Y).Should().BeLessThan(0.05, "the green light's spread survives the taps");
        Math.Abs(error.Z).Should().BeLessThan(0.05, "the blue light's spread survives the taps");
        ShouldBeNear(
            actual: profileVariance,
            expected: SecondMomentOfTheMarginal(),
            tolerance: 1e-12,
            because: "the marginal's variance is the weighted sum of its Gaussians' variances"
        );
    }
}
