using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// An entity's pose and velocities in a frame that the reader names, computed from its transform row through the frame
/// tree in doubles. The callers check both handles first: the entity is live and <c>to</c> is a live frame. The owner of
/// a root frame has no row frame, so its reads go through the frame that it owns, like a point at that frame's origin.
/// </summary>
internal static class FrameReads
{
    /// <summary>
    /// The pose in <paramref name="to"/>. A read in another frame starts from the physics home, through the lowest common
    /// ancestor of the home and <paramref name="to"/>, so a read between nearby entities far from their row frame's origin
    /// stays exact (see "Nesting for vast distances" in docs/plans/numeric-frames.md).
    /// </summary>
    public static DPose PoseIn(FrameTree frames, Entity entity, in TransformRow row, FrameHandle to)
    {
        if (row.Frame == to)
            return row.Pose;
        if (row.Frame.IsNull)
            return frames.Relative(OwnFrame(frames, entity), to);
        return DPose.Compose(frames.Relative(row.PhysicsHome, to), row.HomePose);
    }

    /// <summary>
    /// The linear velocity in <paramref name="to"/>, from <see cref="SemanticVelocity"/>. <paramref name="own"/> is the frame
    /// that the entity owns, or null.
    /// </summary>
    public static Double3 VelocityIn(FrameTree frames, in TransformRow row, FrameHandle own, FrameHandle to) =>
        row.Frame.IsNull
            ? RootOwnerVelocityIn(frames: frames, own: own, to: to)
            : SemanticVelocityIn(frames: frames, row: row, semantic: SemanticVelocity(frames, row, own), to: to);

    /// <summary>
    /// The linear velocity relative to the row frame, in its axes, in doubles. A frame owner's comes from its frame, which the
    /// tree holds, because its row narrows it to a float, which at orbital speed steps by 0.5 mm/s.
    /// </summary>
    public static Double3 SemanticVelocity(FrameTree frames, in TransformRow row, FrameHandle own) =>
        frames.HomeVelocity(row.PhysicsHome, row.Frame)
        + (own.IsNull ? row.LinearVelocity : frames.TreeLinearVelocity(own));

    /// <summary>The linear velocity in <paramref name="to"/> of an entity with a row frame whose velocity relative to that
    /// frame is <paramref name="semantic"/>.</summary>
    public static Double3 SemanticVelocityIn(FrameTree frames, in TransformRow row, Double3 semantic, FrameHandle to) =>
        row.Frame == to
            ? semantic
            : frames.ConvertVelocity(from: row.Frame, to: to, position: row.Pose.Position, v: semantic);

    /// <summary>The linear velocity in <paramref name="to"/> of the owner of the root frame <paramref name="own"/>, a point at
    /// that frame's origin.</summary>
    public static Double3 RootOwnerVelocityIn(FrameTree frames, FrameHandle own, FrameHandle to) =>
        frames.ConvertVelocity(from: own, to: to, position: Double3.Zero, v: Double3.Zero);

    public static Vector3 AngularVelocityIn(FrameTree frames, Entity entity, in TransformRow row, FrameHandle to)
    {
        if (row.Frame == to)
            return row.AngularVelocity;
        if (row.Frame.IsNull)
            return frames.AngularVelocityRelative(OwnFrame(frames, entity), to).ToVector3();
        DPose relative = frames.Relative(row.Frame, to);
        return (
            relative.TransformDirection(row.AngularVelocity) + frames.AngularVelocityRelative(row.Frame, to)
        ).ToVector3();
    }

    private static FrameHandle OwnFrame(FrameTree frames, Entity entity)
    {
        FrameHandle? own = frames.OwnedFrame(entity);
        Contract.Assert(own is not null);
        return own.Value;
    }
}
