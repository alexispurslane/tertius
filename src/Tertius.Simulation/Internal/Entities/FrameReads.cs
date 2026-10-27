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
    /// The linear velocity in <paramref name="to"/>, in doubles. <paramref name="own"/> is the frame that the entity owns, or
    /// null. A frame owner's is its frame origin's, from the tree alone, whose double and parent are current, while the row
    /// holds a float and the parent of the last mirror.
    /// </summary>
    public static Double3 VelocityIn(FrameTree frames, in TransformRow row, FrameHandle own, FrameHandle to) =>
        own.IsNull
            ? SemanticVelocityIn(
                frames: frames,
                row: row,
                semantic: frames.HomeVelocity(row.PhysicsHome, row.Frame) + row.LinearVelocity,
                to: to
            )
            : frames.ConvertVelocity(from: own, to: to, position: Double3.Zero, v: Double3.Zero);

    /// <summary>The linear velocity in <paramref name="to"/> of an entity that owns no frame and whose velocity relative to
    /// its row frame, in its axes, is <paramref name="semantic"/>.</summary>
    public static Double3 SemanticVelocityIn(FrameTree frames, in TransformRow row, Double3 semantic, FrameHandle to) =>
        row.Frame == to
            ? semantic
            : frames.ConvertVelocity(from: row.Frame, to: to, position: row.Pose.Position, v: semantic);

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
