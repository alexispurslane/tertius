namespace Tertius.Rendering.Tests;

/// <summary>
/// The rules of the guided shares (ARCHITECTURE.md, "Guided rays", <see cref="GuidedShares"/>): the one-sample mixture
/// density, the reflection kind's share of a pixel from its roughness and its tile's share, the evidence of one
/// reflection sample for its tile, the tile's step from one draw to the next, and a block's bilinear read of the tiles.
/// The GPU tests compare the shaders with these copies.
/// </summary>
public sealed class GuidedSharesTests
{
    [Fact]
    public void TheMixtureDensityWeighsTheLobeDensityAndTheGuidedDensityByTheShare() =>
        GuidedShares.MixtureDensity(share: 0.25f, lobeDensity: 2, guidedDensity: 10).Should().Be(0.75f * 2 + 0.25f * 10);

    [Fact]
    public void AtAShareOfZeroTheMixtureDensityIsTheLobeDensity() =>
        GuidedShares.MixtureDensity(share: 0, lobeDensity: 3, guidedDensity: 1000).Should().Be(3);

    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.1f)]
    public void ASurfaceAtOrBelowTheRampsStartRoughnessTakesNoGuidedReflectionRay(float roughness) =>
        GuidedShares
            .ReflectionShareOf(roughness, tileShare: GuidedShares.ReflectionCeiling)
            .Should()
            .Be(0, "a guided draw lands where a sharp lobe gives almost no density, so it would waste the ray");

    [Fact]
    public void TheReflectionShareRisesLinearlyOverTheRamp()
    {
        float middle = (GuidedShares.RampStart + GuidedShares.RampEnd) / 2;

        GuidedShares
            .ReflectionShareOf(middle, tileShare: GuidedShares.ReflectionCeiling)
            .Should()
            .BeApproximately(GuidedShares.ReflectionCeiling / 2, 1e-6f);
    }

    [Theory]
    [InlineData(0.38f)]
    [InlineData(0.6f)]
    public void FromTheRampsEndTheReflectionShareIsTheTilesShare(float roughness) =>
        GuidedShares.ReflectionShareOf(roughness, tileShare: 0.1f).Should().Be(0.1f);

    [Fact]
    public void TheRampCapsATilesShareOnASmootherSurface()
    {
        float quarter = GuidedShares.RampStart + 0.25f * (GuidedShares.RampEnd - GuidedShares.RampStart);

        GuidedShares
            .ReflectionShareOf(quarter, tileShare: GuidedShares.ReflectionCeiling)
            .Should()
            .BeApproximately(GuidedShares.ReflectionCeiling / 4, 1e-6f);
    }

    [Fact]
    public void TheRoughnessesOfTheBaysWallsAndTheGlossyFloorAreAtTheFullShare() =>
        GuidedShares
            .ReflectionShareOf(0.38f, tileShare: GuidedShares.ReflectionCeiling)
            .Should()
            .Be(GuidedShares.ReflectionCeiling, "stage 1 measured its gains and losses on surfaces of a roughness of 0.38");

    [Fact]
    public void TheEvidenceOfASampleIsItsSquaredWeightAndTheSlopeOfItsSecondMomentInTheShare()
    {
        (float slope, float moment) = GuidedShares.EvidenceOf(weight: 3, share: 0.25f, lobeDensity: 2, guidedDensity: 10);

        moment.Should().Be(9);
        slope.Should().BeApproximately(9f * (2 - 10) / (0.75f * 2 + 0.25f * 10), 1e-5f);
    }

    [Fact]
    public void ASampleThatTheGuideFindsMoreOftenThanTheLobeAsksForALargerShare() =>
        GuidedShares
            .EvidenceOf(weight: 1, share: 0.25f, lobeDensity: 1, guidedDensity: 50)
            .Slope.Should()
            .BeNegative("a larger share lowers the second moment there");

    [Fact]
    public void ASampleThatTheGuideMissesAsksForASmallerShare() =>
        GuidedShares
            .EvidenceOf(weight: 1, share: 0.25f, lobeDensity: 1, guidedDensity: 0)
            .Slope.Should()
            .BePositive();

    [Fact]
    public void BelowTheGuardTheEvidenceWeighsBySquaredWeightOverTheMixtureAtTheGuard()
    {
        (float slope, _) = GuidedShares.EvidenceOf(weight: 2, share: 0, lobeDensity: 1, guidedDensity: 20);

        float guard = GuidedShares.Guard;
        slope
            .Should()
            .BeApproximately(
                4f * (1 - 20) / ((1 - guard) * 1 + guard * 20),
                1e-5f,
                "at a share of 0 the exact slope needs the guided draws' density, which the guard gives a floor"
            );
    }

    [Fact]
    public void AWeightAbove10To12CountsAs10To12() =>
        GuidedShares
            .EvidenceOf(weight: float.MaxValue, share: 0.25f, lobeDensity: 1, guidedDensity: 1)
            .Moment.Should()
            .Be(1e24f, "a sum of 64 such moments stays finite");

    [Fact]
    public void ATileWhoseSamplesAskForASmallerShareStepsDownByTheStepTimesTheirRatio() =>
        GuidedShares
            .NextShare(share: 0.2f, slopeSum: 1.5f, momentSum: 3)
            .Should()
            .BeApproximately(0.2f - GuidedShares.Step * 0.5f, 1e-7f);

    [Fact]
    public void ATileWhoseSamplesAskForALargerShareStepsUp() =>
        GuidedShares
            .NextShare(share: 0.1f, slopeSum: -3, momentSum: 6)
            .Should()
            .BeApproximately(0.1f + GuidedShares.Step * 0.5f, 1e-7f);

    [Fact]
    public void ATilesStepIsAtMostTheStepTimesTheMostRatio()
    {
        float share = GuidedShares.NextShare(share: 0.2f, slopeSum: 1000, momentSum: 1);

        share.Should().BeApproximately(0.2f - GuidedShares.Step * GuidedShares.MostRatio, 1e-7f);
    }

    [Fact]
    public void ATilesShareStopsAtTheCeiling() =>
        GuidedShares
            .NextShare(share: GuidedShares.ReflectionCeiling, slopeSum: -1, momentSum: 1)
            .Should()
            .Be(GuidedShares.ReflectionCeiling);

    [Fact]
    public void ATilesShareStopsAtZero() =>
        GuidedShares.NextShare(share: 0.001f, slopeSum: 1, momentSum: 1).Should().Be(0);

    [Fact]
    public void ATileWithoutEvidenceKeepsItsShare() =>
        GuidedShares
            .NextShare(share: 0.125f, slopeSum: 0, momentSum: 0)
            .Should()
            .Be(0.125f, "a tile that no rough reflection ray hit anything from learns nothing");

    [Fact]
    public void ABlockTakesTheSharesOfTheTwoTilesAroundItByItsDistanceToTheirCenters() =>
        GuidedShares
            .TileShareAt(shares: [0, 0.16f], imageWide: 2, tilesWide: 2, tilesHigh: 1, blockX: 8, blockY: 0)
            .Should()
            .BeApproximately(
                0.09f,
                1e-7f,
                "block 8's center lies 9 / 16 of a tile past the first tile's center toward the second's"
            );

    [Fact]
    public void ABlockNearTheEdgeTakesTheShareOfTheEdgesTile() =>
        GuidedShares
            .TileShareAt(shares: [0.2f, 0], imageWide: 2, tilesWide: 2, tilesHigh: 1, blockX: 0, blockY: 0)
            .Should()
            .Be(0.2f, "no tile lies beyond the edge to blend with");

    [Fact]
    public void ABlockInTheLastTileOfTheImageReadsNoTileBeyondIt() =>
        GuidedShares
            .TileShareAt(shares: [0.2f], imageWide: 1, tilesWide: 1, tilesHigh: 1, blockX: 7, blockY: 7)
            .Should()
            .Be(0.2f, "the tile past the last one lies outside the image");

    [Fact]
    public void ABlockReadsOnlyTheTilesThatTheRenderSizeCovers() =>
        GuidedShares
            .TileShareAt(
                shares: [0.1f, 0.25f, 0.25f, 0.25f],
                imageWide: 2,
                tilesWide: 1,
                tilesHigh: 1,
                blockX: 7,
                blockY: 7
            )
            .Should()
            .Be(0.1f, "the image's other tiles hold shares of a larger render size");
}
