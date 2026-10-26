namespace Tertius.Rendering.Tests;

/// <summary>
/// The cone that stands for the specular lobe around a reflected ray, and the level of the radiance pyramid that its
/// footprint at the hit selects (docs/plans/screen-space-reflections.md, "The cone").
/// </summary>
public sealed class ReflectionConeTests
{
    private const float PixelAngle = 0.00097f;
    private const int TopLevel = 11;

    [Theory]
    [InlineData(0.045)]
    [InlineData(0.1)]
    [InlineData(0.2)]
    [InlineData(0.38)]
    [InlineData(0.5)]
    [InlineData(0.6)]
    public void TheConeHoldsTheShareOneMinusXiOfTheGgxHalfVectors(double roughness)
    {
        var lobe = new GgxLobe(roughness);
        double halfAngle = Math.Atan(ReflectionCone.OfRoughness((float)roughness).TanHalfAngle);

        // The midpoint rule of GgxLobeTests over the polar angle of h, from the normal to the cone's half angle.
        const int count = 200_000;
        double step = halfAngle / count;
        double integral = 0;
        for (int index = 0; index < count; index++)
        {
            double theta = (index + 0.5) * step;
            integral += lobe.Distribution(Math.Cos(theta)) * Math.Cos(theta) * Math.Sin(theta) * step;
        }

        (2 * Math.PI * integral).Should().BeApproximately(1 - ReflectionCone.UludagShare, 1e-3);
    }

    [Fact]
    public void ARougherSurfaceHasAWiderCone() =>
        ReflectionCone
            .OfRoughness(0.3f)
            .TanHalfAngle.Should()
            .BeGreaterThan(ReflectionCone.OfRoughness(0.2f).TanHalfAngle);

    [Fact]
    public void TheRoughnessFloorGivesTheNarrowestCone()
    {
        var floor = ReflectionCone.OfRoughness((float)GgxLobe.MinRoughness);

        ReflectionCone.OfRoughness(0).Should().Be(floor);
        floor.TanHalfAngle.Should().BeApproximately(0.0036f, 1e-4f);
    }

    [Fact]
    public void AFootprintOfOnePixelOrLessReadsLevelZero()
    {
        var cone = ReflectionCone.OfRoughness(0.2f);
        // The ray length whose cone radius at the hit is exactly one pixel.
        float onePixel = 4 * PixelAngle / cone.TanHalfAngle;

        cone.Level(rayLength: onePixel, hitDistance: 4, pixelAngle: PixelAngle, topLevel: TopLevel)
            .Should()
            .BeApproximately(0, 1e-5f);
        cone.Level(rayLength: onePixel / 3, hitDistance: 4, pixelAngle: PixelAngle, topLevel: TopLevel).Should().Be(0);
        cone.Level(rayLength: 0, hitDistance: 4, pixelAngle: PixelAngle, topLevel: TopLevel).Should().Be(0);
    }

    [Fact]
    public void DoublingTheRayLengthRaisesTheLevelByOne()
    {
        var cone = ReflectionCone.OfRoughness(0.2f);

        float level = cone.Level(rayLength: 2, hitDistance: 4, pixelAngle: PixelAngle, topLevel: TopLevel);

        level.Should().BeApproximately(5.18f, 0.01f, "the doc's example: a 36 px radius");
        cone.Level(rayLength: 4, hitDistance: 4, pixelAngle: PixelAngle, topLevel: TopLevel)
            .Should()
            .BeApproximately(level + 1, 1e-4f);
    }

    [Fact]
    public void DoublingTheDistanceOfTheHitLowersTheLevelByOne()
    {
        var cone = ReflectionCone.OfRoughness(0.2f);

        float level = cone.Level(rayLength: 2, hitDistance: 4, pixelAngle: PixelAngle, topLevel: TopLevel);

        cone.Level(rayLength: 2, hitDistance: 8, pixelAngle: PixelAngle, topLevel: TopLevel)
            .Should()
            .BeApproximately(level - 1, 1e-4f);
    }

    [Fact]
    public void TheLevelNeverPassesTheTopLevel() =>
        ReflectionCone
            .OfRoughness(0.6f)
            .Level(rayLength: 1_000, hitDistance: 1, pixelAngle: PixelAngle, topLevel: TopLevel)
            .Should()
            .Be(TopLevel);
}
