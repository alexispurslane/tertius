namespace Tertius.Rendering.Tests;

/// <summary>
/// The joint bilateral upsample of the occlusion from half to full resolution: bilinear weights cut by the distance of
/// each texel from the pixel's tangent plane (docs/plans/ambient-occlusion.md, "The upsample").
/// </summary>
public sealed class OcclusionUpsampleTests
{
    // A pixel 2 m in front of the eye on a floor whose geometric normal points up, and the width of one texel there,
    // a power of two near 4.3 mm, so that the offsets below are exact.
    private const double TexelWidth = 1.0 / 256;
    private static readonly Double3 Pixel = new(0, -1, 2);
    private static readonly Double3 Up = Double3.UnitY;

    // The half-resolution image of a 1,920 × 1,080 image.
    private static readonly (int Width, int Height) HalfSize = (Width: 960, Height: 540);

    /// <summary>The occlusion of the full-resolution <paramref name="pixel"/> at <see cref="Pixel"/>, over <paramref name="texels"/>.</summary>
    private static double OcclusionOf((int X, int Y) pixel, Dictionary<(int X, int Y), UpsampleTexel> texels) =>
        OcclusionUpsample.Occlusion(
            pixel: pixel,
            position: Pixel,
            geometricNormal: Up,
            texelWidth: TexelWidth,
            halfSize: HalfSize,
            texelAt: coordinates => texels[coordinates]
        );

    [Fact]
    public void ARepresentativePixelTakesItsOwnTexel()
    {
        // Pixel (6, 10) is the top-left pixel of texel (3, 5), so its other three texels have a bilinear weight of 0.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel, Occlusion: 0.4),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(TexelWidth, 0, 0), Occlusion: 0.9),
            [(X: 3, Y: 6)] = new(Position: Pixel + new Double3(0, 0, TexelWidth), Occlusion: 0.9),
            [(X: 4, Y: 6)] = new(Position: Pixel + new Double3(TexelWidth, 0, TexelWidth), Occlusion: 0.9),
        };

        OcclusionOf(pixel: (X: 6, Y: 10), texels: texels).Should().Be(0.4);
    }

    [Fact]
    public void ATexelOfNoBilinearWeightIsNeverTheTexelNearestThePlane()
    {
        // Pixel (6, 10) takes only texel (3, 5), here on a box two texel widths above the floor. Texel (4, 5) lies on
        // the floor, but its bilinear weight is 0.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel + new Double3(0, 2 * TexelWidth, 0), Occlusion: 0.4),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(TexelWidth, 0, 0), Occlusion: 0.9),
            [(X: 3, Y: 6)] = new(Position: Pixel + new Double3(0, 0, TexelWidth), Occlusion: 0.9),
            [(X: 4, Y: 6)] = new(Position: Pixel + new Double3(TexelWidth, 0, TexelWidth), Occlusion: 0.9),
        };

        OcclusionOf(pixel: (X: 6, Y: 10), texels: texels)
            .Should()
            .Be(0.4, "the pass skips a texel of no bilinear weight before it measures the texel's distance");
    }

    [Fact]
    public void APixelBetweenTwoTexelsOnItsPlaneTakesTheirMean()
    {
        // Pixel (7, 10) lies between texels (3, 5) and (4, 5), both on the floor.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel - new Double3(TexelWidth / 2, 0, 0), Occlusion: 0.4),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(TexelWidth / 2, 0, 0), Occlusion: 0.8),
        };

        OcclusionOf(pixel: (X: 7, Y: 10), texels: texels).Should().BeApproximately(0.6, 1e-15);
    }

    [Fact]
    public void ATexelOffThePixelsPlaneByATexelWidthOrMoreGetsNoWeight()
    {
        // The second texel lies on a box one texel width above the floor: behind a silhouette, as the pixel sees it.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel - new Double3(TexelWidth / 2, 0, 0), Occlusion: 0.4),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(TexelWidth / 2, TexelWidth, 0), Occlusion: 0.8),
        };

        OcclusionOf(pixel: (X: 7, Y: 10), texels: texels).Should().Be(0.4);
    }

    [Fact]
    public void ATexelNearerThePixelsPlaneGetsMoreWeight()
    {
        // A quarter of a texel width off the plane keeps three quarters of the bilinear weight.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel, Occlusion: 0.4),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(TexelWidth, TexelWidth / 4, 0), Occlusion: 0.8),
        };

        OcclusionOf(pixel: (X: 7, Y: 10), texels: texels).Should().BeApproximately((0.4 + 0.75 * 0.8) / 1.75, 1e-12);
    }

    [Fact]
    public void WhenNoTexelLiesOnThePlaneThePixelTakesTheTexelNearestToIt()
    {
        // Pixel (7, 11) lies between the four texels of its block.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel + new Double3(0, 3 * TexelWidth, 0), Occlusion: 0.4),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(0, -2 * TexelWidth, 0), Occlusion: 0.7),
            [(X: 3, Y: 6)] = new(Position: Pixel + new Double3(0, 5 * TexelWidth, 0), Occlusion: 0.9),
            [(X: 4, Y: 6)] = new(Position: Pixel + new Double3(0, 4 * TexelWidth, 0), Occlusion: 0.6),
        };

        OcclusionOf(pixel: (X: 7, Y: 11), texels: texels)
            .Should()
            .Be(0.7, "the texel 2 widths below the plane is nearer than the ones 3 to 5 widths above it");
    }

    [Fact]
    public void ATexelBeyondTheEdgeOfTheHalfResolutionImageGetsNoWeight()
    {
        // The corner pixel (1,919, 1,079) lies between texel (959, 539) and three texels past the right and the bottom
        // edges of the 960 × 540 half-resolution image. The lookup gives those three values on the plane, as an image
        // read past its edge may.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 959, Y: 539)] = new(Position: Pixel, Occlusion: 0.4),
            [(X: 960, Y: 539)] = new(Position: Pixel + new Double3(TexelWidth / 2, 0, 0), Occlusion: 0.8),
            [(X: 959, Y: 540)] = new(Position: Pixel + new Double3(0, 0, TexelWidth / 2), Occlusion: 0.8),
            [(X: 960, Y: 540)] = new(Position: Pixel + new Double3(TexelWidth / 2, 0, TexelWidth / 2), Occlusion: 0.8),
        };

        OcclusionOf(pixel: (X: 1919, Y: 1079), texels: texels)
            .Should()
            .Be(0.4, "the pass reads no texel outside the image, and the one texel left keeps its value");
    }

    [Fact]
    public void AnEstimateAboveOneIsClampedToOne()
    {
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel, Occlusion: 1.2),
        };

        OcclusionOf(pixel: (X: 6, Y: 10), texels: texels)
            .Should()
            .Be(1, "the half-resolution terms keep the overshoot of a single slice, and the upsample clamps it");
    }

    [Fact]
    public void AnEvenCoordinateTakesItsOwnTexelAndAnOddOneTheMeanOfTwo()
    {
        OcclusionUpsample.AxisWeights(10).Should().Be((1.0, 0.0));
        OcclusionUpsample.AxisWeights(11).Should().Be((0.5, 0.5));
    }
}
