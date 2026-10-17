using System.Numerics;

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
}
