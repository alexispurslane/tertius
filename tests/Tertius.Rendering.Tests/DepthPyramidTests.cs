namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# copy of the depth pyramid: its level count and sizes, and the min-max bounds of inverse depth of its cells
/// (docs/plans/screen-space-reflections.md, "The depth pyramid").
/// </summary>
public sealed class DepthPyramidTests
{
    [Fact]
    public void TheTopLevelOfA1920By1080ScreenIsEleven() => DepthPyramid.LevelCount(1920, 1080).Should().Be(11);

    [Fact]
    public void AOnePixelScreenHasOneLevel() => DepthPyramid.LevelCount(1, 1).Should().Be(1);

    [Fact]
    public void EachLevelHalvesItsSidesRoundingUp()
    {
        (int Width, int Height)[] sizes =
        [
            (1920, 1080),
            (960, 540),
            (480, 270),
            (240, 135),
            (120, 68),
            (60, 34),
            (30, 17),
            (15, 9),
            (8, 5),
            (4, 3),
            (2, 2),
            (1, 1),
        ];

        for (int level = 0; level < sizes.Length; level++)
            DepthPyramid.LevelSize(1920, 1080, level).Should().Be(sizes[level], $"level {level}");
    }

    [Fact]
    public void ACellHoldsTheNearestAndTheFarthestInverseDepthOfItsSurfacePixels()
    {
        float[] inverseDepths = [0.1f, 0.5f, 0.3f, 0.3f, 0.25f, 0.2f, 0.3f, 0.3f];

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 4, height: 2);

        pyramid.Bounds(1, 0, 0).Should().Be(new CellBounds(Near: 0.5f, Far: 0.1f));
        pyramid.Bounds(0, 2, 1).Should().Be(CellBounds.OfSurface(0.3f));
    }

    [Fact]
    public void SkyAddsNoSurfaceToACell()
    {
        float[] inverseDepths = [0.1f, 0, 0.25f, 0.2f];

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 2, height: 2);

        pyramid.Bounds(1, 0, 0).Should().Be(new CellBounds(Near: 0.25f, Far: 0.1f));
    }

    [Fact]
    public void ACellOfOnlySkyHasEmptyBoundsAndAnEmptySlab()
    {
        float[] inverseDepths = [0, 0, 0, 0, 0.2f, 0.2f];

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 2, height: 3);
        CellBounds sky = pyramid.Bounds(1, 0, 0);

        sky.Should().Be(new CellBounds(Near: 0, Far: CellBounds.EmptyFar));
        (float low, float high) = sky.Slab(CellBounds.ThicknessShare);
        low.Should().BeGreaterThan(high, "no inverse depth lies in the slab of a cell without a surface");
    }

    [Fact]
    public void ACellOutsideItsLevelHoldsNoSurface()
    {
        float[] inverseDepths = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f];

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 3, height: 2);

        pyramid.Bounds(0, -1, 0).Should().Be(CellBounds.Empty);
        pyramid.Bounds(0, 3, 1).Should().Be(CellBounds.Empty);
        pyramid.Bounds(0, 0, 2).Should().Be(CellBounds.Empty);
        pyramid.Bounds(1, 2, 0).Should().Be(CellBounds.Empty);
        pyramid.Bounds(1, 0, -1).Should().Be(CellBounds.Empty);
    }

    [Fact]
    public void TheLastCellOfAnOddLevelCoversTheRemainingPixels()
    {
        // 5 × 3 pixels: level 1 has 3 × 2 cells, and its last cell holds the one pixel (4, 2).
        float[] inverseDepths = [.. Enumerable.Repeat(0.1f, 15)];
        inverseDepths[2 * 5 + 4] = 0.7f;
        inverseDepths[0 * 5 + 4] = 0.4f;
        inverseDepths[1 * 5 + 4] = 0.5f;

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 5, height: 3);

        pyramid.Bounds(1, 2, 1).Should().Be(CellBounds.OfSurface(0.7f));
        pyramid.Bounds(1, 2, 0).Should().Be(new CellBounds(Near: 0.5f, Far: 0.4f));
        pyramid.Bounds(2, 1, 0).Should().Be(new CellBounds(Near: 0.7f, Far: 0.4f));
    }

    [Fact]
    public void TheTopCellBoundsEveryPixel()
    {
        var random = new Random(7);
        float[] inverseDepths =
        [
            .. Enumerable.Range(0, 37 * 23).Select(_ => random.Next(4) == 0 ? 0 : random.NextSingle()),
        ];
        float[] surfaces = [.. inverseDepths.Where(inverseDepth => inverseDepth > 0)];

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 37, height: 23);

        pyramid.TopLevel.Should().Be(6);
        pyramid.Bounds(pyramid.TopLevel, 0, 0).Should().Be(new CellBounds(Near: surfaces.Max(), Far: surfaces.Min()));
    }

    [Fact]
    public void AThicknessShareOfOneTenthFillsATenthOfTheDepthBehindASurface()
    {
        (float low, float high) = CellBounds.OfSurface(1 / 5f).Slab(CellBounds.ThicknessShare);

        (1 / high).Should().BeApproximately(5, 1e-5f);
        (1 / low).Should().BeApproximately(5.5f, 1e-5f, "a surface 5 m away fills the depths to 5.5 m");
    }
}
