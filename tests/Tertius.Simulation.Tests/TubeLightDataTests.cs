using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>What <see cref="TubeLightData"/> works out from its fields: the radiance of its capsule's surface.</summary>
public sealed class TubeLightDataTests
{
    [Fact]
    public void ACapsuleSendsItsPowerFromItsWholeSurface()
    {
        // A capsule of radius R around a segment of length L has the surface 2π R L + 4π R² = 2π R (L + 2R). A surface that
        // sends the same radiance in every direction leaves π times it, so the radiance is P / (π · 2π R (L + 2R)).
        var light = new TubeLightData(
            Offset: default,
            Color: new(1, 0.5f, 0),
            Power: 100,
            Radius: 0.1f,
            Length: 1.8f,
            CastsShadow: false
        );

        float radiance = (float)(100 / (Math.PI * 2 * Math.PI * 0.1 * 2.0));
        Vector3 surface = light.SurfaceRadiance;
        surface.X.Should().BeApproximately(radiance, radiance * 1e-6f);
        surface.Y.Should().BeApproximately(radiance / 2, radiance * 1e-6f);
        surface.Z.Should().Be(0);
    }

    [Fact]
    public void ATubeOfLengthZeroSendsTheRadianceOfTheSphereOfItsRadius()
    {
        var tube = new TubeLightData(
            Offset: default,
            Color: Vector3.One,
            Power: 7,
            Radius: 0.3f,
            Length: 0,
            CastsShadow: false
        );
        var sphere = new PointLightData(
            Offset: default,
            Color: Vector3.One,
            Power: 7,
            Radius: 0.3f,
            CastsShadow: false
        );

        tube.SurfaceRadiance.X.Should().BeApproximately(sphere.SurfaceRadiance.X, sphere.SurfaceRadiance.X * 1e-6f);
    }
}
