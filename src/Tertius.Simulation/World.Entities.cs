using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

public sealed partial class World
{
    // The destroy batch of the sync point or of an outside-tick Destroy: the dying entities in the order they joined
    // it, and a mark per entity for a constant-time test. Releasing an entity clears its mark.
    private readonly List<Entity> dying = [];

    private readonly EntityTable.Column<bool> dyingMarks;

    // Owners of surviving frames whose parent frame died in the batch. Their rows follow the frame tree afterwards.
    private readonly List<Entity> movedFrameOwners = [];

    // The attached spawns of the sync point that wait for their parent to become live, in chunk order.
    private readonly List<PendingSpawn> waitingAttachedSpawns = [];

    // The component types whose class constructor the world has run (see InitializeComponentType).
    private readonly HashSet<Type> initializedComponentTypes = [];

    // Scratch of ConvertIntentsOfDyingFrames: the conversions of the destroy batch, in the order of the intent entities.
    private readonly List<IntentConversion> dyingFrameConversions = [];

    /// <summary>
    /// Creates a root frame and the entity that owns it. The owner is in no frame; its pose is the frame's pose.
    /// Outside a tick only.
    /// </summary>
    public FrameHandle CreateRootFrame(FrameSpec spec, RenderData? render = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Entity owner = Spawn(new() { AsFrame = spec, Render = render });
        return Entities.OwnedFrame(owner);
    }

    /// <summary>
    /// Creates an entity. Outside a tick it is live at once and gets <see cref="Spawned"/> on its first tick.
    /// During a tick, components call <see cref="Self.Spawn(SpawnDesc)"/> instead. Throws <see cref="ArgumentException"/>,
    /// before anything changes, for a description that the world cannot make live: a pose that is not valid (see
    /// <see cref="DPose.IsValid"/>), a velocity that is not finite, a body that physics cannot create (see
    /// <see cref="BodyDesc.Problem"/> and <see cref="IPhysicsBackend.ShapeProblem"/>, which checks a frame's hull as a body
    /// with the motion of its description), a gravity field or a medium that is not valid, or an attachment
    /// parent that is not live or that owns a root frame, a first motion intent that the entity would refuse (see
    /// <see cref="SpawnDesc.FirstMove"/>), or a rig pose that it refuses (see <see cref="SpawnDesc.RigPose"/>). The World checks nothing else about rails motion: a rails component checks its own
    /// rules (see <see cref="KeplerMotion"/> and <see cref="SpinMotion"/>). An attached entity is live in
    /// the frame of its parent, whatever frame the description names (see <see cref="SpawnDesc.AttachTo"/>).
    /// </summary>
    public Entity Spawn(SpawnDesc desc)
    {
        ThrowIfInTick(nameof(Spawn));
        SpawnPlan plan = PrepareSpawn(desc);
        FrameHandle frame = desc.Frame;
        if (!desc.AttachTo.IsNull && Attachments.CheckParent(desc.AttachTo, out frame) is { } problem)
            throw new ArgumentException($"Cannot attach: {problem}", nameof(desc));

        Entity entity = Entities.Reserve();
        MakeLive(entity: entity, desc: desc, plan: plan, frame: frame);
        if (!desc.AttachTo.IsNull)
            Attachments.Attach(entity, desc.AttachTo, desc.AttachOffset);
        FinishOutsideTickChange();
        return entity;
    }

    /// <summary>Creates an entity from a registered blueprint. See <see cref="Spawn(SpawnDesc)"/>.</summary>
    public Entity Spawn(string blueprint, FrameHandle frame, in DPose pose) =>
        Spawn(Blueprints.CreateSpawnDesc(blueprint, frame, pose));

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
        if (!Entities.IsAlive(entity))
            return false;
        // Physics applies destroys before creates, so earlier outside-tick creations must reach it before this batch,
        // which may destroy them.
        FlushPhysicsChanges();
        // Outside a tick no part's push waits: the sync point carried the pushes of the last tick.
        MarkDying(entity);
        GrowDestroyBatch();
        FinishDestroyBatch();
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
        Entities.AddComponent(entity, component);
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
        if (!Entities.RemoveComponent(entity, component))
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
            throw new ArgumentException(
                $"Cannot give {frame} this gravity field: the field is not valid (see GravityField.IsValid).",
                nameof(gravity)
            );

        Frames.SetGravity(frame, gravity);
    }

    /// <summary>
    /// Gives a frame a new medium, outside a tick only (see <see cref="FrameTree.Medium"/>). The frame's puffs feel it from
    /// the next step on. Physics reads no medium, so the frame's bodies move as they did.
    /// </summary>
    /// <exception cref="InvalidOperationException">During a tick, when the sync point reads the frame tree.</exception>
    /// <exception cref="ArgumentException">
    /// A frame that is gone, or a medium that is not valid (see <see cref="MediumField.IsValid"/>). The frame keeps its medium.
    /// </exception>
    public void SetFrameMedium(FrameHandle frame, in MediumField medium)
    {
        ThrowIfInTick(nameof(SetFrameMedium));
        if (!Frames.IsAlive(frame))
            throw new ArgumentException($"Cannot set the medium of {frame}: the frame is gone.", nameof(frame));
        if (!medium.IsValid)
            throw new ArgumentException(
                $"Cannot give {frame} this medium: the medium is not valid (see MediumField.IsValid).",
                nameof(medium)
            );

        Frames.SetMedium(frame, medium);
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
        CheckSpawnFrame(desc);
        CheckSpawnMotion(desc);
        CheckSpawnAttachment(desc);
        CheckSpawnBody(desc);
        CheckSpawnFrameSpec(desc);
        if (SpawnVelocityProblem(desc) is { } velocityProblem)
            throw new ArgumentException(velocityProblem, nameof(desc));
        if (FirstMoveProblem(desc) is { } firstMove)
            throw new ArgumentException(
                $"The new entity cannot take the first motion intent {desc.FirstMove}: it {firstMove}.",
                nameof(desc)
            );
        if (RigPoseProblem(desc) is { } rigPose)
            throw new ArgumentException($"The new entity cannot take its rig pose: {rigPose}.", nameof(desc));
        (IComponent[] components, EventMask[] wants) = ComponentsByPriority(desc);
        Topic[] subscriptions = desc.Subscriptions is { Count: > 0 } subs ? [.. subs] : [];
        return new(components, wants, subscriptions);
    }

    /// <summary>Throws unless <paramref name="desc"/> names a live frame, or needs none as a root frame or an attached entity.</summary>
    private void CheckSpawnFrame(SpawnDesc desc)
    {
        if (desc.Frame.IsNull)
        {
            // An attached entity takes the frame of its parent when it becomes live.
            if (desc.AsFrame is null && desc.AttachTo.IsNull)
                throw new ArgumentException(
                    "A spawned entity needs a frame. Only a frame entity can be a root, and only an attached entity takes the frame of its parent.",
                    nameof(desc)
                );
        }
        else if (!Frames.IsAlive(desc.Frame))
        {
            throw new ArgumentException($"Cannot spawn in {desc.Frame}: the frame is stale.", nameof(desc));
        }
    }

    /// <summary>Throws unless the pose and the velocities of <paramref name="desc"/> are ones that physics can take.</summary>
    private static void CheckSpawnMotion(SpawnDesc desc)
    {
        // Physics turns a pose or velocity that is not finite into NaN in its first step, and a zero rotation too.
        if (!desc.Pose.IsValid)
            throw new ArgumentException(
                $"Cannot spawn at the pose {desc.Pose}: its position must be finite, and its rotation finite and not zero.",
                nameof(desc)
            );
        if (!IsFinite(desc.LinearVelocity))
            throw new ArgumentException(
                $"Cannot spawn with the linear velocity {desc.LinearVelocity}: it must be finite.",
                nameof(desc)
            );
        if (!IsFinite(desc.AngularVelocity))
            throw new ArgumentException(
                $"Cannot spawn with the angular velocity {desc.AngularVelocity}: it must be finite.",
                nameof(desc)
            );
    }

    /// <summary>Throws when an attached entity in <paramref name="desc"/> has a body or owns a frame.</summary>
    private static void CheckSpawnAttachment(SpawnDesc desc)
    {
        if (desc.AttachTo.IsNull)
            return;
        if (desc.Body != null)
            throw new ArgumentException("An attached entity cannot have a physics body.", nameof(desc));
        if (desc.AsFrame != null)
            throw new ArgumentException("An attached entity cannot be a frame.", nameof(desc));
    }

    /// <summary>Throws unless physics can create the body of <paramref name="desc"/>, if it has one, in its role.</summary>
    private void CheckSpawnBody(SpawnDesc desc)
    {
        if (desc.Body is not { } body)
            return;
        bool ownsFrame = desc.AsFrame is not null;
        // The body of a frame owner is the frame's hull when the frame has a parent. A root frame has no hull.
        BodyRole role = BodyRole.OwnsNoFrame;
        if (ownsFrame)
            role = desc.Frame.IsNull ? BodyRole.OwnsRootFrame : BodyRole.Hull;
        if (body.Problem(role) is { } problem)
            throw new ArgumentException($"Physics cannot create this body: it {problem}.", nameof(desc));
        // Only the backend can tell that it cannot build some shapes, for example a convex hull whose points coincide.
        if (Physics?.ShapeProblem(body.Shape) is { } shape)
            throw new ArgumentException($"Physics cannot create this body: its shape {shape}.", nameof(desc));
        if (ownsFrame && body.InteriorShape is { } interior && Physics?.ShapeProblem(interior) is { } inside)
            throw new ArgumentException($"Physics cannot create this body: its interior shape {inside}.", nameof(desc));
    }

    /// <summary>Throws unless the frame tree can create the frame of <paramref name="desc"/>, if it owns one.</summary>
    private static void CheckSpawnFrameSpec(SpawnDesc desc)
    {
        if (desc.AsFrame is not { } spec)
            return;
        if (!(spec.Bounds.MinRadius > 0))
            throw new ArgumentException("A frame needs a bubble with positive radii.", nameof(desc));
        if (!spec.Gravity.IsValid)
            throw new ArgumentException(
                "A frame needs a gravity field that physics can apply: this gravity field is not valid (see GravityField.IsValid).",
                nameof(desc)
            );
        if (!spec.Medium.IsValid)
            throw new ArgumentException(
                "A frame needs a medium with numbers in their ranges: this medium is not valid (see MediumField.IsValid).",
                nameof(desc)
            );
    }

    /// <summary>
    /// The components of <paramref name="desc"/> in a stable order of priority, and what each wants. Reads each component's
    /// priority and wants once, and gives its type an id.
    /// </summary>
    private static (IComponent[] Components, EventMask[] Wants) ComponentsByPriority(SpawnDesc desc)
    {
        IReadOnlyList<IComponent> source = desc.Components ?? [];
        var components = new IComponent[source.Count];
        int[] priorities = new int[source.Count];
        var wants = new EventMask[source.Count];
        for (int i = 0; i < components.Length; i++)
        {
            IComponent component =
                source[i]
                ?? throw new ArgumentException($"Component {i} of the spawn description is null.", nameof(desc));
            for (int j = 0; j < i; j++)
                if (ReferenceEquals(components[j], component))
                    throw new ArgumentException(
                        $"The same {component.GetType().Name} instance appears twice. Each entity needs its own component instances.",
                        nameof(desc)
                    );
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

        return (components, wants);
    }

    /// <summary>
    /// The sentence that refuses the spawn velocities of <paramref name="desc"/>, or null. A spawn velocity is the first
    /// velocity of a body that physics integrates, a dynamic body or a character, and physics carries it on from there. Every
    /// other entity moves only by its motion intents, its teleports, its transform writes or its parent, so a velocity at its
    /// spawn would claim a motion that nothing carries out. A kinematic hull is the one exception: its spawn velocity is its
    /// frame's velocity until its first sync point, from which the numeric update gives a fast hull its following frame at
    /// spawn (docs/plans/rails-components.md, "Which entity takes a spawn velocity").
    /// </summary>
    private static string? SpawnVelocityProblem(SpawnDesc desc)
    {
        if (desc.LinearVelocity == Double3.Zero && desc.AngularVelocity == Vector3.Zero)
            return null;
        if (OnlyMover(desc) is not { } mover)
            return null;
        string velocity =
            desc.LinearVelocity != Double3.Zero
                ? $"the linear velocity {desc.LinearVelocity}"
                : $"the angular velocity {desc.AngularVelocity}";
        return $"{mover}, so it cannot spawn with {velocity}.";
    }

    /// <summary>
    /// What alone moves an entity of <paramref name="desc"/>, as the start of a sentence, or null when physics carries a
    /// spawn velocity on (see <see cref="SpawnVelocityProblem"/>).
    /// </summary>
    private static string? OnlyMover(SpawnDesc desc)
    {
        bool ownsFrame = desc.AsFrame is not null;
        if (!desc.AttachTo.IsNull)
            return "An attached entity moves with its parent";
        if (desc.Body is not { } body)
            return "An entity without a body moves only by its motion intents and transform writes";
        if (ownsFrame && desc.Frame.IsNull)
            return "The owner of a root frame has no hull, so only its motion intents move it";
        return body.Motion switch
        {
            BodyMotion.Static => "A static body moves only by its teleports",
            BodyMotion.Kinematic when !ownsFrame =>
                "A kinematic body that owns no frame moves only by its motion intents and teleports",
            BodyMotion.Kinematic or BodyMotion.Dynamic or BodyMotion.Character => null,
        };
    }

    /// <summary>
    /// The reason that the entity of <paramref name="desc"/> refuses its first motion intent (see
    /// <see cref="SpawnDesc.FirstMove"/>), or null. It judges the case by the rules of the guard (see
    /// <see cref="MotionIntentProblem"/>), through <see cref="MotionIntent.ProblemFor"/>, with three facts of the
    /// description: whether it attaches the entity, the motion of its body, which the body keeps in its first step, because a
    /// change of motion that the new entity writes in its first tick acts in its second step, and whether the entity owns a
    /// root frame. Then it refuses a number that is not finite and a zero rotation, as the writers do.
    /// </summary>
    private static string? FirstMoveProblem(SpawnDesc desc)
    {
        MotionIntent move = desc.FirstMove;
        bool ownsRootFrame = desc.AsFrame is not null && desc.Frame.IsNull;
        return move.ProblemFor(desc.Body?.Motion, attached: !desc.AttachTo.IsNull, ownsRootFrame)
            ?? MotionIntentNumberProblem(move);
    }

    /// <summary>
    /// The reason that a spawn refuses the rig pose of <paramref name="desc"/>, or null (see <see cref="SpawnDesc.RigPose"/>):
    /// a rig pose without a render row, a default array, more joint poses than a rig has, a number that is not finite, or a
    /// rotation that is not a rotation. A tick can still write any value into it, and the renderer's guards catch that.
    /// </summary>
    private static string? RigPoseProblem(SpawnDesc desc)
    {
        if (desc.RigPose is not { } pose)
            return null;
        if (desc.Render is null)
            return "it has no render row to pose";
        if (pose.Joints.IsDefault || pose.Weights.IsDefault)
            return "its joint poses or its weights are not set";
        if (pose.Joints.Length > Rig.MaxJoints)
            return $"it has {pose.Joints.Length} joint poses, and a rig has at most {Rig.MaxJoints} joints";
        for (int joint = 0; joint < pose.Joints.Length; joint++)
        {
            Pose each = pose.Joints[joint];
            Quaternion rotation = each.Rotation;
            bool finite = IsFinite(each.Position) && IsFinite(rotation);
            if (!finite)
                return $"the pose {each} of the joint {joint} is not finite";
            // The rule of Rig for a rest rotation: a rotation that the renderer can normalize.
            float lengthSquared = rotation.LengthSquared();
            bool isRotation = float.IsFinite(lengthSquared) && lengthSquared > 1e-12f;
            if (!isRotation)
                return $"the rotation {rotation} of the joint {joint} is not a rotation";
        }

        for (int morph = 0; morph < pose.Weights.Length; morph++)
            if (!float.IsFinite(pose.Weights[morph]))
                return $"the weight {pose.Weights[morph]} of the morph {morph} is not finite";
        return null;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool IsFinite(Quaternion q) =>
        float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W);

    private static bool IsFinite(Double3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    /// <summary>
    /// A spawn by the running entity <paramref name="spawner"/>. The slot comes from the spawner's chunk pool, so the
    /// handle is final and deterministic. Throws <see cref="StaleEntityException"/> for a stale spawner.
    /// </summary>
    internal Entity SpawnDuringTick(ChunkState chunk, Entity spawner, SpawnDesc desc)
    {
        SpawnPlan plan = PrepareSpawn(desc);
        Entity entity = Entities.ReserveForSpawn(spawner);
        chunk.Spawns.Add(new(entity, desc, plan));
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
            own = CreateOwnedFrame(entity: entity, desc: desc, spec: spec, parent: frame, pose: pose);
            row = FrameOwnerRow(own);
            velocity = Frames.LinearVelocity(own);
        }

        WriteSpawnRows(entity: entity, desc: desc, plan: plan, row: row, own: own);
        // The first motion intent, relative to the spawn's frame, goes into the new entity's front intent, which the next step
        // applies as an intent that the entity wrote in the spawn's tick (docs/plans/rails-components.md, "The first motion
        // intent of a spawn"). Step 6 runs after the swap of step 4, and a spawn outside a tick goes live at the call, so either
        // way the next step is the entity's first.
        if (desc.FirstMove.HasValue)
            Entities.SetFirstMove(entity, frame, WithUnitRotation(desc.FirstMove));
        if (desc.Body != null)
            AddBodyCreate(
                new(
                    Entity: entity,
                    Frame: row.Frame,
                    Desc: desc.Body,
                    Pose: row.Pose,
                    LinearVelocity: velocity,
                    AngularVelocity: row.AngularVelocity
                )
            );

        // A new entity joins the members of its frame. That is no change of frame, so it hears no FrameChanged.
        _ = topics.MoveMember(entity, row.Frame);
        foreach (Topic topic in plan.Subscriptions)
            SubscribeEntity(entity, topic);
    }

    /// <summary>
    /// Writes the rows of the reserved <paramref name="entity"/> in the entity table: its transform <paramref name="row"/>,
    /// its render data, its components, its body and the frame <paramref name="own"/> that it owns, and its rig pose.
    /// </summary>
    private void WriteSpawnRows(Entity entity, SpawnDesc desc, SpawnPlan plan, in TransformRow row, FrameHandle own)
    {
        foreach (IComponent component in plan.Components)
            InitializeComponentType(component.GetType());
        // The backend keeps the shape that it built for the description, so the center of mass costs no second build. The
        // lever of every part of the entity is measured from it (docs/plans/parts.md).
        Vector3 centerOfMass =
            desc.Body is { } spawnBody && Physics != null ? Physics.ShapeCenterOfMass(spawnBody.Shape) : Vector3.Zero;
        Entities.Activate(
            entity: entity,
            row: row,
            render: desc.Render ?? new RenderData(),
            components: plan.Components,
            wants: plan.Wants,
            body: desc.Body,
            bodyCenterOfMass: centerOfMass,
            ownedFrame: own
        );
        if (desc.RigPose is { } rigPose)
            Entities.SetSpawnRigPose(entity, rigPose);
    }

    /// <summary>
    /// Creates the frame that <paramref name="entity"/> owns, below <paramref name="parent"/> at <paramref name="pose"/>,
    /// with the bubble, the fields, the hull motion and the velocities of <paramref name="desc"/>, and its topic.
    /// </summary>
    private FrameHandle CreateOwnedFrame(
        Entity entity,
        SpawnDesc desc,
        FrameSpec spec,
        FrameHandle parent,
        in DPose pose
    )
    {
        FrameHandle own = Frames.Create(
            frameOwner: entity,
            parent: parent,
            localPose: pose,
            frameBounds: spec.Bounds,
            field: spec.Gravity,
            medium: spec.Medium
        );
        // A frame with a parent whose owner has a body has a hull, with the motion of the body's description. A root frame
        // has no hull, and neither has a frame without a body: the tree gives a new frame no hull motion.
        if (!parent.IsNull && desc.Body is { } hullBody)
            Frames.SetHullMotion(own, hullBody.Motion);
        topics.AddFrame(own);
        // Every new frame stands at the pose of its spawn description, with its velocities. Its first step runs before its
        // components do, so a frame on rails follows the first motion intent of its spawn in that step, or without one
        // holds still there, and the first sample of its rails component puts it on its rails in the second step
        // (docs/plans/rails-components.md, "The first motion intent of a spawn").
        Frames.SetTreeLinearVelocity(own, desc.LinearVelocity);
        Frames.SetAngularVelocity(own, desc.AngularVelocity);
        physicsChanges.FramesCreated.Add(own);
        return own;
    }

    /// <summary>
    /// Makes the spawns of every chunk live, in chunk order. Step 6 of the sync point. The attached spawns come last, each
    /// after its parent, because each one takes the frame of its parent. So a child can also name a parent that a later
    /// chunk spawned in the same tick.
    /// </summary>
    private void MakeSpawnsLive()
    {
        foreach (ChunkState chunk in Entities.Chunks)
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
                Report(
                    $"tick {TickNumber}: dropped the spawn of {spawn.Entity}: its frame {frame} was destroyed before it became live.",
                    isError: true
                );
                Entities.Release(spawn.Entity);
                continue;
            }

            MakeLive(entity: spawn.Entity, desc: spawn.Desc, plan: spawn.Plan, frame: frame);
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
        for (bool madeOneLive = true; madeOneLive; )
        {
            madeOneLive = false;
            int kept = 0;
            for (int i = 0; i < waitingAttachedSpawns.Count; i++)
            {
                PendingSpawn spawn = waitingAttachedSpawns[i];
                if (Attachments.CheckParent(spawn.Desc.AttachTo, out FrameHandle frame) is null)
                {
                    MakeLive(entity: spawn.Entity, desc: spawn.Desc, plan: spawn.Plan, frame: frame);
                    Attachments.Attach(spawn.Entity, spawn.Desc.AttachTo, spawn.Desc.AttachOffset);
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
            string? problem = Attachments.CheckParent(spawn.Desc.AttachTo, out _);
            Report($"tick {TickNumber}: dropped the spawn of {spawn.Entity}: {problem}", isError: true);
            Entities.Release(spawn.Entity);
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
        if (!Entities.IsAlive(entity))
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
        if (!Entities.IsAlive(entity) || dyingMarks[entity])
            return false;
        dyingMarks[entity] = true;
        dying.Add(entity);
        return true;
    }

    /// <summary>
    /// Grows the destroy batch to everything that dies with it: the attached children of each dying entity, and the members
    /// of each dying frame that has no surviving ancestor. Step 5 of the sync point, and a destroy outside a tick, before
    /// <see cref="FinishDestroyBatch"/>.
    /// </summary>
    private void GrowDestroyBatch()
    {
        // Grow the batch until nothing changes. One pass is not enough: an ancestor frame can join the batch after
        // one of its descendants was looked at.
        for (bool grew = true; grew; )
        {
            grew = false;
            for (int i = 0; i < dying.Count; i++)
            {
                Entity entity = dying[i];
                foreach (Entity child in Attachments.ChildrenOf(entity))
                    grew |= MarkDying(child);
                FrameHandle own = Entities.OwnedFrame(entity);
                if (!own.IsNull && SurvivingAncestor(own).IsNull)
                    foreach (Entity member in topics.MembersOf(own))
                        grew |= MarkDying(member);
            }
        }
    }

    /// <summary>
    /// Destroys every entity of the batch that <see cref="GrowDestroyBatch"/> grew. The survivors in a dying frame move to the
    /// nearest ancestor frame that survives. Step 5 of the sync point, and a destroy outside a tick.
    /// </summary>
    private void FinishDestroyBatch()
    {
        // Move the survivors in each dying frame straight into the nearest surviving ancestor: rows, topics and
        // bodies. The frame tree moves surviving child frames itself when their parent dies (FrameTree.Destroy), so
        // their owners are only remembered here. The loop reads no topic set, so the member span stays valid.
        movedFrameOwners.Clear();
        for (int i = 0; i < dying.Count; i++)
        {
            FrameHandle frame = Entities.OwnedFrame(dying[i]);
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
                if (!Entities.OwnedFrame(member).IsNull)
                    movedFrameOwners.Add(member);
            }
        }

        ConvertIntentsOfDyingFrames();
        Puffs.MoveOutOfDyingFrames(heirOf);

        foreach (Entity entity in dying)
            FreeEntity(entity);
        dying.Clear();

        // Every dying frame is gone now, so the frame tree holds each moved child frame under its final parent, with
        // its pose and velocity converted. The owner's row mirrors the frame exactly. A KeplerMotion of the frame reads the
        // G·M of the new parent in the next tick, and holds the frame still when that parent has none.
        foreach (Entity owner in movedFrameOwners)
        {
            FrameHandle moved = Entities.OwnedFrame(owner);
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
    /// rehomes and before the frames die. Each converts by the motion of its old frame in its new one over the next step
    /// (see <see cref="FrameTree.MotionRelativeInStep"/>), in which every dying frame keeps its relation at the moment of its
    /// death. That motion reads the motion intents of the owners of the frames on the path, and some of those owners convert
    /// here too: a surviving frame whose parent dies moves with its owner. So every motion comes first, from the intents as
    /// gameplay wrote them, while the tree still holds each frame under the parent that they name, and the intents convert
    /// after.
    /// </summary>
    private void ConvertIntentsOfDyingFrames()
    {
        dyingFrameConversions.Clear();
        ReadOnlySpan<bool> dying = dyingMarks.AsSpan();
        foreach (Entity entity in Entities.IntentEntities.Span)
        {
            if (dyingMarks[entity])
                continue;
            FrameHandle from = Entities.NextStepIntent(entity).Frame;
            if (from.IsNull || !dyingMarks[from.Entity])
                continue;
            (FrameMotion start, FrameMotion end) = Frames.MotionRelativeInStep(
                of: from,
                to: Entities.BackRow(entity).Frame,
                stepIntents: Entities.FrontIntents.Span,
                step: DeltaTime,
                dying: dying
            );
            dyingFrameConversions.Add(new(entity, start, end));
        }

        foreach (IntentConversion conversion in dyingFrameConversions)
        {
            ref readonly TransformRow row = ref Entities.BackRow(conversion.Entity);
            ConvertIntent(
                intent: ref Entities.NextStepIntent(conversion.Entity),
                to: row.Frame,
                position: row.Pose.Position,
                start: conversion.Start,
                end: conversion.End
            );
        }

        dyingFrameConversions.Clear();
    }

    /// <summary>The motion of the old frame of <see cref="Entity"/>'s intent in its new frame over the next step, by which the intent converts.</summary>
    private readonly record struct IntentConversion(Entity Entity, FrameMotion Start, FrameMotion End);

    /// <summary>Moves an entity from a dying frame into an ancestor frame, keeping its pose and velocity relative to everything else.</summary>
    private void Rehome(Entity entity, FrameHandle from, FrameHandle to)
    {
        ref readonly TransformRow row = ref Entities.BackRow(entity);
        DPose relative = Frames.Relative(from, to);
        Double3 velocity = Frames.ConvertVelocity(
            from: from,
            to: to,
            position: row.Pose.Position,
            v: VelocityIn(entity: entity, row: row, to: from)
        );
        var pose = DPose.Compose(relative, row.Pose);
        var moved = new TransformRow
        {
            Frame = to,
            PhysicsHome = to,
            Pose = pose,
            HomePosition = pose.Position,
            LinearVelocity = velocity.ToVector3(),
            AngularVelocity = (
                relative.TransformDirection(row.AngularVelocity) + Frames.AngularVelocityRelative(from, to)
            ).ToVector3(),
        };
        Entities.SetBackTransform(entity, moved);
        MoveMembership(entity, to);

        // The body moves to the other physics system: physics destroys it before it creates the new one, as the body is
        // now: with the collision groups and mask that it has now, and for a hull, the motion of the next step, which the
        // new body runs first.
        if (Entities.BodyToRecreate(entity) is { } body)
        {
            FrameHandle own = Entities.OwnedFrame(entity);
            if (!own.IsNull && NextStepHullMotion(own) is { } motion && motion != body.Motion)
                body = body with { Motion = motion };
            physicsChanges.BodiesDestroyed.Add(entity);
            AddBodyCreate(
                new(
                    Entity: entity,
                    Frame: to,
                    Desc: body,
                    Pose: moved.Pose,
                    LinearVelocity: velocity,
                    AngularVelocity: moved.AngularVelocity
                )
            );
        }
    }

    /// <summary>
    /// Frees one entity of the batch: its frame and its body, then its slot. Releasing the entity takes it out of its
    /// topic sets, its attachment chain and its grid cell.
    /// </summary>
    private void FreeEntity(Entity entity)
    {
        FrameHandle own = Entities.OwnedFrame(entity);
        if (!own.IsNull && Frames.IsAlive(own))
        {
            physicsChanges.FramesDestroyed.Add(own);
            topics.RemoveFrame(own);
            // Moves each surviving child frame up to this frame's parent, converting its pose and velocity. The numeric
            // frames in this frame go too, and Rehome has moved their bodies out.
            Frames.Destroy(own, physicsChanges.FramesDestroyed);
        }

        if (Entities.Body(entity) != null)
            physicsChanges.BodiesDestroyed.Add(entity);
        Entities.Release(entity);
    }

    // ---- Components -----------------------------------------------------------------------------------------------

    /// <summary>Applies the component changes that the running entity made. Runs on the worker, right after the entity's run.</summary>
    private void ApplyComponentChanges(ChunkState chunk)
    {
        foreach (ComponentChange change in chunk.ComponentChanges)
            try
            {
                if (change.Add)
                {
                    Entities.AddComponent(change.Entity, change.Component);
                    // Its class constructor runs at the sync point, before the component's first run in the next tick.
                    chunk.AddedComponentTypes.Add(change.Component.GetType());
                }
                else if (!Entities.RemoveComponent(change.Entity, change.Component))
                {
                    chunk.Messages.Add(
                        new(
                            $"tick {TickNumber}: {change.Entity}: RemoveComponent: the entity has no such {change.Component.GetType().Name} instance.",
                            false
                        )
                    );
                }
            }
            catch (Exception ex) when (ex is not ContractException)
            {
                chunk.Messages.Add(
                    new(
                        $"tick {TickNumber}: {change.Entity}: {(change.Add ? "AddComponent" : "RemoveComponent")} {change.Component.GetType().FullName} failed: {ex.GetType().Name}: {ex.Message}",
                        true
                    )
                );
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
            Report(
                $"tick {TickNumber}: the class constructor of the component type {type.FullName} failed: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}",
                isError: true
            );
        }

        initializedComponentTypes.Add(type);
    }

    /// <summary>Step 1 of the sync point: the class constructors of the component types that the entities added during the tick.</summary>
    private void InitializeAddedComponentTypes()
    {
        foreach (ChunkState chunk in Entities.Chunks)
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TransformRow OwnerRow(in FrameOwnerView view) =>
        new()
        {
            Frame = view.Semantic,
            PhysicsHome = view.Home,
            Pose = new(view.PositionInSemantic, view.LocalPose.Rotation),
            HomePosition = view.LocalPose.Position,
            LinearVelocity = view.LinearVelocity.ToVector3(),
            AngularVelocity = view.AngularVelocity,
        };

    /// <summary>The owner's transform row mirrors the frame's local pose, which is the authoritative one (see
    /// <see cref="MirrorOwner"/>).</summary>
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MirrorOwner(in FrameOwnerView view)
    {
        Entity owner = view.Frame.Entity;
        Entities.SetBackTransform(owner, OwnerRow(view));
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
        if (topics.MoveMember(entity, to) is not { } change)
            return;
        router.AddEngineEvent(Event.Create(target: new(entity), sender: Entity.Null, payload: change));
    }

    /// <summary>Subscribes an entity, or reports why the topic's frame cannot take it. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.</summary>
    private void SubscribeEntity(Entity entity, Topic topic)
    {
        if (!topics.Subscribe(entity, topic))
            Report(
                $"tick {TickNumber}: {entity} cannot subscribe to {Topics.NameOf(topic)}: that frame is not alive.",
                isError: false
            );
    }

    /// <summary>
    /// Derives every attached entity's back row from its chain root. An attached entity that followed its root into
    /// another frame changes topic and hears about it.
    /// </summary>
    private void DeriveAttachments()
    {
        Attachments.Derive();
        foreach (Entity attached in Attachments.Attached)
            MoveMembership(attached, Entities.BackRow(attached).Frame);
    }

    /// <summary>Applies the effects of an outside-tick change that the sync point would otherwise apply.</summary>
    private void FinishOutsideTickChange()
    {
        // A body spawned or moved outside a tick joins its numeric frame before the next step (see PrepareTick).
        numericUpdateDue = Physics is not null;
        DeriveAttachments();
        SwapSnapshot();
        FlushLog();
    }
}
