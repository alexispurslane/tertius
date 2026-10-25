namespace Tertius.Rendering.Tests;

/// <summary>
/// The joint bilateral upsample of the estimate, the bounce and the occlusion, from half to full resolution: bilinear
/// weights cut by the distance of each texel from the pixel's tangent plane (docs/plans/ambient-occlusion.md, "The
/// upsample", and docs/plans/screen-space-illumination.md, "The denoise and the upsample").
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

    // The color of the bounce per unit of occlusion: each channel differs, so a test sees every channel move.
    private static readonly Double3 BounceColor = new(0.5, 1.5, 3);

    /// <summary>The estimate of a texel with the occlusion <paramref name="occlusion"/> and a bounce in proportion to it.</summary>
    private static AmbientEstimate Lit(double occlusion) => new(Bounce: BounceColor * occlusion, Occlusion: occlusion);

    private static void ShouldBeNear(AmbientEstimate actual, AmbientEstimate expected, double tolerance)
    {
        actual.Occlusion.Should().BeApproximately(expected.Occlusion, tolerance);
        actual.Bounce.X.Should().BeApproximately(expected.Bounce.X, tolerance);
        actual.Bounce.Y.Should().BeApproximately(expected.Bounce.Y, tolerance);
        actual.Bounce.Z.Should().BeApproximately(expected.Bounce.Z, tolerance);
    }

    /// <summary>The estimate of the full-resolution <paramref name="pixel"/> at <see cref="Pixel"/>, over <paramref name="texels"/>.</summary>
    private static AmbientEstimate EstimateOf((int X, int Y) pixel, Dictionary<(int X, int Y), UpsampleTexel> texels) =>
        OcclusionUpsample.Estimate(
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
            [(X: 3, Y: 5)] = new(Position: Pixel, Estimate: Lit(0.4)),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(TexelWidth, 0, 0), Estimate: Lit(0.9)),
            [(X: 3, Y: 6)] = new(Position: Pixel + new Double3(0, 0, TexelWidth), Estimate: Lit(0.9)),
            [(X: 4, Y: 6)] = new(Position: Pixel + new Double3(TexelWidth, 0, TexelWidth), Estimate: Lit(0.9)),
        };

        EstimateOf(pixel: (X: 6, Y: 10), texels: texels).Should().Be(Lit(0.4));
    }

    [Fact]
    public void ATexelOfNoBilinearWeightIsNeverTheTexelNearestThePlane()
    {
        // Pixel (6, 10) takes only texel (3, 5), here on a box two texel widths above the floor. Texel (4, 5) lies on
        // the floor, but its bilinear weight is 0.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel + new Double3(0, 2 * TexelWidth, 0), Estimate: Lit(0.4)),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(TexelWidth, 0, 0), Estimate: Lit(0.9)),
            [(X: 3, Y: 6)] = new(Position: Pixel + new Double3(0, 0, TexelWidth), Estimate: Lit(0.9)),
            [(X: 4, Y: 6)] = new(Position: Pixel + new Double3(TexelWidth, 0, TexelWidth), Estimate: Lit(0.9)),
        };

        EstimateOf(pixel: (X: 6, Y: 10), texels: texels)
            .Should()
            .Be(Lit(0.4), "the pass skips a texel of no bilinear weight before it measures the texel's distance");
    }

    [Fact]
    public void APixelBetweenTwoTexelsOnItsPlaneTakesTheirMean()
    {
        // Pixel (7, 10) lies between texels (3, 5) and (4, 5), both on the floor.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel - new Double3(TexelWidth / 2, 0, 0), Estimate: Lit(0.4)),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(TexelWidth / 2, 0, 0), Estimate: Lit(0.8)),
        };

        ShouldBeNear(EstimateOf(pixel: (X: 7, Y: 10), texels: texels), Lit(0.6), 1e-15);
    }

    [Fact]
    public void ATexelOffThePixelsPlaneByATexelWidthOrMoreGetsNoWeight()
    {
        // The second texel lies on a box one texel width above the floor: behind a silhouette, as the pixel sees it.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel - new Double3(TexelWidth / 2, 0, 0), Estimate: Lit(0.4)),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(TexelWidth / 2, TexelWidth, 0), Estimate: Lit(0.8)),
        };

        EstimateOf(pixel: (X: 7, Y: 10), texels: texels).Should().Be(Lit(0.4));
    }

    [Fact]
    public void ATexelNearerThePixelsPlaneGetsMoreWeight()
    {
        // A quarter of a texel width off the plane keeps three quarters of the bilinear weight.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel, Estimate: Lit(0.4)),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(TexelWidth, TexelWidth / 4, 0), Estimate: Lit(0.8)),
        };

        ShouldBeNear(EstimateOf(pixel: (X: 7, Y: 10), texels: texels), Lit((0.4 + 0.75 * 0.8) / 1.75), 1e-12);
    }

    [Fact]
    public void WhenNoTexelLiesOnThePlaneThePixelTakesTheTexelNearestToIt()
    {
        // Pixel (7, 11) lies between the four texels of its block.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel + new Double3(0, 3 * TexelWidth, 0), Estimate: Lit(0.4)),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(0, -2 * TexelWidth, 0), Estimate: Lit(0.7)),
            [(X: 3, Y: 6)] = new(Position: Pixel + new Double3(0, 5 * TexelWidth, 0), Estimate: Lit(0.9)),
            [(X: 4, Y: 6)] = new(Position: Pixel + new Double3(0, 4 * TexelWidth, 0), Estimate: Lit(0.6)),
        };

        EstimateOf(pixel: (X: 7, Y: 11), texels: texels)
            .Should()
            .Be(Lit(0.7), "the texel 2 widths below the plane is nearer than the ones 3 to 5 widths above it");
    }

    [Fact]
    public void ATexelBeyondTheEdgeOfTheHalfResolutionImageGetsNoWeight()
    {
        // The corner pixel (1,919, 1,079) lies between texel (959, 539) and three texels past the right and the bottom
        // edges of the 960 × 540 half-resolution image. The lookup gives those three values on the plane, as an image
        // read past its edge may.
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 959, Y: 539)] = new(Position: Pixel, Estimate: Lit(0.4)),
            [(X: 960, Y: 539)] = new(Position: Pixel + new Double3(TexelWidth / 2, 0, 0), Estimate: Lit(0.8)),
            [(X: 959, Y: 540)] = new(Position: Pixel + new Double3(0, 0, TexelWidth / 2), Estimate: Lit(0.8)),
            [(X: 960, Y: 540)] = new(
                Position: Pixel + new Double3(TexelWidth / 2, 0, TexelWidth / 2),
                Estimate: Lit(0.8)
            ),
        };

        EstimateOf(pixel: (X: 1919, Y: 1079), texels: texels)
            .Should()
            .Be(Lit(0.4), "the pass reads no texel outside the image, and the one texel left keeps its value");
    }

    [Fact]
    public void AnOcclusionAboveOneIsClampedToOne()
    {
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel, Estimate: Lit(1.2)),
        };

        EstimateOf(pixel: (X: 6, Y: 10), texels: texels)
            .Should()
            .Be(
                new AmbientEstimate(Bounce: BounceColor * 1.2, Occlusion: 1),
                "the upsample clamps the occlusion that the far rays' rounding moves past its range, and only the occlusion"
            );
    }

    [Fact]
    public void TheBounceTakesTheWeightsOfTheOcclusion()
    {
        // A quarter of a texel width off the plane keeps three quarters of the bilinear weight. The bounces are not in
        // proportion to the occlusions, so the bounce must take the same weights on its own.
        Double3 nearBounce = new(0.25, 2, 0.5);
        Double3 offBounce = new(4, 0.125, 1);
        Dictionary<(int X, int Y), UpsampleTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel, Estimate: new(Bounce: nearBounce, Occlusion: 0.4)),
            [(X: 4, Y: 5)] = new(
                Position: Pixel + new Double3(TexelWidth, TexelWidth / 4, 0),
                Estimate: new(Bounce: offBounce, Occlusion: 0.8)
            ),
        };

        ShouldBeNear(
            EstimateOf(pixel: (X: 7, Y: 10), texels: texels),
            new AmbientEstimate(Bounce: (nearBounce + offBounce * 0.75) / 1.75, Occlusion: (0.4 + 0.75 * 0.8) / 1.75),
            1e-12
        );
    }

    [Fact]
    public void AnEvenCoordinateTakesItsOwnTexelAndAnOddOneTheMeanOfTwo()
    {
        OcclusionUpsample.AxisWeights(10).Should().Be((1.0, 0.0));
        OcclusionUpsample.AxisWeights(11).Should().Be((0.5, 0.5));
    }
}
