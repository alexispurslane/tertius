using System.Numerics;

namespace Tertius;

/// <summary>
/// Which entities a physics query may hit. The backend asks it about the entity of each candidate body, inside the
/// query, on the thread that runs the query, before it tests the body's shape. So it must be fast, and it must not call
/// the backend. An exception that it throws stops the query: the backend rejects every later candidate, and throws the
/// exception again from the query once the native query has returned.
/// </summary>
public interface IPhysicsQueryFilter
{
    bool Accepts(Entity entity);
}

/// <summary>What a ray cast of <see cref="IPhysicsBackend.CastRay"/> reports.</summary>
public enum RayCastKind : byte
{
    /// <summary>The nearest hit that the filter accepts, if any.</summary>
    Nearest,
    /// <summary>Every hit that the filter accepts, by distance. A body can be hit more than once, for example a mesh.</summary>
    All,
}

/// <summary>
/// One hit of a ray cast in one frame's physics system. <see cref="Distance"/> is along the ray from its origin, in
/// meters, and <see cref="Normal"/> is the unit normal of the hit surface in the axes of that frame.
/// </summary>
public readonly record struct RayCastHit(Entity Entity, double Distance, Vector3 Normal);

/// <summary>
/// One hit of a shape cast in one frame's physics system. <see cref="Distance"/> is how far the shape moved along the
/// cast's direction from its start before it touched the body, in meters. <see cref="Point"/> is where it touched the
/// body's surface, in that frame's coordinates, and <see cref="Normal"/> is the unit normal of that surface there, in
/// that frame's axes.
/// </summary>
public readonly record struct ShapeCastHit(Entity Entity, double Distance, Double3 Point, Vector3 Normal);

/// <summary>The kind of a <see cref="QueryShape"/>.</summary>
public enum QueryShapeKind : byte
{
    Sphere,
    Box,
    Capsule,
}

/// <summary>
/// The shape of a shape cast or an overlap query: a sphere, a box or a capsule, with its center at the query's pose and
/// its axes along the pose's axes. Sizes are in meters. It is a value, so that a component can make one for each request
/// without allocating. The default value is a sphere of radius 0, which every query refuses.
/// </summary>
public readonly record struct QueryShape
{
    private QueryShape(QueryShapeKind kind, Vector3 halfExtents, float radius, float halfHeight)
    {
        Kind = kind;
        HalfExtents = halfExtents;
        Radius = radius;
        HalfHeight = halfHeight;
    }

    public QueryShapeKind Kind { get; }

    /// <summary>The half extents of a box along its axes, or zero for a sphere and a capsule.</summary>
    public Vector3 HalfExtents { get; }

    /// <summary>The radius of a sphere or a capsule, or zero for a box.</summary>
    public float Radius { get; }

    /// <summary>Half the length of the cylinder part of a capsule, which lies along the capsule's +Y, or zero for the other kinds.</summary>
    public float HalfHeight { get; }

    public static QueryShape Sphere(float radius) => new(QueryShapeKind.Sphere, Vector3.Zero, radius, 0);

    public static QueryShape Box(Vector3 halfExtents) => new(QueryShapeKind.Box, halfExtents, 0, 0);

    /// <summary>A capsule along +Y, with a total height of 2 · (<paramref name="halfHeight"/> + <paramref name="radius"/>). A half height of 0 makes a sphere.</summary>
    public static QueryShape Capsule(float halfHeight, float radius) => new(QueryShapeKind.Capsule, Vector3.Zero, radius, halfHeight);

    /// <summary>The radius of the smallest sphere about the shape's center that holds the shape, in meters.</summary>
    public double BoundingRadius => Kind switch
    {
        QueryShapeKind.Box => ((Double3)HalfExtents).Length,
        QueryShapeKind.Capsule => (double)HalfHeight + Radius,
        _ => Radius,
    };

    /// <summary>
    /// Why no query can use this shape, or null when every query can: a radius or a half extent that is not finite and
    /// more than 0, or a half height of a capsule that is not finite and at least 0. The message completes a sentence that
    /// names the shape.
    /// </summary>
    public string? Problem() => Kind switch
    {
        QueryShapeKind.Sphere when !(Radius > 0) || !float.IsFinite(Radius) => $"is a sphere with a radius of {Radius} m, which is not finite and more than 0",
        QueryShapeKind.Box when !(HalfExtents.X > 0 && HalfExtents.Y > 0 && HalfExtents.Z > 0) || !float.IsFinite(HalfExtents.X + HalfExtents.Y + HalfExtents.Z) =>
            $"is a box with half extents of {HalfExtents} m, which are not all finite and more than 0",
        QueryShapeKind.Capsule when !(Radius > 0) || !float.IsFinite(Radius) || !(HalfHeight >= 0) || !float.IsFinite(HalfHeight) =>
            $"is a capsule with a half height of {HalfHeight} m and a radius of {Radius} m, which need a finite radius of more than 0 and a finite half height of at least 0",
        QueryShapeKind.Sphere or QueryShapeKind.Box or QueryShapeKind.Capsule => null,
        _ => $"has the kind {Kind}, which is not a sphere, a box or a capsule",
    };
}
