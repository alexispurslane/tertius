using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// The running entity, as its components see it. The scheduler passes it by value to <see cref="IComponent.Handle"/>.
/// It is valid only during that call. Reads come from the front snapshot of the previous tick. Writes go to this
/// entity's own back rows, or into the chunk's deferred lists, and take effect at the sync point. It holds the entity's
/// handle and goes through the same checked members as everything else; the running entity is live for the whole call,
/// so the checks pass.
/// </summary>
public readonly ref struct Self
{
    private readonly World world;
    private readonly ChunkState chunk;

    internal Self(World world, ChunkState chunk, Entity entity)
    {
        this.world = world;
        this.chunk = chunk;
        Entity = entity;
    }

    /// <summary>The running entity.</summary>
    public Entity Entity { get; }

    /// <summary>The frame the entity is in. Null only for the owner of a root frame.</summary>
    public FrameHandle Frame => world.Entities.FrontRow(Entity).Frame;

    /// <summary>The number of the tick that is running.</summary>
    public long Tick => world.TickNumber;

    /// <summary>The fixed time step, in seconds.</summary>
    public float DeltaTime => world.DeltaTime;

    /// <summary>Simulation time at the start of this tick, in seconds.</summary>
    public double Time => world.Time;

    /// <summary>
    /// This entity's pose in <paramref name="frame"/>, from the front snapshot (the end of the previous tick). Throws
    /// <see cref="ArgumentException"/> for a stale frame.
    /// </summary>
    public DPose PoseIn(FrameHandle frame) => world.PoseIn(Entity, frame);

    /// <summary>This entity's linear velocity relative to <paramref name="frame"/>, in its axes, from the front snapshot.</summary>
    public Double3 VelocityIn(FrameHandle frame) => world.VelocityIn(Entity, frame);

    /// <summary>This entity's angular velocity relative to <paramref name="frame"/>, in its axes, from the front snapshot.</summary>
    public Vector3 AngularVelocityIn(FrameHandle frame) => world.AngularVelocityIn(Entity, frame);

    /// <summary>
    /// Sets this entity's pose in <see cref="Frame"/>. The new pose takes effect at the sync point. Throws
    /// <see cref="InvalidOperationException"/> if physics or a frame driver owns the transform, or if the entity is
    /// attached to a parent.
    /// </summary>
    public void SetTransform(in DPose pose) => world.WritableTransform(Entity) = pose;

    /// <summary>A proxy over this entity's physics intent for this tick. Physics receives it in the next step.</summary>
    public BodyHandle Body => new(world, Entity);

    /// <summary>This entity's physics queries, such as ray casts. Each answer arrives as an event in the next tick.</summary>
    public PhysicsQueries Physics => new(world, chunk, Entity);

    /// <summary>This entity's own render row (back buffer). Changes reach the renderer after the sync point.</summary>
    public ref RenderData Render => ref world.Entities.WritableRender(Entity);

    /// <summary>
    /// The offset from the attachment parent, for writing. The new offset takes effect at the sync point.
    /// Throws <see cref="InvalidOperationException"/> if the entity is not attached.
    /// </summary>
    public ref Pose AttachOffset => ref world.Attachments.WritableOffset(Entity);

    /// <summary>The attachment parent, or null.</summary>
    public Entity AttachedTo => world.Attachments.ParentOf(Entity);

    /// <summary>
    /// A read-only view of the front snapshot, for reading other entities. Its entity queries run around this entity,
    /// for <see cref="EntityQuery.Near(double)"/>.
    /// </summary>
    public Snapshot Snapshot => new(world, Entity);

    /// <summary>Creates an entity. The handle is final and valid at once. The entity becomes live at the sync point and runs from the next tick.</summary>
    public Entity Spawn(SpawnDesc desc) => world.SpawnDuringTick(chunk, Entity, desc);

    /// <summary>Creates an entity from a blueprint. See <see cref="Spawn(SpawnDesc)"/>.</summary>
    public Entity Spawn(string blueprint, FrameHandle frame, in DPose pose) =>
        world.SpawnDuringTick(chunk, Entity, world.Blueprints.CreateSpawnDesc(blueprint, frame, pose));

    /// <summary>Destroys this entity at the sync point, together with its attached children.</summary>
    public void Destroy() => world.RequestDestroy(chunk, Entity);

    /// <summary>
    /// Adds a component. It takes part from the entity's next run. The entity mask updates right after this run, so
    /// the routing at this tick's sync point already uses it.
    /// </summary>
    public void AddComponent(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        chunk.ComponentChanges.Add(new ComponentChange(Entity, component, true));
    }

    /// <summary>Removes a component after this run.</summary>
    public void RemoveComponent(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        chunk.ComponentChanges.Add(new ComponentChange(Entity, component, false));
    }

    /// <summary>The first of this entity's components that is a <typeparamref name="T"/>, or null. Components added during this run are not visible yet.</summary>
    public T? GetComponent<T>() where T : class, IComponent => World.FindComponent<T>(world.Entities.Components(Entity));

    /// <summary>Subscribes to a topic at the sync point. Messages sent to the topic during this tick already reach the entity.</summary>
    public void Subscribe(Topic topic) => chunk.SubscriptionChanges.Add(new SubscriptionChange(Entity, topic, true));

    /// <summary>Unsubscribes from a topic at the sync point. The automatic frame topic cannot be removed.</summary>
    public void Unsubscribe(Topic topic) => chunk.SubscriptionChanges.Add(new SubscriptionChange(Entity, topic, false));

    /// <summary>Writes a message to <see cref="World.Log"/> at the sync point.</summary>
    public void Log(string message) => chunk.Messages.Add(new ChunkMessage($"tick {world.TickNumber}: {Entity}: {message}", false));
}

/// <summary>
/// A proxy over the running entity's physics intent. All vectors are in frame axes. Forces, torques and impulses add
/// up during the tick. Velocity changes, kinematic targets, teleports, character moves and jumps keep the last value
/// written. Physics applies the intent in the next step. Every method throws <see cref="InvalidOperationException"/>
/// when the entity has no body. The properties work for every entity: <see cref="Pending"/> is the empty intent for an
/// entity without a body.
/// </summary>
public readonly ref struct BodyHandle
{
    private readonly World world;
    private readonly Entity entity;

    internal BodyHandle(World world, Entity entity)
    {
        this.world = world;
        this.entity = entity;
    }

    /// <summary>True if the entity has a physics body.</summary>
    public bool Exists => world.Entities.Body(entity) != null;

    /// <summary>
    /// True if physics stood this character on walkable ground under gravity in its last step: the step that ran during
    /// the previous tick. False for a character in the air, on steep ground or weightless, for any other body, and for
    /// an entity without a body.
    /// </summary>
    public bool Standing => world.IsStanding(entity);

    /// <summary>The intent written so far during this tick.</summary>
    public PhysicsIntent Pending => world.Entities.PendingIntent(entity);

    /// <summary>Adds a force for the next step, in newtons.</summary>
    public void AddForce(Vector3 force) => Intent.Force += force;

    /// <summary>Adds a torque for the next step, in newton-meters.</summary>
    public void AddTorque(Vector3 torque) => Intent.Torque += torque;

    /// <summary>Adds a linear impulse, in newton-seconds.</summary>
    public void AddImpulse(Vector3 impulse) => Intent.Impulse += impulse;

    /// <summary>Adds an angular impulse.</summary>
    public void AddAngularImpulse(Vector3 impulse) => Intent.AngularImpulse += impulse;

    /// <summary>
    /// Sets the linear velocity before the next step, relative to the entity's frame, in its axes. Physics converts it
    /// when it applies it.
    /// </summary>
    public void SetVelocity(Double3 velocity)
    {
        ref PhysicsIntent intent = ref Intent;
        intent.Velocity = velocity;
        intent.Flags |= IntentFlags.SetVelocity;
    }

    /// <summary>Sets the angular velocity before the next step.</summary>
    public void SetAngularVelocity(Vector3 velocity)
    {
        ref PhysicsIntent intent = ref Intent;
        intent.AngularVelocity = velocity;
        intent.Flags |= IntentFlags.SetAngularVelocity;
    }

    /// <summary>Moves a kinematic body to <paramref name="target"/> over the next step.</summary>
    public void MoveKinematic(in DPose target)
    {
        ref PhysicsIntent intent = ref Intent;
        intent.Target = target;
        intent.Flags |= IntentFlags.KinematicTarget;
    }

    /// <summary>
    /// Puts the body at <paramref name="pose"/> before the next step, without sweeping. It is separate from
    /// <see cref="MoveKinematic"/>, so a body can teleport and then move kinematically in the same step.
    /// </summary>
    public void Teleport(in DPose pose)
    {
        ref PhysicsIntent intent = ref Intent;
        intent.TeleportPose = pose;
        intent.Flags |= IntentFlags.Teleport;
    }

    /// <summary>
    /// Sets the desired movement velocity of a character body, in frame axes. It moves the character only while physics
    /// stands it on walkable ground under gravity (see <see cref="Standing"/>), and then only the part across the
    /// character's up counts. It does not move a character in the air, on steep ground or weightless, but physics keeps
    /// it, and it applies once the character stands.
    /// </summary>
    public void MoveCharacter(Vector3 desiredVelocity)
    {
        ref PhysicsIntent intent = ref Intent;
        intent.CharacterVelocity = desiredVelocity;
        intent.Flags |= IntentFlags.CharacterMove;
    }

    /// <summary>
    /// Makes a character jump with this upward speed in the next step, if physics stands it on walkable ground under
    /// gravity then (see <see cref="Standing"/>). Otherwise the jump does nothing.
    /// </summary>
    public void Jump(float speed)
    {
        ref PhysicsIntent intent = ref Intent;
        intent.JumpSpeed = speed;
        intent.Flags |= IntentFlags.Jump;
    }

    /// <summary>
    /// Puts the body in the collision groups <paramref name="groups"/> with the collision mask <paramref name="mask"/>,
    /// from the next step on. Two bodies collide only when each body's mask matches the other body's groups. Every Jolt
    /// body of the entity takes them, except the hull of a frame that it owns: its own body, a character's movement, the
    /// frame's interior and its ghosts. Throws <see cref="ArgumentException"/> when <paramref name="mask"/> has an all-of
    /// or a none-of part, which a body's mask cannot have (see <see cref="BodyDesc.CollisionMask"/>).
    /// </summary>
    public void SetCollision(CollisionGroups groups, CollisionMask mask)
    {
        if (mask.BodyProblem() is { } problem)
            throw new ArgumentException($"{entity} cannot take the collision mask {mask}: it {problem}.", nameof(mask));
        ref PhysicsIntent intent = ref Intent;
        intent.CollisionGroups = groups;
        intent.CollisionMask = mask;
        intent.Flags |= IntentFlags.SetCollision;
    }

    private ref PhysicsIntent Intent => ref world.WritableIntent(entity);
}
