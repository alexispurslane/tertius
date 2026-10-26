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

    /// <summary>
    /// One dispatch prefilters a run of levels, each in a storage image of its own, so that a frame waits on as few
    /// dispatches as the four storage images of every Vulkan device allow.
    /// </summary>
    [Theory]
    [InlineData(EnvironmentMapSetting.MinFaceSize)]
    [InlineData(128)]
    [InlineData(EnvironmentMapSetting.MaxFaceSize)]
    public void TheDispatchesTakeTheLevelsInOrderAtMostFourAtATime(int faceSize)
    {
        int levels = PrefilterLevels.Count(faceSize);
        var taken = new List<int>();

        for (int dispatch = 0; dispatch < PrefilterLevels.Dispatches(levels); dispatch++)
        {
            (int first, int count) = PrefilterLevels.LevelsOfDispatch(dispatch: dispatch, levels: levels);
            count.Should().BeInRange(1, 4, "dispatch {0} binds one storage image per level", dispatch);
            taken.AddRange(Enumerable.Range(first, count));
        }

        taken.Should().Equal(Enumerable.Range(0, levels), "each level is prefiltered once, in order");
        PrefilterLevels
            .Dispatches(levels)
            .Should()
            .Be((levels + 3) / 4, "every dispatch but the last takes four levels");
    }

    /// <summary>
    /// One dispatch of the downsample makes a run of levels of the radiance map from the level above the run: the last
    /// level of the run before, so that each dispatch waits on one barrier, and a workgroup's 8 by 8 texels of the run's
    /// first level hold the children of its texels of the two levels below.
    /// </summary>
    [Theory]
    [InlineData(EnvironmentMapSetting.MinFaceSize)]
    [InlineData(128)]
    [InlineData(EnvironmentMapSetting.MaxFaceSize)]
    public void TheDownsampleMakesTheMipsInOrderAtMostThreeFromTheLastLevelMade(int faceSize)
    {
        int levels = PrefilterLevels.RadianceLevels(faceSize);
        var made = new List<int>();
        int lastMade = 0;

        for (int dispatch = 0; dispatch < PrefilterLevels.DownsampleDispatches(levels); dispatch++)
        {
            (int source, int count) = PrefilterLevels.LevelsOfDownsample(dispatch: dispatch, levels: levels);
            source.Should().Be(lastMade, "dispatch {0} reads the last level that the chain made", dispatch);
            count.Should().BeInRange(1, 3, "dispatch {0} binds its source and one storage image per level", dispatch);
            made.AddRange(Enumerable.Range(source + 1, count));
            lastMade = source + count;
        }

        made.Should().Equal(Enumerable.Range(1, levels - 1), "each mip is made once, in order");
        PrefilterLevels
            .DownsampleDispatches(levels)
            .Should()
            .Be((levels - 1 + 2) / 3, "every dispatch but the last makes three levels");
    }

    /// <summary>
    /// A level of a dispatch takes the workgroups that its texels need, <see cref="PrefilterLevels.GroupInvocations"/> /
    /// <see cref="PrefilterLevels.Lanes"/> texels each, and no workgroup more, so the next level's workgroups follow.
    /// </summary>
    [Theory]
    [InlineData(EnvironmentMapSetting.MinFaceSize)]
    [InlineData(128)]
    [InlineData(EnvironmentMapSetting.MaxFaceSize)]
    public void EachLevelTakesTheWorkgroupsThatItsTexelsNeedAndNoMore(int faceSize)
    {
        for (int level = 0; level < PrefilterLevels.Count(faceSize); level++)
        {
            long texels = CubeFaces.Count * (long)(faceSize >> level) * (faceSize >> level);
            long texelsPerGroup = PrefilterLevels.GroupInvocations / PrefilterLevels.Lanes(level);
            long groups = PrefilterLevels.Workgroups(level: level, faceSize: faceSize);

            (groups * texelsPerGroup)
                .Should()
                .BeGreaterThanOrEqualTo(texels, "level {0} reaches its last texel", level);
            ((groups - 1) * texelsPerGroup).Should().BeLessThan(texels, "level {0} has no idle workgroup", level);
        }
    }

    /// <summary>
    /// A dispatch of the specular map counts its workgroups along X and Y, each at most 65,535, the least
    /// <c>maxComputeWorkGroupCount</c> that the Vulkan specification allows (Intel's and lavapipe's), and its grid holds
    /// every workgroup of its levels with no idle row. At 1024 texels the first dispatch needs 172,032 workgroups.
    /// </summary>
    [Theory]
    [InlineData(EnvironmentMapSetting.MinFaceSize)]
    [InlineData(128)]
    [InlineData(EnvironmentMapSetting.MaxFaceSize)]
    public void EveryDispatchOfTheSpecularMapFitsTheLeastWorkgroupCountOfEveryDevice(int faceSize)
    {
        const int leastLimit = 65535;
        int levels = PrefilterLevels.Count(faceSize);
        for (int dispatch = 0; dispatch < PrefilterLevels.Dispatches(levels); dispatch++)
        {
            (int first, int count) = PrefilterLevels.LevelsOfDispatch(dispatch: dispatch, levels: levels);
            long groups = Enumerable
                .Range(first, count)
                .Sum(level => (long)PrefilterLevels.Workgroups(level: level, faceSize: faceSize));

            (int x, int y) = PrefilterLevels.Grid((int)groups);

            x.Should()
                .BeInRange(
                    1,
                    leastLimit,
                    "dispatch {0} of a map of {1} texels counts X within the limit",
                    dispatch,
                    faceSize
                );
            y.Should()
                .BeInRange(
                    1,
                    leastLimit,
                    "dispatch {0} of a map of {1} texels counts Y within the limit",
                    dispatch,
                    faceSize
                );
            ((long)x * y)
                .Should()
                .BeGreaterThanOrEqualTo(groups, "the grid of dispatch {0} holds every workgroup", dispatch);
            ((long)x * (y - 1)).Should().BeLessThan(groups, "the grid of dispatch {0} has no idle row", dispatch);
        }
    }

    /// <summary>
    /// The grid of any count of workgroups has as few rows as hold them at 65,535 per row, and rows as short as hold them,
    /// so it holds every workgroup and ends fewer than a row's count of rows past the last.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(65535, 65535, 1)]
    [InlineData(65536, 32768, 2)]
    [InlineData(131071, 43691, 3)]
    [InlineData(172032, 57344, 3)]
    public void TheGridHoldsTheWorkgroupsInTheFewestAndShortestRows(int groups, int x, int y) =>
        PrefilterLevels.Grid(groups).Should().Be((x, y));

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
