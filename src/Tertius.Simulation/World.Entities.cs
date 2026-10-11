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
    /// During a tick, components call <see cref="Self.Spawn(SpawnDesc)"/> instead. Throws <see cref="ArgumentException"/>,
    /// before anything changes, for a description that the world cannot make live: a pose that is not valid (see
    /// <see cref="DPose.IsValid"/>), a velocity that is not finite, a body that physics cannot create (see
    /// <see cref="BodyDesc.Problem"/> and <see cref="IPhysicsBackend.ShapeProblem"/>), a frame with a driver that its hull
    /// cannot follow (see <see cref="BodyDesc.FrameHullProblem"/>), a Kepler frame whose orbit is not valid, a Spin frame
    /// whose spin is not valid, or a gravity field that is not valid.
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

    /// <summary>
    /// Gives a frame a motion driver, for example to park a ship. Outside a tick only. A frame that gets the static driver
    /// stops where it is: both of its velocities become zero, and it keeps its proper acceleration, so the cargo of a ship
    /// that is parked on the ground keeps its weight. A frame that gets the Kepler driver moves to its place on its orbit at
    /// once, as for <see cref="SetFrameOrbit"/>, so physics never drives its hull across the gap. A frame that gets the
    /// Spin driver keeps its place and turns to its spin at once, as for <see cref="SetFrameSpin"/>. A frame that gets the
    /// physics driver keeps its pose and velocities until physics moves it. The owner's transform mirrors the frame at once. Physics follows at the start of its next
    /// step: the hull gets the motion that the driver needs, at the frame's pose and velocity in the tree. Every call does
    /// that, also a call that gives the frame the driver that it has, so a park and an unpark between two ticks stop the
    /// ship.
    /// </summary>
    /// <exception cref="InvalidOperationException">During a tick, when physics reads the frame tree on its own thread.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A driver that <see cref="MotionDriver"/> does not define.</exception>
    /// <exception cref="ArgumentException">
    /// A frame that is gone. The physics driver for a frame with a parent whose owner's body cannot be dynamic: it has no
    /// mass, or it is a mesh, which never moves. The Kepler driver for a frame with a parent whose owner's body is a mesh.
    /// The Kepler driver for a frame whose orbit is not valid (see <see cref="KeplerOrbit.IsValid"/>). The Spin driver for a
    /// frame with a parent whose owner's body is a mesh, or for a frame whose spin is not valid (see
    /// <see cref="FrameSpin.IsValid"/>). A root frame has no hull, so any body of its owner can have any driver. The frame
    /// keeps its driver.
    /// </exception>
    public void SetFrameDriver(FrameHandle frame, MotionDriver driver)
    {
        ThrowIfInTick(nameof(SetFrameDriver));
        if (!Enum.IsDefined(driver))
            throw new ArgumentOutOfRangeException(nameof(driver), driver, "MotionDriver does not define this driver.");
        if (!Frames.IsAlive(frame))
            throw new ArgumentException($"Cannot set the driver of {frame}: the frame is gone.", nameof(frame));
        // A root frame has no parent, so its owner has no hull that must follow the driver.
        if (!Frames.Parent(frame).IsNull && entities.Body(frame.Entity)?.FrameHullProblem(driver) is { } problem)
            throw new ArgumentException($"Cannot give {frame} the {driver} driver: the hull of its owner {frame.Entity} {problem}.", nameof(driver));
        if (driver == MotionDriver.Kepler && !Frames.Orbit(frame).IsValid)
            throw new ArgumentException($"Cannot give {frame} the Kepler driver: its orbit is not valid. Call SetFrameOrbit first.", nameof(driver));
        if (driver == MotionDriver.Spin && !Frames.Spin(frame).IsValid)
            throw new ArgumentException($"Cannot give {frame} the Spin driver: its spin is not valid. Call SetFrameSpin first.", nameof(driver));

        Frames.SetDriver(frame, driver);
        if (driver == MotionDriver.Kepler)
            ApplyKepler(frame, Time);
        else if (driver == MotionDriver.Spin)
            ApplySpin(frame, Time);
        MirrorFrameOwner(frame);
        FinishOutsideTickChange();
    }

    /// <summary>
    /// Gives a frame a new spin, outside a tick only. A frame with the Spin driver turns to its place on the new spin at
    /// once, and its owner's transform follows. Physics follows at the start of its next step, as it follows a new driver.
    /// A frame with another driver keeps the spin for a later <see cref="SetFrameDriver"/> with the Spin driver.
    /// </summary>
    /// <exception cref="InvalidOperationException">During a tick, when physics reads the frame tree on its own thread.</exception>
    /// <exception cref="ArgumentException">
    /// A frame that is gone, or a spin that is not valid (see <see cref="FrameSpin.IsValid"/>). The frame keeps its spin.
    /// </exception>
    public void SetFrameSpin(FrameHandle frame, in FrameSpin spin)
    {
        ThrowIfInTick(nameof(SetFrameSpin));
        if (!Frames.IsAlive(frame))
            throw new ArgumentException($"Cannot set the spin of {frame}: the frame is gone.", nameof(frame));
        if (!spin.IsValid)
            throw new ArgumentException($"Cannot give {frame} this spin: the spin is not valid (see FrameSpin.IsValid).", nameof(spin));

        Frames.SetSpin(frame, spin);
        if (Frames.Driver(frame) == MotionDriver.Spin)
        {
            ApplySpin(frame, Time);
            MirrorFrameOwner(frame);
        }
        FinishOutsideTickChange();
    }

    /// <summary>
    /// Gives a frame a new orbit around its parent, outside a tick only. A frame with the Kepler driver moves to its place on
    /// the new orbit at once, and its owner's transform follows. Physics follows at the start of its next step, as it
    /// follows a new driver: the hull takes the frame's pose and velocity in the tree. A frame with another driver keeps the
    /// orbit for a later <see cref="SetFrameDriver"/> with the Kepler driver.
    /// </summary>
    /// <exception cref="InvalidOperationException">During a tick, when physics reads the frame tree on its own thread.</exception>
    /// <exception cref="ArgumentException">
    /// A frame that is gone, or an orbit that is not valid (see <see cref="KeplerOrbit.IsValid"/>). The frame keeps its orbit.
    /// </exception>
    public void SetFrameOrbit(FrameHandle frame, in KeplerOrbit orbit)
    {
        ThrowIfInTick(nameof(SetFrameOrbit));
        if (!Frames.IsAlive(frame))
            throw new ArgumentException($"Cannot set the orbit of {frame}: the frame is gone.", nameof(frame));
        if (!orbit.IsValid)
            throw new ArgumentException($"Cannot give {frame} this orbit: the orbit is not valid (see KeplerOrbit.IsValid).", nameof(orbit));

        Frames.SetOrbit(frame, orbit);
        if (Frames.Driver(frame) == MotionDriver.Kepler)
        {
            ApplyKepler(frame, Time);
            MirrorFrameOwner(frame);
        }
        FinishOutsideTickChange();
    }

    /// <summary>
    /// Gives a frame a new gravity field, outside a tick only. Physics wakes the frame's bodies at the start of its next
    /// step, and they feel the new field from then on.
    /// </summary>
    /// <exception cref="InvalidOperationException">During a tick, when physics reads the frame tree on its own thread.</exception>
    /// <exception cref="ArgumentException">
    /// A frame that is gone, or a field that is not valid (see <see cref="GravityField.IsValid"/>). The frame keeps its field.
    /// </exception>
    public void SetFrameGravity(FrameHandle frame, in GravityField gravity)
    {
        ThrowIfInTick(nameof(SetFrameGravity));
        if (!Frames.IsAlive(frame))
            throw new ArgumentException($"Cannot set the gravity of {frame}: the frame is gone.", nameof(frame));
        if (!gravity.IsValid)
            throw new ArgumentException($"Cannot give {frame} this gravity field: the field is not valid (see GravityField.IsValid).", nameof(gravity));

        Frames.SetGravity(frame, gravity);
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
        // Physics turns a pose or velocity that is not finite into NaN in its first step, and a zero rotation too.
        if (!desc.Pose.IsValid)
            throw new ArgumentException($"Cannot spawn at the pose {desc.Pose}: its position must be finite, and its rotation finite and not zero.", nameof(desc));
        if (!IsFinite(desc.LinearVelocity))
            throw new ArgumentException($"Cannot spawn with the linear velocity {desc.LinearVelocity}: it must be finite.", nameof(desc));
        if (!IsFinite(desc.AngularVelocity))
            throw new ArgumentException($"Cannot spawn with the angular velocity {desc.AngularVelocity}: it must be finite.", nameof(desc));
        if (!desc.AttachTo.IsNull)
        {
            if (desc.Body != null)
                throw new ArgumentException("An attached entity cannot have a physics body.", nameof(desc));
            if (desc.AsFrame != null)
                throw new ArgumentException("An attached entity cannot be a frame.", nameof(desc));
        }
        if (desc.Body is { } body)
        {
            bool ownsFrame = desc.AsFrame is not null;
            if (body.Problem(ownsFrame) is { } problem)
                throw new ArgumentException($"Physics cannot create this body: it {problem}.", nameof(desc));
            // Only the backend can tell that it cannot build some shapes, for example a convex hull whose points coincide.
            if (physics?.ShapeProblem(body.Shape) is { } shape)
                throw new ArgumentException($"Physics cannot create this body: its shape {shape}.", nameof(desc));
            if (ownsFrame && body.InteriorShape is { } interior && physics?.ShapeProblem(interior) is { } inside)
                throw new ArgumentException($"Physics cannot create this body: its interior shape {inside}.", nameof(desc));
        }
        if (desc.AsFrame is { } spec)
        {
            if (!(spec.Bounds.MinRadius > 0))
                throw new ArgumentException("A frame needs a bubble with positive radii.", nameof(desc));
            if (!Enum.IsDefined(spec.Driver))
                throw new ArgumentException($"MotionDriver does not define the driver {spec.Driver}.", nameof(desc));
            if (spec.Driver == MotionDriver.Kepler && !spec.Orbit.IsValid)
                throw new ArgumentException("A frame with the Kepler driver needs an orbit that it can follow: this orbit is not valid (see KeplerOrbit.IsValid).", nameof(desc));
            if (spec.Driver == MotionDriver.Spin && !spec.Spin.IsValid)
                throw new ArgumentException("A frame with the Spin driver needs a spin that it can follow: this spin is not valid (see FrameSpin.IsValid).", nameof(desc));
            if (!spec.Gravity.IsValid)
                throw new ArgumentException("A frame needs a gravity field that physics can apply: this gravity field is not valid (see GravityField.IsValid).", nameof(desc));
            // So a frame with the physics driver whose owner has a body always has a dynamic hull. A root frame has no parent,
            // so its owner has no hull, and its body is only the frame's static interior.
            if (!desc.Frame.IsNull && desc.Body?.FrameHullProblem(spec.Driver) is { } problem)
                throw new ArgumentException($"A frame with the {spec.Driver} driver cannot have this hull: it {problem}.", nameof(desc));
        }

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

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

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
            own = Frames.Create(entity, desc.Frame, desc.Pose, spec.Bounds, spec.Driver, spec.Gravity, spec.Orbit, spec.Spin);
            topics.AddFrame(own);
            if (spec.Driver == MotionDriver.Kepler)
            {
                ApplyKepler(own, time);
            }
            else if (spec.Driver == MotionDriver.Spin)
            {
                ApplySpin(own, time);
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
        Frames.AngularVelocity(frame) = orbit.AngularVelocityAt(time).ToVector3();
        Frames.ProperAcceleration(frame) = Vector3.Zero;
    }

    /// <summary>
    /// A Spin frame keeps its place in its parent and turns to its spin at <paramref name="time"/>. Like a static frame, it
    /// keeps its proper acceleration, so the cargo of a landed ship that starts to turn keeps its weight. A new frame
    /// starts with none.
    /// </summary>
    private void ApplySpin(FrameHandle frame, double time)
    {
        FrameSpin spin = Frames.Spin(frame);
        Frames.SetLocalPose(frame, new DPose(Frames.LocalPose(frame).Position, spin.RotationAt(time)));
        Frames.LinearVelocity(frame) = Double3.Zero;
        Frames.AngularVelocity(frame) = spin.AngularVelocity.ToVector3();
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
