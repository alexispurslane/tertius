using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Core.Tests;

/// <summary>
/// The shape of a shape cast or an overlap query, and the answers of both. A shape is a value with a usable size or a
/// reason why a query refuses it, and each answer fits one event payload.
/// </summary>
public sealed class QueryShapeTests
{
    [Fact]
    public void AShapeHitAndAnOverlapHitEachFitInOnePayload()
    {
        Assert.True(Unsafe.SizeOf<ShapeHit>() <= EventTypeRegistry.MaxPayloadSize);
        Assert.True(Unsafe.SizeOf<OverlapHit>() <= EventTypeRegistry.MaxPayloadSize);
        Assert.True(EventType<ShapeHit>.Id >= 0);
        Assert.True(EventType<OverlapHit>.Id >= 0);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(-0.48f, 0.6f, -0.64f)]
    public void TheNormalOfAShapeHitComesBackAsTheUnitVectorThatWentIn(float x, float y, float z)
    {
        Vector3 normal = Vector3.Normalize(new(x, y, z));

        var hit = new ShapeHit { Entity = new(3, 1), Normal = normal };

        Assert.True(Vector3.Distance(hit.Normal, normal) < 1e-6f, $"{normal} came back as {hit.Normal}");
    }

    [Fact]
    public void AMissOfAShapeCastAndAnEmptyOverlapNameNoEntity()
    {
        var miss = new ShapeHit { Tag = 7 };
        var none = new OverlapHit { Tag = 8 };

        Assert.False(miss.IsHit);
        Assert.Equal(Vector3.Zero, miss.Normal);
        Assert.False(none.IsHit);
        Assert.True(new OverlapHit { Entity = new(1, 1) }.IsHit);
    }

    public static TheoryData<string, double> BoundingRadii() => new()
    {
        { "sphere", 2 },
        { "box", 3 },
        { "capsule", 1.5 }
    };

    [Theory]
    [MemberData(nameof(BoundingRadii))]
    public void TheBoundingRadiusIsTheRadiusOfTheSmallestSphereAboutTheCenterThatHoldsTheShape(string kind,
        double radius)
    {
        QueryShape shape = kind switch
        {
            "sphere" => QueryShape.Sphere(2),
            // The corner of a box with half extents 1, 2 and 2 is 3 m from its center.
            "box" => QueryShape.Box(new(1, 2, 2)),
            _ => QueryShape.Capsule(1, 0.5f)
        };

        Assert.Equal(radius, shape.BoundingRadius, 1e-12);
    }

    [Fact]
    public void ASphereABoxAndACapsuleOfUsableSizesHaveNoProblem()
    {
        Assert.Null(QueryShape.Sphere(0.01f).Problem());
        Assert.Null(QueryShape.Box(new(0.1f, 2, 30)).Problem());
        Assert.Null(QueryShape.Capsule(1, 0.5f).Problem());
        Assert.Null(QueryShape.Capsule(0, 0.5f).Problem());
    }

    public static TheoryData<string, string> Refused() => new()
    {
        { "sphere 0", "radius" },
        { "sphere -1", "radius" },
        { "sphere NaN", "radius" },
        { "sphere infinite", "radius" },
        { "box with a zero side", "half extents" },
        { "box with a NaN side", "half extents" },
        { "box with an infinite side", "half extents" },
        { "capsule with radius 0", "radius" },
        { "capsule with a negative half height", "half height" },
        { "capsule with an infinite half height", "half height" }
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void AShapeWithASizeThatIsNotFiniteAndPositiveIsRefusedWithTheSizeNamed(string which, string named)
    {
        QueryShape shape = which switch
        {
            "sphere 0" => QueryShape.Sphere(0),
            "sphere -1" => QueryShape.Sphere(-1),
            "sphere NaN" => QueryShape.Sphere(float.NaN),
            "sphere infinite" => QueryShape.Sphere(float.PositiveInfinity),
            "box with a zero side" => QueryShape.Box(new(1, 0, 1)),
            "box with a NaN side" => QueryShape.Box(new(1, 1, float.NaN)),
            "box with an infinite side" => QueryShape.Box(new(float.PositiveInfinity, 1, 1)),
            "capsule with radius 0" => QueryShape.Capsule(1, 0),
            "capsule with a negative half height" => QueryShape.Capsule(-0.1f, 1),
            "capsule with an infinite half height" => QueryShape.Capsule(float.PositiveInfinity, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "No such shape.")
        };

        string? problem = shape.Problem();

        Assert.NotNull(problem);
        Assert.Contains(named, problem);
    }

    [Fact]
    public void TwoShapesOfTheSameKindAndSizeAreEqual()
    {
        Assert.Equal(QueryShape.Capsule(1, 0.5f), QueryShape.Capsule(1, 0.5f));
        Assert.NotEqual(QueryShape.Capsule(1, 0.5f), QueryShape.Capsule(0.5f, 1));
        Assert.NotEqual(QueryShape.Sphere(1), QueryShape.Capsule(0, 1));
    }
}
