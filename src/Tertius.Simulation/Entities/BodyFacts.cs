using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// The physics facts of an entity's body that any component can read from the snapshot with
/// <see cref="Snapshot.BodyOf"/>, for example to decide whether it can push, lift or stand on another entity.
/// </summary>
/// <param name="Motion">
/// How the body moved in the last finished step, the step that ran during the previous tick. A body that owns no frame has
/// the motion of its description, and a character has <see cref="BodyMotion.Character"/>. The body of a frame owner is its
/// frame's hull in the parent frame, with the stored motion that the frame tree keeps for it (see
/// <see cref="FrameTree.HullMotion"/>): at spawn, the motion of its description, and after a change of motion (see
/// <see cref="BodyHandle.SetMotion"/>), the new motion from the sync point after the step that applies it. So a
/// <c>SetMotion</c> in tick N shows here from tick N + 2 on, like a <see cref="BodyHandle.SetCollision"/>. A root frame has
/// no parent and so no hull, so the body of its owner is only the frame's static interior, with
/// <see cref="BodyMotion.Static"/>. The entity itself reads the motion of the step that applies its writes with
/// <see cref="BodyHandle.NextStepMotion"/>.
/// </param>
/// <param name="Mass">The mass of the description, in kg. Only a dynamic body moves by its mass.</param>
/// <param name="ShapeKind">The kind of the body's shape. For a frame owner, that is the shape of its hull.</param>
/// <param name="BoundingRadius">
/// The radius, in m, of the smallest sphere about the entity's origin that holds the body's shape (see
/// <see cref="ShapeDesc.BoundingRadius"/>). For a frame owner, that is the radius of its hull.
/// </param>
/// <param name="CenterOfMass">
/// The center of mass of the body's shape, in m, in the body's own axes, relative to the entity's origin, as physics
/// computes it for the shape (see <see cref="IPhysicsBackend.ShapeCenterOfMass"/>). The body's own forces act there, and
/// the lever of a part's push is measured from it (see <see cref="BodyHandle"/>). For a frame owner, that is the center of
/// mass of its hull. Zero in a World without a physics backend.
/// </param>
/// <param name="CollisionGroups">The collision groups that physics had the body in during the step of the previous tick.</param>
/// <param name="CollisionMask">
/// The collision mask that physics gave the body during the step of the previous tick. It holds only an any-of set. A
/// <see cref="BodyHandle.SetCollision"/> in tick N reaches physics in the step of tick N + 1, so these facts show the new
/// groups and mask from tick N + 2 on.
/// </param>
/// <param name="IsSensor">True for a sensor body, which detects touches but does not push (see
/// <see cref="BodyDesc.IsSensor"/>).</param>
public readonly record struct BodyFacts(
    BodyMotion Motion,
    float Mass,
    ShapeKind ShapeKind,
    float BoundingRadius,
    Vector3 CenterOfMass,
    CollisionGroups CollisionGroups,
    CollisionMask CollisionMask,
    bool IsSensor
);
