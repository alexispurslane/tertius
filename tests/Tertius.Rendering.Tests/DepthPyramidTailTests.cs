using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Which mips of the depth pyramid image the tail dispatch builds (docs/plans/screen-space-reflections.md, "The passes"),
/// with no GPU: one workgroup builds every mip from the first one that its shared memory holds, after one dispatch per
/// larger mip, so the small mips cost one barrier instead of one each.
/// </summary>
public sealed class DepthPyramidTailTests
{
    public static TheoryData<uint, uint> Images =>
        new()
        {
            { 2, 1 },
            { 3, 3 },
            { 64, 32 },
            { 640, 360 },
            { 1920, 1080 },
            { 1024, 544 },
            { 16384, 8 },
            { 16384, 16384 },
        };

    [Fact]
    public void ADeviceWithTheSpecsMinimumOfFourStorageImagesInAStageCannotBuildTheTail() =>
        DepthPyramidTail
            .StorageImageProblem(maxPerStageStorageImages: 4)
            .Should()
            .Contain("10 storage images", "the tail binds one storage image for each of its mips");

    [Fact]
    public void ADeviceWithAStorageImageInAStageForEachMipOfTheTailCanBuildIt() =>
        DepthPyramidTail.StorageImageProblem(maxPerStageStorageImages: DepthPyramidTail.Mips).Should().BeNull();

    [Fact]
    public void TheTailOfA1080pDepthPyramidStartsAtItsMipOf16By16() =>
        // At 1920 × 1080 the depth image is padded to 1024 × 1024 (DepthPyramidImages.DepthImageExtent).
        DepthPyramidTail.FirstMip(new Extent2D(1024, 1024), mipCount: 11).Should().Be(6, "mip 5 is 32 × 32");

    [Theory]
    [MemberData(nameof(Images))]
    public void TheTailStartsAtTheFirstMipWhoseTexelsOneWorkgroupHolds(uint width, uint height)
    {
        uint mipCount = (uint)BitOperations.Log2(Math.Max(width, height)) + 1;

        int start = DepthPyramidTail.FirstMip(new Extent2D(width, height), mipCount);

        start.Should().BeLessThan((int)mipCount, "an image of more than one mip has a tail");
        Texels(width, height, start).Should().BeLessThanOrEqualTo(DepthPyramidTail.Texels);
        if (start > 1)
            Texels(width, height, start - 1)
                .Should()
                .BeGreaterThan(DepthPyramidTail.Texels, "a mip that one workgroup holds belongs to the tail");
    }

    [Fact]
    public void MipZeroIsBuiltOnItsOwnEvenWhenOneWorkgroupHoldsIt() =>
        DepthPyramidTail.FirstMip(new Extent2D(32, 32), mipCount: 6).Should().Be(1, "mip 0 reads the distance target");

    [Fact]
    public void AnImageOfOneMipHasNoTail() =>
        DepthPyramidTail.FirstMip(new Extent2D(1, 1), mipCount: 1).Should().Be(1, "the only mip is mip 0");

    [Theory]
    [MemberData(nameof(Images))]
    public void TheTailNeverHoldsMoreMipsThanItsSetBinds(uint width, uint height)
    {
        uint mipCount = (uint)BitOperations.Log2(Math.Max(width, height)) + 1;

        int start = DepthPyramidTail.FirstMip(new Extent2D(width, height), mipCount);

        ((int)mipCount - start).Should().BeLessThanOrEqualTo(DepthPyramidTail.Mips);
    }

    private static long Texels(uint width, uint height, int mip) =>
        (long)Math.Max(width >> mip, 1) * Math.Max(height >> mip, 1);
}
