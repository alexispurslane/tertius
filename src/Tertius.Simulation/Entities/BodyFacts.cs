namespace Tertius.Simulation;

/// <summary>
/// The physics facts of an entity's body that any component can read from the snapshot with
/// <see cref="Snapshot.TryGetBody"/>, for example to decide whether it can push, lift or stand on another entity.
/// </summary>
/// <param name="Motion">
/// How the body moves. A body that owns no frame has the motion of its description, and a character has
/// <see cref="BodyMotion.Character"/>. The body of a frame owner is its frame's hull in the parent frame, whose motion
/// follows the frame's driver (see <see cref="BodyDesc.FrameHullMotion"/>). A root frame has no parent and so no hull, so
/// the body of its owner is only the frame's static interior, with <see cref="BodyMotion.Static"/>. A driver changes only
/// between ticks, and physics follows it at the start of its next step, so the motion is the one of the step that runs
/// during this tick.
/// </param>
/// <param name="Mass">The mass of the description, in kg. Only a dynamic body moves by its mass.</param>
/// <param name="ShapeKind">The kind of the body's shape. For a frame owner, that is the shape of its hull.</param>
/// <param name="BoundingRadius">
/// The radius, in m, of the smallest sphere about the entity's origin that holds the body's shape (see
/// <see cref="ShapeDesc.BoundingRadius"/>). For a frame owner, that is the radius of its hull.
/// </param>
/// <param name="CollisionGroups">The collision groups that physics had the body in during the step of the previous tick.</param>
/// <param name="CollisionMask">
/// The collision mask that physics gave the body during the step of the previous tick. It holds only an any-of set. A
/// <see cref="BodyHandle.SetCollision"/> in tick N reaches physics in the step of tick N + 1, so these facts show the new
/// groups and mask from tick N + 2 on.
/// </param>
/// <param name="IsSensor">True for a sensor body, which detects touches but does not push (see <see cref="BodyDesc.IsSensor"/>).</param>
public readonly record struct BodyFacts(
    BodyMotion Motion,
    float Mass,
    ShapeKind ShapeKind,
    float BoundingRadius,
    CollisionGroups CollisionGroups,
    CollisionMask CollisionMask,
    bool IsSensor);
