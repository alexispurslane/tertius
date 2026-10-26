using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The six faces of the environment map: their axes after Vulkan's face table, the solid angle of their texels, their
/// view projections and their culling (docs/plans/environment-map.md, "The faces and their axes").
/// </summary>
public sealed class CubeFacesTests
{
    private static readonly CubeFace[] Faces =
    [
        CubeFace.PositiveX,
        CubeFace.NegativeX,
        CubeFace.PositiveY,
        CubeFace.NegativeY,
        CubeFace.PositiveZ,
        CubeFace.NegativeZ,
    ];

    /// <summary>
    /// The table of the Vulkan specification, "Cube Map Face Selection": the major axis picks the face, and sc, tc and
    /// ma give s = ½ (sc / |ma| + 1) and t = ½ (tc / |ma| + 1).
    /// </summary>
    private static (CubeFace Face, float S, float T) SpecificationLocate(Vector3 r)
    {
        float ax = MathF.Abs(r.X);
        float ay = MathF.Abs(r.Y);
        float az = MathF.Abs(r.Z);
        (CubeFace face, float sc, float tc, float ma) = (ax >= ay && ax >= az, ay >= az) switch
        {
            (true, _) when r.X >= 0 => (CubeFace.PositiveX, -r.Z, -r.Y, ax),
            (true, _) => (CubeFace.NegativeX, r.Z, -r.Y, ax),
            (false, true) when r.Y >= 0 => (CubeFace.PositiveY, r.X, r.Z, ay),
            (false, true) => (CubeFace.NegativeY, r.X, -r.Z, ay),
            (false, false) when r.Z >= 0 => (CubeFace.PositiveZ, r.X, -r.Y, az),
            (false, false) => (CubeFace.NegativeZ, -r.X, -r.Y, az),
        };
        return (face, 0.5f * (sc / ma + 1), 0.5f * (tc / ma + 1));
    }

    private static Vector2 Ndc(Matrix4x4 viewProjection, Vector3 point)
    {
        var clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
        return new(clip.X / clip.W, clip.Y / clip.W);
    }

    [Fact]
    public void EachFaceLooksAlongItsAxis()
    {
        Vector3[] axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        foreach (CubeFace face in Faces)
        {
            Vector3 axis = axes[(int)face];
            CubeFaces.Basis(face).Forward.Should().Be(axis, $"face {face}");
            Vector3
                .Distance(CubeFaces.Direction(face, 0.5f, 0.5f), axis)
                .Should()
                .BeLessThan(1e-6f, $"the center of face {face}");
        }
    }

    [Fact]
    public void EachFacesRightAndUpFollowTheVulkanFaceTable()
    {
        var random = new Random(7);
        for (int index = 0; index < 2_000; index++)
        {
            var direction = Vector3.Normalize(
                new(random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1)
            );
            (CubeFace face, float s, float t) = SpecificationLocate(direction);

            (CubeFace Face, float S, float T) located = CubeFaces.Locate(direction);

            located.Face.Should().Be(face, $"direction {direction}");
            located.S.Should().BeApproximately(s, 1e-5f, $"direction {direction}");
            located.T.Should().BeApproximately(t, 1e-5f, $"direction {direction}");
            Vector3
                .Distance(CubeFaces.Direction(face, s, t), direction)
                .Should()
                .BeLessThan(1e-5f, $"Direction inverts Locate for {direction}");
        }
    }

    [Fact]
    public void EveryFaceBasisIsLeftHanded()
    {
        foreach (CubeFace face in Faces)
        {
            FaceBasis basis = CubeFaces.Basis(face);
            Vector3
                .Cross(basis.Right, basis.Up)
                .Should()
                .Be(
                    basis.Forward,
                    $"face {face} mirrors a right-handed camera, so its pipeline's front face is clockwise"
                );
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(32)]
    [InlineData(128)]
    public void TheTexelSolidAnglesOfAFaceAddUpToASixthOfTheSphere(int size)
    {
        double sum = 0;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            sum += CubeFaces.TexelSolidAngle(x, y, size);

        sum.Should().BeApproximately(4 * Math.PI / 6, 1e-9);
    }

    [Fact]
    public void ACornerTexelCoversLessOfTheSphereThanACenterTexel()
    {
        double corner = CubeFaces.TexelSolidAngle(0, 0, 32);
        double center = CubeFaces.TexelSolidAngle(16, 16, 32);

        corner.Should().BePositive();
        corner.Should().BeLessThan(center * 0.25, "a corner texel is √3 times farther from the eye and seen obliquely");
        // A texel near the center spans about (2 / 32)² sr, the area of a flat texel at distance 1.
        center.Should().BeApproximately(4.0 / (32 * 32), 2e-4);
    }

    [Fact]
    public void AFaceCullsASphereOutsideItsSidePlanesAndKeepsOneThatTouchesThem()
    {
        foreach (CubeFace face in Faces)
        {
            FaceBasis basis = CubeFaces.Basis(face);
            // The side plane through the eye with normal (forward − right) / √2 holds every point with q·right = q·forward.
            Vector3 onSidePlane = 10 * basis.Forward + 10 * basis.Right;
            var outward = Vector3.Normalize(basis.Right - basis.Forward);
            Vector3 onLowerPlane = 10 * basis.Forward - 10 * basis.Up;
            var upward = Vector3.Normalize(-basis.Up - basis.Forward);

            CubeFaces
                .Culls(face, onSidePlane + 2.1f * outward, 2f)
                .Should()
                .BeTrue($"face {face}, beyond its right plane");
            CubeFaces.Culls(face, onSidePlane + 1.9f * outward, 2f).Should().BeFalse($"face {face}, touching it");
            CubeFaces
                .Culls(face, onLowerPlane + 2.1f * upward, 2f)
                .Should()
                .BeTrue($"face {face}, beyond its lower plane");
            CubeFaces.Culls(face, onLowerPlane + 1.9f * upward, 2f).Should().BeFalse($"face {face}, touching it");
            CubeFaces.Culls(face, -basis.Forward, 0.9f).Should().BeTrue($"face {face}, behind its near plane");
            CubeFaces.Culls(face, -basis.Forward, 1.1f).Should().BeFalse($"face {face}, reaching past its near plane");
        }
    }

    [Fact]
    public void AFacesViewProjectionPutsItsForwardAxisAtTheCenterAndItsUpAtTheTop()
    {
        foreach (CubeFace face in Faces)
        {
            FaceBasis basis = CubeFaces.Basis(face);
            Matrix4x4 viewProjection = CubeFaces.ViewProjection(face, DepthRanges.Near);

            Vector2 center = Ndc(viewProjection, 10 * basis.Forward);
            Vector2 top = Ndc(viewProjection, 10 * basis.Forward + 10 * basis.Up);
            Vector2 right = Ndc(viewProjection, 10 * basis.Forward + 10 * basis.Right);

            Vector2.Distance(center, Vector2.Zero).Should().BeLessThan(1e-5f, $"face {face}");
            Vector2.Distance(top, new(0, -1)).Should().BeLessThan(1e-5f, $"face {face}: row 0 is the top, at y = −1");
            Vector2.Distance(right, new(1, 0)).Should().BeLessThan(1e-5f, $"face {face}: the last column is at x = 1");
        }
    }
}
