using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>The case of a <see cref="PhysicsQuestion"/> that asks for a ray cast: <c>CastRay</c> and <c>CastRayAll</c>.</summary>
/// <param name="Origin">Where the ray starts, in the coordinates of the request's frame, in meters. Every coordinate is finite.</param>
/// <param name="Direction">The direction of the ray, in the axes of the request's frame. It is a unit vector.</param>
/// <param name="Length">How far the ray reaches, in meters. It is finite and more than 0.</param>
/// <param name="Kind">Whether the answer is the nearest hit alone or every hit.</param>
internal readonly record struct RayQuestion(Double3 Origin, Double3 Direction, double Length, RayCastKind Kind);

/// <summary>The case that asks for a shape cast: <c>CastShape</c>.</summary>
/// <param name="Shape">The shape that moves. It has no <see cref="QueryShape.Problem"/>.</param>
/// <param name="Start">The pose of the shape at the start, in the request's frame. Its rotation is a unit quaternion.</param>
/// <param name="Direction">The direction in which the shape moves, without turning, in the axes of the request's frame. It is a unit vector.</param>
/// <param name="Length">How far the shape moves, in meters. It is finite and more than 0.</param>
internal readonly record struct ShapeCastQuestion(QueryShape Shape, DPose Start, Double3 Direction, double Length);

/// <summary>The case that asks for an overlap test: <c>Overlap</c>.</summary>
/// <param name="Shape">The shape to test. It has no <see cref="QueryShape.Problem"/>.</param>
/// <param name="Pose">The pose of the shape, in the request's frame. Its rotation is a unit quaternion.</param>
internal readonly record struct OverlapQuestion(QueryShape Shape, DPose Pose);

/// <summary>
/// What a <see cref="PhysicsRequest"/> asks of the physics bodies: a ray cast, a shape cast or an overlap test. The three
/// cases have different fields, so each case holds only its own. The request holds what every request has: who asked, in
/// which frame, which entities may be hit, and the tag of the answer.
/// </summary>
/// <remarks>
/// <para>
/// This is a custom C# 15 union of <see cref="RayQuestion"/>, <see cref="ShapeCastQuestion"/> and
/// <see cref="OverlapQuestion"/>. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler
/// pattern-match it without boxing. <see cref="Value"/> boxes, so use it only for debugging. The default value holds no
/// case, and only a bug puts it in a request: the World builds every question from a case after it checked the
/// arguments, and the runner refuses a request whose question is the default. The constructors store what they get. The
/// checks and the unit direction belong to the World (<c>RequestRay</c>, <c>RequestShapeCast</c> and
/// <c>RequestOverlap</c>), because their messages name the arguments of the public calls. The cases share their fields:
/// the start position of all three is one field, the direction and the length of a ray and of a shape cast are one each,
/// and the shape and the rotation of a shape cast and of an overlap are one each. A case reads only its own fields, so
/// the fields of the cases that a question does not hold may hold anything and never matter.
/// </para>
/// <para>
/// Every question has the same sweep: a ball of radius <see cref="Reach"/> that moves from <see cref="Origin"/> along
/// <see cref="Direction"/> for <see cref="Length"/> meters. A ray reaches 0 sideways. An overlap moves nowhere: its length
/// and direction are zero, and its ball is its shape's bounding sphere. The runner walks the frames by this sweep, and
/// switches over the held case only for what the case alone decides: the line along which the walk tests the bubbles
/// (an overlap, which moves nowhere, takes any line through its center), the Jolt call, whether an entity counts once
/// however many of its bodies are hit (only for an overlap), and the answer event.
/// </para>
/// </remarks>
[Union]
internal readonly struct PhysicsQuestion : IEquatable<PhysicsQuestion>
{
    private const byte KindNone = 0, KindRay = 1, KindShapeCast = 2, KindOverlap = 3;

    private readonly byte kind;

    /// <summary>The origin of <see cref="RayQuestion"/>, the position of the start pose of <see cref="ShapeCastQuestion"/>, and the position of the pose of
    /// <see cref="OverlapQuestion"/>.</summary>
    private readonly Double3 position;

    /// <summary>The rotation of the start pose of <see cref="ShapeCastQuestion"/> and of the pose of
    /// <see cref="OverlapQuestion"/>.</summary>
    private readonly DQuaternion rotation;

    /// <summary>The direction of <see cref="RayQuestion"/> and of <see cref="ShapeCastQuestion"/>.</summary>
    private readonly Double3 direction;

    /// <summary>The length of <see cref="RayQuestion"/> and of <see cref="ShapeCastQuestion"/>, in meters.</summary>
    private readonly double length;

    /// <summary>The shape of <see cref="ShapeCastQuestion"/> and of <see cref="OverlapQuestion"/>.</summary>
    private readonly QueryShape shape;

    /// <summary>The kind of <see cref="RayQuestion"/>.</summary>
    private readonly RayCastKind rayKind;

    public PhysicsQuestion(RayQuestion ray)
    {
        kind = KindRay;
        position = ray.Origin;
        direction = ray.Direction;
        length = ray.Length;
        rayKind = ray.Kind;
    }

    public PhysicsQuestion(ShapeCastQuestion shapeCast)
    {
        kind = KindShapeCast;
        shape = shapeCast.Shape;
        position = shapeCast.Start.Position;
        rotation = shapeCast.Start.Rotation;
        direction = shapeCast.Direction;
        length = shapeCast.Length;
    }

    public PhysicsQuestion(OverlapQuestion overlap)
    {
        kind = KindOverlap;
        shape = overlap.Shape;
        position = overlap.Pose.Position;
        rotation = overlap.Pose.Rotation;
    }

    /// <summary>False for the default question, which asks nothing and which no request holds.</summary>
    public bool HasValue => kind != KindNone;

    public object? Value => kind switch
    {
        KindRay => new RayQuestion(position, direction, length, rayKind),
        KindShapeCast => new ShapeCastQuestion(shape, new(position, rotation), direction, length),
        KindOverlap => new OverlapQuestion(shape, new(position, rotation)),
        _ => null
    };

    // The sweep view switches over the cases with no discard arm, so a new case breaks the build here until it answers.

    /// <summary>Where the sweep starts, in the request's frame, in meters: a ray's origin, a shape's center at the start, or an overlap's center. Zero for the default question.</summary>
    public Double3 Origin => this switch
    {
        RayQuestion ray => ray.Origin,
        ShapeCastQuestion shapeCast => shapeCast.Start.Position,
        OverlapQuestion overlap => overlap.Pose.Position,
        null => Double3.Zero
    };

    /// <summary>The unit direction of the sweep in the axes of the request's frame. Zero for an overlap, which moves nowhere, and for the default question.</summary>
    public Double3 Direction => this switch
    {
        RayQuestion ray => ray.Direction,
        ShapeCastQuestion shapeCast => shapeCast.Direction,
        OverlapQuestion => Double3.Zero,
        null => Double3.Zero
    };

    /// <summary>How far the sweep moves, in meters. 0 for an overlap, which moves nowhere, and for the default question.</summary>
    public double Length => this switch
    {
        RayQuestion ray => ray.Length,
        ShapeCastQuestion shapeCast => shapeCast.Length,
        OverlapQuestion => 0,
        null => 0
    };

    /// <summary>How far from its line the sweep reaches, in meters: the bounding radius of the shape of a shape cast or an overlap, and 0 for a ray and for the default question.</summary>
    public double Reach => this switch
    {
        RayQuestion => 0,
        ShapeCastQuestion shapeCast => shapeCast.Shape.BoundingRadius,
        OverlapQuestion overlap => overlap.Shape.BoundingRadius,
        null => 0
    };

    /// <summary>True when only the nearest hit is the answer: for a ray of kind <see cref="RayCastKind.Nearest"/> and for a shape cast. False for a ray of kind
    /// <see cref="RayCastKind.All"/>, for an overlap and for the default question.</summary>
    public bool FirstHitOnly => this switch
    {
        RayQuestion ray => ray.Kind == RayCastKind.Nearest,
        ShapeCastQuestion => true,
        OverlapQuestion => false,
        null => false
    };

    public static implicit operator PhysicsQuestion(RayQuestion ray) => new(ray);
    public static implicit operator PhysicsQuestion(ShapeCastQuestion shapeCast) => new(shapeCast);
    public static implicit operator PhysicsQuestion(OverlapQuestion overlap) => new(overlap);

    public bool TryGetValue(out RayQuestion value)
    {
        value = kind == KindRay ? new RayQuestion(position, direction, length, rayKind) : default;
        return kind == KindRay;
    }

    public bool TryGetValue(out ShapeCastQuestion value)
    {
        value = kind == KindShapeCast
            ? new ShapeCastQuestion(shape, new(position, rotation), direction, length)
            : default;
        return kind == KindShapeCast;
    }

    public bool TryGetValue(out OverlapQuestion value)
    {
        value = kind == KindOverlap ? new OverlapQuestion(shape, new(position, rotation)) : default;
        return kind == KindOverlap;
    }

    // The held case alone decides equality: the fields of the other cases are not compared.
    public bool Equals(PhysicsQuestion other) => kind == other.kind && kind switch
    {
        KindRay => position == other.position && direction == other.direction && length.Equals(other.length) &&
                   rayKind == other.rayKind,
        KindShapeCast => shape == other.shape && position == other.position && rotation == other.rotation
                         && direction == other.direction && length.Equals(other.length),
        KindOverlap => shape == other.shape && position == other.position && rotation == other.rotation,
        _ => true
    };

    public override bool Equals(object? obj) => obj is PhysicsQuestion question && Equals(question);

    public override int GetHashCode() => kind switch
    {
        KindRay => HashCode.Combine(kind, position, direction, length, rayKind),
        KindShapeCast => HashCode.Combine(kind, shape, position, rotation, direction, length),
        KindOverlap => HashCode.Combine(kind, shape, position, rotation),
        _ => 0
    };

    public static bool operator ==(PhysicsQuestion a, PhysicsQuestion b) => a.Equals(b);
    public static bool operator !=(PhysicsQuestion a, PhysicsQuestion b) => !a.Equals(b);

    public override string ToString() => this switch
    {
        RayQuestion ray => $"PhysicsQuestion({ray})",
        ShapeCastQuestion shapeCast => $"PhysicsQuestion({shapeCast})",
        OverlapQuestion overlap => $"PhysicsQuestion({overlap})",
        null => "PhysicsQuestion(none)"
    };
}
