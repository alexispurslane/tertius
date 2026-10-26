namespace Tertius.Rendering.Tests;

/// <summary>
/// The faces that each drawn frame draws: the slots of all maps in turn by the frame number, or all six faces of a map on
/// a refresh (docs/plans/environment-map.md, "Which faces a frame draws", and
/// docs/plans/environment-maps-per-frame.md, decisions 10 and 11).
/// </summary>
public sealed class EnvironmentFacesTests
{
    private static FaceMask Faces(params int[] layers) =>
        layers.Aggregate(FaceMask.None, (mask, layer) => mask | (FaceMask)(1 << layer));

    /// <summary>The faces of each of <paramref name="maps"/> maps that drawn frame <paramref name="frame"/> draws in turn.</summary>
    private static FaceMask[] InTurn(long frame, int facesPerFrame, int maps)
    {
        var faces = new FaceMask[maps];
        EnvironmentFaces.InTurn(frame, facesPerFrame, faces);
        return faces;
    }

    [Fact]
    public void OneFacePerFrameVisitsTheFacesInLayerOrder()
    {
        FaceMask[] drawn = [.. Enumerable.Range(0, 8).Select(frame => InTurn(frame, facesPerFrame: 1, maps: 1)[0])];

        drawn
            .Should()
            .Equal(
                FaceMask.PositiveX,
                FaceMask.NegativeX,
                FaceMask.PositiveY,
                FaceMask.NegativeY,
                FaceMask.PositiveZ,
                FaceMask.NegativeZ,
                FaceMask.PositiveX,
                FaceMask.NegativeX
            );
    }

    [Fact]
    public void AtOneFacePerFrameEveryFaceIsDrawnOnceInSixFrames()
    {
        foreach (long start in new long[] { 0, 5, 1_000_003 })
        {
            int[] counts = new int[CubeFaces.Count];
            for (long frame = start; frame < start + 6; frame++)
            for (int layer = 0; layer < CubeFaces.Count; layer++)
                if (InTurn(frame, facesPerFrame: 1, maps: 1)[0].HasFlag((FaceMask)(1 << layer)))
                    counts[layer]++;

            counts.Should().AllBeEquivalentTo(1, $"the six frames from {start}");
        }
    }

    [Fact]
    public void FourFacesPerFrameWrapAroundTheLayers()
    {
        InTurn(0, facesPerFrame: 4, maps: 1).Should().Equal(Faces(0, 1, 2, 3));
        InTurn(1, facesPerFrame: 4, maps: 1).Should().Equal(Faces(4, 5, 0, 1));
        InTurn(2, facesPerFrame: 4, maps: 1).Should().Equal(Faces(2, 3, 4, 5));
    }

    [Fact]
    public void SixFacesPerFrameDrawEveryFace() => InTurn(17, facesPerFrame: 6, maps: 1).Should().Equal(FaceMask.All);

    [Fact]
    public void OneFacePerFrameVisitsEveryFaceOfOneMapBeforeTheNextMap()
    {
        FaceMask[][] drawn = [.. Enumerable.Range(0, 12).Select(frame => InTurn(frame, facesPerFrame: 1, maps: 2))];

        FaceMask[][] expected =
        [
            .. Enumerable.Range(0, 6).Select(layer => new[] { Faces(layer), FaceMask.None }),
            .. Enumerable.Range(0, 6).Select(layer => new[] { FaceMask.None, Faces(layer) }),
        ];
        drawn.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    [Fact]
    public void AtOneFacePerFrameEveryFaceOfFourMapsIsDrawnOnceInTwentyFourFrames()
    {
        foreach (long start in new long[] { 0, 5, 1_000_003 })
        {
            int[] counts = new int[4 * CubeFaces.Count];
            for (long frame = start; frame < start + 24; frame++)
            {
                FaceMask[] faces = InTurn(frame, facesPerFrame: 1, maps: 4);
                for (int slot = 0; slot < counts.Length; slot++)
                    if (faces[slot / CubeFaces.Count].HasFlag((FaceMask)(1 << (slot % CubeFaces.Count))))
                        counts[slot]++;
            }

            counts.Should().AllBeEquivalentTo(1, $"the 24 frames from {start}");
        }
    }

    [Fact]
    public void FourFacesPerFrameCanFallInTwoMaps() =>
        InTurn(1, facesPerFrame: 4, maps: 2).Should().Equal(Faces(4, 5), Faces(0, 1));

    [Fact]
    public void AMapWithoutASlotInTheFrameDrawsNoFace()
    {
        FaceMask[] faces = [FaceMask.All, FaceMask.All, FaceMask.All];

        EnvironmentFaces.InTurn(7, 1, faces);

        faces.Should().Equal(FaceMask.None, Faces(1), FaceMask.None);
    }

    [Fact]
    public void AMapThatIsNotKeptRefreshesEveryFace() =>
        EnvironmentFaces.RefreshesAll(drawnFor: [10, 11], served: [10, 12], map: 1, longStep: false).Should().BeTrue();

    [Fact]
    public void ALongStepRefreshesEveryMap()
    {
        long[] chain = [10, 11, 12];

        bool[] refreshed =
        [
            .. Enumerable
                .Range(0, chain.Length)
                .Select(map => EnvironmentFaces.RefreshesAll(drawnFor: chain, served: chain, map: map, longStep: true)),
        ];

        refreshed.Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public void AKeptMapWithoutALongStepDrawsItsFacesInTurn()
    {
        long[] chain = [10, 11, 12];

        bool[] refreshed =
        [
            .. Enumerable
                .Range(0, chain.Length)
                .Select(map =>
                    EnvironmentFaces.RefreshesAll(drawnFor: chain, served: chain, map: map, longStep: false)
                ),
        ];

        refreshed.Should().AllBeEquivalentTo(false);
    }

    /// <summary>
    /// The most frames that a face of a kept map waits between two draws when the served maps go from
    /// <paramref name="before"/> to <paramref name="after"/>, at one face per frame, over every frame of the change in two
    /// full cycles of both counts.
    /// </summary>
    private static int OldestKeptFaceAcrossAChange(int before, int after)
    {
        int cycle = CubeFaces.Count * before * CubeFaces.Count * after;
        int oldest = 0;
        for (int change = cycle; change < 2 * cycle; change++)
        for (int map = 0; map < Math.Min(before, after); map++)
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        {
            var face = (FaceMask)(1 << layer);
            int last = change - 1;
            while (!InTurn(last, facesPerFrame: 1, maps: before)[map].HasFlag(face))
                last--;
            int next = change;
            while (!InTurn(next, facesPerFrame: 1, maps: after)[map].HasFlag(face))
                next++;
            oldest = Math.Max(oldest, next - 1 - last);
        }

        return oldest;
    }

    [Fact]
    public void AcrossAChangeOfTheMapCountAKeptFaceCanBeOlderThanOneCycleButNoOlderThanBothCyclesLessTwo()
    {
        int oldest = OldestKeptFaceAcrossAChange(before: 3, after: 4);

        oldest.Should().BeGreaterThan(6 * 4 - 1, "the kept face waits for its slot in the new schedule");
        oldest.Should().BeLessThanOrEqualTo(6 * 3 + 6 * 4 - 2);
    }
}
