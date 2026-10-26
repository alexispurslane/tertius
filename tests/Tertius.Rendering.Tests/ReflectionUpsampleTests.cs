namespace Tertius.Rendering.Tests;

/// <summary>
/// The joint bilateral upsample of the half-resolution trace of the reflections (docs/plans/screen-space-reflections.md,
/// "The half-resolution trace"): bilinear weights cut by each texel's distance from the pixel's tangent plane and by the
/// turn of its geometric normal, over the texels whose representative pixel started a ray.
/// </summary>
public sealed class ReflectionUpsampleTests
{
    // A pixel 2 m in front of the eye on a floor whose geometric normal points up, and the width of one texel there,
    // a power of two near 4.3 mm, so that the offsets below are exact.
    private const double TexelWidth = 1.0 / 256;
    private static readonly Double3 Pixel = new(0, -1, 2);
    private static readonly Double3 Up = Double3.UnitY;

    // The half-resolution image of a 1,920 × 1,080 image.
    private static readonly (int Width, int Height) HalfSize = (Width: 960, Height: 540);

    private static readonly RayResult Bright = new(Radiance: new(4, 2, 1), Coverage: 1);
    private static readonly RayResult Dim = new(Radiance: new(2, 1, 0.5), Coverage: 0.5);

    /// <summary>The upsampled ray of the full-resolution <paramref name="pixel"/> at <see cref="Pixel"/>, over <paramref name="texels"/>.</summary>
    private static RayResult? RayOf((int X, int Y) pixel, Dictionary<(int X, int Y), TracedTexel> texels) =>
        ReflectionUpsample.Ray(
            pixel: pixel,
            position: Pixel,
            geometricNormal: Up,
            texelWidth: TexelWidth,
            halfSize: HalfSize,
            texelAt: coordinates => texels[coordinates]
        );

    private static TracedTexel OnTheFloor(double x, RayResult? ray) =>
        new(Position: Pixel + new Double3(x, 0, 0), GeometricNormal: Up, Ray: ray);

    [Fact]
    public void ARepresentativePixelTakesItsOwnTexelsRay()
    {
        // Pixel (6, 10) is the top-left pixel of texel (3, 5), so its other three texels have a bilinear weight of 0.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: 0, ray: Dim),
            [(X: 4, Y: 5)] = OnTheFloor(x: TexelWidth, ray: Bright),
            [(X: 3, Y: 6)] = OnTheFloor(x: 0, ray: Bright),
            [(X: 4, Y: 6)] = OnTheFloor(x: TexelWidth, ray: Bright),
        };

        RayOf(pixel: (X: 6, Y: 10), texels: texels).Should().Be(Dim);
    }

    [Fact]
    public void APixelBetweenTwoTexelsOnItsPlaneTakesTheMeanOfTheirRays()
    {
        // Pixel (7, 10) lies between texels (3, 5) and (4, 5), both on the floor.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: Dim),
            [(X: 4, Y: 5)] = OnTheFloor(x: TexelWidth / 2, ray: Bright),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels)
            .Should()
            .Be(new RayResult(Radiance: new(3, 1.5, 0.75), Coverage: 0.75));
    }

    [Fact]
    public void ARayThatMissedCountsAsAMissInTheMean()
    {
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: new RayResult(Radiance: default, Coverage: 0)),
            [(X: 4, Y: 5)] = OnTheFloor(x: TexelWidth / 2, ray: Bright),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels)
            .Should()
            .Be(new RayResult(Radiance: new(2, 1, 0.5), Coverage: 0.5), "a missed ray keeps the ambient specular term");
    }

    [Fact]
    public void ATexelOffThePixelsPlaneByATexelWidthOrMoreGetsNoWeight()
    {
        // The second texel lies on a box one texel width above the floor: behind a silhouette, as the pixel sees it.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: Dim),
            [(X: 4, Y: 5)] = new(
                Position: Pixel + new Double3(TexelWidth / 2, TexelWidth, 0),
                GeometricNormal: Up,
                Ray: Bright
            ),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels).Should().Be(Dim);
    }

    [Fact]
    public void ATexelAcrossARightAngleCreaseGetsNoWeight()
    {
        // The second texel lies on the wall that rises from the floor at the crease: on the pixel's plane, but its rays
        // leave a surface that faces another way.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: Dim),
            [(X: 4, Y: 5)] = new(
                Position: Pixel + new Double3(TexelWidth / 2, 0, 0),
                GeometricNormal: -Double3.UnitX,
                Ray: Bright
            ),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels).Should().Be(Dim);
    }

    [Fact]
    public void ATexelWhoseNormalTurnsByTenDegreesKeepsMostOfItsWeight()
    {
        // The facets of a sphere of 32 segments turn by about 11 degrees from one to the next, and their rays blend.
        double turn = 10 * Math.PI / 180;
        var tilted = new Double3(Math.Sin(turn), Math.Cos(turn), 0);
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: new RayResult(Radiance: default, Coverage: 0)),
            [(X: 4, Y: 5)] = new(
                Position: Pixel + new Double3(TexelWidth / 2, 0, 0),
                GeometricNormal: tilted,
                Ray: Bright
            ),
        };

        RayResult? ray = RayOf(pixel: (X: 7, Y: 10), texels: texels);

        double tiltedWeight = Math.Pow(Math.Cos(turn), ReflectionUpsample.NormalPower);
        tiltedWeight.Should().BeGreaterThan(0.8, "a turn of 10 degrees keeps most of the weight");
        ray!.Value.Coverage.Should().BeApproximately(tiltedWeight / (1 + tiltedWeight), 1e-12);
    }

    [Fact]
    public void ATexelWhoseRepresentativeStartedNoRayGetsNoWeight()
    {
        // The second texel's representative pixel is rougher than the maximum, or faces its ray away, and traced nothing.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: Dim),
            [(X: 4, Y: 5)] = OnTheFloor(x: TexelWidth / 2, ray: null),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels).Should().Be(Dim, "a texel with no ray holds no estimate");
    }

    [Fact]
    public void APixelWithNoTexelOfWeightReflectsNothing()
    {
        // The pixel shows the floor through a gap in a grate half a metre above it, and every texel around it shows the
        // grate.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel + new Double3(0, 0.5, 0), GeometricNormal: Up, Ray: Bright),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(0, 0.5, 0), GeometricNormal: Up, Ray: Bright),
            [(X: 3, Y: 6)] = new(Position: Pixel + new Double3(0, 0.5, 0), GeometricNormal: Up, Ray: Bright),
            [(X: 4, Y: 6)] = new(Position: Pixel + new Double3(0, 0.5, 0), GeometricNormal: Up, Ray: Bright),
        };

        RayOf(pixel: (X: 7, Y: 11), texels: texels)
            .Should()
            .BeNull("the pixel keeps the ambient specular term, as for a ray that finds nothing");
    }

    [Fact]
    public void APixelWhoseTexelsWeighLessThanTheSmallestNormalFloatReflectsNothing()
    {
        // The texel's normal turns from the pixel's by all but 2^−16 rad of a right angle, as at a sphere's silhouette, so
        // its normal weight (2^−16)^8 = 2^−128 lies below the smallest normal single float. A GPU may flush that to 0,
        // and Vulkan bounds a division's error only for a divisor of at least 2^−126.
        double facing = Math.ScaleB(1, -16);
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(
                Position: Pixel,
                GeometricNormal: new Double3(Math.Sqrt(1 - facing * facing), facing, 0),
                Ray: Bright
            ),
        };

        RayOf(pixel: (X: 6, Y: 10), texels: texels)
            .Should()
            .BeNull("a weight sum below the smallest normal float counts as no texel of weight on every driver");
    }

    [Fact]
    public void ATexelBeyondTheHalfImageIsNeverRead()
    {
        // Pixel (1919, 1079) lies in the last texel (959, 539); its neighbors past the edge do not exist.
        Dictionary<(int X, int Y), TracedTexel> texels = new() { [(X: 959, Y: 539)] = OnTheFloor(x: 0, ray: Bright) };

        RayOf(pixel: (X: 1919, Y: 1079), texels: texels).Should().Be(Bright);
    }
}
