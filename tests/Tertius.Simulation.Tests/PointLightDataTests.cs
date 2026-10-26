using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>What <see cref="PointLightData"/> works out from its fields: the radiance of its sphere's surface.</summary>
public sealed class PointLightDataTests
{
    [Fact]
    public void ASphereSendsItsPowerFromItsSurface()
    {
        // A sphere of radius R that sends P evenly leaves P / (4π R²) per square meter of its surface, and a surface that
        // sends the same radiance L in every direction leaves π L. So L = P / (4π² R²), per channel of the color.
        var light = new PointLightData(
            Offset: default,
            Color: new(1, 0.5f, 0),
            Power: 100,
            Radius: 0.5f,
            CastsShadow: false
        );

        float radiance = (float)(100 / (4 * Math.PI * Math.PI * 0.25));
        Vector3 surface = light.SurfaceRadiance;
        surface.X.Should().BeApproximately(radiance, radiance * 1e-6f);
        surface.Y.Should().BeApproximately(radiance / 2, radiance * 1e-6f);
        surface.Z.Should().Be(0);
    }

    [Fact]
    public void TheSunsSurfaceSendsAboutTwentyMillionWattsPerSquareMeterAndSteradian()
    {
        // 3.828 × 10²⁶ W from a sphere of 6.957 × 10⁸ m: the surface radiance that the plan gives the Sun's sphere.
        var sun = new PointLightData(
            Offset: default,
            Color: Vector3.One,
            Power: 3.828e26f,
            Radius: 6.957e8f,
            CastsShadow: true
        );

        sun.SurfaceRadiance.X.Should().BeApproximately(2.0035e7f, 1e3f);
    }
}
