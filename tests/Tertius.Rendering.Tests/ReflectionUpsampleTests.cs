namespace Tertius.Rendering.Tests;

/// <summary>
/// The joint bilateral upsample of the half-resolution trace of the reflections (docs/plans/screen-space-reflections.md,
/// "The half-resolution trace"): bilinear weights cut by each texel's distance from the pixel's tangent plane and by the
/// turn of its geometric normal, over the texels whose representative pixel started a ray, and the pixel's own ray
/// where the texels' rays hit different surfaces.
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
    private static UpsampledRay RayOf((int X, int Y) pixel, Dictionary<(int X, int Y), TracedTexel> texels) =>
        ReflectionUpsample.Ray(
            pixel: pixel,
            position: Pixel,
            geometricNormal: Up,
            texelWidth: TexelWidth,
            halfSize: HalfSize,
            texelAt: coordinates => texels[coordinates]
        );

    private static TracedTexel OnTheFloor(double x, TexelRay ray) =>
        new(Position: Pixel + new Double3(x, 0, 0), GeometricNormal: Up, Ray: ray);

    /// <summary>A ray that hits the wall 9 m from the eye with <paramref name="result"/>.</summary>
    private static TexelRay OnTheWall(RayResult result) => new TexelHit(Result: result, HitDepth: 9);

    /// <summary>The upsample's result for a pixel whose texels agree, with the mean <paramref name="mean"/>.</summary>
    private static UpsampledRay Mean(RayResult mean) => new TexelMean(mean);

    [Fact]
    public void ARepresentativePixelTakesItsOwnTexelsRay()
    {
        // Pixel (6, 10) is the top-left pixel of texel (3, 5), so its other three texels have a bilinear weight of 0.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: 0, ray: OnTheWall(Dim)),
            [(X: 4, Y: 5)] = OnTheFloor(x: TexelWidth, ray: OnTheWall(Bright)),
            [(X: 3, Y: 6)] = OnTheFloor(x: 0, ray: OnTheWall(Bright)),
            [(X: 4, Y: 6)] = OnTheFloor(x: TexelWidth, ray: OnTheWall(Bright)),
        };

        RayOf(pixel: (X: 6, Y: 10), texels: texels).Should().Be(Mean(Dim));
    }

    [Fact]
    public void APixelBetweenTwoTexelsOnItsPlaneTakesTheMeanOfTheirRays()
    {
        // Pixel (7, 10) lies between texels (3, 5) and (4, 5), both on the floor.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: OnTheWall(Dim)),
            [(X: 4, Y: 5)] = OnTheFloor(x: TexelWidth / 2, ray: OnTheWall(Bright)),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels)
            .Should()
            .Be(Mean(new RayResult(Radiance: new(3, 1.5, 0.75), Coverage: 0.75)));
    }

    [Fact]
    public void RaysThatAllMissedGiveAMeanOfNoRadianceAndNoCoverage()
    {
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: new TexelMiss()),
            [(X: 4, Y: 5)] = OnTheFloor(x: TexelWidth / 2, ray: new TexelMiss()),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels)
            .Should()
            .Be(Mean(new RayResult(Radiance: default, Coverage: 0)), "a missed ray keeps the ambient specular term");
    }

    [Fact]
    public void APixelBetweenAHitAndAMissOnItsPlaneTracesItsOwnRay()
    {
        // The pixel lies on the reflected outline of an object: the ray of one texel hits it, and the other's passes beside.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: new TexelMiss()),
            [(X: 4, Y: 5)] = OnTheFloor(x: TexelWidth / 2, ray: OnTheWall(Bright)),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels).Should().Be(new UpsampledRay(new OwnRay()));
    }

    [Fact]
    public void APixelBetweenHitsOnSurfacesFarApartInDepthTracesItsOwnRay()
    {
        // The pixel lies on the reflected outline of an object 6 m away before a wall 9 m away.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: new TexelHit(Result: Dim, HitDepth: 6)),
            [(X: 4, Y: 5)] = OnTheFloor(x: TexelWidth / 2, ray: OnTheWall(Bright)),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels).Should().Be(new UpsampledRay(new OwnRay()));
    }

    [Fact]
    public void ATexelWithNoWeightTakesNoPartInWhetherTheRaysAgree()
    {
        // The second texel lies on a box above the floor, behind a silhouette, and its ray misses.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: OnTheWall(Dim)),
            [(X: 4, Y: 5)] = new(
                Position: Pixel + new Double3(TexelWidth / 2, TexelWidth, 0),
                GeometricNormal: Up,
                Ray: new TexelMiss()
            ),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels)
            .Should()
            .Be(Mean(Dim), "the pixel blends only the texel on its plane");
    }

    [Fact]
    public void HitsOnOneSurfaceAgree()
    {
        // The second hit lies deeper by less than the slab thickness, a tenth of the depth.
        ReflectionUpsample
            .RaysDisagree([new TexelHit(Result: Dim, HitDepth: 9), new TexelHit(Result: Bright, HitDepth: 9.5)])
            .Should()
            .BeFalse();
    }

    [Fact]
    public void AHitBesideAMissDisagrees() =>
        ReflectionUpsample.RaysDisagree([new TexelHit(Result: Bright, HitDepth: 9), new TexelMiss()]).Should().BeTrue();

    [Fact]
    public void AMissBesideAHitDisagrees() =>
        ReflectionUpsample.RaysDisagree([new TexelMiss(), new TexelHit(Result: Bright, HitDepth: 9)]).Should().BeTrue();

    [Fact]
    public void HitsWhoseDepthsDifferByMoreThanTheSlabThicknessDisagree()
    {
        // 10 m lies past 9 m by 11 %, outside the slab that a surface 9 m away fills, to 9.9 m.
        ReflectionUpsample
            .RaysDisagree([
                new TexelHit(Result: Dim, HitDepth: 9),
                new TexelHit(Result: Dim, HitDepth: 9.2),
                new TexelHit(Result: Bright, HitDepth: 10),
            ])
            .Should()
            .BeTrue("the nearest and the farthest hit decide, whatever lies between them");
    }

    [Fact]
    public void MissesAloneAgree() =>
        ReflectionUpsample.RaysDisagree([new TexelMiss(), new TexelMiss()]).Should().BeFalse();

    [Fact]
    public void ATexelOffThePixelsPlaneByATexelWidthOrMoreGetsNoWeight()
    {
        // The second texel lies on a box one texel width above the floor: behind a silhouette, as the pixel sees it.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: OnTheWall(Dim)),
            [(X: 4, Y: 5)] = new(
                Position: Pixel + new Double3(TexelWidth / 2, TexelWidth, 0),
                GeometricNormal: Up,
                Ray: OnTheWall(Bright)
            ),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels).Should().Be(Mean(Dim));
    }

    [Fact]
    public void ATexelAcrossARightAngleCreaseGetsNoWeight()
    {
        // The second texel lies on the wall that rises from the floor at the crease: on the pixel's plane, but its rays
        // leave a surface that faces another way.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: OnTheWall(Dim)),
            [(X: 4, Y: 5)] = new(
                Position: Pixel + new Double3(TexelWidth / 2, 0, 0),
                GeometricNormal: -Double3.UnitX,
                Ray: OnTheWall(Bright)
            ),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels).Should().Be(Mean(Dim));
    }

    [Fact]
    public void ATexelWhoseNormalTurnsByTenDegreesKeepsMostOfItsWeight()
    {
        // The facets of a sphere of 32 segments turn by about 11 degrees from one to the next, and their rays blend.
        double turn = 10 * Math.PI / 180;
        var tilted = new Double3(Math.Sin(turn), Math.Cos(turn), 0);
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: OnTheWall(Dim)),
            [(X: 4, Y: 5)] = new(
                Position: Pixel + new Double3(TexelWidth / 2, 0, 0),
                GeometricNormal: tilted,
                Ray: OnTheWall(Bright)
            ),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels).TryGetValue(out TexelMean mean).Should().BeTrue();

        double tiltedWeight = Math.Pow(Math.Cos(turn), ReflectionUpsample.NormalPower);
        tiltedWeight.Should().BeGreaterThan(0.8, "a turn of 10 degrees keeps most of the weight");
        mean.Mean.Coverage.Should().BeApproximately((Dim.Coverage + tiltedWeight) / (1 + tiltedWeight), 1e-12);
    }

    [Fact]
    public void ATexelWhoseRepresentativeStartedNoRayGetsNoWeight()
    {
        // The second texel's representative pixel is rougher than the maximum, or faces its ray away, and traced nothing.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = OnTheFloor(x: -TexelWidth / 2, ray: OnTheWall(Dim)),
            [(X: 4, Y: 5)] = OnTheFloor(x: TexelWidth / 2, ray: new NoRay()),
        };

        RayOf(pixel: (X: 7, Y: 10), texels: texels).Should().Be(Mean(Dim), "a texel with no ray holds no estimate");
    }

    [Fact]
    public void APixelWithNoTexelOfWeightReflectsNothing()
    {
        // The pixel shows the floor through a gap in a grate half a metre above it, and every texel around it shows the
        // grate.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 3, Y: 5)] = new(Position: Pixel + new Double3(0, 0.5, 0), GeometricNormal: Up, Ray: OnTheWall(Bright)),
            [(X: 4, Y: 5)] = new(Position: Pixel + new Double3(0, 0.5, 0), GeometricNormal: Up, Ray: OnTheWall(Bright)),
            [(X: 3, Y: 6)] = new(Position: Pixel + new Double3(0, 0.5, 0), GeometricNormal: Up, Ray: OnTheWall(Bright)),
            [(X: 4, Y: 6)] = new(Position: Pixel + new Double3(0, 0.5, 0), GeometricNormal: Up, Ray: OnTheWall(Bright)),
        };

        RayOf(pixel: (X: 7, Y: 11), texels: texels)
            .Should()
            .Be(
                new UpsampledRay(new NoTexelOfWeight()),
                "the pixel keeps the ambient specular term, as for a ray that finds nothing"
            );
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
                Ray: OnTheWall(Bright)
            ),
        };

        RayOf(pixel: (X: 6, Y: 10), texels: texels)
            .Should()
            .Be(
                new UpsampledRay(new NoTexelOfWeight()),
                "a weight below the smallest normal float counts as none on every driver"
            );
    }

    [Fact]
    public void ATexelBeyondTheHalfImageIsNeverRead()
    {
        // Pixel (1919, 1079) lies in the last texel (959, 539); its neighbors past the edge do not exist.
        Dictionary<(int X, int Y), TracedTexel> texels = new()
        {
            [(X: 959, Y: 539)] = OnTheFloor(x: 0, ray: OnTheWall(Bright)),
        };

        RayOf(pixel: (X: 1919, Y: 1079), texels: texels).Should().Be(Mean(Bright));
    }
}
