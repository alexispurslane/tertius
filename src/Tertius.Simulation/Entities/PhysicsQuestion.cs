using System.Diagnostics.CodeAnalysis;

namespace Tertius.Simulation;

/// <summary>The case of a <see cref="PhysicsQuestion"/> that asks for a ray cast: <c>CastRay</c> and <c>CastRayAll</c>.</summary>
/// <param name="Origin">Where the ray starts, in the coordinates of the request's frame, in meters. Every coordinate is finite.</param>
/// <param name="Direction">The direction of the ray, in the axes of the request's frame. It is a unit vector.</param>
/// <param name="Length">How far the ray reaches, in meters. It is finite and more than 0.</param>
/// <param name="Kind">Whether the answer is the nearest hit alone or every hit.</param>
[SuppressMessage("Tertius.Unions", "TU0005",
    Justification =
        "The kind says how many hits the answer holds. The origin, the direction and the length mean something for every kind.")]
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
/// This is a generated union (docs/plans/union-tooling.md) of <see cref="RayQuestion"/>, <see cref="ShapeCastQuestion"/>
/// and <see cref="OverlapQuestion"/>. The default value holds no case, and only a bug puts it in a request: the World
/// builds every question from a case after it checked the arguments, and the runner refuses a request whose question is
/// the default. The constructors store what they get. The checks and the unit direction belong to the World
/// (<c>RequestRay</c>, <c>RequestShapeCast</c> and <c>RequestOverlap</c>), because their messages name the arguments of
/// the public calls.
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
[GeneratedUnion(typeof(RayQuestion), typeof(ShapeCastQuestion), typeof(OverlapQuestion))]
internal readonly partial struct PhysicsQuestion
{
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
}
