using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The facts that a shape description gives about itself, with no physics backend: its kind, and the radius of the
/// smallest sphere about the body's origin that holds it.
/// </summary>
public sealed class ShapeDescFactsTests
{
    private static readonly CompoundShapeDesc SphereAndTurnedCube = new(
    [
        new(new SphereShapeDesc(1), new(new(3, 0, 0), Quaternion.Identity)),
        new(new BoxShapeDesc(new(1)), new(new(0, -3, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4)))
    ]);

    public static TheoryData<string, ShapeKind> Shapes() => new()
    {
        { "box", ShapeKind.Box },
        { "sphere", ShapeKind.Sphere },
        { "capsule", ShapeKind.Capsule },
        { "convex hull", ShapeKind.ConvexHull },
        { "mesh", ShapeKind.Mesh },
        { "compound", ShapeKind.Compound }
    };

    private static ShapeDesc Shape(string name) => name switch
    {
        "box" => new BoxShapeDesc(new(1)),
        "sphere" => new SphereShapeDesc(1),
        "capsule" => new CapsuleShapeDesc(1, 0.5f),
        "convex hull" => new ConvexHullShapeDesc([new(1, 0, 0), new(-1, 0, 0), new(0, 2, 0), new(0, 0, -3)]),
        "mesh" => new MeshShapeDesc([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2]),
        "compound" => SphereAndTurnedCube,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EachShapeRecordNamesItsOwnKind(string shape, ShapeKind kind)
    {
        Shape(shape).Kind.Should().Be(kind);
    }

    [Fact]
    public void TheBoundingRadiusOfABoxReachesItsCorners()
    {
        new BoxShapeDesc(new(1, 2, 2)).BoundingRadius().Should().BeApproximately(3f, 5e-6f);
    }

    [Fact]
    public void TheBoundingRadiusOfASphereIsItsRadius()
    {
        new SphereShapeDesc(1.5f).BoundingRadius().Should().Be(1.5f);
    }

    [Fact]
    public void TheBoundingRadiusOfACapsuleReachesTheTopOfItsCap()
    {
        new CapsuleShapeDesc(1, 0.5f).BoundingRadius().Should().BeApproximately(1.5f, 5e-6f);
        new CapsuleShapeDesc(0, 0.5f).BoundingRadius().Should().BeApproximately(0.5f, 5e-6f);
    }

    [Fact]
    public void TheBoundingRadiusOfAConvexHullReachesItsFarthestPoint()
    {
        Shape("convex hull").BoundingRadius().Should().BeApproximately(3f, 5e-6f);
    }

    [Fact]
    public void TheBoundingRadiusOfAMeshCountsOnlyTheVerticesThatATriangleUses()
    {
        var mesh = new MeshShapeDesc([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(100, 100, 100)], [0, 1, 2]);

        mesh.BoundingRadius().Should().BeApproximately(1f, 5e-6f);
    }

    [Fact]
    public void TheBoundingRadiusOfACompoundPlacesEachChildAtItsPose()
    {
        // A unit sphere at x = 3, and a unit cube at y = -3 turned 45° about Z. The cube's farthest corner is at
        // (0, -3 - √2, ±1).
        double expected = Math.Sqrt((3 + Math.Sqrt(2)) * (3 + Math.Sqrt(2)) + 1);

        ((double)SphereAndTurnedCube.BoundingRadius()).Should().BeApproximately(expected, 5e-5);
    }

    [Fact]
    public void TheBoundingRadiusOfNestedCompoundsComposesTheirPoses()
    {
        var nested = new CompoundShapeDesc([new(SphereAndTurnedCube, new(new(0, 0, 10), Quaternion.Identity))]);

        ((double)nested.BoundingRadius()).Should()
            .BeApproximately(Math.Sqrt((3 + Math.Sqrt(2)) * (3 + Math.Sqrt(2)) + 121), 5e-5);
    }

    [Fact]
    public void TheBoundingRadiusRoundsUpSoThatTheFloatStillHoldsTheShape()
    {
        var box = new BoxShapeDesc(new(0.1f, 0.2f, 0.3f));

        ((double)box.BoundingRadius()).Should()
            .BeGreaterThanOrEqualTo(Math.Sqrt(0.1f * 0.1f + 0.2f * 0.2f + 0.3f * 0.3f));
    }
}
