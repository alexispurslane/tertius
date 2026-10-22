using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Reads of an entity in its own frame, the frame that most tests check against. Every read in the engine names a frame;
/// these name the entity's own, so that a test stays short. The owner of a root frame is in no frame, so its reads
/// are in the frame that it owns. A velocity comes back as a float vector, as the test values are.
/// </summary>
public static class OwnFrameReads
{
    public static DPose PoseInOwnFrame(this World world, Entity entity) =>
        world.PoseIn(entity, OwnFrame(world, entity));

    public static Vector3 VelocityInOwnFrame(this World world, Entity entity) =>
        world.VelocityIn(entity, OwnFrame(world, entity)).ToVector3();

    public static Vector3 AngularVelocityInOwnFrame(this World world, Entity entity) =>
        world.AngularVelocityIn(entity, OwnFrame(world, entity));

    public static DPose PoseInOwnFrame(this Snapshot snapshot, Entity entity) =>
        snapshot.TryGetPoseIn(entity, OwnFrame(snapshot, entity), out DPose pose)
            ? pose
            : throw new InvalidOperationException($"{entity} has no pose in its frame.");

    public static Vector3 VelocityInOwnFrame(this Snapshot snapshot, Entity entity) =>
        snapshot.TryGetVelocityIn(entity, OwnFrame(snapshot, entity), out Double3 velocity)
            ? velocity.ToVector3()
            : throw new InvalidOperationException($"{entity} has no velocity in its frame.");

    public static Vector3 AngularVelocityInOwnFrame(this Snapshot snapshot, Entity entity) =>
        snapshot.TryGetAngularVelocityIn(entity, OwnFrame(snapshot, entity), out Vector3 velocity)
            ? velocity
            : throw new InvalidOperationException($"{entity} has no angular velocity in its frame.");

    private static FrameHandle OwnFrame(World world, Entity entity) =>
        world.GetFrame(entity) is { IsNull: false } frame ? frame : world.GetOwnedFrame(entity);

    private static FrameHandle OwnFrame(Snapshot snapshot, Entity entity) =>
        snapshot.FrameOf(entity) is { IsNull: false } frame ? frame :
        snapshot.Frames.TryGetFrame(entity, out FrameHandle own) ? own : FrameHandle.Null;

    public static DPose PoseInOwnFrame(this Self self) => self.PoseIn(self.Frame);

    public static Vector3 VelocityInOwnFrame(this Self self) => self.VelocityIn(self.Frame).ToVector3();
}
