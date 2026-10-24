using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The convex radius that a box description and a convex hull description carry: the largest rounding, in m, that physics
/// may give the edges of the shape. These tests check the rules that need no physics backend.
/// </summary>
public sealed class ShapeDescConvexRadiusTests
{
    private static readonly Vector3[] Tetrahedron = [new(1, 0, 0), new(-1, 0, 0), new(0, 2, 0), new(0, 0, -3)];

    public static TheoryData<string> Shapes() => ["box", "convex hull"];

    private static ShapeDesc Shape(string name, float convexRadius) => name switch
    {
        "box" => new BoxShapeDesc(new(1), convexRadius),
        "convex hull" => new ConvexHullShapeDesc(Tetrahedron, convexRadius),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AShapeWithAZeroConvexRadiusHasNoProblem(string shape)
    {
        Shape(shape, 0).Problem().Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AShapeWithAPositiveConvexRadiusHasNoProblem(string shape)
    {
        Shape(shape, 0.3f).Problem().Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AShapeRefusesANegativeConvexRadius(string shape)
    {
        Shape(shape, -1).Problem().Should().Be("has the convex radius -1, which must be finite and not negative");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AShapeRefusesAConvexRadiusThatIsNotANumber(string shape)
    {
        Shape(shape, float.NaN).Problem().Should()
            .Be("has the convex radius NaN, which must be finite and not negative");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AShapeRefusesAnInfiniteConvexRadius(string shape)
    {
        Shape(shape, float.PositiveInfinity).Problem().Should().NotBeNull();
        Shape(shape, float.NegativeInfinity).Problem().Should().NotBeNull();
    }

    [Fact]
    public void ABoxWithoutANamedConvexRadiusGetsTheDefault()
    {
        new BoxShapeDesc(new(1)).ConvexRadius.Should().Be(ShapeDesc.DefaultConvexRadius);
    }

    [Fact]
    public void AConvexHullWithoutANamedConvexRadiusGetsTheDefault()
    {
        new ConvexHullShapeDesc(Tetrahedron).ConvexRadius.Should().Be(ShapeDesc.DefaultConvexRadius);
    }

    [Fact]
    public void ACompoundNamesTheShapeOfAChildWhoseConvexRadiusIsRefused()
    {
        var compound = new CompoundShapeDesc([new(new BoxShapeDesc(new(1), -1), Pose.Identity)]);

        compound.Problem().Should()
            .Be("has child 0, which has the convex radius -1, which must be finite and not negative");
    }

    [Fact]
    public void ABoxRefusesANegativeHalfExtentBeforeItLooksAtTheConvexRadius()
    {
        // The existing rule about the half extents keeps its message, whatever the radius is.
        new BoxShapeDesc(new(-1, 1, 1), -1).Problem().Should().StartWith("has the half extents");
    }
}
