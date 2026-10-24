namespace Tertius;

/// <summary>
/// The recipient of a message: one entity, the subscribers of a topic, or every live entity.
/// The union generator writes its union members (docs/plans/union-tooling.md). The entity's index and generation share
/// their two <c>uint</c> slots with the topic's generation, and the topic's id has an <c>int</c> slot, so a target is 16
/// bytes, as the hand-written union that packed every case into one 64-bit number was.
/// </summary>
[GeneratedUnion(typeof(Entity), typeof(Topic), typeof(Broadcast))]
public readonly partial struct Target
{
    public static implicit operator Target(FrameHandle frame) => new(frame.Entity);
}
