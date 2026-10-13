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

    /// <summary>The world's camera. Only code between ticks changes it, so it holds for the whole tick.</summary>
    public Entity Camera => world.Camera;

    /// <summary>The frame tree. It does not change during a tick.</summary>
    public FrameTree Frames => world.Frames;

    /// <summary>True if the entity was live at the end of the previous tick.</summary>
    public bool IsAlive(Entity entity) => world.IsAlive(entity);

    /// <summary>The frame the entity is in. Null for the owner of a root frame.</summary>
    public FrameHandle FrameOf(Entity entity) => world.Entities.FrontRow(entity).Frame;

    /// <summary>The entity's render row.</summary>
    public RenderData RenderOf(Entity entity) => world.Entities.FrontRender(entity);

    /// <summary>True if the entity has a physics body.</summary>
    public bool HasBody(Entity entity) => world.Entities.Body(entity) != null;

    /// <summary>
    /// True if physics stood the character on walkable ground under gravity in its last step. False for a character in
    /// the air, on steep ground or weightless, for any other body, and for an entity without a body.
    /// </summary>
    public bool IsStanding(Entity entity) => world.IsStanding(entity);

    /// <summary>The entity's pose in <paramref name="frame"/>, through the frame tree. False if either handle is stale.</summary>
    public bool TryGetPoseIn(Entity entity, FrameHandle frame, out DPose pose)
    {
        if (!Live(entity, frame))
        {
            pose = default;
            return false;
        }
        pose = FrameReads.PoseIn(world.Frames, entity, world.Entities.FrontRow(entity), frame);
        return true;
    }

    /// <summary>
    /// The entity's linear velocity relative to <paramref name="frame"/>, in its axes, through the frame tree in doubles.
    /// False if either handle is stale.
    /// </summary>
    public bool TryGetVelocityIn(Entity entity, FrameHandle frame, out Double3 velocity)
    {
        if (!Live(entity, frame))
        {
            velocity = default;
            return false;
        }
        velocity = FrameReads.VelocityIn(world.Frames, entity, world.Entities.FrontRow(entity), frame);
        return true;
    }

    /// <summary>
    /// The entity's angular velocity relative to <paramref name="frame"/>, in its axes. False if either handle is stale.
    /// </summary>
    public bool TryGetAngularVelocityIn(Entity entity, FrameHandle frame, out Vector3 angularVelocity)
    {
        if (!Live(entity, frame))
        {
            angularVelocity = default;
            return false;
        }
        angularVelocity = FrameReads.AngularVelocityIn(world.Frames, entity, world.Entities.FrontRow(entity), frame);
        return true;
    }

    private bool Live(Entity entity, FrameHandle frame) => world.IsAlive(entity) && world.Frames.IsAlive(frame);

    /// <summary>
    /// Clears <paramref name="results"/>, then adds every live entity in <paramref name="frame"/> whose position is
    /// within <paramref name="radius"/> of <paramref name="center"/>, sorted by entity index. The test is exact; the
    /// grid only narrows the search.
    /// </summary>
    public void QueryRadius(FrameHandle frame, Double3 center, double radius, List<Entity> results) =>
        world.QueryRadius(frame, center, radius, results);
}
