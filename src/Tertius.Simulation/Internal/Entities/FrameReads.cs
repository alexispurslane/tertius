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
    public static DPose PoseIn(FrameTree frames, Entity entity, in TransformRow row, FrameHandle to) =>
        row.Frame == to
            ? row.Pose
            : row.Frame.IsNull
                ? frames.Relative(OwnFrame(frames, entity), to)
                : DPose.Compose(frames.Relative(row.PhysicsHome, to), row.HomePose);

    public static Double3 VelocityIn(FrameTree frames, Entity entity, in TransformRow row, FrameHandle to)
    {
        if (row.Frame.IsNull)
            return frames.ConvertVelocity(OwnFrame(frames, entity), to, Double3.Zero, Double3.Zero);
        Double3 velocity = frames.HomeVelocity(row.PhysicsHome, row.Frame) + row.LinearVelocity;
        return row.Frame == to ? velocity : frames.ConvertVelocity(row.Frame, to, row.Pose.Position, velocity);
    }

    public static Vector3 AngularVelocityIn(FrameTree frames, Entity entity, in TransformRow row, FrameHandle to)
    {
        if (row.Frame == to)
            return row.AngularVelocity;
        if (row.Frame.IsNull)
            return frames.AngularVelocityRelative(OwnFrame(frames, entity), to).ToVector3();
        DPose relative = frames.Relative(row.Frame, to);
        return (relative.TransformDirection(row.AngularVelocity) + frames.AngularVelocityRelative(row.Frame, to))
            .ToVector3();
    }

    private static FrameHandle OwnFrame(FrameTree frames, Entity entity)
    {
        bool owns = frames.TryGetFrame(entity, out FrameHandle own);
        Contract.Assert(owns);
        return own;
    }
}
