namespace Tertius.Rendering.Tests;

/// <summary>
/// The levels of the specular map: how many, the roughness that each holds, the level that a roughness reads, and the
/// level that a prefilter sample reads (docs/plans/environment-map.md, "The specular levels").
/// </summary>
public sealed class PrefilterLevelsTests
{
    [Fact]
    public void AMapOf128TexelsHasSixLevelsFrom128To4()
    {
        int levels = PrefilterLevels.Count(128);

        levels.Should().Be(6);
        (128 >> (levels - 1)).Should().Be(PrefilterLevels.SmallestFaceSize);
    }

    [Fact]
    public void AMapOf16TexelsHasThree() => PrefilterLevels.Count(16).Should().Be(3);

    [Fact]
    public void TheRadianceMapsMipChainReachesOneTexel()
    {
        PrefilterLevels.RadianceLevels(128).Should().Be(8);
        PrefilterLevels.RadianceLevels(16).Should().Be(5);
    }

    [Fact]
    public void LevelKHoldsRoughnessKOverTheLastLevel()
    {
        float[] roughness = [.. Enumerable.Range(0, 6).Select(level => PrefilterLevels.Roughness(level, 6))];

        roughness.Should().Equal(0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f);
    }

    [Fact]
    public void ARoughnessReadsTheLevelOfItsShareOfTheLastLevel() =>
        PrefilterLevels.Lod(0.6f, 6).Should().BeApproximately(3f, 1e-6f);

    [Fact]
    public void TheRoughnessFloorReadsMostlyTheMirrorLevel()
    {
        PrefilterLevels.Lod(0.045f, 6).Should().BeApproximately(0.225f, 1e-6f);
        PrefilterLevels.Lod(0f, 6).Should().BeApproximately(0.225f, 1e-6f, "the reader floors the roughness first");
    }

    [Fact]
    public void ASampleOfLowerDensityReadsACoarserLevel()
    {
        // At the density where one sample stands for 4^j texels of level 0, it reads level j plus the bias.
        const int faceSize = 128;
        const int samples = PrefilterLevels.SamplesPerTexel;
        double texel = 4 * Math.PI / (6.0 * faceSize * faceSize);
        float OneSamplePer(double texels) => (float)(1 / (samples * texels * texel));

        PrefilterLevels.SampleLod(OneSamplePer(4), samples, faceSize).Should().BeApproximately(2f, 1e-4f);
        PrefilterLevels.SampleLod(OneSamplePer(64), samples, faceSize).Should().BeApproximately(4f, 1e-4f);
        PrefilterLevels
            .SampleLod(OneSamplePer(64), samples, faceSize)
            .Should()
            .BeGreaterThan(PrefilterLevels.SampleLod(OneSamplePer(16), samples, faceSize));
    }

    [Fact]
    public void TheLevelOfASampleNeverFallsBelowZero() =>
        PrefilterLevels.SampleLod(pdf: 1e9f, samples: 64, faceSize: 128).Should().Be(0f);
}
