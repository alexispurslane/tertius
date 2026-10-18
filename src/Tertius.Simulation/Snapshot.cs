using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// A read-only view of the front snapshot: the state at the end of the previous tick. During a tick, any thread may
/// read it. Lookups with a stale or null handle throw <see cref="StaleEntityException"/>, an
/// <see cref="InvalidOperationException"/>. The <c>Try</c> methods and <see cref="HasComponent{T}"/> return false
/// instead.
/// </summary>
/// <remarks>
/// The snapshot of <see cref="Self.Snapshot"/> knows the entity that reads it, the asker, which entity queries with
/// <see cref="EntityQuery.Near(double)"/> run around. The snapshot of <see cref="World.Snapshot"/> has none.
/// </remarks>
public readonly struct Snapshot
{
    private readonly World world;
    private readonly Entity asker;

    internal Snapshot(World world) => this.world = world;

    internal Snapshot(World world, Entity asker)
    {
        this.world = world;
        this.asker = asker;
    }

    internal World World => world;

    /// <summary>The entity that reads this snapshot, or null for the snapshot of the world.</summary>
    internal Entity Asker => asker;

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
    /// True if the entity had a <typeparamref name="T"/> component at the end of the previous tick. False for an entity
    /// that is not live: a dead entity has no components.
    /// </summary>
    public bool HasComponent<T>(Entity entity) where T : IComponent =>
        world.Entities.IsAlive(entity) && world.Entities.FrontComponents(entity).Contains(ComponentType<T>.Id);

    /// <summary>
    /// The physics facts of the entity's body: its motion, mass, shape kind, bounding radius, collision groups and mask,
    /// and whether it is a sensor (see <see cref="BodyFacts"/>). False, with the default facts, for an entity that was not
    /// live at the end of the previous tick and for an entity without a body. It allocates nothing.
    /// </summary>
    public bool TryGetBody(Entity entity, out BodyFacts facts)
    {
        EntityTable entities = world.Entities;
        if (!entities.IsAlive(entity) || entities.Body(entity) is not { } body)
        {
            facts = default;
            return false;
        }
        ref readonly BodyCollision collision = ref entities.FrontCollision(entity);
        facts = new BodyFacts(MotionOf(body, entities.OwnedFrame(entity)), body.Mass, body.Shape.Kind, entities.BodyBoundingRadius(entity),
            collision.Groups, collision.Mask, body.IsSensor);
        return true;
    }

    /// <summary>
    /// The motion of a body with the description <paramref name="body"/> whose entity owns <paramref name="ownedFrame"/>,
    /// or no frame when it is null, in the last finished step: the stored motion of the frame's hull, or static for the owner
    /// of a root frame, whose body is only the frame's static interior. The frame tree does not change during a tick, so any
    /// worker may read it.
    /// </summary>
    private BodyMotion MotionOf(BodyDesc body, FrameHandle ownedFrame) =>
        ownedFrame.IsNull ? body.Motion : world.Frames.HullMotion(ownedFrame) ?? BodyMotion.Static;

    /// <summary>
    /// Binds a stored query to this snapshot. An ending such as <c>foreach</c> or <see cref="FoundEntities.Count"/> runs
    /// it, and yields the matching entities in entity index order.
    /// </summary>
    public FoundEntities Find(in EntityQuery query) => new(this, query, default);

    /// <summary>
    /// True if <paramref name="query"/> matches <paramref name="entity"/>: the entity was live at the end of the previous
    /// tick and passes every filter of the query, as a run of <see cref="Find"/> would find it. False for an entity that
    /// was not live. It allocates nothing.
    /// </summary>
    public bool Matches(in EntityQuery query, Entity entity) => world.Entities.IsAlive(entity) && query.Matches(this, entity, default);

    /// <summary>
    /// Every live entity, the start of the shorthand chain, such as <c>Entities.With&lt;Flammable&gt;().Near(5)</c>. Its words
    /// build the same query as <see cref="EntityQuery"/>'s and run it, and it allocates nothing.
    /// </summary>
    public FoundEntities Entities => new(this, default, default);

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
