using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The served chain, the map of an object, the draw rule and the kept maps (docs/plans/environment-maps-per-frame.md,
/// "The served chain and the map of an object", decisions 8 to 10 and 19). The frame ids of the cabin's chain are 10 for
/// the sun, 11 for earth, 12 for earth-surface and 13 for the shuttle.
/// </summary>
public sealed class EnvironmentChainTests
{
    private const long Sun = 10;
    private const long Earth = 11;
    private const long EarthSurface = 12;
    private const long Shuttle = 13;

    /// <summary>A chain of the given frame ids, camera first, each in the axes of the camera's frame.</summary>
    private static List<RenderChainFrame> Chain(params long[] cameraFirst) =>
        [.. cameraFirst.Select(id => new RenderChainFrame(id, Quaternion.Identity, Quaternion.Identity))];

    private static long[] Served(List<RenderChainFrame> chain)
    {
        long[] served = new long[EnvironmentChain.Maps(chain.Count)];
        Array.Fill(served, -1);
        EnvironmentChain.Serve(chain, served);
        return served;
    }

    /// <summary>Whether each map of <paramref name="served"/> is kept, map 0 first.</summary>
    private static bool[] KeptMaps(long[] drawnFor, long[] served) =>
        [.. Enumerable.Range(0, served.Length).Select(map => EnvironmentChain.Keeps(drawnFor, served, map))];

    [Fact]
    public void AnEmptyChainServesOneMapOfFrameZero() => Served(Chain()).Should().Equal(0L);

    [Fact]
    public void TheRootTakesMapZeroAndTheCamerasFrameTheLastMap() =>
        Served(Chain(Shuttle, EarthSurface, Earth, Sun)).Should().Equal(Sun, Earth, EarthSurface, Shuttle);

    [Fact]
    public void AnObjectTakesTheMapOfItsChainDepth()
    {
        int[] maps = [.. Enumerable.Range(0, 4).Select(depth => EnvironmentChain.MapOf(chainDepth: depth, maps: 4))];

        maps.Should().Equal([3, 2, 1, 0], "the seat at depth 0 takes the shuttle's map, the Sun at depth 3 the sun's");
    }

    [Fact]
    public void AChainOfMoreThanFortyTwoFramesMergesItsOutermostFramesIntoMapZero()
    {
        long[] cameraFirst = [.. Enumerable.Range(0, 45).Select(depth => 100L + depth)];

        long[] served = Served(Chain(cameraFirst));

        served.Should().HaveCount(EnvironmentChain.MaxMaps);
        served[0].Should().Be(141, "the 42nd frame from the camera, at depth 41, is the outermost served frame");
        served[^1].Should().Be(100, "the camera's frame");
        EnvironmentChain.MapOf(chainDepth: 41, maps: 42).Should().Be(0);
        EnvironmentChain.MapOf(chainDepth: 44, maps: 42).Should().Be(0, "the root, deeper than the served chain");
    }

    [Fact]
    public void TheCamerasFrameMapDrawsEveryObject()
    {
        bool[] drawn = [.. Enumerable.Range(0, 4).Select(objectMap => EnvironmentChain.Draws(map: 3, objectMap))];

        drawn.Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public void AMapDrawsTheObjectsOfItsFrameAndOfTheFramesOutsideItAndNoneBelow()
    {
        bool[] drawn = [.. Enumerable.Range(0, 4).Select(objectMap => EnvironmentChain.Draws(map: 1, objectMap))];

        drawn.Should().Equal([true, true, false, false], "earth's map draws the Sun and the Moon, not the ground");
    }

    [Fact]
    public void AMapDrawnForTheSameFrameAndTheSameFrameBelowItIsKept()
    {
        long[] drawnFor = [Sun, Earth, EarthSurface, 99];
        long[] served = [Sun, Earth, EarthSurface, Shuttle];

        KeptMaps(drawnFor, served)
            .Should()
            .Equal([true, true, false, false], "maps 0 and 1 keep their frames and the frames below them");
    }

    [Fact]
    public void TheCamerasFrameMapDrawnForTheSameChainIsKept() =>
        KeptMaps(drawnFor: [Sun, Earth, EarthSurface], served: [Sun, Earth, EarthSurface])
            .Should()
            .AllBeEquivalentTo(true);

    [Fact]
    public void AMapWhoseFrameBelowChangedIsNotKept() =>
        EnvironmentChain
            .Keeps(drawnFor: [Sun, Earth, EarthSurface], served: [Sun, Earth, 14], map: 1)
            .Should()
            .BeFalse();

    [Fact]
    public void WhenTheCameraEntersAChildOnlyTheMapsOfTheOldAndTheNewCameraFrameAreNotKept() =>
        KeptMaps(drawnFor: [Sun, Earth, EarthSurface], served: [Sun, Earth, EarthSurface, Shuttle])
            .Should()
            .Equal(
                [true, true, false, false],
                "earth-surface's map now leaves out the shuttle, and the shuttle's is new"
            );

    [Fact]
    public void WhenTheCameraLeavesForItsParentOnlyTheParentsMapIsNotKept() =>
        KeptMaps(drawnFor: [Sun, Earth, EarthSurface, Shuttle], served: [Sun, Earth, EarthSurface])
            .Should()
            .Equal([true, true, false], "earth-surface's map no longer leaves out anything");

    [Fact]
    public void WhenTheCamerasFrameHandsOffToItsGrandparentTheMapsAtAndBelowTheChangeAreNotKept() =>
        KeptMaps(drawnFor: [Sun, Earth, EarthSurface, Shuttle], served: [Sun, Earth, Shuttle])
            .Should()
            .Equal([true, false, false], "earth's map now leaves out the shuttle, and map 2 is now the shuttle's");

    [Fact]
    public void NoMapIsKeptBeforeTheFirstDrawnFrame() =>
        KeptMaps(drawnFor: [], served: [Sun, Earth, EarthSurface]).Should().AllBeEquivalentTo(false);
}
