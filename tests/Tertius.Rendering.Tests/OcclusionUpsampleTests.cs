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

    /// <summary>The bilinear weight of the texel (a, b) of the block above and left of full-resolution pixel (x, y).</summary>
    private static double Bilinear(int x, int y, int a, int b)
    {
        (double low, double high) = OcclusionUpsample.AxisWeights(x);
        (double top, double bottom) = OcclusionUpsample.AxisWeights(y);
        return (a == 0 ? low : high) * (b == 0 ? top : bottom);
    }

    [Fact]
    public void ARepresentativePixelTakesItsOwnTexel()
    {
        // Pixel (6, 10) is the top-left pixel of texel (3, 5), so its other three texels have a bilinear weight of 0.
        UpsampleTexel[] texels =
        [
            new(Position: Pixel, Occlusion: 0.4, BilinearWeight: Bilinear(x: 6, y: 10, a: 0, b: 0)),
            new(
                Position: Pixel + new Double3(TexelWidth, 0, 0),
                Occlusion: 0.9,
                BilinearWeight: Bilinear(x: 6, y: 10, a: 1, b: 0)
            ),
            new(
                Position: Pixel + new Double3(0, 0, TexelWidth),
                Occlusion: 0.9,
                BilinearWeight: Bilinear(x: 6, y: 10, a: 0, b: 1)
            ),
            new(
                Position: Pixel + new Double3(TexelWidth, 0, TexelWidth),
                Occlusion: 0.9,
                BilinearWeight: Bilinear(x: 6, y: 10, a: 1, b: 1)
            ),
        ];

        OcclusionUpsample
            .Occlusion(position: Pixel, geometricNormal: Up, texelWidth: TexelWidth, texels: texels)
            .Should()
            .Be(0.4);
    }

    [Fact]
    public void APixelBetweenTwoTexelsOnItsPlaneTakesTheirMean()
    {
        // Pixel (7, 10) lies between texels (3, 5) and (4, 5), both on the floor.
        UpsampleTexel[] texels =
        [
            new(
                Position: Pixel - new Double3(TexelWidth / 2, 0, 0),
                Occlusion: 0.4,
                BilinearWeight: Bilinear(x: 7, y: 10, a: 0, b: 0)
            ),
            new(
                Position: Pixel + new Double3(TexelWidth / 2, 0, 0),
                Occlusion: 0.8,
                BilinearWeight: Bilinear(x: 7, y: 10, a: 1, b: 0)
            ),
        ];

        OcclusionUpsample
            .Occlusion(position: Pixel, geometricNormal: Up, texelWidth: TexelWidth, texels: texels)
            .Should()
            .BeApproximately(0.6, 1e-15);
    }

    [Fact]
    public void ATexelOffThePixelsPlaneByATexelWidthOrMoreGetsNoWeight()
    {
        // The second texel lies on a box one texel width above the floor: behind a silhouette, as the pixel sees it.
        UpsampleTexel[] texels =
        [
            new(Position: Pixel - new Double3(TexelWidth / 2, 0, 0), Occlusion: 0.4, BilinearWeight: 0.5),
            new(Position: Pixel + new Double3(TexelWidth / 2, TexelWidth, 0), Occlusion: 0.8, BilinearWeight: 0.5),
        ];

        OcclusionUpsample
            .Occlusion(position: Pixel, geometricNormal: Up, texelWidth: TexelWidth, texels: texels)
            .Should()
            .Be(0.4);
    }

    [Fact]
    public void ATexelNearerThePixelsPlaneGetsMoreWeight()
    {
        // A quarter of a texel width off the plane keeps three quarters of the bilinear weight.
        UpsampleTexel[] texels =
        [
            new(Position: Pixel, Occlusion: 0.4, BilinearWeight: 0.5),
            new(Position: Pixel + new Double3(TexelWidth, TexelWidth / 4, 0), Occlusion: 0.8, BilinearWeight: 0.5),
        ];

        OcclusionUpsample
            .Occlusion(position: Pixel, geometricNormal: Up, texelWidth: TexelWidth, texels: texels)
            .Should()
            .BeApproximately((0.4 + 0.75 * 0.8) / 1.75, 1e-12);
    }

    [Fact]
    public void WhenNoTexelLiesOnThePlaneThePixelTakesTheTexelNearestToIt()
    {
        UpsampleTexel[] texels =
        [
            new(Position: Pixel + new Double3(0, 3 * TexelWidth, 0), Occlusion: 0.4, BilinearWeight: 0.25),
            new(Position: Pixel + new Double3(0, -2 * TexelWidth, 0), Occlusion: 0.7, BilinearWeight: 0.25),
            new(Position: Pixel + new Double3(0, 5 * TexelWidth, 0), Occlusion: 0.9, BilinearWeight: 0.5),
        ];

        OcclusionUpsample
            .Occlusion(position: Pixel, geometricNormal: Up, texelWidth: TexelWidth, texels: texels)
            .Should()
            .Be(0.7, "the texel 2 widths below the plane is nearer than the ones 3 and 5 widths above it");
    }

    [Fact]
    public void ATexelBeyondTheEdgeOfTheHalfResolutionImageGetsNoWeight()
    {
        // On an image 1,920 pixels wide, pixel 1,919 lies between texel 959 and texel 960, which the 960 texels of the
        // half-resolution image do not hold. The pass leaves texel 960 out, and the one texel left keeps its value.
        UpsampleTexel[] texels =
        [
            new(Position: Pixel, Occlusion: 0.4, BilinearWeight: Bilinear(x: 1919, y: 10, a: 0, b: 0)),
        ];

        OcclusionUpsample
            .Occlusion(position: Pixel, geometricNormal: Up, texelWidth: TexelWidth, texels: texels)
            .Should()
            .BeApproximately(0.4, 1e-15, "the weights are normalized over the texels that the image holds");
    }

    [Fact]
    public void AnEstimateAboveOneIsClampedToOne()
    {
        UpsampleTexel[] texels = [new(Position: Pixel, Occlusion: 1.2, BilinearWeight: 1)];

        OcclusionUpsample
            .Occlusion(position: Pixel, geometricNormal: Up, texelWidth: TexelWidth, texels: texels)
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
