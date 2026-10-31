using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# copy of the exposure's meter and approach (docs/plans/auto-exposure.md, "The math"): the bin of a luminance,
/// the trimmed mean and the bright edge of a histogram, the target that maps the mean to middle gray or the bright edge
/// to 1, whichever is lower, inside the clamp, and the frame-rate independent approach toward it with one time constant
/// per direction, and the highest exposure that a frame can use, which sets the lights' reach.
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
    public void TheBrightEdgeLiesInsideItsBinByTheShareOfTheBinBelowTheEdge() =>
        ExposureMeter
            .BrightEdgeStops(Histogram((100, 80), (120, 20)), brightShare: 0.15f)
            .Should()
            .BeApproximately(
                ExposureMeter.BinCenterStops(120) - BinWidth / 2 + BinWidth / 4,
                1e-5f,
                "the edge at 85 of the 100 pixels lies 5 of the 20 pixels of bin 120 up from its lower edge"
            );

    [Fact]
    public void WithNoBrightShareTheBrightEdgeIsTheTopOfTheBrightestBin() =>
        ExposureMeter
            .BrightEdgeStops(Histogram((100, 80), (120, 20)), brightShare: 0)
            .Should()
            .BeApproximately(ExposureMeter.BinCenterStops(120) + BinWidth / 2, 1e-5f);

    [Fact]
    public void AHistogramWithNoCountHasNoReading() =>
        ExposureMeter.Reading(Histogram(), darkShare: 0.1f, brightShare: 0.1f).Should().BeNull();

    [Fact]
    public void WhenATenthOrMoreOfTheSurfaceIsFarBrighterTheTargetMapsTheBrightEdgeToOne()
    {
        const float used = 1.5f;
        // The bright 15 % lie 10 stops above the rest, so their edge lies far more than 2.47 stops above the mean.
        (float TrimmedMeanStops, float BrightEdgeStops) reading = ExposureMeter
            .Reading(Histogram((100, 85), (180, 15)), darkShare: 0.1f, brightShare: 0.1f)!
            .Value;

        float target = ExposureMeter.TargetStops(
            reading: reading,
            usedStops: used,
            compensationStops: 0,
            minStops: -8,
            maxStops: 8
        );

        MathF
            .Pow(2, reading.BrightEdgeStops - used + target)
            .Should()
            .BeApproximately(1, 1e-5f, "the bright area sits at the start of the tone curve's shoulder");
        MathF
            .Pow(2, reading.TrimmedMeanStops - used + target)
            .Should()
            .BeLessThan(
                ExposureMeter.MiddleGray,
                "the highlight target is the lower one, so the mean falls below gray"
            );
    }

    [Fact]
    public void WhenTheBrightEdgeShowsBelowOneAtTheAverageTargetTheTargetMapsTheTrimmedMeanToMiddleGray()
    {
        const float used = 1.5f;
        const float mean = -3;

        float target = ExposureMeter.TargetStops(
            reading: (TrimmedMeanStops: mean, BrightEdgeStops: mean + 2),
            usedStops: used,
            compensationStops: 0,
            minStops: -8,
            maxStops: 8
        );

        MathF
            .Pow(2, mean - used + target)
            .Should()
            .BeApproximately(
                ExposureMeter.MiddleGray,
                1e-6f,
                "at middle gray the edge 2 stops above shows at 0.72, below 1"
            );
    }

    [Fact]
    public void ADimSceneWithNoBrightAreaKeepsTheAverageTarget()
    {
        const float used = 4;
        // A dim view whose luminances spread over 2 stops, about 7 stops below middle gray in radiance.
        float[] view = [.. Enumerable.Range(0, 17).Select(step => MathF.Pow(2, -10 + step / 8f))];
        uint[] histogram = Metered(view, used);

        float target = ExposureMeter.TargetStops(
            reading: ExposureMeter.Reading(histogram, darkShare: 0.1f, brightShare: 0.1f),
            usedStops: used,
            compensationStops: 0,
            minStops: -8,
            maxStops: 8
        );

        float mean = ExposureMeter.TrimmedMeanStops(histogram, darkShare: 0.1f, brightShare: 0.1f)!.Value;
        MathF.Pow(2, mean - used + target).Should().BeApproximately(ExposureMeter.MiddleGray, 1e-6f);
    }

    [Fact]
    public void TheTargetDoesNotDependOnTheExposureThatTheFrameUsed()
    {
        float[] view = [.. Enumerable.Range(40, 60).Select(bin => MathF.Pow(2, ExposureMeter.BinCenterStops(bin)))];
        float TargetAt(float used) =>
            ExposureMeter.TargetStops(
                reading: ExposureMeter.Reading(Metered(view, used), darkShare: 0.1f, brightShare: 0.1f),
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
                reading: (
                    TrimmedMeanStops: MathF.Log2(ExposureMeter.MiddleGray) - 6,
                    BrightEdgeStops: MathF.Log2(ExposureMeter.MiddleGray) - 5
                ),
                usedStops: 0,
                compensationStops: 4,
                minStops: -8,
                maxStops: 8
            )
            .Should()
            .BeApproximately(8, 1e-5f, "the target of 6 stops plus 4 stops of compensation is clamped to 8");

    [Fact]
    public void TheCompensationAddsItsStopsToTheHighlightTarget() =>
        ExposureMeter
            .TargetStops(
                reading: (TrimmedMeanStops: -6, BrightEdgeStops: 2),
                usedStops: 0,
                compensationStops: 1,
                minStops: -8,
                maxStops: 8
            )
            .Should()
            .BeApproximately(-1, 1e-5f, "the highlight target of −2 stops is below the average target of 3.53");

    [Fact]
    public void TheClampBoundsTheHighlightTarget() =>
        ExposureMeter
            .TargetStops(
                reading: (TrimmedMeanStops: 0, BrightEdgeStops: 12),
                usedStops: 0,
                compensationStops: 0,
                minStops: -8,
                maxStops: 8
            )
            .Should()
            .Be(-8, "the highlight target of −12 stops is below the clamp, and the average target of −2.47 inside it");

    [Theory]
    [InlineData(-30f, 8f)]
    [InlineData(30f, -8f)]
    public void TheTargetStaysInsideTheClamp(float trimmedMeanStops, float clampedTarget) =>
        ExposureMeter
            .TargetStops(
                reading: (TrimmedMeanStops: trimmedMeanStops, BrightEdgeStops: trimmedMeanStops + 1),
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
            .TargetStops(reading: null, usedStops: 2.5f, compensationStops: 1, minStops: -8, maxStops: 8)
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

    [Fact]
    public void AFixedExposureIsTheHighestExposureOfEveryFrame() =>
        ExposureMeter
            .HighestStops(new FixedExposure(-2.5f), knownStops: 7, steps: [float.PositiveInfinity])
            .Should()
            .Be(-2.5f, "nothing moves a fixed exposure");

    [Fact]
    public void WithNoStepSinceTheKnownFrameTheHighestExposureIsTheKnownOne() =>
        ExposureMeter.HighestStops(AutoExposure.Default, knownStops: 1.5f, steps: []).Should().Be(1.5f);

    [Fact]
    public void EachStepRaisesTheHighestExposureAsFarAsTheAdaptStepCanRiseTowardTheHighestStops()
    {
        AutoExposure auto = AutoExposure.Default with { MaxStops = 10, IntoDarkSeconds = 3 };

        ExposureMeter
            .HighestStops(auto, knownStops: 0, steps: [3])
            .Should()
            .BeApproximately(
                10 * (1 - 1 / MathF.E),
                1e-5f,
                "one time constant into the dark covers all but 1/e of the way"
            );
    }

    [Fact]
    public void TheStepsSinceTheKnownFrameRaiseTheHighestExposureOneAfterAnother()
    {
        AutoExposure auto = AutoExposure.Default with { MaxStops = 10, IntoDarkSeconds = 3 };

        ExposureMeter
            .HighestStops(auto, knownStops: 0, steps: [1.5f, 1.5f])
            .Should()
            .BeApproximately(ExposureMeter.HighestStops(auto, knownStops: 0, steps: [3]), 1e-5f);
    }

    [Fact]
    public void AnEndlessStepMakesTheHighestStopsTheHighestExposure() =>
        ExposureMeter
            .HighestStops(AutoExposure.Default, knownStops: -11, steps: [0.016f, float.PositiveInfinity, 0])
            .Should()
            .Be(AutoExposure.Default.MaxStops, "a metering draw can settle anywhere inside the clamp");

    [Theory]
    [InlineData(-14f)]
    [InlineData(-3f)]
    [InlineData(4.5f)]
    [InlineData(10f)]
    public void NoTargetInsideTheClampRaisesTheExposureAboveTheHighestExposure(float target)
    {
        AutoExposure auto = AutoExposure.Default;
        float stops = -6;
        float[] steps = [0.016f, 0.25f, 0.033f];
        foreach (float step in steps)
            stops = ExposureMeter.Approach(
                stops: stops,
                target: target,
                intoDarkFactor: ExposureMeter.ApproachFactor(step, auto.IntoDarkSeconds),
                intoLightFactor: ExposureMeter.ApproachFactor(step, auto.IntoLightSeconds)
            );

        stops.Should().BeLessThanOrEqualTo(ExposureMeter.HighestStops(auto, knownStops: -6, steps: steps));
    }
}
