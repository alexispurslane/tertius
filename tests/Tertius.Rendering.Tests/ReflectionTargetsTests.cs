using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The size of the depth pyramid image and the largest screen that the pyramids cover
/// (docs/plans/screen-space-reflections.md, "Images"), with no GPU. A Vulkan mip halves its image and rounds down, and a
/// level of the depth pyramid halves the screen and rounds up, so the image is padded until each of its mips holds its
/// level.
/// </summary>
public sealed class ReflectionTargetsTests
{
    public static TheoryData<uint, uint> Screens =>
        new()
        {
            { 1, 1 },
            { 2, 1 },
            { 3, 3 },
            { 640, 360 },
            { 641, 361 },
            { 1920, 1080 },
            { 1920, 200 },
            { 2560, 1440 },
            { 1024, 512 },
            { 1025, 3 },
            { 16384, 16384 },
        };

    [Theory]
    [InlineData(16_384, 16_384, true)]
    [InlineData(16_384, 1, true)]
    [InlineData(16_385, 1, false)]
    [InlineData(1, 16_385, false)]
    public void TheReflectionsCoverAScreenOfUpTo16384PxASide(uint width, uint height, bool covered) =>
        (ReflectionTargets.ScreenProblem(width, height) is null).Should().Be(covered);

    [Fact]
    public void AScreenOfTheLongestSideNeedsAsManyLevelsAsTheSetsHold()
    {
        int side = ReflectionTargets.MaxSide;

        DepthPyramid.LevelCount(side, side).Should().Be(DepthPyramid.MaxLevels, "one depth level set per level");
        (BitOperations.Log2((uint)side) + 1)
            .Should()
            .Be(DepthPyramid.MaxLevels + 1, "one radiance level set per mip, from the screen's size to 1 × 1");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void EachMipOfTheDepthImageHoldsItsLevelOfTheDepthPyramid(uint width, uint height)
    {
        Extent2D image = ReflectionTargets.DepthImageExtent(new(width, height));
        int topLevel = DepthPyramid.LevelCount((int)width, (int)height);

        for (int level = 1; level <= topLevel; level++)
        {
            (int levelWidth, int levelHeight) = DepthPyramid.LevelSize((int)width, (int)height, level);
            int mip = level - 1;
            MipSide(image.Width, mip)
                .Should()
                .BeGreaterThanOrEqualTo((uint)levelWidth, "mip {0} holds every cell of level {1} across", mip, level);
            MipSide(image.Height, mip).Should().BeGreaterThanOrEqualTo((uint)levelHeight, "and down");
        }
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void TheDepthImagesFullMipChainHasOneMipPerLevelAboveTheDistanceTarget(uint width, uint height)
    {
        Extent2D image = ReflectionTargets.DepthImageExtent(new(width, height));

        int mips = BitOperations.Log2(Math.Max(image.Width, image.Height)) + 1;

        mips.Should().Be(DepthPyramid.LevelCount((int)width, (int)height), "mips 0 to L − 1 hold levels 1 to L");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void TheDepthImageIsTheSmallestWhoseMipsHoldEveryLevel(uint width, uint height)
    {
        Extent2D image = ReflectionTargets.DepthImageExtent(new(width, height));
        int topLevel = DepthPyramid.LevelCount((int)width, (int)height);
        IEnumerable<(int Width, int Height)> levels = Enumerable
            .Range(1, topLevel)
            .Select(level => DepthPyramid.LevelSize((int)width, (int)height, level));

        bool narrowerHolds = levels
            .Select((size, mip) => MipSide(image.Width - 1, mip) >= size.Width)
            .All(holds => holds);
        bool shorterHolds = levels
            .Select((size, mip) => MipSide(image.Height - 1, mip) >= size.Height)
            .All(holds => holds);

        (image.Width == 1 || !narrowerHolds)
            .Should()
            .BeTrue("a narrower image of {0} px would hold every level", image.Width - 1);
        (image.Height == 1 || !shorterHolds)
            .Should()
            .BeTrue("a shorter image of {0} px would hold every level", image.Height - 1);
    }

    private static uint MipSide(uint side, int mip) => Math.Max(side >> mip, 1);
}
