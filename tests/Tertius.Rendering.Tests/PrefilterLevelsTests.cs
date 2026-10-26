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

    /// <summary>
    /// A level of a quarter of the texels splits each texel's samples among four times the invocations, so that no level
    /// waits on a longer chain of texture reads than it needs (docs/plans/environment-map.md, "The specular levels").
    /// </summary>
    [Theory]
    [InlineData(EnvironmentMapSetting.MinFaceSize)]
    [InlineData(128)]
    [InlineData(EnvironmentMapSetting.MaxFaceSize)]
    public void EachRougherLevelKeepsTheInvocationsOfLevelOneUntilEachSampleHasItsOwn(int faceSize)
    {
        int levels = PrefilterLevels.Count(faceSize);
        long levelOne = (long)(faceSize / 2) * (faceSize / 2) * PrefilterLevels.Lanes(1);

        PrefilterLevels.Lanes(0).Should().Be(1, "the mirror level copies, one invocation per texel");
        PrefilterLevels.Lanes(1).Should().Be(1, "the largest prefiltered level fills the GPU with one per texel");
        for (int level = 1; level < levels; level++)
        {
            int lanes = PrefilterLevels.Lanes(level);
            int size = faceSize >> level;
            if (lanes < PrefilterLevels.SamplesPerTexel)
                ((long)size * size * lanes).Should().Be(levelOne, "level {0} keeps level 1's invocations", level);
            lanes.Should().BeLessThanOrEqualTo(PrefilterLevels.SamplesPerTexel, "a lane holds at least one sample");
            (PrefilterLevels.SamplesPerTexel % lanes).Should().Be(0, "every lane of level {0} holds as many", level);
        }
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
