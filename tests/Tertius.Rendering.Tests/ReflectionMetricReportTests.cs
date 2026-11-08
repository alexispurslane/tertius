using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>Fixed-reference regions retain occluded light, denominator identity and temporal error meaning.</summary>
public sealed class ReflectionMetricReportTests
{
    [Fact]
    public void AnOccludedBrightRowStaysInTotalsAndMakesBandCoverageExplicit()
    {
        var basis = new MetricReference(
            new(Left: 0, Top: 0, Width: 7, Height: 2),
            [0, 2, 8, 10, 8, 2, 0, 0, 0, 80, 100, 80, 20, 0],
            [1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1]
        );

        MetricComparison measured = basis.Compare(basis.Reference, new double[14]);
        MetricRegionResult band = measured.Regions.Single(region => region.Name == "left_outer");

        measured.Regions.Single(region => region.Name == "full").Totals.Missing.Should().Be(310);
        band.ValidRows.Should().Be(1);
        band.InvalidRows.Should().Be(1);
        band.InvalidReasons.Should().ContainKey("OCCLUSION_AFFECTED");
        band.CoveredReferenceRadiance.Should().Be(2);
        band.ReferenceCoverageFraction.Should().BeApproximately(2.0 / 310, 1e-15);
        band.EligibleRowReferenceFraction.Should().BeApproximately(30.0 / 310, 1e-15);
    }

    [Fact]
    public void OneInvalidSideDoesNotDiscardTheOtherBandOrTheFullCrop()
    {
        var basis = new MetricReference(
            new(Left: 0, Top: 0, Width: 7, Height: 1),
            [0, 2, 8, 10, 8, 2, 0],
            [0, 1, 1, 1, 1, 1, 1]
        );

        MetricComparison measured = basis.Compare(basis.Reference, basis.Reference);

        measured.Regions.Single(region => region.Name == "left_outer").ValidRows.Should().Be(0);
        measured.Regions.Single(region => region.Name == "left_outer").Totals.MissingFraction.Should().BeNull();
        measured.Regions.Single(region => region.Name == "right_outer").ValidRows.Should().Be(1);
        measured.Regions.Single(region => region.Name == "full").Totals.ReferenceSum.Should().Be(30);
    }

    [Fact]
    public void OnVersusOffUsesOffLightButDoesNotMoveTheReferenceBands()
    {
        var basis = new MetricReference(
            new(Left: 0, Top: 0, Width: 7, Height: 1),
            [0, 2, 8, 10, 8, 2, 0],
            [1, 1, 1, 1, 1, 1, 1]
        );

        MetricComparison measured = basis.Compare([0, 4, 16, 20, 16, 4, 0], [0, 2, 8, 10, 8, 2, 0]);
        MetricRegionResult band = measured.Regions.Single(region => region.Name == "left_outer");

        band.CoveredReferenceRadiance.Should().Be(2);
        band.Totals.ReferenceSum.Should().Be(4);
        band.Totals.MissingFraction.Should().Be(0.5);
    }

    [Fact]
    public void FullCropMetricsReuseTheOldDifferenceArithmeticExactly()
    {
        var reference = new ReferenceImage(2, 1, [new(4, 4, 4), new(4, 4, 4)]);
        var candidate = new ReferenceImage(2, 1, [new(2, 2, 2), new(6, 6, 6)]);

        var difference = ReferenceDifference.Compare(reference, candidate);
        RadianceTotals measured = MetricMath.Measure(
            [.. reference.Pixels.Select(ReferenceImage.Luminance)],
            [.. candidate.Pixels.Select(ReferenceImage.Luminance)],
            [1, 1]
        );

        measured.MissingFraction.Should().Be(difference.MissingFraction);
        measured.ExcessFraction.Should().Be(difference.ExcessFraction);
        measured.RelativeL1.Should().Be(difference.RelativeL1);
    }

    [Fact]
    public void ZeroContrastRowsCannotCreateFiniteBandsOrFakeCoverage()
    {
        var basis = new MetricReference(new(Left: 0, Top: 0, Width: 3, Height: 1), [0, 0, 0], [1, 1, 1]);

        MetricComparison measured = basis.Compare(basis.Reference, [1, 1, 1]);
        MetricRegionResult band = measured.Regions.Single(region => region.Name == "outer");

        band.ValidRows.Should().Be(0);
        band.ReferenceCoverageFraction.Should().BeNull();
        band.InvalidReasons.Should().ContainKey("NO_CONTRAST");
        measured.Regions.Single(region => region.Name == "full").Totals.Excess.Should().Be(3);
    }

    [Fact]
    public void PixelNoiseAndPersistentMeanErrorAreKeptSeparate()
    {
        var basis = new MetricReference(new(Left: 0, Top: 0, Width: 1, Height: 1), [2], [1]);
        double[][] frames = [.. Enumerable.Range(0, 32).Select(index => new[] { index % 2 == 0 ? 1.0 : 3.0 })];

        MetricTemporalResult temporal = basis.Temporal(frames);

        temporal.Pixels[0].MeanSignedError.Should().Be(0);
        temporal.Pixels[0].Temporal.SampleStandardDeviation.Should().BeApproximately(Math.Sqrt(32.0 / 31), 1e-12);
        temporal.Pixels[0].Temporal.First8Last8Difference.Should().Be(0);
        temporal.Regions.Single(region => region.MeanImage.Name == "full").MeanImage.Totals.RelativeL1.Should().Be(0);
    }
}
