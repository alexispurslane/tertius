using System.Numerics;
using System.Runtime.CompilerServices;

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

/// <summary>The case of a <see cref="QueryShape"/> that is a sphere: <c>QueryShape.Sphere(radius)</c>.</summary>
/// <param name="Radius">The radius, in meters. A query can use it only when it is finite and more than 0.</param>
public readonly record struct QuerySphere(float Radius);

/// <summary>The case of a <see cref="QueryShape"/> that is a box: <c>QueryShape.Box(halfExtents)</c>.</summary>
/// <param name="HalfExtents">Half the size of the box along each of its axes, in meters. A query can use them only when all three are finite and more than 0.</param>
public readonly record struct QueryBox(Vector3 HalfExtents);

/// <summary>The case of a <see cref="QueryShape"/> that is a capsule along +Y: <c>QueryShape.Capsule(halfHeight, radius)</c>.</summary>
/// <param name="HalfHeight">Half the length of the cylinder part, in meters. A query can use it only when it is finite and at least 0. A half height of 0 makes a sphere.</param>
/// <param name="Radius">The radius of the cylinder part and of both ends, in meters. A query can use it only when it is finite and more than 0.</param>
public readonly record struct QueryCapsule(float HalfHeight, float Radius);

/// <summary>
/// The shape of a shape cast or an overlap query: a sphere, a box or a capsule, with its center at the query's pose and
/// its axes along the pose's axes. Sizes are in meters. It is a value, so that a component can make one for each request
/// without allocating. The default value holds no shape, and every query refuses it.
/// </summary>
/// <remarks>
/// This is a custom C# 15 union of <see cref="QuerySphere"/>, <see cref="QueryBox"/> and <see cref="QueryCapsule"/>.
/// <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler pattern-match it without boxing.
/// <see cref="Value"/> boxes, so use it only for debugging. The constructors do not check sizes: a sphere of radius 0 is
/// a shape that holds a size that no query can use, and <see cref="Problem"/> says why, so that the query that gets it
/// can name the shape. The cases share their fields: the radius of a sphere and of a capsule is one field. A case reads
/// only its own fields, so the fields of the cases that a shape does not hold are zero and never matter.
/// </remarks>
[Union]
public readonly struct QueryShape : IEquatable<QueryShape>
{
    private const byte KindNone = 0, KindSphere = 1, KindBox = 2, KindCapsule = 3;

    private readonly byte kind;
    /// <summary>The radius of <see cref="QuerySphere"/> and of <see cref="QueryCapsule"/>, in meters.</summary>
    private readonly float radius;
    /// <summary>The half extents of <see cref="QueryBox"/>, in meters.</summary>
    private readonly Vector3 halfExtents;
    /// <summary>The half height of <see cref="QueryCapsule"/>, in meters.</summary>
    private readonly float halfHeight;

    public QueryShape(QuerySphere sphere) { kind = KindSphere; radius = sphere.Radius; }
    public QueryShape(QueryBox box) { kind = KindBox; halfExtents = box.HalfExtents; }
    public QueryShape(QueryCapsule capsule) { kind = KindCapsule; radius = capsule.Radius; halfHeight = capsule.HalfHeight; }

    public static implicit operator QueryShape(QuerySphere sphere) => new(sphere);
    public static implicit operator QueryShape(QueryBox box) => new(box);
    public static implicit operator QueryShape(QueryCapsule capsule) => new(capsule);

    public static QueryShape Sphere(float radius) => new(new QuerySphere(radius));

    public static QueryShape Box(Vector3 halfExtents) => new(new QueryBox(halfExtents));

    /// <summary>A capsule along +Y, with a total height of 2 · (<paramref name="halfHeight"/> + <paramref name="radius"/>). A half height of 0 makes a sphere.</summary>
    public static QueryShape Capsule(float halfHeight, float radius) => new(new QueryCapsule(halfHeight, radius));

    /// <summary>False for the default shape, which holds no sphere, box or capsule.</summary>
    public bool HasValue => kind != KindNone;

    public bool TryGetValue(out QuerySphere value)
    {
        value = kind == KindSphere ? new QuerySphere(radius) : default;
        return kind == KindSphere;
    }

    public bool TryGetValue(out QueryBox value)
    {
        value = kind == KindBox ? new QueryBox(halfExtents) : default;
        return kind == KindBox;
    }

    public bool TryGetValue(out QueryCapsule value)
    {
        value = kind == KindCapsule ? new QueryCapsule(halfHeight, radius) : default;
        return kind == KindCapsule;
    }

    public object? Value => kind switch
    {
        KindSphere => new QuerySphere(radius),
        KindBox => new QueryBox(halfExtents),
        KindCapsule => new QueryCapsule(halfHeight, radius),
        _ => null,
    };

    /// <summary>The radius of the smallest sphere about the shape's center that holds the shape, in meters. 0 for the default shape.</summary>
    public double BoundingRadius => this switch
    {
        QuerySphere(var sphereRadius) => sphereRadius,
        QueryBox(var boxHalfExtents) => ((Double3)boxHalfExtents).Length,
        QueryCapsule(var capsuleHalfHeight, var capsuleRadius) => (double)capsuleHalfHeight + capsuleRadius,
        null => 0,
    };

    /// <summary>
    /// Why no query can use this shape, or null when every query can: a radius or a half extent that is not finite and
    /// more than 0, a half height of a capsule that is not finite and at least 0, or the default shape, which holds no
    /// shape. The message completes a sentence that names the shape.
    /// </summary>
    public string? Problem() => this switch
    {
        QuerySphere(var sphereRadius) when !(sphereRadius > 0) || !float.IsFinite(sphereRadius) =>
            $"is a sphere with a radius of {sphereRadius} m, which is not finite and more than 0",
        QueryBox(var boxHalfExtents) when !(boxHalfExtents.X > 0 && boxHalfExtents.Y > 0 && boxHalfExtents.Z > 0)
                                          || !float.IsFinite(boxHalfExtents.X + boxHalfExtents.Y + boxHalfExtents.Z) =>
            $"is a box with half extents of {boxHalfExtents} m, which are not all finite and more than 0",
        QueryCapsule(var capsuleHalfHeight, var capsuleRadius) when !(capsuleRadius > 0) || !float.IsFinite(capsuleRadius)
                                                                     || !(capsuleHalfHeight >= 0) || !float.IsFinite(capsuleHalfHeight) =>
            $"is a capsule with a half height of {capsuleHalfHeight} m and a radius of {capsuleRadius} m, which need a finite radius of more than 0 and a finite half height of at least 0",
        QuerySphere or QueryBox or QueryCapsule => null,
        null => "is the default shape, which holds no sphere, box or capsule",
    };

    // The held case alone decides equality: the fields of the other cases are not compared.
    public bool Equals(QueryShape other) => kind == other.kind && kind switch
    {
        KindSphere => radius.Equals(other.radius),
        KindBox => halfExtents.Equals(other.halfExtents),
        KindCapsule => radius.Equals(other.radius) && halfHeight.Equals(other.halfHeight),
        _ => true,
    };

    public override bool Equals(object? obj) => obj is QueryShape shape && Equals(shape);

    public override int GetHashCode() => kind switch
    {
        KindSphere => HashCode.Combine(kind, radius),
        KindBox => HashCode.Combine(kind, halfExtents),
        KindCapsule => HashCode.Combine(kind, radius, halfHeight),
        _ => 0,
    };

    public static bool operator ==(QueryShape a, QueryShape b) => a.Equals(b);
    public static bool operator !=(QueryShape a, QueryShape b) => !a.Equals(b);

    public override string ToString() => this switch
    {
        QuerySphere sphere => $"QueryShape({sphere})",
        QueryBox box => $"QueryShape({box})",
        QueryCapsule capsule => $"QueryShape({capsule})",
        null => "QueryShape(none)",
    };
}
