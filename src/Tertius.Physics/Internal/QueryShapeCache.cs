using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// Native Jolt shapes of the shapes that shape casts and overlap queries ask with, by value, so that a component that asks
/// with the same shape every tick makes it once. It holds at most <see cref="Capacity"/> shapes, and frees them all when a
/// new one would pass that, so a component that asks with ever new sizes cannot grow it without bound: it pays for a new
/// native shape for each new size instead. Not thread-safe: use it at the sync point, under
/// <see cref="JoltRuntime.NativeGate"/>.
/// </summary>
internal sealed class QueryShapeCache : IDisposable
{
    public const int Capacity = 64;

    private readonly Dictionary<QueryShape, Shape> shapes = new(Capacity);

    /// <summary>The native shape of <paramref name="shape"/>, which <see cref="QueryShape.Problem"/> accepts, built on first use.</summary>
    public Shape Get(in QueryShape shape)
    {
        Contract.Requires(shape.Problem() is null);
        if (!shapes.TryGetValue(shape, out Shape? native))
        {
            if (shapes.Count == Capacity)
                Dispose();
            native = Create(shape);
            shapes.Add(shape, native);
        }
        return native;
    }

    /// <summary>How many native shapes the cache holds.</summary>
    public int Count => shapes.Count;

    public void Dispose()
    {
        foreach (Shape native in shapes.Values)
            native.Dispose();
        shapes.Clear();
    }

    private static Shape Create(in QueryShape shape) => shape.Kind switch
    {
        // A query box has sharp edges: no convex radius rounds them.
        QueryShapeKind.Box => NativeOwnership.Owned(new BoxShape(shape.HalfExtents, 0)),
        // Jolt does not accept a capsule without a cylinder part. That capsule is a sphere.
        QueryShapeKind.Capsule when shape.HalfHeight > 0 => NativeOwnership.Owned(new CapsuleShape(shape.HalfHeight, shape.Radius)),
        _ => NativeOwnership.Owned(new SphereShape(shape.Radius)),
    };
}
