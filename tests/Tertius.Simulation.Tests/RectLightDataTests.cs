using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>What <see cref="RectLightData"/> works out from its fields: the radiance of its rectangle's front face.</summary>
public sealed class RectLightDataTests
{
    [Fact]
    public void ARectangleSendsItsPowerFromItsFrontFace()
    {
        // A one-sided face of area W H that sends the same radiance in every direction leaves π times it per m², so the
        // radiance is P / (π W H).
        var light = new RectLightData(
            Offset: default,
            Color: new(1, 0.5f, 0),
            Power: 10,
            Width: 1.2f,
            Height: 0.5f,
            CastsShadow: false
        );

        float radiance = (float)(10 / (Math.PI * 1.2 * 0.5));
        Vector3 surface = light.SurfaceRadiance;
        surface.X.Should().BeApproximately(radiance, radiance * 1e-6f);
        surface.Y.Should().BeApproximately(radiance / 2, radiance * 1e-6f);
        surface.Z.Should().Be(0);
    }
}
