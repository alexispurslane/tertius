namespace Tertius.Rendering.Tests;

/// <summary>
/// The size of the deform region, of the pose buffer and the job table, and the dispatches of the deform pass, without a GPU
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
    public void RegionHoldsAtLeastSixtyFourVertices() =>
        GeometryStore
            .RegionCapacityFor(vertices: 3, maxRegion: 1 << 20)
            .Should()
            .Be(64, "the region's capacity stays a multiple of 3,328 bytes, 13 times 256");

    [Fact]
    public void RegionStopsAtTheMostThatItsWindowHolds() =>
        GeometryStore.RegionCapacityFor(vertices: 3_000_000, maxRegion: 2_581_056).Should().Be(2_581_056);

    [Fact]
    public void LargestRegionIsAMultipleOfSixtyFourVerticesWithinTheStorageRange()
    {
        // The spec's least maxStorageBufferRange, 2^27 bytes.
        int largest = GeometryStore.MaxRegion(1u << 27);

        largest.Should().Be(2_581_056, "2^27 / 52 is 2,581,110.15, and the multiple of 64 below it is 2,581,056");
        ((long)largest * 52).Should().BeLessThanOrEqualTo(1L << 27);
    }

    [Fact]
    public void PoseBufferOfTheRegionStaysWithinTheStorageRange()
    {
        // The spec's least maxStorageBufferRange, 2^27 bytes, and a pose row of 16 bytes.
        var region = DeformRegion.Within(vertexBase: 0, capacity: 16, maxStorageBufferRange: 1u << 27);

        ((long)region.PoseRowCapacity * 16).Should().BeLessThanOrEqualTo(1L << 27);
        ((long)(region.PoseRowCapacity + 1) * 16)
            .Should()
            .BeGreaterThan(1L << 27, "the buffer holds every row that fits");
    }

    [Fact]
    public void JobTableOfTheRegionStaysWithinTheStorageRange()
    {
        // A job record of 28 bytes.
        var region = DeformRegion.Within(vertexBase: 0, capacity: 16, maxStorageBufferRange: 1u << 27);

        ((long)region.JobCapacity * 28).Should().BeLessThanOrEqualTo(1L << 27);
        ((long)(region.JobCapacity + 1) * 28)
            .Should()
            .BeGreaterThan(1L << 27, "the table holds every record that fits");
    }

    [Fact]
    public void StorageArrayGrowsByDoubling() =>
        HostArray<GpuDeformJob>.GrownCapacity(capacity: 16, needed: 40, maxCapacity: 1 << 20).Should().Be(64);

    [Fact]
    public void StorageArrayStopsGrowingAtTheMostThatTheStorageRangeHolds() =>
        HostArray<GpuDeformJob>
            .GrownCapacity(capacity: 16, needed: 40, maxCapacity: 50)
            .Should()
            .Be(50, "a buffer bound whole stays within maxStorageBufferRange while its data does");

    [Fact]
    public void StorageArrayHoldsDataPastTheStorageRange() =>
        HostArray<GpuDeformJob>
            .GrownCapacity(capacity: 16, needed: 60, maxCapacity: 50)
            .Should()
            .Be(60, "the array never drops data: the batcher keeps the deform pass's data within the range");

    [Theory]
    [InlineData(1u, 1u)]
    [InlineData(64u, 1u)]
    [InlineData(65u, 2u)]
    [InlineData(425u, 7u)]
    public void OneGroupDeformsSixtyFourVertices(uint vertices, uint groups) =>
        DeformPass.Groups(vertices).Should().Be(groups);
}
