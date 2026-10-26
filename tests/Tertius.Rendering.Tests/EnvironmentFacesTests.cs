namespace Tertius.Rendering.Tests;

/// <summary>
/// The faces that each drawn frame draws: in turn by the frame number, or all six on a refresh
/// (docs/plans/environment-map.md, "Which faces a frame draws").
/// </summary>
public sealed class EnvironmentFacesTests
{
    private static FaceMask Faces(params int[] layers) =>
        layers.Aggregate(FaceMask.None, (mask, layer) => mask | (FaceMask)(1 << layer));

    [Fact]
    public void OneFacePerFrameVisitsTheFacesInLayerOrder()
    {
        FaceMask[] drawn = [.. Enumerable.Range(0, 8).Select(frame => EnvironmentFaces.InTurn(frame, 1))];

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
                if (EnvironmentFaces.InTurn(frame, 1).HasFlag((FaceMask)(1 << layer)))
                    counts[layer]++;

            counts.Should().AllBeEquivalentTo(1, $"the six frames from {start}");
        }
    }

    [Fact]
    public void FourFacesPerFrameWrapAroundTheLayers()
    {
        EnvironmentFaces.InTurn(0, 4).Should().Be(Faces(0, 1, 2, 3));
        EnvironmentFaces.InTurn(1, 4).Should().Be(Faces(4, 5, 0, 1));
        EnvironmentFaces.InTurn(2, 4).Should().Be(Faces(2, 3, 4, 5));
    }

    [Fact]
    public void SixFacesPerFrameDrawEveryFace() => EnvironmentFaces.InTurn(17, 6).Should().Be(FaceMask.All);

    [Fact]
    public void AMapWithNoFrameRefreshesEveryFace() =>
        EnvironmentFaces.RefreshesAll(mapFrameId: null, originFrameId: 4, longStep: false).Should().BeTrue();

    [Fact]
    public void AChangeOfTheCamerasFrameRefreshesEveryFace() =>
        EnvironmentFaces.RefreshesAll(mapFrameId: 4, originFrameId: 9, longStep: false).Should().BeTrue();

    [Fact]
    public void ALongStepRefreshesEveryFace() =>
        EnvironmentFaces.RefreshesAll(mapFrameId: 4, originFrameId: 4, longStep: true).Should().BeTrue();

    [Fact]
    public void ADrawInTheSameFrameWithoutALongStepDrawsItsFacesInTurn() =>
        EnvironmentFaces.RefreshesAll(mapFrameId: 4, originFrameId: 4, longStep: false).Should().BeFalse();
}
