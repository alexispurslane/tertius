using System.Numerics;
using Tertius.Demo;
using static Tertius.Host.Tests.MeshChecks;

namespace Tertius.Host.Tests;

/// <summary>
/// A ring of <see cref="RevolvedSolid"/> with one segment left out (<see cref="RevolvedSolid.LeavingOut"/>), as the middle
/// band of the station's ceiling leaves out the segment under a spoke (docs/plans/blue-danube-demo.md, "The exit", "The
/// chute"). The segment is open: no hull of the shape and no triangle of the mesh lies there. The mesh stays closed,
/// because it caps the ring's cut ends.
/// </summary>
public sealed class RevolvedSolidGapTests
{
    /// <summary>A ring like the ceiling's middle band, small enough to test fast: 20 m to 20.5 m from the axis, 4 m wide, in 32 segments.</summary>
    private const float Inner = 20,
        Outer = 20.5f,
        HalfWidth = 2;

    private const int Segments = 32;

    /// <summary>The segment left out: segment 16 spans θ from 180 to 191.25 degrees.</summary>
    private const int Gap = 16;

    private static RevolvedSolid Ring() =>
        RevolvedSolid
            .Ring(innerRadius: Inner, outerRadius: Outer, halfWidth: HalfWidth, segments: Segments)
            .LeavingOut(Gap);

    /// <summary>The point in the middle of the ring's wall at the angle θ, in radians, measured from +X toward -Z.</summary>
    private static Vector3 InTheWall(double theta) =>
        new((float)((Inner + Outer) / 2 * Math.Cos(theta)), 0, (float)(-(Inner + Outer) / 2 * Math.Sin(theta)));

    /// <summary>The middle angle of segment k, in radians.</summary>
    private static double Middle(int k) => Math.Tau * (k + 0.5) / Segments;

    private static bool InSegment(Vector3 p, int k)
    {
        double theta = Math.Atan2(-p.Z, p.X);
        if (theta < 0)
            theta += Math.Tau;
        return theta >= Math.Tau * k / Segments - 1e-6 && theta <= Math.Tau * (k + 1) / Segments + 1e-6;
    }

    /// <summary>The shape has one hull for each segment but the one left out, and no hull holds a point of that segment.</summary>
    [Fact]
    public void ARingWithASegmentLeftOutHasNoHullThere()
    {
        var shape = (CompoundShapeDesc)Ring().Shape();
        shape.Children.Should().HaveCount(Segments - 1, "the left-out segment has no hull");
        foreach (CompoundChild child in shape.Children)
        {
            Vector3[] points = ((ConvexHullShapeDesc)child.Shape).Points;
            Vector3 center = points.Aggregate(Vector3.Zero, (sum, p) => sum + p) / points.Length;
            InSegment(center, Gap).Should().BeFalse($"no hull lies in segment {Gap}, but one is centered at {center}");
        }
    }

    /// <summary>
    /// The mesh is closed, and a line through the wall in the middle of the left-out segment crosses no triangle, while a
    /// line through the wall of each neighbor crosses its two faces.
    /// </summary>
    [Fact]
    public void ItsMeshLeavesTheSegmentOpenAndCapsItsCutEnds()
    {
        MeshData mesh = Ring().Mesh();
        ExpectClosed(mesh);
        Vector3 Out(int k) => InTheWall(Middle(k)) * (25f / ((Inner + Outer) / 2));
        Vector3 In(int k) => InTheWall(Middle(k)) * (15f / ((Inner + Outer) / 2));
        Crossings(mesh, In(Gap), Out(Gap)).Should().Be(0, "the left-out segment is open");
        Crossings(mesh, In(Gap - 1), Out(Gap - 1)).Should().Be(2, "the segment before the gap is closed");
        Crossings(mesh, In(Gap + 1), Out(Gap + 1)).Should().Be(2, "the segment after the gap is closed");
        // A short chord in the ring's wall, from just before the gap to just after it, crosses the two caps at the cut
        // ends. Its sag inward, 0.12 m, keeps it inside the wall, 0.5 m thick.
        Vector3 before = InTheWall(Math.Tau * (Gap - 0.05) / Segments),
            after = InTheWall(Math.Tau * (Gap + 1.05) / Segments);
        Crossings(mesh, before, after).Should().Be(2, "each cut end is capped");
    }

    /// <summary>Every triangle faces out of the ring with the gap.</summary>
    [Fact]
    public void ItsMeshFacesOutOfTheRingWithTheGap()
    {
        static bool Inside(Vector3 p)
        {
            float rho = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
            return rho >= Inner && rho <= Outer && MathF.Abs(p.Y) <= HalfWidth && !InSegment(p, Gap);
        }

        // The facets lie up to 0.1 m inside the outer circle, 20.5 m (1 - cos(π / 32)), so the probe looks 0.15 m off each face.
        ExpectFacingOut(mesh: Ring().Mesh(), inside: Inside, probe: 0.15f, maxNormalAngle: MathF.PI / 12);
    }

    /// <summary>A segment that is not one of the ring's is an error, and so is a gap in a cylinder, which is one convex hull.</summary>
    [Fact]
    public void LeavingOutASegmentThatIsNotThereIsAnError()
    {
        FluentActions
            .Invoking(() =>
                RevolvedSolid
                    .Ring(innerRadius: Inner, outerRadius: Outer, halfWidth: HalfWidth, segments: Segments)
                    .LeavingOut(Segments)
            )
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() =>
                RevolvedSolid
                    .Ring(innerRadius: Inner, outerRadius: Outer, halfWidth: HalfWidth, segments: Segments)
                    .LeavingOut(-1)
            )
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => RevolvedSolid.Cylinder(1, 1, 32).LeavingOut(0))
            .Should()
            .Throw<InvalidOperationException>();
    }
}
