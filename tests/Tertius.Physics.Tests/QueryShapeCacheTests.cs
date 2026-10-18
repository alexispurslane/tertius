using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics.Tests;

/// <summary>
/// The backend's native shapes for queries: one for each shape value that a query asks with, and never more than the
/// cache's capacity, so that a component that asks with ever new sizes cannot grow it without bound.
/// </summary>
public class QueryShapeCacheTests
{
    [Fact]
    public void TheSameShapeValueGivesTheSameNativeShape()
    {
        JoltRuntime.EnsureInitialized();
        using var cache = new QueryShapeCache();

        var first = cache.Get(QueryShape.Box(new Vector3(1, 2, 3)));
        var again = cache.Get(QueryShape.Box(new Vector3(1, 2, 3)));
        var other = cache.Get(QueryShape.Capsule(2, 1));

        again.Should().BeSameAs(first);
        other.Should().NotBeSameAs(first);
        cache.Count.Should().Be(2);
    }

    [Fact]
    public void ACacheThatIsFullFreesEveryShapeBeforeItMakesANewOne()
    {
        JoltRuntime.EnsureInitialized();
        using var cache = new QueryShapeCache();
        for (int i = 1; i <= QueryShapeCache.Capacity; i++)
            cache.Get(QueryShape.Sphere(i));
        cache.Count.Should().Be(QueryShapeCache.Capacity);

        cache.Get(QueryShape.Sphere(QueryShapeCache.Capacity + 1));

        cache.Count.Should().Be(1);
    }

    [Fact]
    public void ASphereMakesANativeSphereOfItsRadius()
    {
        JoltRuntime.EnsureInitialized();
        using var cache = new QueryShapeCache();

        Shape native = cache.Get(QueryShape.Sphere(2.5f));

        native.Should().BeOfType<SphereShape>().Which.Radius.Should().Be(2.5f);
    }

    [Fact]
    public void ABoxMakesANativeBoxOfItsHalfExtentsWithSharpEdges()
    {
        JoltRuntime.EnsureInitialized();
        using var cache = new QueryShapeCache();

        Shape native = cache.Get(QueryShape.Box(new Vector3(1, 2, 3)));

        var box = native.Should().BeOfType<BoxShape>().Subject;
        box.HalfExtent.Should().Be(new Vector3(1, 2, 3));
        box.ConvexRadius.Should().Be(0);
    }

    [Fact]
    public void ACapsuleMakesANativeCapsuleOfItsHalfHeightAndRadius()
    {
        JoltRuntime.EnsureInitialized();
        using var cache = new QueryShapeCache();

        Shape native = cache.Get(QueryShape.Capsule(2, 0.5f));

        var capsule = native.Should().BeOfType<CapsuleShape>().Subject;
        capsule.HalfHeightOfCylinder.Should().Be(2);
        capsule.Radius.Should().Be(0.5f);
    }

    [Fact]
    public void ACapsuleWithoutACylinderPartMakesANativeSphereBecauseJoltRefusesThatCapsule()
    {
        JoltRuntime.EnsureInitialized();
        using var cache = new QueryShapeCache();

        Shape native = cache.Get(QueryShape.Capsule(0, 0.75f));

        native.Should().BeOfType<SphereShape>().Which.Radius.Should().Be(0.75f);
    }

    [Fact]
    public void TheDefaultShapeHoldsNoShapeSoTheCacheMakesNoNativeShapeOfIt()
    {
        JoltRuntime.EnsureInitialized();
        using var cache = new QueryShapeCache();

        // The backend refuses the default shape before it asks the cache, so only a bug gets here. A Debug build fails the
        // precondition of Get (a ContractException), and a Release build fails in Create (an ArgumentException).
        Action ask = () => cache.Get(default);

        ask.Should().Throw<Exception>();
        cache.Count.Should().Be(0);
    }
}
