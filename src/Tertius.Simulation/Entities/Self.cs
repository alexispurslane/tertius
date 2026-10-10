using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// The running entity, as its components see it. The scheduler passes it by value to <see cref="IComponent.Handle"/>.
/// It is valid only during that call. Reads come from the front snapshot of the previous tick. Writes go to this
/// entity's own back rows, or into the chunk's deferred lists, and take effect at the sync point.
/// </summary>
public readonly ref struct Self
{
    private readonly World world;
    private readonly ChunkState chunk;
    private readonly int slot;

    internal Self(World world, ChunkState chunk, int slot, Entity entity)
    {
        this.world = world;
        this.chunk = chunk;
        this.slot = slot;
        Entity = entity;
    }

    /// <summary>The running entity.</summary>
    public Entity Entity { get; }

    /// <summary>The frame the entity is in. Null only for the owner of a root frame.</summary>
    public FrameHandle Frame => world.FrontRow(slot).Frame;

    /// <summary>The number of the tick that is running.</summary>
    public long Tick => world.TickNumber;

    /// <summary>The fixed time step, in seconds.</summary>
    public float DeltaTime => world.DeltaTime;

    /// <summary>Simulation time at the start of this tick, in seconds.</summary>
    public double Time => world.Time;

    /// <summary>This entity's pose in <see cref="Frame"/>, from the front snapshot (the end of the previous tick).</summary>
    public DPose Pose => world.FrontRow(slot).Pose;

    /// <summary>
    /// This entity's back transform row, for writing. The new pose takes effect at the sync point. Throws
    /// <see cref="InvalidOperationException"/> if physics or a frame driver owns the transform, or if the entity is
    /// attached to a parent.
    /// </summary>
    public ref DPose Transform => ref world.WritableTransform(chunk, slot);

    /// <summary>Linear velocity in frame axes, from the front snapshot.</summary>
    public Vector3 Velocity => world.FrontRow(slot).LinearVelocity;

    /// <summary>Angular velocity in frame axes, from the front snapshot.</summary>
    public Vector3 AngularVelocity => world.FrontRow(slot).AngularVelocity;

    /// <summary>A proxy over this entity's physics intent for this tick. Physics receives it in the next step.</summary>
    public BodyHandle Body => new(world, chunk, slot);

    /// <summary>This entity's own render row (back buffer). Changes reach the renderer after the sync point.</summary>
    public ref RenderData Render => ref world.WritableRender(chunk, slot);

    /// <summary>
    /// The offset from the attachment parent, for writing. The new offset takes effect at the sync point.
    /// Throws <see cref="InvalidOperationException"/> if the entity is not attached.
    /// </summary>
    public ref Pose AttachOffset => ref world.WritableAttachOffset(slot);

    /// <summary>The attachment parent, or null.</summary>
    public Entity AttachedTo => world.AttachParentOf(slot);

    /// <summary>A read-only view of the front snapshot, for reading other entities.</summary>
    public Snapshot Snapshot => new(world);

    /// <summary>Creates an entity. The handle is final and valid at once. The entity becomes live at the sync point and runs from the next tick.</summary>
    public Entity Spawn(SpawnDesc desc) => world.SpawnDuringTick(chunk, desc);

    /// <summary>Creates an entity from a blueprint. See <see cref="Spawn(SpawnDesc)"/>.</summary>
    public Entity Spawn(string blueprint, FrameHandle frame, in DPose pose) =>
        world.SpawnDuringTick(chunk, world.Blueprints.CreateSpawnDesc(blueprint, frame, pose));

    /// <summary>Destroys this entity at the sync point, together with its attached children.</summary>
    public void Destroy() => world.RequestDestroy(chunk, slot);

    /// <summary>
    /// Adds a component. It takes part from the entity's next run. The entity mask updates right after this run, so
    /// the routing at this tick's sync point already uses it.
    /// </summary>
    public void AddComponent(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        chunk.ComponentChanges.Add(new ComponentChange((uint)slot, component, true));
    }

    /// <summary>Removes a component after this run.</summary>
    public void RemoveComponent(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        chunk.ComponentChanges.Add(new ComponentChange((uint)slot, component, false));
    }

    /// <summary>The first of this entity's components that is a <typeparamref name="T"/>, or null. Components added during this run are not visible yet.</summary>
    public T? GetComponent<T>() where T : class, IComponent => World.FindComponent<T>(world.ComponentsOf(slot));

    /// <summary>Subscribes to a topic at the sync point. Messages sent to the topic during this tick already reach the entity.</summary>
    public void Subscribe(Topic topic) => chunk.SubscriptionChanges.Add(new SubscriptionChange((uint)slot, topic, true));

    /// <summary>Unsubscribes from a topic at the sync point. The automatic frame topic cannot be removed.</summary>
    public void Unsubscribe(Topic topic) => chunk.SubscriptionChanges.Add(new SubscriptionChange((uint)slot, topic, false));

    /// <summary>Writes a message to <see cref="World.Log"/> at the sync point.</summary>
    public void Log(string message) => chunk.Messages.Add(new ChunkMessage($"tick {world.TickNumber}: {Entity}: {message}", false));
}

/// <summary>
/// A proxy over the running entity's physics intent. All vectors are in frame axes. Forces, torques and impulses add
/// up during the tick. Velocity changes, kinematic targets, teleports, character moves and jumps keep the last value
/// written. Physics applies the intent in the next step. Every method throws <see cref="InvalidOperationException"/>
/// when the entity has no body.
/// </summary>
public readonly ref struct BodyHandle
{
    private readonly World world;
    private readonly ChunkState chunk;
    private readonly int slot;

    internal BodyHandle(World world, ChunkState chunk, int slot)
    {
        this.world = world;
        this.chunk = chunk;
        this.slot = slot;
    }

    /// <summary>True if the entity has a physics body.</summary>
    public bool Exists => world.HasBody(slot);

    /// <summary>The intent written so far during this tick.</summary>
    public PhysicsIntent Pending => world.PendingIntent(slot);

    /// <summary>Adds a force for the next step, in newtons.</summary>
    public void AddForce(Vector3 force) => Intent.Force += force;

    /// <summary>Adds a torque for the next step, in newton-meters.</summary>
    public void AddTorque(Vector3 torque) => Intent.Torque += torque;

    /// <summary>Adds a linear impulse, in newton-seconds.</summary>
    public void AddImpulse(Vector3 impulse) => Intent.Impulse += impulse;

    /// <summary>Adds an angular impulse.</summary>
    public void AddAngularImpulse(Vector3 impulse) => Intent.AngularImpulse += impulse;

    /// <summary>Sets the linear velocity before the next step.</summary>
    public void SetVelocity(Vector3 velocity)
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

    /// <summary>Puts the body at <paramref name="pose"/> before the next step, without sweeping.</summary>
    public void Teleport(in DPose pose)
    {
        ref PhysicsIntent intent = ref Intent;
        intent.Target = pose;
        intent.Flags |= IntentFlags.Teleport;
    }

    /// <summary>Sets the desired movement velocity of a character body.</summary>
    public void MoveCharacter(Vector3 desiredVelocity)
    {
        ref PhysicsIntent intent = ref Intent;
        intent.CharacterVelocity = desiredVelocity;
        intent.Flags |= IntentFlags.CharacterMove;
    }

    /// <summary>Makes a character jump with this upward speed, if it stands on something.</summary>
    public void Jump(float speed)
    {
        ref PhysicsIntent intent = ref Intent;
        intent.JumpSpeed = speed;
        intent.Flags |= IntentFlags.Jump;
    }

    private ref PhysicsIntent Intent => ref world.WritableIntent(chunk, slot);
}
