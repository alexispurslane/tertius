using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>Reference metrics retain light, fixed contrast, all roots and undefined states without fitted masks.</summary>
public sealed class ReflectionMetricTests
{
    [Fact]
    public void ExtraLightCannotCancelMissingLight()
    {
        RadianceTotals measured = MetricMath.Measure([4, 4], [2, 6], [1, 1]);

        measured.SignedDifference.Should().Be(0);
        measured.MissingFraction.Should().Be(0.25);
        measured.ExcessFraction.Should().Be(0.25);
        measured.RelativeL1.Should().Be(0.5);
    }

    [Fact]
    public void AZeroReferenceKeepsRawExcessAndHasNoNormalizedSuccess()
    {
        RadianceTotals? measured = null;
        Action measure = () => measured = MetricMath.Measure([0, 0], [2, 3], [1, 1]);

        measure.Should().NotThrow();
        measured.Should().NotBeNull();
        measured!.Excess.Should().Be(5);
        measured.MissingFraction.Should().BeNull();
        measured.NormalizationReason.Should().Be("NO_POSITIVE_REFERENCE_LIGHT");
    }

    [Fact]
    public void AnEmptyBandDoesNotInventAZeroErrorFraction()
    {
        RadianceTotals? measured = null;
        Action measure = () => measured = MetricMath.Measure([4, 8], [1, 1], [0, 0]);

        measure.Should().NotThrow();
        measured!.RelativeL1.Should().BeNull();
        measured.ReferenceSum.Should().Be(0);
    }

    [Fact]
    public void AnExactLevelSampleCountsOnceRatherThanOncePerAdjacentSegment()
    {
        MetricRoots roots = MetricMath.Roots([0, 1, 2], [1, 1, 1], 1);

        roots.Points.Should().ContainSingle();
        roots.Points[0].Position.Should().Be(1.5);
        roots.Points[0].Direction.Should().Be("rising");
        roots.Plateaus.Should().BeEmpty();
    }

    [Fact]
    public void AnExactLevelPlateauRetainsItsIntervalWithoutChoosingItsMidpoint()
    {
        MetricRoots roots = MetricMath.Roots([0, 1, 1, 2], [1, 1, 1, 1], 1);

        roots.Points.Should().BeEmpty();
        roots.Plateaus.Should().Equal(new MetricInterval(1.5, 2.5));
    }

    [Fact]
    public void MultipleDirectedCrossingsRemainAmbiguous()
    {
        MetricLevel level = MetricMath.Level(values: [0, 2, 0, 2, 0], coverage: [1, 1, 1, 1, 1], level: 1, origin: 530);

        level.Roots.Points.Should().HaveCount(4);
        level.Left.Position.Should().BeNull();
        level.Right.Position.Should().BeNull();
        level.Left.Reasons.Should().Contain("AMBIGUOUS_MULTIPLE_CROSSINGS");
    }

    [Fact]
    public void AUniformlyDimProfileIsMissingAtTheLevelRatherThanCropTruncated()
    {
        MetricLevel level = MetricMath.Level(values: [0.1, 0.1, 0.1], coverage: [1, 1, 1], level: 1, origin: 0);

        level.Left.Reasons.Should().Equal("MISSING_AT_LEVEL");
        level.Right.Reasons.Should().Equal("MISSING_AT_LEVEL");
        level.Left.Position.Should().BeNull();
    }

    [Fact]
    public void AboveLevelValuesAtTheActualCropBoundaryReportAnOpenEdge()
    {
        MetricLevel level = MetricMath.Level(values: [2, 2, 2], coverage: [1, 1, 1], level: 1, origin: 0);

        level.Left.Reasons.Should().Contain("CROP_TRUNCATED");
        level.Right.Reasons.Should().Contain("CROP_TRUNCATED");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    public void AnOccludedOrPartlyCoveredCrossingIsNotAReflectionEdge(double coverage)
    {
        MetricLevel level = MetricMath.Level(values: [0, 2, 0], coverage: [coverage, 1, 1], level: 1, origin: 0);

        level.Left.Position.Should().BeNull();
        level.Left.Reasons.Should().Contain("OCCLUSION_AFFECTED");
        level.Right.Position.Should().Be(2);
        level.Roots.Points[0].Occluded.Should().BeTrue();
    }

    [Fact]
    public void DimmingCannotLowerTheReferenceContrastThresholds()
    {
        var basis = new MetricProfileBasis([0, 2, 8, 10, 8, 2, 0], [1, 1, 1, 1, 1, 1, 1], 0);

        MetricProfile profile = basis.Measure([0, 1, 4, 5, 4, 1, 0]);

        profile.Levels[2].Height.Should().Be(9);
        profile.Levels[2].Left.Reasons.Should().Contain("MISSING_AT_LEVEL");
        profile.LeftTransition.Should().BeNull();
    }

    [Fact]
    public void AShiftedEdgeIsMeasuredInOriginalPixelsWithoutBrightnessNormalization()
    {
        var basis = new MetricProfileBasis([0, 0, 2, 8, 10, 8, 2, 0, 0], [1, 1, 1, 1, 1, 1, 1, 1, 1], 530);

        MetricProfile profile = basis.Measure([0, 0, 0, 2, 8, 10, 8, 2, 0]);

        profile.Levels[1].Left.Position.Should().Be(534);
        profile.Levels[1].Right.Position.Should().Be(537);
        profile.LeftTransition.Should().Be(2);
    }

    [Fact]
    public void FractionalBandsUseTheSamePixelCellOverlapsForBothImages()
    {
        double[] weights = MetricMath.Weights(2, new(0.25, 1.5));

        RadianceTotals measured = MetricMath.Measure([4, 8], [0, 0], weights);

        weights.Should().Equal(0.75, 0.5);
        measured.ReferenceSum.Should().Be(7);
        measured.Missing.Should().Be(7);
    }

    [Fact]
    public void CentralNinetyMeansTheFiveToNinetyFivePercentPixelCellQuantiles()
    {
        MetricWidth width = MetricMath.Central90([0, 10, 10, 0], 530);

        width.Lower.Should().Be(531.1);
        width.Upper.Should().Be(532.9);
        width.Width.Should().BeApproximately(1.8, 1e-12);
    }

    [Fact]
    public void AZeroSignalHasNeitherQuantilesNorContrastEdges()
    {
        var basis = new MetricProfileBasis([0, 0, 0], [1, 1, 1], 0);

        MetricProfile profile = basis.Measure([0, 0, 0]);

        profile.Central90.Width.Should().BeNull();
        profile.Central90.Reason.Should().Be("NO_POSITIVE_LIGHT");
        profile.Levels[0].Left.Reasons.Should().Contain("NO_CONTRAST");
    }

    [Fact]
    public void TemporalDriftIsReportedSeparatelyFromAlternatingNoise()
    {
        MetricTemporal drift = MetricMath.Temporal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]);
        MetricTemporal noise = MetricMath.Temporal([0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2]);

        drift.First8Last8Difference.Should().Be(8);
        noise.First8Last8Difference.Should().Be(0);
        noise.SampleStandardDeviation.Should().BeApproximately(Math.Sqrt(16.0 / 15), 1e-12);
    }
}
