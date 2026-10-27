using System.Numerics;
using Tertius.Frames.Rails;

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
    public double DeltaTime => world.DeltaTime;

    /// <summary>Simulation time at the start of this tick, in seconds.</summary>
    public double Time => world.Time;

    /// <summary>
    /// The length of <paramref name="ticks"/> ticks, in seconds, as the world clock divides it (see
    /// <see cref="World.Seconds"/>). A component that counts ticks compares this with a time in seconds.
    /// </summary>
    public double Seconds(long ticks) => world.Seconds(ticks);

    /// <summary>The frame that this entity owns, or null when it owns none.</summary>
    public FrameHandle OwnedFrame => world.Entities.OwnedFrame(Entity);

    /// <summary>A proxy over this entity's physics intent for this tick. Physics receives it in the next step.</summary>
    public BodyHandle Body => new(world, Entity);

    /// <summary>This entity's physics queries, such as ray casts. Each answer arrives as an event in the next tick.</summary>
    public PhysicsQueries Physics => new(world, chunk, Entity);

    /// <summary>This entity's own render row (back buffer). Changes reach the renderer after the sync point.</summary>
    public ref RenderData Render => ref world.Entities.WritableRender(Entity);

    /// <summary>
    /// This entity's own rig pose (back copy), for writing. Changes reach the renderer after the sync point, with the render
    /// row. Throws <see cref="InvalidOperationException"/> if the entity spawned without a rig pose.
    /// </summary>
    public RigPoseWriter RigPose => world.Entities.WritableRigPose(Entity);

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

    /// <summary>
    /// The simulation time at the start of tick <paramref name="tick"/>, in seconds: the same double that
    /// <see cref="World.Time"/> holds during that tick. Use it for a time in another tick, because a sum such as
    /// <c>Time + 2 * DeltaTime</c> can round to another double. The intent that this tick writes acts in the step that ends
    /// at <c>TimeAt(Tick + 2)</c>.
    /// </summary>
    public double TimeAt(long tick) => world.TimeAt(tick);

    /// <summary>
    /// This entity's pose in <paramref name="frame"/>, from the front snapshot (the end of the previous tick). Throws
    /// <see cref="ArgumentException"/> for a stale frame.
    /// </summary>
    public DPose PoseIn(FrameHandle frame) => world.PoseIn(Entity, frame);

    /// <summary>
    /// The linear velocity of this entity's origin, the point that its pose names, relative to <paramref name="frame"/>, in
    /// its axes, from the front snapshot.
    /// </summary>
    public Double3 VelocityIn(FrameHandle frame) => world.VelocityIn(Entity, frame);

    /// <summary>This entity's angular velocity relative to <paramref name="frame"/>, in its axes, from the front snapshot.</summary>
    public Vector3 AngularVelocityIn(FrameHandle frame) => world.AngularVelocityIn(Entity, frame);

    /// <summary>
    /// Sets this entity's pose in <see cref="Frame"/>. The new pose takes effect at the sync point, and leaves the entity at
    /// rest: its velocities become zero. The write wins over the motion intent of the step that runs beside this tick, and the
    /// next motion intent moves the entity on from the new pose. Throws <see cref="InvalidOperationException"/> if the entity
    /// has a body, which physics moves, or if it is attached to a parent. The owner of a frame without a body can write it:
    /// the frame takes the pose at the sync point of the tick, and a frame on rails goes back on its rails with its next
    /// sample.
    /// </summary>
    public void SetTransform(in DPose pose) => world.WritableTransform(Entity) = pose;

    /// <summary>
    /// This entity's pose in <paramref name="frame"/> where it starts the next physics step, the step in which this tick's
    /// intent acts: where the motion intent of the step that runs beside this tick ends, an end pose or a full sample, which
    /// puts the entity there exactly; else where that step's teleport puts it; else its pose now. A kinematic body without a
    /// motion intent, hull or plain, holds still in its semantic frame in that step, whatever numeric frame holds it, so
    /// the read gives its pose now. Physics moves a dynamic body or a character, which the read does not predict: for one,
    /// it gives the pose now too. A steer target gives no exact end, so the read skips it. The null frame is the root's own space, in which
    /// the owner of a root frame writes its motion intents. A rails component reads it to start its motion where the motion before it, such as the last sample
    /// of a component that it replaced, leaves the entity (see <see cref="SpinMotion"/>). Throws
    /// <see cref="ArgumentException"/> for a stale frame.
    /// </summary>
    public DPose PoseAtNextStepStartIn(FrameHandle frame) => world.PoseAtNextStepStartIn(Entity, frame);

    /// <summary>
    /// Writes <paramref name="sample"/>, the full motion of this entity over the next physics step, as the motion intent of
    /// that step (see <see cref="MotionSample"/>): the step in which this tick's intents act, which ends at
    /// <c>TimeAt(Tick + 2)</c>. The sample is relative to <see cref="Frame"/>, in its axes. Physics or the sync point puts the
    /// entity exactly on it, and the entity holds still in a step without a motion intent. The frame tree derives the
    /// proper acceleration of a frame that the entity owns from it (see <see cref="FrameTree.ProperAcceleration"/>). The last
    /// motion intent of a tick wins, whatever its case. Throws <see cref="ArgumentException"/> for a sample with a field that
    /// is not a finite number or with a zero rotation, and makes any other rotation unit length. Throws
    /// <see cref="InvalidOperationException"/> with the reason of <see cref="MotionIntentProblem"/> when the entity refuses a
    /// full sample: a kinematic body, an entity without a body and the owner of a root frame take one.
    /// </summary>
    public void MoveOnRails(in MotionSample sample) => world.MoveOnRails(Entity, sample);

    /// <summary>
    /// Writes the end pose <paramref name="endPose"/>, relative to <see cref="Frame"/>, as the motion intent of the next step
    /// (see <see cref="EndPose"/>): the entity moves there over the step at a constant velocity, physics or the sync point
    /// puts it exactly there, and it holds still in the step after its last motion intent. The last motion intent of a tick
    /// wins, whatever its case. Throws <see cref="ArgumentException"/> for a pose that is not finite or has a zero rotation,
    /// and <see cref="InvalidOperationException"/> with the reason of <see cref="MotionIntentProblem"/> when the entity
    /// refuses an end pose, as <see cref="MoveOnRails"/> does.
    /// </summary>
    public void MoveKinematic(in DPose endPose) =>
        world.WriteMotionIntent(Entity, nameof(MoveKinematic), new(new EndPose(endPose)));

    /// <summary>
    /// The reason that this entity refuses the case of <paramref name="intent"/> in the next step, or null when it takes it
    /// (see <see cref="World.MotionIntentProblem"/>). The writers of a motion intent throw with that reason, so a rails
    /// component reads it before it writes.
    /// </summary>
    public string? MotionIntentProblem(in MotionIntent intent) => world.MotionIntentProblem(Entity, intent);

    /// <summary>
    /// True when this entity takes the case of <paramref name="intent"/> in the next step (see
    /// <see cref="World.TakesMotionIntent"/>): <see cref="MotionIntentProblem"/> gives no reason. It builds no reason, so it
    /// allocates nothing, and a rails component asks it in every tick and reads the reason only for its one log line.
    /// </summary>
    public bool TakesMotionIntent(in MotionIntent intent) => world.TakesMotionIntent(Entity, intent);

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
        chunk.ComponentChanges.Add(new(Entity, component, true));
    }

    /// <summary>Removes a component after this run.</summary>
    public void RemoveComponent(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        chunk.ComponentChanges.Add(new(Entity, component, false));
    }

    /// <summary>The first of this entity's components that is a <typeparamref name="T"/>, or null. Components added during this run are not visible yet.</summary>
    public T? GetComponent<T>()
        where T : class, IComponent => World.FindComponent<T>(world.Entities.Components(Entity));

    /// <summary>Subscribes to a topic at the sync point. Messages sent to the topic during this tick already reach the entity.</summary>
    public void Subscribe(Topic topic) => chunk.SubscriptionChanges.Add(new(Entity, topic, true));

    /// <summary>Unsubscribes from a topic at the sync point. The automatic frame topic cannot be removed.</summary>
    public void Unsubscribe(Topic topic) => chunk.SubscriptionChanges.Add(new(Entity, topic, false));

    /// <summary>Writes a message to <see cref="World.Log"/> at the sync point.</summary>
    public void Log(string message) => chunk.Messages.Add(new($"tick {world.TickNumber}: {Entity}: {message}", false));
}

/// <summary>
/// A proxy over the running entity's physics intent. All vectors are in frame axes. Forces, torques and impulses add
/// up during the tick. Velocity changes, the motion intent (an end pose or a steer target), teleports, character moves
/// and jumps keep the last value written. Physics applies the intent in the next step. Every method throws
/// <see cref="InvalidOperationException"/>
/// when the entity has no body, except the four that push: <see cref="AddForce"/>, <see cref="AddTorque"/>,
/// <see cref="AddImpulse"/> and <see cref="AddAngularImpulse"/>. Those also work on a part, an attached entity whose chain
/// root has a body (docs/plans/parts.md): the part writes its own push, and the sync point after the tick carries it to
/// the root's body, where a force and an impulse act at the part's place, with the torque of their lever about the body's
/// center of mass (see <see cref="BodyFacts.CenterOfMass"/>). The root's components do not see a part's push, as they do
/// not see a contact. The properties work for every entity: <see cref="Pending"/> is the empty intent for an entity
/// without a body that does not push, and a part's own push for a part.
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

    /// <summary>
    /// The motion that the body has in the next step, the step that applies this tick's writes: null for an entity without
    /// a body, the motion of the description for a body that owns no frame, and static for the owner of a root frame, whose
    /// body is only the frame's static interior. For a hull it counts the change of motion that the entity wrote so far in
    /// this tick (see <see cref="SetMotion"/>), and the one of the tick before, which the running step applies. The body facts
    /// of the snapshot describe the last finished step, so they show a change only from the tick after the step that applies
    /// it (see <see cref="BodyFacts.Motion"/>). A rails component asks it on its first run, to teleport a kinematic body onto
    /// its rails.
    /// </summary>
    public BodyMotion? NextStepMotion => world.NextStepMotion(entity);

    /// <summary>The intent written so far during this tick.</summary>
    public PhysicsIntent Pending => world.Entities.PendingIntent(entity);

    private ref PhysicsIntent Intent => ref world.WritableIntent(entity);

    private ref PhysicsIntent PushIntent => ref world.WritablePush(entity);

    /// <summary>
    /// Adds a force for the next step, in newtons. On a part, the force acts on the body of the part's chain root at the
    /// part's place (see <see cref="BodyHandle"/>).
    /// </summary>
    public void AddForce(Vector3 force) => PushIntent.Force += force;

    /// <summary>
    /// Adds a torque for the next step, in newton-meters. On a part, the torque acts on the body of the part's chain root.
    /// </summary>
    public void AddTorque(Vector3 torque) => PushIntent.Torque += torque;

    /// <summary>
    /// Adds a linear impulse, in newton-seconds. On a part, the impulse acts on the body of the part's chain root at the
    /// part's place.
    /// </summary>
    public void AddImpulse(Vector3 impulse) => PushIntent.Impulse += impulse;

    /// <summary>
    /// Adds an angular impulse, in newton-meter-seconds. On a part, it acts on the body of the part's chain root.
    /// </summary>
    public void AddAngularImpulse(Vector3 impulse) => PushIntent.AngularImpulse += impulse;

    /// <summary>
    /// Sets the linear velocity of the entity's origin, the point that its pose names, before the next step, relative to the
    /// entity's frame, in its axes. Physics converts it when it applies it. When the body spins and its center of mass lies
    /// off its origin, the center of mass moves at this velocity plus the angular velocity × that offset. So a zero velocity
    /// stops the origin, and the center of mass then drifts at the angular velocity × the offset. Throws
    /// <see cref="InvalidOperationException"/> for a body that is kinematic in the next step, hull or plain, which moves
    /// only by its motion intents and teleports (see <see cref="Self.MoveKinematic"/>).
    /// </summary>
    public void SetVelocity(Double3 velocity)
    {
        ref PhysicsIntent intent = ref world.WritableMotionIntent(entity, nameof(SetVelocity));
        intent.Velocity = velocity;
        intent.Flags |= IntentFlags.SetVelocity;
    }

    /// <summary>
    /// Sets the angular velocity before the next step. Throws <see cref="InvalidOperationException"/> for a body that is
    /// kinematic in the next step, hull or plain, which moves only by its motion intents and teleports.
    /// </summary>
    public void SetAngularVelocity(Vector3 velocity)
    {
        ref PhysicsIntent intent = ref world.WritableMotionIntent(entity, nameof(SetAngularVelocity));
        intent.AngularVelocity = velocity;
        intent.Flags |= IntentFlags.SetAngularVelocity;
    }

    /// <summary>
    /// Writes the steer target <paramref name="target"/>, relative to the entity's frame, as the motion intent of the next
    /// step (see <see cref="SteerTarget"/>): physics gives a dynamic body the velocities that carry it to the target over
    /// the step. Contacts and gravity still act on it, so it can end the step away from the target. In the step after its
    /// last steer target, physics stops it, unless that step sets its velocity. The last motion intent of a tick wins,
    /// whatever its case. Throws <see cref="ArgumentException"/> for a pose that is not finite or has a zero rotation, and
    /// <see cref="InvalidOperationException"/> with the reason of <see cref="World.MotionIntentProblem"/> when the entity
    /// refuses a steer target: only a dynamic body takes one.
    /// </summary>
    public void SteerTo(in DPose target) =>
        world.WriteMotionIntent(entity, nameof(SteerTo), new(new SteerTarget(target)));

    /// <summary>
    /// Puts the body at <paramref name="pose"/> before the next step, without sweeping. It is separate from the motion
    /// intent (<see cref="Self.MoveKinematic"/>, <see cref="Self.MoveOnRails"/> and <see cref="SteerTo"/>), so a body can
    /// teleport and then move on from the teleport's pose in the same step.
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

    /// <summary>
    /// Gives the hull of the frame that the entity owns the motion <paramref name="motion"/> from the next step on, for
    /// example <see cref="BodyMotion.Static"/> to park a ship (docs/plans/rails-components.md, "Changes of motion"). Physics
    /// gives the Jolt body the new motion at the start of the next step, which runs beside the next tick, and every read
    /// about that step counts it at once: <see cref="NextStepMotion"/> and the guards of the writes after it in this tick and
    /// in the next one. The stored motion, which <see cref="FrameTree.HullMotion"/> and the body facts show, describes the
    /// last finished step, as the collision groups do: it changes at step 4 of the sync point after the next step. The hull
    /// keeps its body, its system and its contacts. The last change in a tick wins, and a change to the motion that the hull
    /// has changes nothing. The hull drops the motion intent written in this tick that the guard refuses under its motion in
    /// the next step (see <see cref="Self.MotionIntentProblem"/>): one that is not kinematic drops an end pose or a full
    /// sample, and one that is not dynamic drops a steer target. One that is kinematic also drops the velocities set in this
    /// tick (<see cref="SetVelocity"/> and <see cref="SetAngularVelocity"/>), because a kinematic hull moves only by its
    /// motion intents and teleports. A parked frame stops at the sync point after the next step.
    /// </summary>
    /// <exception cref="InvalidOperationException">The body is no hull: the entity has no body, owns no frame, or owns a root frame.</exception>
    /// <exception cref="ArgumentException">
    /// A motion that the hull cannot take: a value that <see cref="BodyMotion"/> does not define, a character, a dynamic
    /// motion for a hull without mass, or any motion but static for a hull whose shape is or holds a mesh.
    /// </exception>
    public void SetMotion(BodyMotion motion) => world.WriteHullMotion(entity, motion);
}
