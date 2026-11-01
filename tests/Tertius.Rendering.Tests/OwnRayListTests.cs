using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# side of the own-ray list of the half-resolution reflections (docs/plans/screen-space-reflections.md, "Rays at
/// outlines"), with no GPU: what the read hook decodes from its words, and the largest screen whose list one storage
/// buffer descriptor covers.
/// </summary>
public sealed class OwnRayListTests
{
    [Fact]
    public void TheListGivesItsCountedEntriesAsPixelsAndIgnoresTheWordsPastTheCount()
    {
        uint[] words = [1, 1, 1, 2, 4 | 5 << 16, 1919 | 1079 << 16, 3 | 3 << 16];

        OwnRayList.Pixels(words).Should().Equal((4, 5), (1919, 1079));
    }

    [Fact]
    public void AClearedListHoldsNoPixels()
    {
        uint[] words = [.. OwnRayList.EmptyHeader, 4 | 5 << 16];

        OwnRayList.Pixels(words).Should().BeEmpty("the clear sets the count to 0 before the upsample appends");
    }

    [Fact]
    public void TheListOfAScreenHoldsTheHeaderAndOneWordPerPixel() =>
        OwnRayList.Bytes(new Extent2D(1920, 1080)).Should().Be(16 + 4 * 1920 * 1080, "8.3 MB at 1080p");

    [Fact]
    public void AScreenWhoseListFillsTheDevicesStorageBufferRangeExactlyIsCovered()
    {
        var screen = new Extent2D(1920, 1080);

        OwnRayList.RangeProblem(screen, maxStorageBufferRange: (uint)OwnRayList.Bytes(screen)).Should().BeNull();
    }

    [Fact]
    public void AScreenWhoseListPassesTheDevicesStorageBufferRangeIsRefused()
    {
        var screen = new Extent2D(1920, 1080);

        OwnRayList
            .RangeProblem(screen, maxStorageBufferRange: (uint)OwnRayList.Bytes(screen) - 1)
            .Should()
            .Be(
                "The reflections' own-ray list of a screen of 1920 × 1080 takes 8294416 bytes, above the device's maxStorageBufferRange of 8294415."
            );
    }

    [Fact]
    public void TheSpecificationsLeastStorageBufferRangeCoversAnEightKScreen() =>
        OwnRayList.RangeProblem(new Extent2D(7680, 4320), maxStorageBufferRange: 1u << 27).Should().BeNull();
}
