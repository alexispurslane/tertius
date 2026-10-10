using System.Numerics;

namespace Tertius.Simulation;

public sealed partial class World
{
    // The destroy batch of the sync point or of an outside-tick Destroy: the dying entities in the order they joined
    // it, and a mark per entity for a constant-time test. Releasing an entity clears its mark.
    private readonly List<Entity> dying = new();
    private readonly EntityTable.Column<bool> dyingMarks;
    // Owners of surviving frames whose parent frame died in the batch. Their rows follow the frame tree afterwards.
    private readonly List<Entity> movedFrameOwners = new();

    /// <summary>
    /// Creates a root frame and the entity that owns it. The owner is in no frame; its pose is the frame's pose.
    /// Outside a tick only.
    /// </summary>
    public FrameHandle CreateRootFrame(FrameSpec spec, RenderData? render = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Entity owner = Spawn(new SpawnDesc { AsFrame = spec, Render = render });
        return entities.OwnedFrame(owner);
    }

    /// <summary>
    /// Creates an entity. Outside a tick it is live at once and gets <see cref="Spawned"/> on its first tick.
    /// During a tick, components call <see cref="Self.Spawn(SpawnDesc)"/> instead.
    /// </summary>
    public Entity Spawn(SpawnDesc desc)
    {
        ThrowIfInTick(nameof(Spawn));
        SpawnPlan plan = PrepareSpawn(desc);
        if (!desc.AttachTo.IsNull && attachments.CheckParent(desc.AttachTo, desc.Frame) is { } problem)
            throw new ArgumentException($"Cannot attach: {problem}", nameof(desc));

        Entity entity = entities.Reserve();
        MakeLive(entity, desc, plan, Time);
        if (!desc.AttachTo.IsNull)
            attachments.Attach(entity, desc.AttachTo, desc.AttachOffset);
        FinishOutsideTickChange();
        return entity;
    }

    /// <summary>Creates an entity from a registered blueprint. See <see cref="Spawn(SpawnDesc)"/>.</summary>
    public Entity Spawn(string blueprint, FrameHandle frame, in DPose pose) => Spawn(Blueprints.CreateSpawnDesc(blueprint, frame, pose));

    /// <summary>
    /// Destroys an entity at once, together with its attached children. If it owns a frame, the frame's contents move
    /// to the parent frame (or are destroyed, for a root frame). Returns false, and does nothing, if the entity is not
    /// live: a stale or null handle, or an entity destroyed before. Outside a tick only; during a tick, send a
    /// <see cref="Tertius.Destroy"/> message or call <see cref="Self.Destroy"/>.
    /// </summary>
    public bool Destroy(Entity entity)
    {
        ThrowIfInTick(nameof(Destroy));
        if (!entities.IsAlive(entity))
            return false;
        // Physics applies destroys before creates, so earlier outside-tick creations must reach it before this batch,
        // which may destroy them.
        FlushPhysicsChanges();
        MarkDying(entity);
        DestroyDying();
        FlushPhysicsChanges();
        FinishOutsideTickChange();
        return true;
    }

    /// <summary>Adds a component to an entity. Throws <see cref="StaleEntityException"/> for a stale handle. Outside a tick only.</summary>
    public void AddComponent(Entity entity, IComponent component)
    {
        ThrowIfInTick(nameof(AddComponent));
        ArgumentNullException.ThrowIfNull(component);
        entities.AddComponent(entity, component);
    }

    /// <summary>
    /// Removes a component from an entity. Returns false, and does nothing, if the entity does not have it, which
    /// includes an entity that is not live. Outside a tick only.
    /// </summary>
    public bool RemoveComponent(Entity entity, IComponent component)
    {
        ThrowIfInTick(nameof(RemoveComponent));
        ArgumentNullException.ThrowIfNull(component);
        return entities.RemoveComponent(entity, component);
    }

    /// <summary>Subscribes an entity to a topic. Throws <see cref="StaleEntityException"/> for a stale handle. Outside a tick only.</summary>
    public void Subscribe(Entity entity, Topic topic)
    {
        ThrowIfInTick(nameof(Subscribe));
        SubscribeEntity(entity, topic);
        FlushLog();
    }

    /// <summary>
    /// Unsubscribes an entity from a topic. Returns false, and does nothing, if the entity was not subscribed, which
    /// includes an entity that is not live. Outside a tick only.
    /// </summary>
    public bool Unsubscribe(Entity entity, Topic topic)
    {
        ThrowIfInTick(nameof(Unsubscribe));
        return topics.Unsubscribe(entity, topic);
    }

    // ---- Spawning -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Validates a spawn description and copies its lists, so that later changes by the caller do not matter. It also
    /// reads each component's priority and wants here, so that a component that throws fails the spawn call instead of
    /// the sync point.
    /// </summary>
    private SpawnPlan PrepareSpawn(SpawnDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        if (desc.Frame.IsNull)
        {
            if (desc.AsFrame is null)
                throw new ArgumentException("A spawned entity needs a frame. Only a frame entity can be a root.", nameof(desc));
        }
        else if (!Frames.IsAlive(desc.Frame))
        {
            throw new ArgumentException($"Cannot spawn in {desc.Frame}: the frame is stale.", nameof(desc));
        }
        if (!desc.AttachTo.IsNull)
        {
            if (desc.Body != null)
                throw new ArgumentException("An attached entity cannot have a physics body.", nameof(desc));
            if (desc.AsFrame != null)
                throw new ArgumentException("An attached entity cannot be a frame.", nameof(desc));
            if (desc.Frame.IsNull)
                throw new ArgumentException("An attached entity needs the frame of its parent.", nameof(desc));
        }
        if (desc.AsFrame is { } spec && !(spec.Bounds.MinRadius > 0))
            throw new ArgumentException("A frame needs a bubble with positive radii.", nameof(desc));

        IReadOnlyList<IComponent> source = desc.Components ?? [];
        var components = new IComponent[source.Count];
        var priorities = new int[source.Count];
        var wants = new EventMask[source.Count];
        for (int i = 0; i < components.Length; i++)
        {
            IComponent component = source[i] ?? throw new ArgumentException($"Component {i} of the spawn description is null.", nameof(desc));
            for (int j = 0; j < i; j++)
                if (ReferenceEquals(components[j], component))
                    throw new ArgumentException($"The same {component.GetType().Name} instance appears twice. Each entity needs its own component instances.", nameof(desc));
            // Stable insertion sort by priority: equal priorities keep the order in which they were given.
            int priority = component.Priority;
            EventMask mask = component.Wants;
            int at = i;
            while (at > 0 && priorities[at - 1] > priority)
            {
                components[at] = components[at - 1];
                priorities[at] = priorities[at - 1];
                wants[at] = wants[at - 1];
                at--;
            }
            components[at] = component;
            priorities[at] = priority;
            wants[at] = mask;
        }
        Topic[] subscriptions = desc.Subscriptions is { Count: > 0 } subs ? [.. subs] : [];
        return new SpawnPlan(components, wants, subscriptions);
    }

    /// <summary>
    /// A spawn by the running entity <paramref name="spawner"/>. The slot comes from the spawner's chunk pool, so the
    /// handle is final and deterministic. Throws <see cref="StaleEntityException"/> for a stale spawner.
    /// </summary>
    internal Entity SpawnDuringTick(ChunkState chunk, Entity spawner, SpawnDesc desc)
    {
        SpawnPlan plan = PrepareSpawn(desc);
        Entity entity = entities.ReserveForSpawn(spawner);
        chunk.Spawns.Add(new PendingSpawn(entity, desc, plan));
        return entity;
    }

    /// <summary>Makes a reserved entity live: writes its rows and registers it with frames, physics and topics.</summary>
    private void MakeLive(Entity entity, SpawnDesc desc, SpawnPlan plan, double time)
    {
        var row = new TransformRow
        {
            Frame = desc.Frame,
            Pose = desc.Pose,
            LinearVelocity = desc.LinearVelocity,
            AngularVelocity = desc.AngularVelocity,
        };
        FrameHandle own = FrameHandle.Null;
        if (desc.AsFrame is { } spec)
        {
            own = Frames.Create(entity, desc.Frame, desc.Pose, spec.Bounds, spec.Driver, spec.Gravity, spec.Orbit);
            topics.AddFrame(own);
            if (spec.Driver == MotionDriver.Kepler)
            {
                ApplyKepler(own, time);
            }
            else
            {
                Frames.LinearVelocity(own) = desc.LinearVelocity;
                Frames.AngularVelocity(own) = desc.AngularVelocity;
            }
            physicsChanges.FramesCreated.Add(own);
            row = FrameOwnerRow(own);
        }

        entities.Activate(entity, row, desc.Render ?? new RenderData(), plan.Components, plan.Wants, desc.Body, own);
        if (desc.Body != null)
            physicsChanges.BodiesCreated.Add(new BodyCreate(entity, row.Frame, desc.Body, row.Pose, row.LinearVelocity, row.AngularVelocity));

        MoveMembership(entity, row.Frame, sendFrameChanged: false);
        foreach (Topic topic in plan.Subscriptions)
            SubscribeEntity(entity, topic);
    }

    /// <summary>Makes the spawns of every chunk live, in chunk order. Step 6 of the sync point.</summary>
    private void MakeSpawnsLive(double time)
    {
        foreach (ChunkState chunk in entities.Chunks)
        {
            foreach (PendingSpawn spawn in chunk.Spawns)
            {
                FrameHandle frame = spawn.Desc.Frame;
                if (!frame.IsNull && !Frames.IsAlive(frame))
                {
                    Report($"tick {TickNumber}: dropped the spawn of {spawn.Entity}: its frame {frame} was destroyed before it became live.", isError: true);
                    entities.Release(spawn.Entity);
                    continue;
                }
                MakeLive(spawn.Entity, spawn.Desc, spawn.Plan, time);
            }
        }

        // Attach after every spawn is live, so that a child may name a parent spawned later in the same tick.
        foreach (ChunkState chunk in entities.Chunks)
        {
            foreach (PendingSpawn spawn in chunk.Spawns)
            {
                Entity parent = spawn.Desc.AttachTo;
                if (parent.IsNull || !entities.IsAlive(spawn.Entity))
                    continue;
                string? problem = attachments.CheckParent(parent, spawn.Desc.Frame);
                if (problem == null)
                {
                    attachments.Attach(spawn.Entity, parent, spawn.Desc.AttachOffset);
                }
                else
                {
                    Report($"tick {TickNumber}: destroyed {spawn.Entity} as it became live: {problem}", isError: true);
                    MarkDying(spawn.Entity);
                }
            }
        }
        if (dying.Count > 0)
            DestroyDying();
    }

    // ---- Destruction ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Puts the running entity in its chunk's destroy list, once. Worker-safe for the running entity. An entity asks
    /// only during its own run, so its requests are consecutive, and comparing with the last one is enough. Returns
    /// false, and does nothing, for an entity that is not live.
    /// </summary>
    internal bool RequestDestroy(ChunkState chunk, Entity entity)
    {
        if (!entities.IsAlive(entity))
            return false;
        List<Entity> destroys = chunk.Destroys;
        if (destroys.Count == 0 || destroys[^1] != entity)
            destroys.Add(entity);
        return true;
    }

    /// <summary>
    /// Adds an entity to the destroy batch. Returns true if it joined the batch; false if it was in the batch already or
    /// is not live.
    /// </summary>
    private bool MarkDying(Entity entity)
    {
        if (!entities.IsAlive(entity) || dyingMarks[entity])
            return false;
        dyingMarks[entity] = true;
        dying.Add(entity);
        return true;
    }

    /// <summary>
    /// Destroys every entity in the batch, plus attached children. The survivors in a dying frame move to the nearest
    /// ancestor frame that survives; if none survives, they die too. Step 5 of the sync point.
    /// </summary>
    private void DestroyDying()
    {
        // Grow the batch until nothing changes. One pass is not enough: an ancestor frame can join the batch after
        // one of its descendants was looked at.
        for (bool grew = true; grew;)
        {
            grew = false;
            for (int i = 0; i < dying.Count; i++)
            {
                Entity entity = dying[i];
                foreach (Entity child in attachments.ChildrenOf(entity))
                    grew |= MarkDying(child);
                FrameHandle own = entities.OwnedFrame(entity);
                if (!own.IsNull && SurvivingAncestor(own).IsNull)
                    foreach (Entity member in topics.MembersOf(own))
                        grew |= MarkDying(member);
            }
        }

        // Move the survivors in each dying frame straight into the nearest surviving ancestor: rows, topics and
        // bodies. The frame tree moves surviving child frames itself when their parent dies (FrameTree.Destroy), so
        // their owners are only remembered here. The loop reads no topic set, so the member span stays valid.
        movedFrameOwners.Clear();
        for (int i = 0; i < dying.Count; i++)
        {
            FrameHandle frame = entities.OwnedFrame(dying[i]);
            if (frame.IsNull)
                continue;
            FrameHandle target = SurvivingAncestor(frame);
            if (target.IsNull)
                continue;
            foreach (Entity member in topics.MembersOf(frame))
            {
                if (dyingMarks[member])
                    continue;
                Rehome(member, frame, target);
                if (!entities.OwnedFrame(member).IsNull)
                    movedFrameOwners.Add(member);
            }
        }

        foreach (Entity entity in dying)
            FreeEntity(entity);
        dying.Clear();

        // Every dying frame is gone now, so the frame tree holds each moved child frame under its final parent, with
        // its pose and velocity converted. The owner's row mirrors that exactly.
        foreach (Entity owner in movedFrameOwners)
            MirrorFrameOwner(entities.OwnedFrame(owner));
        movedFrameOwners.Clear();
    }

    /// <summary>The nearest ancestor of a frame whose owner is not in the destroy batch, or null.</summary>
    private FrameHandle SurvivingAncestor(FrameHandle frame)
    {
        for (FrameHandle parent = Frames.Parent(frame); !parent.IsNull; parent = Frames.Parent(parent))
            if (!dyingMarks[parent.Entity])
                return parent;
        return FrameHandle.Null;
    }

    /// <summary>Moves an entity from a dying frame into an ancestor frame, keeping its pose and velocity relative to everything else.</summary>
    private void Rehome(Entity entity, FrameHandle from, FrameHandle to)
    {
        ref readonly TransformRow row = ref entities.BackRow(entity);
        DPose relative = Frames.Relative(from, to);
        var moved = new TransformRow
        {
            Frame = to,
            Pose = DPose.Compose(relative, row.Pose),
            LinearVelocity = Frames.ConvertVelocity(from, to, row.Pose.Position, row.LinearVelocity).ToVector3(),
            AngularVelocity = (relative.TransformDirection(row.AngularVelocity) + Frames.AngularVelocityRelative(from, to)).ToVector3(),
        };
        entities.SetBackTransform(entity, moved);
        MoveMembership(entity, to, sendFrameChanged: true);

        // The body moves to the other physics system: physics destroys it before it creates the new one.
        if (entities.Body(entity) is { } body)
        {
            physicsChanges.BodiesDestroyed.Add(entity);
            physicsChanges.BodiesCreated.Add(new BodyCreate(entity, to, body, moved.Pose, moved.LinearVelocity, moved.AngularVelocity));
        }
    }

    /// <summary>
    /// Frees one entity of the batch: its frame and its body, then its slot. Releasing the entity takes it out of its
    /// topic sets, its attachment chain and its grid cell.
    /// </summary>
    private void FreeEntity(Entity entity)
    {
        FrameHandle own = entities.OwnedFrame(entity);
        if (!own.IsNull && Frames.IsAlive(own))
        {
            physicsChanges.FramesDestroyed.Add(own);
            topics.RemoveFrame(own);
            // Moves each surviving child frame up to this frame's parent, converting its pose and velocity.
            Frames.Destroy(own);
        }
        if (entities.Body(entity) != null)
            physicsChanges.BodiesDestroyed.Add(entity);
        entities.Release(entity);
    }

    // ---- Components -----------------------------------------------------------------------------------------------

    /// <summary>Applies the component changes that the running entity made. Runs on the worker, right after the entity's run.</summary>
    private void ApplyComponentChanges(ChunkState chunk)
    {
        foreach (ComponentChange change in chunk.ComponentChanges)
        {
            try
            {
                if (change.Add)
                    entities.AddComponent(change.Entity, change.Component);
                else if (!entities.RemoveComponent(change.Entity, change.Component))
                    chunk.Messages.Add(new ChunkMessage($"tick {TickNumber}: {change.Entity}: RemoveComponent: the entity has no such {change.Component.GetType().Name} instance.", false));
            }
            catch (Exception ex) when (ex is not ContractException)
            {
                chunk.Messages.Add(new ChunkMessage($"tick {TickNumber}: {change.Entity}: {(change.Add ? "AddComponent" : "RemoveComponent")} {change.Component.GetType().FullName} failed: {ex.GetType().Name}: {ex.Message}", true));
            }
        }
        chunk.ComponentChanges.Clear();
    }

    // ---- Frames, membership and topics ----------------------------------------------------------------------------

    /// <summary>The pose, velocities and parent of a frame, as the owner's transform row.</summary>
    private TransformRow FrameOwnerRow(FrameHandle frame) => new()
    {
        Frame = Frames.Parent(frame),
        Pose = Frames.LocalPose(frame),
        LinearVelocity = Frames.LinearVelocity(frame).ToVector3(),
        AngularVelocity = Frames.AngularVelocity(frame),
    };

    private void ApplyKepler(FrameHandle frame, double time)
    {
        KeplerOrbit orbit = Frames.Orbit(frame);
        (Double3 position, Double3 velocity) = orbit.StateAt(time);
        Frames.SetLocalPose(frame, new DPose(position, orbit.RotationAt(time)));
        Frames.LinearVelocity(frame) = velocity;
        Frames.AngularVelocity(frame) = orbit.AngularVelocity();
        Frames.ProperAcceleration(frame) = Vector3.Zero;
    }

    /// <summary>The owner's transform row mirrors the frame's local pose, which is the authoritative one.</summary>
    private void MirrorFrameOwner(FrameHandle frame)
    {
        Entity owner = frame.Entity;
        entities.SetBackTransform(owner, FrameOwnerRow(frame));
        MoveMembership(owner, entities.BackRow(owner).Frame, sendFrameChanged: true);
    }

    /// <summary>
    /// Moves an entity to the member set of <paramref name="to"/>. If it was a member of another frame, it optionally
    /// gets a <see cref="FrameChanged"/> on the next tick.
    /// </summary>
    private void MoveMembership(Entity entity, FrameHandle to, bool sendFrameChanged)
    {
        if (!topics.MoveMember(entity, to, out FrameHandle from) || !sendFrameChanged)
            return;
        router.AddEngineEvent(Event.Create(new Target(entity), Entity.Null, new FrameChanged { From = from, To = to }));
    }

    /// <summary>Subscribes an entity, or reports why the topic's frame cannot take it. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    private void SubscribeEntity(Entity entity, Topic topic)
    {
        if (!topics.Subscribe(entity, topic))
            Report($"tick {TickNumber}: {entity} cannot subscribe to {Topics.NameOf(topic)}: that frame is not alive.", isError: false);
    }

    /// <summary>
    /// Derives every attached entity's back row from its chain root. An attached entity that followed its root into
    /// another frame changes topic and hears about it.
    /// </summary>
    private void DeriveAttachments()
    {
        attachments.Derive();
        foreach (Entity attached in attachments.Attached)
            MoveMembership(attached, entities.BackRow(attached).Frame, sendFrameChanged: true);
    }

    /// <summary>Applies the effects of an outside-tick change that the sync point would otherwise apply.</summary>
    private void FinishOutsideTickChange()
    {
        DeriveAttachments();
        SwapSnapshot();
        FlushLog();
    }
}
