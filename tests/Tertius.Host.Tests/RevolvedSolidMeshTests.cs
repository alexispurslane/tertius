using System.Numerics;
using Tertius.Demo;
using static Tertius.Host.Tests.MeshChecks;

namespace Tertius.Host.Tests;

/// <summary>
/// The meshes of <see cref="RevolvedSolid"/>: a cylinder, a ring and a torus, each the solid that a profile sweeps out as
/// it turns about the Y axis in equal segments. Each mesh must be closed. Every triangle must wind counterclockwise around
/// the normals of its corners and face out of the solid. Each vertex lies on the true curved surface, and its normal is
/// the normal of that surface there. Every expectation comes from the geometry of the solid, worked out here.
/// </summary>
public sealed class RevolvedSolidMeshTests
{
    /// <summary>A cylinder 1 m in radius and 1.5 m tall, in 48 segments.</summary>
    private const float CylinderRadius = 1, CylinderHalfHeight = 0.75f;

    private const int CylinderSegments = 48;

    /// <summary>A ring like a station floor: 20 m to 20.5 m from the axis and 4 m wide along it, in 256 segments.</summary>
    private const float RingInner = 20, RingOuter = 20.5f, RingHalfWidth = 2;

    private const int RingSegments = 256;

    /// <summary>A torus whose tube, 1 m in radius, goes round a circle 3 m in radius, in 64 segments of 24 sides each.</summary>
    private const float TorusMajor = 3, TorusMinor = 1;

    private const int TorusSegments = 64, TorusSides = 24;

    /// <summary>
    /// How far off a triangle's center the facing test looks, in meters. The facets lie off the true surface by at most
    /// 1.3 cm, in the torus: 8.6 mm across its tube with 24 sides, r (1 - cos(π / 24)), and 4.8 mm around its 64
    /// segments, 4 m (1 - cos(π / 64)). So 5 cm is clear of the faceting and well inside every wall.
    /// </summary>
    private const float Probe = 0.05f;

    private static MeshData Cylinder() =>
        RevolvedSolid.Cylinder(CylinderRadius, CylinderHalfHeight, CylinderSegments).Mesh();

    private static MeshData Ring() => RevolvedSolid.Ring(innerRadius: RingInner, outerRadius: RingOuter, halfWidth: RingHalfWidth, segments: RingSegments).Mesh();

    private static MeshData Torus() => RevolvedSolid.Torus(majorRadius: TorusMajor, minorRadius: TorusMinor, segments: TorusSegments, sides: TorusSides).Mesh();

    /// <summary>The distance of <paramref name="p"/> from the Y axis.</summary>
    private static float Rho(Vector3 p) => MathF.Sqrt(p.X * p.X + p.Z * p.Z);

    /// <summary>The unit vector from the Y axis out to <paramref name="p"/>, across the axis.</summary>
    private static Vector3 Out(Vector3 p) => Vector3.Normalize(new(p.X, 0, p.Z));

    private static bool InCylinder(Vector3 p) => Rho(p) <= CylinderRadius && MathF.Abs(p.Y) <= CylinderHalfHeight;

    private static bool InRing(Vector3 p) =>
        Rho(p) >= RingInner && Rho(p) <= RingOuter && MathF.Abs(p.Y) <= RingHalfWidth;

    private static bool InTorus(Vector3 p) =>
        (Rho(p) - TorusMajor) * (Rho(p) - TorusMajor) + p.Y * p.Y <= TorusMinor * TorusMinor;

    /// <summary>
    /// Each triangle has an area, winds counterclockwise around each of its corners' normals, which are within 15 degrees
    /// of its own, and faces out of the solid: a point <see cref="Probe"/> in front of its center is outside, and one
    /// behind it is inside.
    /// </summary>
    private static void ExpectFacingOut(MeshData mesh, Func<Vector3, bool> inside) =>
        MeshChecks.ExpectFacingOut(mesh: mesh, inside: inside, probe: Probe, maxNormalAngle: MathF.PI / 12);

    /// <summary>A vertex of a cylinder's cap is the cap's center, on the axis, or on its rim.</summary>
    private static void ExpectCenterOrRim(Vector3 p) =>
        (Rho(p) == 0 || MathF.Abs(Rho(p) - CylinderRadius) < 1e-5f).Should()
        .BeTrue($"a vertex of a cap is its center or on its rim, not {p}");

    private static void ExpectUnit(Vector3 normal) => normal.Length().Should().BeApproximately(1, 1e-5f);

    private static void ExpectNear(Vector3 actual, Vector3 expected, string because) =>
        (actual - expected).Length().Should().BeLessThan(1e-5f, $"{because}: {actual} against {expected}");

    [Fact]
    public void ACylinderHasTwoVerticesPerSegmentForEachCapAndForItsSideAndFourTriangles()
    {
        MeshData cylinder = Cylinder();

        // The rim and the center of each cap, and the bottom and top of the side, at each of the 48 angles. Each segment
        // has one triangle in each cap, from the center to the rim, and two in the side.
        cylinder.Vertices.Length.Should().Be(6 * CylinderSegments);
        cylinder.Indices.Length.Should().Be(4 * 3 * CylinderSegments);
    }

    [Fact]
    public void ACylinderIsClosedAndFacesOut()
    {
        MeshData cylinder = Cylinder();

        ExpectClosed(cylinder);
        ExpectFacingOut(cylinder, InCylinder);
    }

    [Fact]
    public void ACylinderHasTheNormalsOfItsFlatCapsAndOfItsRoundSide()
    {
        MeshData cylinder = Cylinder();

        int top = 0, bottom = 0, side = 0;
        foreach (Vertex v in cylinder.Vertices)
        {
            ExpectUnit(v.Normal);
            if (v.Normal.Y > 0.99f)
            {
                top++;
                ExpectNear(v.Normal, Vector3.UnitY, "the top cap faces up");
                v.Position.Y.Should().Be(CylinderHalfHeight, "a vertex of the top cap is on the top");
                ExpectCenterOrRim(v.Position);
            }
            else if (v.Normal.Y < -0.99f)
            {
                bottom++;
                ExpectNear(v.Normal, -Vector3.UnitY, "the bottom cap faces down");
                v.Position.Y.Should().Be(-CylinderHalfHeight, "a vertex of the bottom cap is on the bottom");
                ExpectCenterOrRim(v.Position);
            }
            else
            {
                side++;
                Rho(v.Position).Should().BeApproximately(CylinderRadius, 1e-5f, "a vertex of the side is on the rim");
                MathF.Abs(v.Position.Y).Should().Be(CylinderHalfHeight, "the side's vertices are at its ends");
                ExpectNear(v.Normal, Out(v.Position), "the side's normal points straight out from the axis");
            }
        }

        (top, bottom, side).Should().Be((2 * CylinderSegments, 2 * CylinderSegments, 2 * CylinderSegments));
    }

    [Fact]
    public void ACylinderHasAVertexOnTheRimAtEachOfItsSegmentAnglesStartingOnPlusX()
    {
        MeshData cylinder = Cylinder();

        // The segments' edges are at the angles 2π k / 48 from +X, turning from +X toward -Z, so the solid's facets are
        // where a scene expects them.
        IOrderedEnumerable<int> angles = cylinder.Vertices.Where(v => Rho(v.Position) > 0.5f)
            .Select(v => MathF.Atan2(-v.Position.Z, v.Position.X))
            .Select(angle =>
                (int)MathF.Round((angle < -1e-4f ? angle + MathF.Tau : angle) / (MathF.Tau / CylinderSegments)))
            .Distinct().Order();
        angles.Should().Equal(Enumerable.Range(0, CylinderSegments));
        cylinder.Vertices.Should().Contain(v => v.Position == new Vector3(CylinderRadius, CylinderHalfHeight, 0));
    }

    [Fact]
    public void ARingHasTwoVerticesPerSegmentForEachOfItsFourFacesAndEightTriangles()
    {
        MeshData ring = Ring();

        ring.Vertices.Length.Should().Be(8 * RingSegments);
        ring.Indices.Length.Should().Be(8 * 3 * RingSegments);
    }

    [Fact]
    public void ARingIsClosedAndFacesOut()
    {
        MeshData ring = Ring();

        ExpectClosed(ring);
        ExpectFacingOut(ring, InRing);
    }

    [Fact]
    public void ARingHasTheNormalsOfItsFlatEndsAndOfItsRoundOuterAndInnerFaces()
    {
        MeshData ring = Ring();

        int top = 0, bottom = 0, outer = 0, inner = 0;
        foreach (Vertex v in ring.Vertices)
        {
            ExpectUnit(v.Normal);
            float outward = Vector3.Dot(v.Normal, Out(v.Position));
            if (v.Normal.Y > 0.99f)
            {
                top++;
                ExpectNear(v.Normal, Vector3.UnitY, "the top end faces up");
                v.Position.Y.Should().Be(RingHalfWidth);
            }
            else if (v.Normal.Y < -0.99f)
            {
                bottom++;
                ExpectNear(v.Normal, -Vector3.UnitY, "the bottom end faces down");
                v.Position.Y.Should().Be(-RingHalfWidth);
            }
            else if (outward > 0.99f)
            {
                outer++;
                Rho(v.Position).Should().BeApproximately(RingOuter, 2e-5f, "the outer face is at the outer radius");
                ExpectNear(v.Normal, Out(v.Position), "the outer face's normal points straight out from the axis");
            }
            else
            {
                inner++;
                Rho(v.Position).Should()
                    .BeApproximately(RingInner, 2e-5f, "the inner face, the floor, is at the inner radius");
                ExpectNear(v.Normal, -Out(v.Position), "the floor's normal points straight at the axis");
            }
        }

        (top, bottom, outer, inner).Should()
            .Be((2 * RingSegments, 2 * RingSegments, 2 * RingSegments, 2 * RingSegments));
    }

    [Fact]
    public void ATorusHasOneVertexPerSideOfEachSegmentAndTwoTriangles()
    {
        MeshData torus = Torus();

        // The tube is smooth, so the two quads that meet at a side's edge share its vertices.
        torus.Vertices.Length.Should().Be(TorusSides * TorusSegments);
        torus.Indices.Length.Should().Be(2 * 3 * TorusSides * TorusSegments);
    }

    [Fact]
    public void ATorusIsClosedAndFacesOut()
    {
        MeshData torus = Torus();

        ExpectClosed(torus);
        ExpectFacingOut(torus, InTorus);
    }

    [Fact]
    public void ATorusHasItsVerticesOnItsTubeWithNormalsAwayFromTheTubesCenter()
    {
        MeshData torus = Torus();

        torus.Vertices.Should().NotBeEmpty("a torus with no vertex proves nothing");
        foreach (Vertex v in torus.Vertices)
        {
            ExpectUnit(v.Normal);
            // The nearest point of the circle that the tube goes round.
            Vector3 center = TorusMajor * Out(v.Position);
            (v.Position - center).Length().Should().BeApproximately(TorusMinor, 1e-5f, "the vertex is on the tube");
            ExpectNear(v.Normal, (v.Position - center) / TorusMinor, "the normal points away from the tube's center");
        }
    }

    [Fact]
    public void ATorusWhoseSidesAreAMultipleOfFourHasAFlatTopAndBottom()
    {
        MeshData torus = Torus();

        // The tube's corners are half a side off the top, so its top and bottom sides are flat, 2 r sin(π / 24) wide, and
        // the torus lies on a floor on them.
        float top = TorusMinor * MathF.Cos(MathF.PI / TorusSides);
        torus.Vertices.Max(v => v.Position.Y).Should().BeApproximately(top, 1e-5f);
        torus.Vertices.Count(v => MathF.Abs(v.Position.Y - top) < 1e-5f).Should().Be(2 * TorusSegments);
        torus.Vertices.Count(v => MathF.Abs(v.Position.Y + top) < 1e-5f).Should().Be(2 * TorusSegments);
    }

    [Theory]
    [InlineData(0f, 1f, 8)]
    [InlineData(1f, 0f, 8)]
    [InlineData(1f, 1f, 2)]
    [InlineData(float.NaN, 1f, 8)]
    [InlineData(1f, 1f, 129)]
    public void ACylinderWithoutARadiusAHeightOrFrom3To128SegmentsIsAnError(float radius, float halfHeight,
        int segments)
    {
        Action build = () => RevolvedSolid.Cylinder(radius, halfHeight, segments);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0f, 1f, 1f, 8)]
    [InlineData(2f, 2f, 1f, 8)]
    [InlineData(2f, 1f, 1f, 8)]
    [InlineData(1f, 2f, 0f, 8)]
    [InlineData(1f, 2f, 1f, 2)]
    public void ARingWithoutAHoleAThicknessAWidthOrThreeSegmentsIsAnError(float inner, float outer, float halfWidth,
        int segments)
    {
        Action build = () => RevolvedSolid.Ring(innerRadius: inner, outerRadius: outer, halfWidth: halfWidth, segments: segments);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(3f, 3f, 8, 8)]
    [InlineData(3f, 0f, 8, 8)]
    [InlineData(3f, 1f, 2, 8)]
    [InlineData(3f, 1f, 8, 2)]
    [InlineData(3f, 1f, 8, 129)]
    public void ATorusWithoutAHoleATubeThreeSegmentsOrAtMost128SidesIsAnError(float major, float minor, int segments,
        int sides)
    {
        // A segment's hull has two corners for each side, and Jolt keeps at most 256 points in a convex hull.
        Action build = () => RevolvedSolid.Torus(majorRadius: major, minorRadius: minor, segments: segments, sides: sides);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0.5f, 128)]
    [InlineData(0.1f, 48)]
    public void ACylinderWhoseSideBendsBy1MillimeterOrLessAtEachEdgeIsAnError(float radius, int segments)
    {
        // The side bends by radius (1 - cos(2π / segments)) at each edge between two facets: 0.6 mm and 0.86 mm here.
        // Jolt drops a corner of a hull that bends by 1 mm or less, so the shape would not match the mesh.
        Action build = () => RevolvedSolid.Cylinder(radius, 1, segments);

        build.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("segments").WithMessage("*Jolt drops*");
    }

    [Theory]
    [InlineData(0.5f, 128)]
    [InlineData(0.1f, 48)]
    public void ATorusWhoseTubeBendsBy1MillimeterOrLessAtEachEdgeIsAnError(float minor, int sides)
    {
        Action build = () => RevolvedSolid.Torus(majorRadius: 3, minorRadius: minor, segments: 8, sides: sides);

        build.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("sides").WithMessage("*Jolt drops*");
    }
}
