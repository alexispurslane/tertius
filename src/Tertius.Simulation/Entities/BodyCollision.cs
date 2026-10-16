namespace Tertius.Simulation;

/// <summary>
/// A body's collision groups and collision mask, as the snapshot publishes them: the ones that physics used in the step
/// of the previous tick. A <see cref="BodyHandle.SetCollision"/> in tick N reaches physics in the step of tick N + 1, so the
/// snapshot shows it from tick N + 2 on.
/// </summary>
/// <param name="Groups">The collision groups that the body is in.</param>
/// <param name="Mask">The groups that the body collides with. It holds only an any-of set.</param>
public readonly record struct BodyCollision(CollisionGroups Groups, CollisionMask Mask);
