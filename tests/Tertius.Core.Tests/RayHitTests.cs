using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Core.Tests;

/// <summary>
/// The answer to a ray cast fits one event payload: the entity, the point, the distance, the tag and the normal. The
/// normal is the part that the payload stores in a smaller form, so these tests check that it comes back as it went in.
/// </summary>
public class RayHitTests
{
    [Fact]
    public void ARayHitFitsInOnePayload()
    {
        Assert.True(Unsafe.SizeOf<RayHit>() <= EventTypeRegistry.MaxPayloadSize);
        Assert.True(EventType<RayHit>.Id >= 0);
    }

    public static TheoryData<float, float, float> Normals() => new()
    {
        { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 },
        { 0.6f, 0, -0.8f }, { -0.48f, 0.6f, -0.64f }, { 0.1f, -0.2f, -0.9747f }, { 0.577f, 0.577f, 0.578f }
    };

    [Theory]
    [MemberData(nameof(Normals))]
    public void TheNormalOfAHitComesBackAsTheUnitVectorThatWentIn(float x, float y, float z)
    {
        Vector3 normal = Vector3.Normalize(new(x, y, z));

        var hit = new RayHit { Entity = new(3, 1), Normal = normal };

        Vector3 back = hit.Normal;
        Assert.True(Vector3.Distance(back, normal) < 1e-6f, $"{normal} came back as {back}");
        Assert.Equal(1, back.Length(), 1e-6f);
    }

    [Fact]
    public void TheNormalsOfASweepOverTheSphereComeBackWithinAMillionthOfARadian()
    {
        double worst = 0;
        for (int i = 0; i < 64; i++)
        for (int j = 0; j <= 32; j++)
        {
            double azimuth = 2 * Math.PI * i / 64, polar = Math.PI * j / 32;
            var normal = new Vector3((float)(Math.Sin(polar) * Math.Cos(azimuth)),
                (float)(Math.Sin(polar) * Math.Sin(azimuth)), (float)Math.Cos(polar));
            normal = Vector3.Normalize(normal);
            Vector3 back = new RayHit { Entity = new(1, 1), Normal = normal }.Normal;
            // The angle between two unit vectors from the chord between them, in doubles: an arccosine of their dot
            // product would lose the small angle to rounding.
            double chord = (back - (Double3)normal).Length;
            worst = Math.Max(worst, 2 * Math.Asin(chord / 2));
        }

        Assert.True(worst < 1e-6, $"the worst angle was {worst} rad");
    }

    [Fact]
    public void AMissNamesNoEntityAndHasNoNormal()
    {
        var miss = new RayHit { Tag = 7 };

        Assert.False(miss.IsHit);
        Assert.Equal(Vector3.Zero, miss.Normal);
        Assert.Equal(7, miss.Tag);
    }

    [Fact]
    public void AHitNamesItsEntity()
    {
        var hit = new RayHit { Entity = new(5, 2), Normal = Vector3.UnitY };

        Assert.True(hit.IsHit);
    }
}
