using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The six faces of the environment map: their axes after Vulkan's face table, the solid angle of their texels, their
/// view projections and their culling (docs/plans/environment-map.md, "The faces and their axes"), and the faces of a map
/// that lies in the axes of its own frame (docs/plans/environment-maps-per-frame.md, decision 9).
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

    /// <summary>A frame turned 90° about y and then 30° about x: its +x axis points along world −z, tipped up.</summary>
    private static readonly Quaternion TurnedFrame = Quaternion.Normalize(
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 6)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)
    );

    private static int[] Bits(params Vector3[] vectors) =>
        [.. vectors.SelectMany(v => new[] { v.X, v.Y, v.Z }).Select(BitConverter.SingleToInt32Bits)];

    private static int[] Bits(Matrix4x4 m) =>
        [
            .. new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24 }
                .Concat([m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44])
                .Select(BitConverter.SingleToInt32Bits),
        ];

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
                .Culls(basis, onSidePlane + 2.1f * outward, 2f)
                .Should()
                .BeTrue($"face {face}, beyond its right plane");
            CubeFaces.Culls(basis, onSidePlane + 1.9f * outward, 2f).Should().BeFalse($"face {face}, touching it");
            CubeFaces
                .Culls(basis, onLowerPlane + 2.1f * upward, 2f)
                .Should()
                .BeTrue($"face {face}, beyond its lower plane");
            CubeFaces.Culls(basis, onLowerPlane + 1.9f * upward, 2f).Should().BeFalse($"face {face}, touching it");
            CubeFaces.Culls(basis, -basis.Forward, 0.9f).Should().BeTrue($"face {face}, behind its near plane");
            CubeFaces.Culls(basis, -basis.Forward, 1.1f).Should().BeFalse($"face {face}, reaching past its near plane");
        }
    }

    [Fact]
    public void AFacesViewProjectionPutsItsForwardAxisAtTheCenterAndItsUpAtTheTop()
    {
        foreach (CubeFace face in Faces)
        {
            FaceBasis basis = CubeFaces.Basis(face);
            Matrix4x4 viewProjection = CubeFaces.ViewProjection(CubeFaces.Basis(face), DepthRanges.Near);

            Vector2 center = Ndc(viewProjection, 10 * basis.Forward);
            Vector2 top = Ndc(viewProjection, 10 * basis.Forward + 10 * basis.Up);
            Vector2 right = Ndc(viewProjection, 10 * basis.Forward + 10 * basis.Right);

            Vector2.Distance(center, Vector2.Zero).Should().BeLessThan(1e-5f, $"face {face}");
            Vector2.Distance(top, new(0, -1)).Should().BeLessThan(1e-5f, $"face {face}: row 0 is the top, at y = −1");
            Vector2.Distance(right, new(1, 0)).Should().BeLessThan(1e-5f, $"face {face}: the last column is at x = 1");
        }
    }

    [Fact]
    public void TheIdentityTurnLeavesEveryFaceBitForBitAsBefore()
    {
        var random = new Random(11);
        foreach (CubeFace face in Faces)
        {
            FaceBasis table = CubeFaces.Basis(face);

            FaceBasis turned = CubeFaces.Basis(face, Quaternion.Identity);

            Bits(turned.Forward, turned.Right, turned.Up)
                .Should()
                .Equal(Bits(table.Forward, table.Right, table.Up), $"face {face}");
            Bits(CubeFaces.ViewProjection(turned, DepthRanges.Near))
                .Should()
                .Equal(Bits(CubeFaces.ViewProjection(CubeFaces.Basis(face), DepthRanges.Near)), $"face {face}");
            for (int index = 0; index < 200; index++)
            {
                Vector3 center =
                    20
                    * new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f);
                float radius = 3 * random.NextSingle();
                CubeFaces
                    .Culls(turned, center, radius)
                    .Should()
                    .Be(
                        CubeFaces.Culls(CubeFaces.Basis(face), center, radius),
                        $"face {face}, the sphere at {center} of radius {radius}"
                    );
            }
        }
    }

    [Fact]
    public void AFaceOfATurnedMapLooksAlongItsFramesAxis()
    {
        foreach (CubeFace face in Faces)
        {
            FaceBasis inFrame = CubeFaces.Basis(face);
            FaceBasis basis = CubeFaces.Basis(face, TurnedFrame);
            var forward = Vector3.Transform(inFrame.Forward, TurnedFrame);
            var up = Vector3.Transform(inFrame.Up, TurnedFrame);
            var right = Vector3.Transform(inFrame.Right, TurnedFrame);

            Matrix4x4 viewProjection = CubeFaces.ViewProjection(basis, DepthRanges.Near);

            Vector2
                .Distance(Ndc(viewProjection, 10 * forward), Vector2.Zero)
                .Should()
                .BeLessThan(1e-5f, $"face {face}");
            Vector2
                .Distance(Ndc(viewProjection, 10 * (forward + up)), new(0, -1))
                .Should()
                .BeLessThan(1e-5f, $"face {face}");
            Vector2
                .Distance(Ndc(viewProjection, 10 * (forward + right)), new(1, 0))
                .Should()
                .BeLessThan(1e-5f, $"face {face}");
        }
    }

    [Fact]
    public void AFaceOfATurnedMapCullsInItsFramesAxes()
    {
        var alongFramesX = Vector3.Transform(10 * Vector3.UnitX, TurnedFrame);

        CubeFaces
            .Culls(CubeFaces.Basis(CubeFace.PositiveX, TurnedFrame), alongFramesX, 1)
            .Should()
            .BeFalse("the sphere lies on the frame's +x axis");
        CubeFaces
            .Culls(CubeFaces.Basis(CubeFace.NegativeX, TurnedFrame), alongFramesX, 1)
            .Should()
            .BeTrue("the sphere lies behind the frame's −x face");
        CubeFaces
            .Culls(CubeFaces.Basis(CubeFace.PositiveX), alongFramesX, 1)
            .Should()
            .BeTrue("in world axes it lies off the +x axis");
    }
}
