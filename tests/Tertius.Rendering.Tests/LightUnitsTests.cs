using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="LightUnits"/>: the intensity of a point and a spot light from its power, the cutoff irradiance from the
/// highest exposure, the reach, the line between near and far, the angular radius, the irradiance at the eye and the
/// interpolated axis (docs/plans/local-light-shadows.md, "The cutoff and the reach").
/// </summary>
public sealed class LightUnitsTests
{
    /// <summary>A light of <paramref name="intensity"/> W/sr, its color, radius and distance from the eye.</summary>
    private static PreparedLight Light(float intensity, Vector3 color, float radius, double distance) =>
        new()
        {
            RadiantIntensity = color * intensity,
            Radius = radius,
            Distance = distance,
        };

    /// <summary>Hable's f(x) with his published constants (GDC 2010, "Uncharted 2: HDR Lighting").</summary>
    private static double HableF(double x)
    {
        const double a = 0.15,
            b = 0.50,
            c = 0.10,
            d = 0.20,
            e = 0.02,
            f = 0.30;
        return (x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f) - e / f;
    }

    /// <summary>The linear color that Hable's curve gives a linear HDR channel, with his exposure bias of 2 and white of 11.2.</summary>
    private static double Hable(double x) => Math.Clamp(HableF(2 * x) / HableF(11.2), 0, 1);

    /// <summary>The linear color that Stephen Hill's ACES fitted curve gives a gray HDR channel, the largest of its three.</summary>
    private static double AcesFittedGray(double x)
    {
        // Row-major, as Hill's HLSL writes them.
        double[,] input =
        {
            { 0.59719, 0.35458, 0.04823 },
            { 0.07600, 0.90834, 0.01566 },
            { 0.02840, 0.13383, 0.83777 },
        };
        double[,] output =
        {
            { 1.60475, -0.53108, -0.07367 },
            { -0.10208, 1.10813, -0.00605 },
            { -0.00327, -0.07276, 1.07602 },
        };
        double[] fitted = new double[3];
        for (int row = 0; row < 3; row++)
        {
            double ap1 = (input[row, 0] + input[row, 1] + input[row, 2]) * x;
            double a = ap1 * (ap1 + 0.0245786) - 0.000090537;
            double b = ap1 * (0.983729 * ap1 + 0.4329510) + 0.238081;
            fitted[row] = a / b;
        }

        double largest = 0;
        for (int row = 0; row < 3; row++)
            largest = Math.Max(
                largest,
                Math.Clamp(output[row, 0] * fitted[0] + output[row, 1] * fitted[1] + output[row, 2] * fitted[2], 0, 1)
            );
        return largest;
    }

    /// <summary>The sRGB encoding of a linear channel in [0, 1], in 8-bit steps.</summary>
    private static double EncodedSteps(double linear) =>
        255 * (linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055);

    [Fact]
    public void APointLightSendsItsPowerEquallyInEveryDirection() =>
        (LightUnits.PointIntensity(power: 10) * 4 * Math.PI)
            .Should()
            .BeApproximately(10, 1e-12, "the intensity over the whole sphere of 4π sr adds up to the power");

    [Fact]
    public void ASpotLightSendsItsPowerThroughItsOuterCone()
    {
        const float outer = 0.5f;
        double solidAngle = 2 * Math.PI * (1 - Math.Cos(outer));

        (LightUnits.SpotIntensity(power: 10, outerConeAngle: outer) * solidAngle)
            .Should()
            .BeApproximately(10, 1e-9, "the intensity over the outer cone's solid angle adds up to the power");
    }

    [Fact]
    public void ANarrowerSpotLightWithTheSamePowerIsBrighter() =>
        LightUnits
            .SpotIntensity(power: 10, outerConeAngle: 0.2f)
            .Should()
            .BeGreaterThan(LightUnits.SpotIntensity(power: 10, outerConeAngle: 0.5f));

    [Fact]
    public void AConeNarrowerThanTheSmallestCountsAsTheSmallest()
    {
        double smallest = LightUnits.SpotIntensity(power: 10, outerConeAngle: LightUnits.MinConeAngle);

        LightUnits.SpotIntensity(power: 10, outerConeAngle: 1e-5f).Should().Be(smallest);
        LightUnits.SpotIntensity(power: 10, outerConeAngle: 0).Should().Be(smallest);
        smallest
            .Should()
            .BeApproximately(
                10 / (2 * Math.PI * (1 - Math.Cos(LightUnits.MinConeAngle))),
                1e-3 * smallest,
                "the smallest cone sends its power through its own solid angle"
            );
    }

    [Fact]
    public void AConeOfAHalfTurnOrWiderSendsLikeAPointLight()
    {
        double point = LightUnits.PointIntensity(power: 10);

        LightUnits.SpotIntensity(power: 10, outerConeAngle: MathF.PI).Should().BeApproximately(point, 1e-12);
        LightUnits.SpotIntensity(power: 10, outerConeAngle: 4).Should().BeApproximately(point, 1e-12);
    }

    [Fact]
    public void TheReachIsWhereTheBrightestChannelFallsToTheCutoff()
    {
        double reach = LightUnits.Reach(peakIntensity: 100, cutoffIrradiance: 1e-4);

        reach.Should().BeApproximately(1_000, 1e-9);
        (100 / (reach * reach)).Should().BeApproximately(1e-4, 1e-16, "I / d² at the reach is the cutoff");
    }

    [Fact]
    public void AReachLongerThanTheLargestStopsThere() =>
        LightUnits.Reach(peakIntensity: 1e34, cutoffIrradiance: 1e-10).Should().Be(LightUnits.MaxReach);

    [Fact]
    public void EachStopOfTheHighestExposureHalvesTheCutoff()
    {
        double atZero = LightUnits.CutoffIrradiance(0);

        atZero.Should().BeApproximately(Math.PI / 8192, 1e-15, "a white matte surface at E_cut sends R_cut = 2^−13");
        LightUnits.CutoffIrradiance(1).Should().BeApproximately(atZero / 2, 1e-15);
        LightUnits.CutoffIrradiance(10).Should().BeApproximately(atZero / 1024, 1e-15);
    }

    [Fact]
    public void TheCutoffRadianceShowsBelowHalfAnEightBitStepThroughEitherToneCurve()
    {
        EncodedSteps(AcesFittedGray(LightUnits.CutoffRadiance)).Should().BeLessThan(0.5, "ACES fitted shows it");
        EncodedSteps(Hable(LightUnits.CutoffRadiance)).Should().BeLessThan(0.5, "Hable's curve shows it");
        EncodedSteps(Hable(1.0 / 256))
            .Should()
            .BeGreaterThan(1, "the approved 1/256 shows through Hable's linear toe, the reason for decision 16");
    }

    [Fact]
    public void ALightIsFarFromTwentyThousandKilometres()
    {
        LightUnits.IsFar(2e7).Should().BeTrue();
        LightUnits.IsFar(Math.BitDecrement(2e7)).Should().BeFalse();
    }

    [Fact]
    public void TheAngularRadiusOfASphereIsTheArcsineOfItsRadiusOverItsDistance()
    {
        LightUnits.AngularRadius(radius: 1, distance: 2).Should().BeApproximately(MathF.PI / 6, 1e-6f);
        LightUnits
            .AngularRadius(radius: 6.957e8f, distance: 1.496e11f)
            .Should()
            .BeApproximately(4.650418e-3f, 1e-8f, "the Sun at 1 AU");
    }

    [Fact]
    public void InsideItsSphereALightsAngularRadiusIsAQuarterTurn()
    {
        LightUnits.AngularRadius(radius: 2, distance: 1).Should().BeApproximately(MathF.PI / 2, 1e-6f);
        LightUnits.AngularRadius(radius: 1, distance: 0).Should().BeApproximately(MathF.PI / 2, 1e-6f);
    }

    [Fact]
    public void APointOfRadiusZeroAtTheEyeHasNoAngularRadius() =>
        LightUnits.AngularRadius(radius: 0, distance: 0).Should().Be(0);

    [Theory]
    [InlineData(2f, 1.0, 10f / 4)]
    [InlineData(2f, 4.0, 10f / 16)]
    [InlineData(0f, 1e-3, 10f / 1e-4f)]
    public void TheIrradianceAtTheEyeFloorsTheDistanceAtTheRadius(float radius, double distance, float expected)
    {
        PreparedLight light = Light(intensity: 10, color: Vector3.One, radius: radius, distance: distance);

        LightUnits
            .EyeIrradiance(light)
            .Should()
            .BeApproximately(expected, 1e-6f * expected, "white has a luminance of 1, and I / max(d², R², 1 cm²)");
    }

    [Fact]
    public void TheIrradianceAtTheEyeWeighsTheColorByItsLuminance()
    {
        PreparedLight green = Light(intensity: 10, color: Vector3.UnitY, radius: 0, distance: 1);

        LightUnits.EyeIrradiance(green).Should().BeApproximately(7.152f, 1e-5f, "green's luminance weight is 0.7152");
    }

    [Fact]
    public void TheRadiantIntensityOfAPowerfulNarrowSpotLightOfDimColorIsFinite()
    {
        // 3.5 × 10^38 W/sr is above the largest float, but its red channel, 3.5 × 10^33 W/sr, is within MaxIntensity.
        double intensity = LightUnits.SpotIntensity(power: 1.1e33, outerConeAngle: LightUnits.MinConeAngle);

        LightUnits
            .RadiantIntensity(color: new(1e-5f, 0, 0), intensity: intensity)
            .Should()
            .Be(
                new Vector3((float)(1e-5f * intensity), 0, 0),
                "each channel is color × intensity, multiplied in doubles"
            );
    }

    [Fact]
    public void AnAxisInterpolatesBetweenItsPreviousAndCurrentDirection()
    {
        LightUnits
            .InterpolateAxis(previous: Vector3.UnitX, current: Vector3.UnitY, alpha: 0.5f)
            .Should()
            .Be(Vector3.Normalize(new(1, 1, 0)));
        LightUnits
            .InterpolateAxis(previous: Vector3.UnitX, current: Vector3.UnitY, alpha: 0.25f)
            .Should()
            .Be(Vector3.Normalize(new(0.75f, 0.25f, 0)));
    }

    [Fact]
    public void NearlyOppositeAxesTakeTheCurrentAxis()
    {
        var nearlyOpposite = Vector3.Normalize(new(-1, 1e-6f, 0));

        LightUnits
            .InterpolateAxis(previous: nearlyOpposite, current: Vector3.UnitX, alpha: 0.5f)
            .Should()
            .Be(Vector3.UnitX);
        LightUnits
            .InterpolateAxis(previous: -Vector3.UnitX, current: Vector3.UnitX, alpha: 0.5f)
            .Should()
            .Be(Vector3.UnitX);
    }
}
