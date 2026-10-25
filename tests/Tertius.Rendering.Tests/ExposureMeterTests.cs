using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# copy of the exposure's meter and approach (docs/plans/auto-exposure.md, "The math"): the bin of a luminance,
/// the trimmed mean of a histogram, the target that maps it to middle gray inside the clamp, and the frame-rate
/// independent approach toward it with one time constant per direction.
/// </summary>
public sealed class ExposureMeterTests
{
    /// <summary>The stops of one bin: an eighth of a stop.</summary>
    private const float BinWidth = 1f / ExposureMeter.BinsPerStop;

    /// <summary>A histogram that holds each named count in its bin and nothing elsewhere.</summary>
    private static uint[] Histogram(params (int Bin, uint Count)[] counts)
    {
        uint[] bins = new uint[ExposureMeter.Bins];
        foreach ((int bin, uint count) in counts)
            bins[bin] = count;
        return bins;
    }

    /// <summary>The histogram of surface luminances in radiance, metered at the exposure <paramref name="stops"/>.</summary>
    private static uint[] Metered(float[] radianceLuminances, float stops)
    {
        uint[] bins = new uint[ExposureMeter.Bins];
        foreach (float luminance in radianceLuminances)
            bins[ExposureMeter.BinOf(luminance * MathF.Pow(2, stops))]++;
        return bins;
    }

    [Fact]
    public void ALuminanceOfZeroFallsInTheFirstBin() => ExposureMeter.BinOf(0).Should().Be(0);

    [Fact]
    public void ALuminanceAtTheLowestEdgeFallsInTheFirstBinAndOneEighthOfAStopAboveItInTheSecond()
    {
        float lowestEdge = MathF.Pow(2, ExposureMeter.LowestStops);
        float secondEdge = MathF.Pow(2, ExposureMeter.LowestStops + BinWidth);

        ExposureMeter.BinOf(lowestEdge).Should().Be(0);
        ExposureMeter.BinOf(secondEdge).Should().Be(1);
    }

    [Fact]
    public void TheLargestHalfFloatFallsInTheLastBin() =>
        ExposureMeter
            .BinOf(65_504)
            .Should()
            .Be(ExposureMeter.Bins - 1, "the clamp keeps a pre-exposed value at or below the largest half float");

    [Fact]
    public void ALuminanceAboveTheHighestEdgeFallsInTheLastBin() =>
        ExposureMeter.BinOf(MathF.Pow(2, 20)).Should().Be(ExposureMeter.Bins - 1);

    [Fact]
    public void EachBinsCenterLiesHalfAnEighthOfAStopAboveItsLowerEdge()
    {
        const float justInside = 1f / 256;
        for (int bin = 1; bin < ExposureMeter.Bins; bin++)
        {
            float lowerEdge = ExposureMeter.BinCenterStops(bin) - BinWidth / 2;

            ExposureMeter.BinOf(MathF.Pow(2, lowerEdge + justInside)).Should().Be(bin);
            ExposureMeter.BinOf(MathF.Pow(2, lowerEdge - justInside)).Should().Be(bin - 1);
        }
    }

    [Fact]
    public void GreenWeighsMostInTheLuminanceAndBlueLeast()
    {
        ExposureMeter.Luminance(new Vector3(1, 0, 0)).Should().BeApproximately(0.2126f, 1e-7f);
        ExposureMeter.Luminance(new Vector3(0, 1, 0)).Should().BeApproximately(0.7152f, 1e-7f);
        ExposureMeter.Luminance(new Vector3(0, 0, 1)).Should().BeApproximately(0.0722f, 1e-7f);
    }

    [Fact]
    public void TheTrimmedMeanDropsTheDarkestAndTheBrightestShares() =>
        ExposureMeter
            .TrimmedMeanStops(Histogram((10, 10), (100, 80), (200, 10)), darkShare: 0.1f, brightShare: 0.1f)
            .Should()
            .BeApproximately(
                ExposureMeter.BinCenterStops(100),
                1e-5f,
                "the darkest tenth fills bin 10 and the brightest tenth fills bin 200"
            );

    [Fact]
    public void ABinThatStraddlesATrimEdgeCountsOnlyItsShareInside()
    {
        float expected = (10 * ExposureMeter.BinCenterStops(10) + 80 * ExposureMeter.BinCenterStops(100)) / 90;

        ExposureMeter
            .TrimmedMeanStops(Histogram((10, 20), (100, 80)), darkShare: 0.1f, brightShare: 0)
            .Should()
            .BeApproximately(expected, 1e-5f, "the darkest 10 of the 20 pixels of bin 10 are dropped");
    }

    [Fact]
    public void AHistogramWithNoCountHasNoMean() =>
        ExposureMeter.TrimmedMeanStops(Histogram(), darkShare: 0.1f, brightShare: 0.1f).Should().BeNull();

    [Fact]
    public void TheTargetMapsTheTrimmedMeanToMiddleGray()
    {
        const float used = 1.5f;
        const float mean = -3;

        float target = ExposureMeter.TargetStops(
            trimmedMeanStops: mean,
            usedStops: used,
            compensationStops: 0,
            minStops: -8,
            maxStops: 8
        );

        MathF
            .Pow(2, mean - used + target)
            .Should()
            .BeApproximately(ExposureMeter.MiddleGray, 1e-6f, "the radiance's mean at the target shows as middle gray");
    }

    [Fact]
    public void TheTargetDoesNotDependOnTheExposureThatTheFrameUsed()
    {
        float[] view = [.. Enumerable.Range(40, 60).Select(bin => MathF.Pow(2, ExposureMeter.BinCenterStops(bin)))];
        float TargetAt(float used) =>
            ExposureMeter.TargetStops(
                trimmedMeanStops: ExposureMeter.TrimmedMeanStops(
                    Metered(view, used),
                    darkShare: 0.1f,
                    brightShare: 0.1f
                ),
                usedStops: used,
                compensationStops: 0,
                minStops: -8,
                maxStops: 8
            );

        TargetAt(0).Should().BeApproximately(TargetAt(2), 1e-5f);
    }

    [Fact]
    public void TheCompensationAddsItsStopsBeforeTheClamp() =>
        ExposureMeter
            .TargetStops(
                trimmedMeanStops: MathF.Log2(ExposureMeter.MiddleGray) - 6,
                usedStops: 0,
                compensationStops: 4,
                minStops: -8,
                maxStops: 8
            )
            .Should()
            .BeApproximately(8, 1e-5f, "the target of 6 stops plus 4 stops of compensation is clamped to 8");

    [Theory]
    [InlineData(-30f, 8f)]
    [InlineData(30f, -8f)]
    public void TheTargetStaysInsideTheClamp(float trimmedMeanStops, float clampedTarget) =>
        ExposureMeter
            .TargetStops(
                trimmedMeanStops: trimmedMeanStops,
                usedStops: 0,
                compensationStops: 0,
                minStops: -8,
                maxStops: 8
            )
            .Should()
            .Be(clampedTarget);

    [Fact]
    public void WithNoSurfaceTheTargetIsTheExposureThatTheFrameUsed() =>
        ExposureMeter
            .TargetStops(trimmedMeanStops: null, usedStops: 2.5f, compensationStops: 1, minStops: -8, maxStops: 8)
            .Should()
            .Be(2.5f);

    [Theory]
    [InlineData(4f)]
    [InlineData(-4f)]
    public void TwoHalfStepsMoveTheExposureAsFarAsOneWholeStep(float target)
    {
        const float intoDark = 3;
        const float intoLight = 1;
        float half = ExposureMeter.Approach(
            stops: 0,
            target: target,
            intoDarkFactor: ExposureMeter.ApproachFactor(0.25f, intoDark),
            intoLightFactor: ExposureMeter.ApproachFactor(0.25f, intoLight)
        );
        float twoHalves = ExposureMeter.Approach(
            stops: half,
            target: target,
            intoDarkFactor: ExposureMeter.ApproachFactor(0.25f, intoDark),
            intoLightFactor: ExposureMeter.ApproachFactor(0.25f, intoLight)
        );

        float whole = ExposureMeter.Approach(
            stops: 0,
            target: target,
            intoDarkFactor: ExposureMeter.ApproachFactor(0.5f, intoDark),
            intoLightFactor: ExposureMeter.ApproachFactor(0.5f, intoLight)
        );

        twoHalves.Should().BeApproximately(whole, 1e-5f);
    }

    [Fact]
    public void AStepOfOneTimeConstantCoversAllButOneOverEOfTheWayToTheTarget() =>
        ExposureMeter.ApproachFactor(2, timeConstant: 2).Should().BeApproximately(1 - 1 / MathF.E, 1e-6f);

    [Fact]
    public void AStepOfZeroLeavesTheExposure() =>
        ExposureMeter
            .Approach(
                stops: 1.5f,
                target: 4,
                intoDarkFactor: ExposureMeter.ApproachFactor(0, timeConstant: 3),
                intoLightFactor: ExposureMeter.ApproachFactor(0, timeConstant: 1)
            )
            .Should()
            .Be(1.5f);

    [Fact]
    public void AStepOfZeroLeavesTheExposureEvenWithATimeConstantOfZero() =>
        ExposureMeter.ApproachFactor(0, timeConstant: 0).Should().Be(0);

    [Fact]
    public void AStepBackInTimeLeavesTheExposure() => ExposureMeter.ApproachFactor(-1, timeConstant: 3).Should().Be(0);

    [Fact]
    public void AnEndlessStepReachesTheTarget() =>
        ExposureMeter
            .Approach(
                stops: 1.5f,
                target: 4,
                intoDarkFactor: ExposureMeter.ApproachFactor(float.PositiveInfinity, timeConstant: 3),
                intoLightFactor: ExposureMeter.ApproachFactor(float.PositiveInfinity, timeConstant: 1)
            )
            .Should()
            .Be(4);

    [Fact]
    public void ARisingExposureUsesTheTimeConstantIntoTheDarkAndAFallingOneTheTimeConstantIntoTheLight()
    {
        ExposureMeter
            .Approach(stops: 0, target: 4, intoDarkFactor: 0.25f, intoLightFactor: 0.75f)
            .Should()
            .Be(1, "the view got darker, so the exposure rises with the factor into the dark");
        ExposureMeter
            .Approach(stops: 0, target: -4, intoDarkFactor: 0.25f, intoLightFactor: 0.75f)
            .Should()
            .Be(-3, "the view got brighter, so the exposure falls with the factor into the light");
    }

    [Theory]
    [InlineData(1e-30f)]
    [InlineData(0.016f)]
    [InlineData(100f)]
    public void ATimeConstantOfZeroReachesTheTargetInAnyStepAboveZero(float seconds) =>
        ExposureMeter.ApproachFactor(seconds, timeConstant: 0).Should().Be(1);
}
