namespace Tertius.Rendering.Tests;

/// <summary>
/// The size of the deform region and the dispatches of the deform pass, without a GPU
/// (docs/plans/skinning-and-morphs.md, "Buffers and the region" and "The pass").
/// </summary>
public sealed class DeformRegionTests
{
    [Fact]
    public void RegionGrowsToTheNextPowerOfTwoOfItsNeed() =>
        GeometryStore.RegionCapacityFor(vertices: 1_000, maxRegion: 1 << 20).Should().Be(1_024);

    [Fact]
    public void RegionOfANeedThatIsAPowerOfTwoHoldsExactlyIt() =>
        GeometryStore.RegionCapacityFor(vertices: 4_096, maxRegion: 1 << 20).Should().Be(4_096);

    [Fact]
    public void RegionHoldsAtLeastSixteenVertices() =>
        GeometryStore
            .RegionCapacityFor(vertices: 3, maxRegion: 1 << 20)
            .Should()
            .Be(16, "the region's capacity stays a multiple of 768 bytes");

    [Fact]
    public void RegionStopsAtTheMostThatItsWindowHolds() =>
        GeometryStore.RegionCapacityFor(vertices: 3_000_000, maxRegion: 2_796_192).Should().Be(2_796_192);

    [Fact]
    public void LargestRegionIsAMultipleOfSixteenVerticesWithinTheStorageRange()
    {
        // The spec's least maxStorageBufferRange, 2^27 bytes.
        int largest = GeometryStore.MaxRegion(1u << 27);

        largest.Should().Be(2_796_192, "2^27 / 48 is 2,796,202.67, and the multiple of 16 below it is 2,796,192");
        ((long)largest * 48).Should().BeLessThanOrEqualTo(1L << 27);
    }

    [Theory]
    [InlineData(1u, 1u)]
    [InlineData(64u, 1u)]
    [InlineData(65u, 2u)]
    [InlineData(425u, 7u)]
    public void OneGroupDeformsSixtyFourVertices(uint vertices, uint groups) =>
        DeformPass.Groups(vertices).Should().Be(groups);
}
