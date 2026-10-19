using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

public sealed partial class World
{
    // The destroy batch of the sync point or of an outside-tick Destroy: the dying entities in the order they joined
    // it, and a mark per entity for a constant-time test. Releasing an entity clears its mark.
    private readonly List<Entity> dying = new();
    private readonly EntityTable.Column<bool> dyingMarks;
    // Owners of surviving frames whose parent frame died in the batch. Their rows follow the frame tree afterwards.
    private readonly List<Entity> movedFrameOwners = new();
    // The attached spawns of the sync point that wait for their parent to become live, in chunk order.
    private readonly List<PendingSpawn> waitingAttachedSpawns = new();
    // The component types whose class constructor the world has run (see InitializeComponentType).
    private readonly HashSet<Type> initializedComponentTypes = new();

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
    /// <see cref="BodyDesc.Problem"/> and <see cref="IPhysicsBackend.ShapeProblem"/>, which checks a frame's hull as a body
    /// with the motion of its description), a gravity field that is not valid, or an attachment
    /// parent that is not live or that owns a root frame. The World checks nothing about rails motion: a rails component
    /// checks its own rules (see <see cref="KeplerMotion"/> and <see cref="SpinMotion"/>). An attached entity is live in
    /// the frame of its parent, whatever frame the description names (see <see cref="SpawnDesc.AttachTo"/>).
    /// </summary>
    public Entity Spawn(SpawnDesc desc)
    {
        ThrowIfInTick(nameof(Spawn));
        SpawnPlan plan = PrepareSpawn(desc);
        FrameHandle frame = desc.Frame;
        if (!desc.AttachTo.IsNull && attachments.CheckParent(desc.AttachTo, out frame) is { } problem)
            throw new ArgumentException($"Cannot attach: {problem}", nameof(desc));

        Entity entity = entities.Reserve();
        MakeLive(entity, desc, plan, frame);
        if (!desc.AttachTo.IsNull)
            attachments.Attach(entity, desc.AttachTo, desc.AttachOffset);
        FinishOutsideTickChange();
        return entity;
    }

    /// <summary>Creates an entity from a registered blueprint. See <see cref="Spawn(SpawnDesc)"/>.</summary>
    public Entity Spawn(string blueprint, FrameHandle frame, in DPose pose) => Spawn(Blueprints.CreateSpawnDesc(blueprint, frame, pose));

    /// <summary>
    /// Destroys an entity at once, together with its attached children. If it owns a frame, the frame's contents move
    /// to the parent frame (or are destroyed, for a root frame). A child frame moves too, with its pose and velocity
    /// converted. A <see cref="KeplerMotion"/> of the child reads the G·M of the new parent in the next tick.
    /// Returns false, and does nothing, if the entity is not live: a stale or null handle, or an entity destroyed before.
    /// Outside a tick only; during a tick, send a <see cref="Tertius.Destroy"/> message or call <see cref="Self.Destroy"/>.
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

    /// <summary>
    /// Adds a component to an entity. The snapshot shows it at once, so the next tick sees it. Throws
    /// <see cref="StaleEntityException"/> for a stale handle, and as <see cref="ComponentTypes.IdOf"/> does for a
    /// component type that cannot have an id. Outside a tick only.
    /// </summary>
    public void AddComponent(Entity entity, IComponent component)
    {
        ThrowIfInTick(nameof(AddComponent));
        ArgumentNullException.ThrowIfNull(component);
        InitializeComponentType(component.GetType());
        entities.AddComponent(entity, component);
        SwapSnapshot();
        FlushLog();
    }

    /// <summary>
    /// Removes a component from an entity. The snapshot shows it at once, so the next tick sees it. Returns false, and
    /// does nothing, if the entity does not have it, which includes an entity that is not live. Outside a tick only.
    /// </summary>
    public bool RemoveComponent(Entity entity, IComponent component)
    {
        ThrowIfInTick(nameof(RemoveComponent));
        ArgumentNullException.ThrowIfNull(component);
        if (!entities.RemoveComponent(entity, component))
            return false;
        SwapSnapshot();
        return true;
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
    /// Gives a frame a new gravity field, outside a tick only. Physics wakes the frame's bodies at the start of its next
    /// step, and they feel the new field from then on. The <see cref="KeplerMotion"/> of a child frame reads the G·M of the
    /// new field in the next tick, so a new G·M moves the child from its next sample on, and a field without one holds the
    /// child still.
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
            // An attached entity takes the frame of its parent when it becomes live.
            if (desc.AsFrame is null && desc.AttachTo.IsNull)
                throw new ArgumentException("A spawned entity needs a frame. Only a frame entity can be a root, and only an attached entity takes the frame of its parent.", nameof(desc));
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
            // The body of a frame owner is the frame's hull when the frame has a parent. A root frame has no hull.
            BodyRole role = !ownsFrame ? BodyRole.OwnsNoFrame : desc.Frame.IsNull ? BodyRole.OwnsRootFrame : BodyRole.Hull;
            if (body.Problem(role) is { } problem)
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
            if (!spec.Gravity.IsValid)
                throw new ArgumentException("A frame needs a gravity field that physics can apply: this gravity field is not valid (see GravityField.IsValid).", nameof(desc));
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
            // The type needs an id for the component flags: a generic type, or one past the cap, fails the spawn here.
            ComponentTypes.IdOf(component.GetType());
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

    private static bool IsFinite(Double3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

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

    /// <summary>
    /// Makes a reserved entity live in <paramref name="frame"/>: writes its rows and registers it with frames, physics and
    /// topics. The frame is the one that the description names, or for an attached entity the frame of its parent.
    /// </summary>
    private void MakeLive(Entity entity, SpawnDesc desc, SpawnPlan plan, FrameHandle frame)
    {
        // The description is the caller's, and its rotation can be any rotation that spawn accepted. The local pose of a
        // frame must be unit length (see FrameTree), so the world normalizes it here, and does not refuse it.
        DPose pose = desc.Pose.WithUnitRotation();
        var row = new TransformRow
        {
            Frame = frame,
            PhysicsHome = frame,
            Pose = pose,
            HomePosition = pose.Position,
            LinearVelocity = desc.LinearVelocity.ToVector3(),
            AngularVelocity = desc.AngularVelocity,
        };
        // The velocity relative to the entity's frame, in doubles, that physics converts when it creates the body.
        Double3 velocity = desc.LinearVelocity;
        FrameHandle own = FrameHandle.Null;
        if (desc.AsFrame is { } spec)
        {
            own = Frames.Create(entity, frame, pose, spec.Bounds, spec.Gravity);
            // A frame with a parent whose owner has a body has a hull, with the motion of the body's description. A root frame
            // has no hull, and neither has a frame without a body: the tree gives a new frame no hull motion.
            if (!frame.IsNull && desc.Body is { } hullBody)
                Frames.SetHullMotion(own, hullBody.Motion);
            topics.AddFrame(own);
            // Every new frame stands at the pose of its spawn description, with its velocities. A frame on rails holds still
            // there for its first step, which runs before its components do, and the first sample of its rails component
            // puts it on its rails (docs/plans/rails-components.md, "Other rules" in "The rails components").
            Frames.SetTreeLinearVelocity(own, desc.LinearVelocity);
            Frames.SetAngularVelocity(own, desc.AngularVelocity);
            physicsChanges.FramesCreated.Add(own);
            row = FrameOwnerRow(own);
            velocity = Frames.LinearVelocity(own);
        }

        foreach (IComponent component in plan.Components)
            InitializeComponentType(component.GetType());
        entities.Activate(entity, row, desc.Render ?? new RenderData(), plan.Components, plan.Wants, desc.Body, own);
        if (desc.Body != null)
            physicsChanges.BodiesCreated.Add(new BodyCreate(entity, row.Frame, desc.Body, row.Pose, velocity, row.AngularVelocity));

        // A new entity joins the members of its frame. That is no change of frame, so it hears no FrameChanged.
        topics.MoveMember(entity, row.Frame, out _);
        foreach (Topic topic in plan.Subscriptions)
            SubscribeEntity(entity, topic);
    }

    /// <summary>
    /// Makes the spawns of every chunk live, in chunk order. Step 6 of the sync point. The attached spawns come last, each
    /// after its parent, because each one takes the frame of its parent. So a child can also name a parent that a later
    /// chunk spawned in the same tick.
    /// </summary>
    private void MakeSpawnsLive()
    {
        foreach (ChunkState chunk in entities.Chunks)
        {
            foreach (PendingSpawn spawn in chunk.Spawns)
            {
                if (!spawn.Desc.AttachTo.IsNull)
                {
                    waitingAttachedSpawns.Add(spawn);
                    continue;
                }
                FrameHandle frame = spawn.Desc.Frame;
                if (!frame.IsNull && !Frames.IsAlive(frame))
                {
                    Report($"tick {TickNumber}: dropped the spawn of {spawn.Entity}: its frame {frame} was destroyed before it became live.", isError: true);
                    entities.Release(spawn.Entity);
                    continue;
                }
                MakeLive(spawn.Entity, spawn.Desc, spawn.Plan, frame);
            }
        }
        MakeAttachedSpawnsLive();
    }

    /// <summary>
    /// Makes each waiting attached spawn live in the frame of its parent at this moment, and attaches it. The spawner saw
    /// the parent in the front snapshot, but in this sync point a hand-off or the death of a frame can have moved the parent
    /// into another frame. So the frame that the description names does not count. A spawn waits while its parent is
    /// itself a waiting spawn: each pass over the list, in chunk order, makes live every spawn whose parent is live. When a
    /// pass makes none live, the rest never become live: the parent died in the destroy batch, or it was never live, or
    /// the spawns wait for each other in a loop. The world drops each of them and records an error.
    /// </summary>
    private void MakeAttachedSpawnsLive()
    {
        for (bool madeOneLive = true; madeOneLive;)
        {
            madeOneLive = false;
            int kept = 0;
            for (int i = 0; i < waitingAttachedSpawns.Count; i++)
            {
                PendingSpawn spawn = waitingAttachedSpawns[i];
                if (attachments.CheckParent(spawn.Desc.AttachTo, out FrameHandle frame) is null)
                {
                    MakeLive(spawn.Entity, spawn.Desc, spawn.Plan, frame);
                    attachments.Attach(spawn.Entity, spawn.Desc.AttachTo, spawn.Desc.AttachOffset);
                    madeOneLive = true;
                }
                else
                {
                    waitingAttachedSpawns[kept++] = spawn;
                }
            }
            waitingAttachedSpawns.RemoveRange(kept, waitingAttachedSpawns.Count - kept);
        }

        foreach (PendingSpawn spawn in waitingAttachedSpawns)
        {
            string? problem = attachments.CheckParent(spawn.Desc.AttachTo, out _);
            Report($"tick {TickNumber}: dropped the spawn of {spawn.Entity}: {problem}", isError: true);
            entities.Release(spawn.Entity);
        }
        waitingAttachedSpawns.Clear();
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
        ConvertIntentsOfDyingFrames();
        ConvertSamplesOfMovedFrames();

        foreach (Entity entity in dying)
            FreeEntity(entity);
        dying.Clear();

        // Every dying frame is gone now, so the frame tree holds each moved child frame under its final parent, with
        // its pose and velocity converted. The owner's row mirrors the frame exactly. A KeplerMotion of the frame reads the
        // G·M of the new parent in the next tick, and holds the frame still when that parent has none.
        foreach (Entity owner in movedFrameOwners)
        {
            FrameHandle moved = entities.OwnedFrame(owner);
            MirrorFrameOwner(moved);
        }
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

    /// <summary>
    /// Converts each intent that the next step applies and that names a frame of the destroy batch into the frame of its
    /// entity, which survives: a body that a rehome moved, or one that a hand-off of the step moved out of the frame before
    /// it died. Step 7b of the sync point converts every other intent of an entity that moved (see
    /// <see cref="ConvertMovedIntents"/>), but by then these frames are gone, so these intents convert here, after the
    /// rehomes and before the frames die.
    /// </summary>
    private void ConvertIntentsOfDyingFrames()
    {
        foreach (Entity entity in entities.IntentEntities.Span)
        {
            if (dyingMarks[entity])
                continue;
            ref PhysicsIntent intent = ref entities.NextStepIntent(entity);
            ref readonly TransformRow row = ref entities.BackRow(entity);
            if (!intent.Frame.IsNull && dyingMarks[intent.Frame.Entity])
                ConvertIntent(ref intent, row.Frame, row.Pose.Position);
        }
    }

    /// <summary>
    /// Converts each applied and followed row of the sample table that is relative to a dying frame, or to a numeric frame in
    /// one, into the frame that the row's frame's owner is in after the rehomes, before the dying frames go. Each row names
    /// the frame that it is relative to: a sample its owner's frame when the owner wrote it, and a record the frame's
    /// semantic parent when the frame took it (see <see cref="KeepRecord"/>). So the rows of the frames that the destroy batch
    /// moves out of a dying frame convert here. Both buffers hold rows at step 5: the followed rows of the step that ended,
    /// which the numeric update of step 6c reads, and the applied samples of the next step. The rows of a dying frame die
    /// with it: a frame that reuses its index has another generation.
    /// </summary>
    private void ConvertSamplesOfMovedFrames()
    {
        int n = Frames.Count;
        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = Frames.AtDense(i);
            if (Frames.IsNumeric(frame) || dyingMarks[frame.Entity])
                continue;
            if (IsRelativeToDyingFrame(Frames.Samples.Applied, frame) || IsRelativeToDyingFrame(Frames.Samples.Followed, frame))
                Frames.Samples.ConvertInto(frame, entities.BackRow(frame.Entity).Frame);
        }
    }

    /// <summary>True when <paramref name="samples"/> holds a row of <paramref name="frame"/> that is relative to a frame that dies in this batch.</summary>
    private bool IsRelativeToDyingFrame(MotionSamples samples, FrameHandle frame) =>
        samples.TryGet(frame, out _, out FrameHandle relativeTo) && Frames.IsAlive(relativeTo) && dyingMarks[Frames.SemanticOf(relativeTo).Entity];

    /// <summary>Moves an entity from a dying frame into an ancestor frame, keeping its pose and velocity relative to everything else.</summary>
    private void Rehome(Entity entity, FrameHandle from, FrameHandle to)
    {
        ref readonly TransformRow row = ref entities.BackRow(entity);
        DPose relative = Frames.Relative(from, to);
        Double3 velocity = Frames.ConvertVelocity(from, to, row.Pose.Position, FrameReads.VelocityIn(Frames, entity, row, from));
        DPose pose = DPose.Compose(relative, row.Pose);
        var moved = new TransformRow
        {
            Frame = to,
            PhysicsHome = to,
            Pose = pose,
            HomePosition = pose.Position,
            LinearVelocity = velocity.ToVector3(),
            AngularVelocity = (relative.TransformDirection(row.AngularVelocity) + Frames.AngularVelocityRelative(from, to)).ToVector3(),
        };
        entities.SetBackTransform(entity, moved);
        MoveMembership(entity, to);

        // The body moves to the other physics system: physics destroys it before it creates the new one, as the body is
        // now: with the collision groups and mask that it has now, and for a hull, the motion of the next step, which the
        // new body runs first.
        if (entities.BodyToRecreate(entity) is { } body)
        {
            FrameHandle own = entities.OwnedFrame(entity);
            if (!own.IsNull && NextStepHullMotion(own) is { } motion && motion != body.Motion)
                body = body with { Motion = motion };
            physicsChanges.BodiesDestroyed.Add(entity);
            physicsChanges.BodiesCreated.Add(new BodyCreate(entity, to, body, moved.Pose, velocity, moved.AngularVelocity));
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
            // Moves each surviving child frame up to this frame's parent, converting its pose and velocity. The numeric
            // frames in this frame go too, and Rehome has moved their bodies out.
            Frames.Destroy(own, physicsChanges.FramesDestroyed);
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
                {
                    entities.AddComponent(change.Entity, change.Component);
                    // Its class constructor runs at the sync point, before the component's first run in the next tick.
                    chunk.AddedComponentTypes.Add(change.Component.GetType());
                }
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

    /// <summary>
    /// Runs the class constructor of a component type the first time the world sees it, outside the gameplay part of a
    /// tick. C# runs a static field initializer at the first use of its class, which could otherwise be a Handle call on
    /// a worker during a tick, where building the heap part of an entity query throws. A class constructor that throws
    /// is reported, and the component's calls then fail as they would have.
    /// </summary>
    private void InitializeComponentType(Type type)
    {
        Contract.Requires(!GameplayThread.InEntityRun);
        if (initializedComponentTypes.Contains(type))
            return;
        try
        {
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }
        catch (TypeInitializationException ex)
        {
            Report($"tick {TickNumber}: the class constructor of the component type {type.FullName} failed: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}", isError: true);
        }
        initializedComponentTypes.Add(type);
    }

    /// <summary>Step 1 of the sync point: the class constructors of the component types that the entities added during the tick.</summary>
    private void InitializeAddedComponentTypes()
    {
        foreach (ChunkState chunk in entities.Chunks)
            foreach (Type type in chunk.AddedComponentTypes)
                InitializeComponentType(type);
    }

    // ---- Frames, membership and topics ----------------------------------------------------------------------------

    /// <summary>The pose, velocities and parent of a frame, as the owner's transform row (see <see cref="OwnerRow"/>).</summary>
    private TransformRow FrameOwnerRow(FrameHandle frame) => OwnerRow(Frames.OwnerView(frame));

    /// <summary>
    /// The transform row of the owner of a frame whose owner view is <paramref name="view"/>. The hull of a frame lives in the
    /// system of the frame's tree parent, so that parent is the owner's physics home, and the row frame is its semantic
    /// frame. The pose moves from the home into the row frame in doubles. The velocity stays relative to the home.
    /// </summary>
    /// <remarks>Inlined: as a call that returns the 136-byte row, it cost the mirror pass about 10 ns per frame.</remarks>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static TransformRow OwnerRow(in FrameOwnerView view) => new()
    {
        Frame = view.Semantic,
        PhysicsHome = view.Home,
        Pose = new DPose(view.PositionInSemantic, view.LocalPose.Rotation),
        HomePosition = view.LocalPose.Position,
        LinearVelocity = view.LinearVelocity.ToVector3(),
        AngularVelocity = view.AngularVelocity,
    };

    /// <summary>The owner's transform row mirrors the frame's local pose, which is the authoritative one (see <see cref="MirrorOwner"/>).</summary>
    private void MirrorFrameOwner(FrameHandle frame) => MirrorOwner(Frames.OwnerView(frame));

    /// <summary>
    /// The mirror rule, for one frame and for the pass of <see cref="MirrorFrameOwners"/>: the row of the owner of the frame
    /// whose owner view is <paramref name="view"/> is filled from the view (see <see cref="OwnerRow"/>), and the owner moves
    /// to the members of the row's frame, the frame's semantic parent (see <see cref="MoveMembership"/>). The frame tree
    /// holds the owner's exact frame, so this is where the owner follows every move of its frame under another parent:
    /// a hand-off of its hull, which reparents the frame before the pass of step 2, and a move that the game makes
    /// itself, such as a docking, which the pass of step 2 of the next tick finds.
    /// </summary>
    /// <remarks>Inlined, so that the pass of <see cref="MirrorFrameOwners"/> pays no call per frame for it.</remarks>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void MirrorOwner(in FrameOwnerView view)
    {
        Entity owner = view.Frame.Entity;
        entities.SetBackTransform(owner, OwnerRow(view));
        MoveMembership(owner, view.Semantic);
    }

    /// <summary>
    /// Moves an entity to the member set of <paramref name="to"/>, its frame, and when that is a change of frame, sends it
    /// one <see cref="FrameChanged"/>, which the next routing delivers. A call for an entity that is a member of
    /// <paramref name="to"/> already changes nothing and sends nothing. So however many of the paths below see one change,
    /// the entity hears it once, from the first of them:
    /// <list type="bullet">
    /// <item>A hand-off moves a plain body (see <see cref="RouteStepContacts"/>).</item>
    /// <item>A move of a frame under another parent moves the frame's owner (see <see cref="MirrorOwner"/>): a hand-off of the
    /// hull, or a move that the game makes itself. Step 2 fills the owners' rows before it routes the hand-offs, so this
    /// path comes first for a hand-off of a hull.</item>
    /// <item>The death of the entity's frame moves it to the nearest ancestor that survives (see <see cref="Rehome"/>). For
    /// the owner of a frame, this comes before the owner's row is filled from the moved frame.</item>
    /// <item>A change of frame of an attached entity's chain root moves the attached entity (see
    /// <see cref="DeriveAttachments"/>).</item>
    /// </list>
    /// A new entity joins the members of its frame without it, because that is no change of frame.
    /// </summary>
    private void MoveMembership(Entity entity, FrameHandle to)
    {
        if (!topics.MoveMember(entity, to, out FrameHandle from))
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
            MoveMembership(attached, entities.BackRow(attached).Frame);
    }

    /// <summary>Applies the effects of an outside-tick change that the sync point would otherwise apply.</summary>
    private void FinishOutsideTickChange()
    {
        // A body spawned or moved outside a tick joins its numeric frame before the next step (see PrepareTick).
        numericUpdateDue = physics is not null;
        DeriveAttachments();
        SwapSnapshot();
        FlushLog();
    }
}
