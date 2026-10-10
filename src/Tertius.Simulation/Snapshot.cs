using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// A read-only view of the front snapshot: the state at the end of the previous tick. During a tick, any thread may
/// read it. Lookups with a stale or null handle throw <see cref="StaleEntityException"/>, an
/// <see cref="InvalidOperationException"/>. The <c>Try</c> methods return false instead.
/// </summary>
public readonly struct Snapshot
{
    private readonly World world;

    internal Snapshot(World world) => this.world = world;

    /// <summary>The tick number of the state that this snapshot shows.</summary>
    public long Tick => world.TickNumber;

    /// <summary>The frame tree. It does not change during a tick.</summary>
    public FrameTree Frames => world.Frames;

    /// <summary>True if the entity was live at the end of the previous tick.</summary>
    public bool IsAlive(Entity entity) => world.IsAlive(entity);

    /// <summary>The frame the entity is in. Null for the owner of a root frame.</summary>
    public FrameHandle FrameOf(Entity entity) => world.Entities.FrontRow(entity).Frame;

    /// <summary>The entity's pose in its frame.</summary>
    public DPose PoseOf(Entity entity) => world.Entities.FrontRow(entity).Pose;

    /// <summary>Linear velocity in frame axes.</summary>
    public Vector3 VelocityOf(Entity entity) => world.Entities.FrontRow(entity).LinearVelocity;

    /// <summary>Angular velocity in frame axes.</summary>
    public Vector3 AngularVelocityOf(Entity entity) => world.Entities.FrontRow(entity).AngularVelocity;

    /// <summary>The entity's render row.</summary>
    public RenderData RenderOf(Entity entity) => world.Entities.FrontRender(entity);

    /// <summary>True if the entity has a physics body.</summary>
    public bool HasBody(Entity entity) => world.Entities.Body(entity) != null;

    /// <summary>The entity's frame and pose in it. False for a stale handle.</summary>
    public bool TryGetPose(Entity entity, out FrameHandle frame, out DPose pose)
    {
        if (!world.IsAlive(entity))
        {
            frame = default;
            pose = default;
            return false;
        }
        ref readonly TransformRow row = ref world.Entities.FrontRow(entity);
        frame = row.Frame;
        pose = row.Pose;
        return true;
    }

    /// <summary>The entity's pose in <paramref name="frame"/>, through the frame tree. False if either handle is stale.</summary>
    public bool TryGetPoseIn(Entity entity, FrameHandle frame, out DPose pose)
    {
        pose = default;
        if (!world.IsAlive(entity) || !world.Frames.IsAlive(frame))
            return false;
        ref readonly TransformRow row = ref world.Entities.FrontRow(entity);
        if (row.Frame == frame)
        {
            pose = row.Pose;
            return true;
        }
        if (row.Frame.IsNull)
        {
            // The owner of a root frame: its pose is the frame's own pose.
            if (!world.Frames.TryGetFrame(entity, out FrameHandle own))
                return false;
            pose = world.Frames.Relative(own, frame);
            return true;
        }
        pose = DPose.Compose(world.Frames.Relative(row.Frame, frame), row.Pose);
        return true;
    }

    /// <summary>
    /// Clears <paramref name="results"/>, then adds every live entity in <paramref name="frame"/> whose position is
    /// within <paramref name="radius"/> of <paramref name="center"/>, sorted by entity index. The test is exact; the
    /// grid only narrows the search.
    /// </summary>
    public void QueryRadius(FrameHandle frame, Double3 center, double radius, List<Entity> results) =>
        world.QueryRadius(frame, center, radius, results);
}
