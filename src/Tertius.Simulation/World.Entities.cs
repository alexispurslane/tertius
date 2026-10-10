using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tertius.Simulation;

public sealed partial class World
{
    // Membership: the entities in each frame, by frame index. Every entity belongs to the set of its frame, which is
    // what makes a frame topic reach "every entity in that frame".
    private SubscriberList?[] frameMembers = new SubscriberList?[16];
    // Explicit subscribers of a frame topic who are not necessarily in the frame, by frame index.
    private SubscriberList?[] frameTopicSubscribers = new SubscriberList?[16];
    // Live frame handles by frame index, so that a frame topic (which carries only an index) can be checked.
    private FrameHandle[] frameAt = new FrameHandle[16];
    private readonly List<SubscriberList?> namedTopics = new();
    private readonly List<SubscriberList> touchedSets = new();

    // Attachment chains. Chains never span frames, so one tree holds them all, with one root per chain.
    private readonly PoseTree<DPose> attachTree = new(16);
    private readonly SubscriberList attachedSlots = new();

    // Sync-point work lists.
    private readonly List<uint> dying = new();
    private readonly List<uint> syncDirty = new();
    private readonly List<uint> gridPending = new();
    private readonly List<uint> scratchSlots = new();
    private readonly List<uint> staleInboxes = new();
    private readonly List<FrameHandle> scratchFrames = new();
    private SpatialGrid?[] grids = new SpatialGrid?[16];

    /// <summary>
    /// Creates a root frame and the entity that owns it. The owner is in no frame; its pose is the frame's pose.
    /// Outside a tick only.
    /// </summary>
    public FrameHandle CreateRootFrame(FrameSpec spec, RenderData? render = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Entity owner = Spawn(new SpawnDesc { AsFrame = spec, Render = render });
        return records[owner.Index].OwnedFrame;
    }

    /// <summary>
    /// Creates an entity. Outside a tick it is live at once and gets <see cref="Spawned"/> on its first tick.
    /// During a tick, components call <see cref="Self.Spawn(SpawnDesc)"/> instead.
    /// </summary>
    public Entity Spawn(SpawnDesc desc)
    {
        ThrowIfInTick(nameof(Spawn));
        SpawnPlan plan = PrepareSpawn(desc);
        if (!desc.AttachTo.IsNull)
            ValidateAttachParent(desc.AttachTo, desc.Frame, throwOnError: true);

        uint slot = AllocateSlot();
        var entity = new Entity(slot, generation[slot]);
        MakeLive(entity, desc, plan, Time);
        if (!desc.AttachTo.IsNull)
            Attach(slot, desc.AttachTo, desc.AttachOffset);
        FinishOutsideTickChange();
        return entity;
    }

    /// <summary>Creates an entity from a registered blueprint. See <see cref="Spawn(SpawnDesc)"/>.</summary>
    public Entity Spawn(string blueprint, FrameHandle frame, in DPose pose) => Spawn(Blueprints.CreateSpawnDesc(blueprint, frame, pose));

    /// <summary>
    /// Destroys an entity at once, together with its attached children. If it owns a frame, the frame's contents move
    /// to the parent frame (or are destroyed, for a root frame). Outside a tick only; during a tick, send a
    /// <see cref="Tertius.Destroy"/> message or call <see cref="Self.Destroy"/>.
    /// </summary>
    public void Destroy(Entity entity)
    {
        ThrowIfInTick(nameof(Destroy));
        ThrowIfStale(entity);
        // Earlier outside-tick creations must reach physics before this batch, which may destroy them.
        FlushPhysicsChanges();
        MarkDying(entity.Index);
        DestroyDying();
        CompactIntentEntities();
        FlushPhysicsChanges();
        FinishOutsideTickChange();
    }

    /// <summary>Adds a component to an entity. Outside a tick only.</summary>
    public void AddComponent(Entity entity, IComponent component)
    {
        ThrowIfInTick(nameof(AddComponent));
        ThrowIfStale(entity);
        ArgumentNullException.ThrowIfNull(component);
        InsertComponent(entity.Index, component);
    }

    /// <summary>Removes a component from an entity. Returns false if the entity did not have it. Outside a tick only.</summary>
    public bool RemoveComponent(Entity entity, IComponent component)
    {
        ThrowIfInTick(nameof(RemoveComponent));
        ThrowIfStale(entity);
        ArgumentNullException.ThrowIfNull(component);
        return DeleteComponent(entity.Index, component);
    }

    /// <summary>Subscribes an entity to a topic. Outside a tick only.</summary>
    public void Subscribe(Entity entity, Topic topic)
    {
        ThrowIfInTick(nameof(Subscribe));
        ThrowIfStale(entity);
        SubscribeSlot(entity.Index, topic);
        CommitTouchedSets();
        FlushLog();
    }

    /// <summary>Unsubscribes an entity from a topic. Outside a tick only.</summary>
    public void Unsubscribe(Entity entity, Topic topic)
    {
        ThrowIfInTick(nameof(Unsubscribe));
        ThrowIfStale(entity);
        UnsubscribeSlot(entity.Index, topic);
        CommitTouchedSets();
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

    /// <summary>Takes a slot from the free list, or a fresh one. Outside a tick or at the sync point only.</summary>
    private uint AllocateSlot()
    {
        uint slot;
        if (freeSlots.Count > 0)
        {
            slot = freeSlots.Dequeue();
        }
        else
        {
            slot = (uint)slotCount++;
            EnsureCapacity(slotCount);
            generation[slot] = 1;
        }
        slotState[slot] = SlotState.Reserved;
        return slot;
    }

    /// <summary>Returns a slot to the free list. The generation increments, so every handle to the old entity is stale.</summary>
    private void FreeSlot(uint slot)
    {
        uint next = generation[slot] + 1;
        generation[slot] = next == 0 ? 1 : next;
        slotState[slot] = SlotState.Free;
        flags[slot] = SlotFlags.None;
        freeSlots.Enqueue(slot);
    }

    /// <summary>A spawn from a component. The slot comes from the running chunk's pool, so the handle is final and deterministic.</summary>
    internal Entity SpawnDuringTick(ChunkState chunk, SpawnDesc desc)
    {
        SpawnPlan plan = PrepareSpawn(desc);
        uint slot, gen;
        if (chunk.SpawnPool.TryDequeue(out slot))
        {
            gen = generation[slot];
        }
        else
        {
            // The pool ran dry. A fresh slot past the high-water mark is safe to take from any thread; the tables
            // grow to cover it at the sync point. Which chunk gets which slot now depends on thread timing.
            slot = (uint)(Interlocked.Increment(ref slotCount) - 1);
            gen = 1;
            chunk.FallbackSpawns++;
        }
        chunk.SpawnsThisTick++;
        var entity = new Entity(slot, gen);
        chunk.Spawns.Add(new PendingSpawn(entity, desc, plan));
        return entity;
    }

    /// <summary>Writes every row of a new entity and registers it with frames, physics, topics and the grid.</summary>
    private void MakeLive(Entity entity, SpawnDesc desc, SpawnPlan plan, double time)
    {
        uint slot = entity.Index;
        ref EntityRecord rec = ref records[slot];
        rec = default;
        rec.AttachOffset = Pose.Identity;

        var row = new TransformRow
        {
            Frame = desc.Frame,
            Pose = desc.Pose,
            LinearVelocity = desc.LinearVelocity,
            AngularVelocity = desc.AngularVelocity,
        };
        if (desc.AsFrame is { } spec)
        {
            FrameHandle own = Frames.Create(entity, desc.Frame, desc.Pose, spec.Bounds, spec.Driver, spec.Gravity, spec.Orbit);
            rec.OwnedFrame = own;
            RegisterFrame(own);
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

        frontTransform[slot] = row;
        backTransform[slot] = row;
        RenderData render = desc.Render ?? new RenderData();
        frontRender[slot] = render;
        backRender[slot] = render;
        frontIntent[slot] = default;
        backIntent[slot] = default;

        rec.Body = desc.Body;
        if (desc.Body != null)
            physicsChanges.BodiesCreated.Add(new BodyCreate(entity, row.Frame, desc.Body, row.Pose, row.LinearVelocity, row.AngularVelocity));

        rec.Components = plan.Components;
        rec.Wants = plan.Wants;
        masks[slot] = MaskOf(plan.Wants);
        slotState[slot] = SlotState.Live;
        flags[slot] = SlotFlags.FirstTick;
        if (!inTick && slot < (uint)routedSlotCount)
        {
            flags[slot] |= SlotFlags.InboxStale;
            staleInboxes.Add(slot);
        }
        liveCount++;
        chunks[slot / chunkSize].LiveCount++;

        MoveMembership(slot, row.Frame, sendFrameChanged: false);
        foreach (Topic topic in plan.Subscriptions)
            SubscribeSlot(slot, topic);
        QueueGridUpdate(slot);
    }

    /// <summary>Makes the spawns of every chunk live, in chunk order. Step 6 of the sync point.</summary>
    private void MakeSpawnsLive(double time)
    {
        int count = ChunkCountFor(runSlotCount);
        for (int c = 0; c < count; c++)
        {
            foreach (PendingSpawn spawn in chunks[c].Spawns)
            {
                FrameHandle frame = spawn.Desc.Frame;
                if (!frame.IsNull && !Frames.IsAlive(frame))
                {
                    Report($"tick {TickNumber}: dropped the spawn of {spawn.Entity}: its frame {frame} was destroyed before it became live.", isError: true);
                    FreeSlot(spawn.Entity.Index);
                    continue;
                }
                MakeLive(spawn.Entity, spawn.Desc, spawn.Plan, time);
            }
        }
        CommitTouchedSets();

        // Attach after every spawn is live, so that a child may name a parent spawned later in the same tick.
        for (int c = 0; c < count; c++)
        {
            foreach (PendingSpawn spawn in chunks[c].Spawns)
            {
                Entity parent = spawn.Desc.AttachTo;
                if (parent.IsNull || !IsAlive(spawn.Entity))
                    continue;
                string? problem = ValidateAttachParent(parent, spawn.Desc.Frame, throwOnError: false);
                if (problem == null)
                {
                    Attach(spawn.Entity.Index, parent, spawn.Desc.AttachOffset);
                }
                else
                {
                    Report($"tick {TickNumber}: destroyed {spawn.Entity} as it became live: {problem}", isError: true);
                    MarkDying(spawn.Entity.Index);
                }
            }
        }
        if (dying.Count > 0)
            DestroyDying();
    }

    // ---- Destruction ----------------------------------------------------------------------------------------------

    internal void RequestDestroy(ChunkState chunk, int slot)
    {
        SlotFlags f = flags[slot];
        if ((f & SlotFlags.DestroyRequested) != 0)
            return;
        flags[slot] = f | SlotFlags.DestroyRequested;
        chunk.Destroys.Add((uint)slot);
    }

    /// <summary>Adds a live entity to the destroy batch. Returns true if it was not in the batch yet.</summary>
    private bool MarkDying(uint slot)
    {
        if (slotState[slot] != SlotState.Live || (flags[slot] & SlotFlags.Dying) != 0)
            return false;
        flags[slot] |= SlotFlags.Dying;
        dying.Add(slot);
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
                ref EntityRecord rec = ref records[dying[i]];
                if (rec.AttachChildren is { } children)
                    foreach (uint child in children)
                        grew |= MarkDying(child);
                if (!rec.OwnedFrame.IsNull && SurvivingAncestor(rec.OwnedFrame).IsNull && frameMembers[rec.OwnedFrame.Index] is { } members)
                    foreach (uint member in members.Items)
                        grew |= MarkDying(member);
            }
        }

        // Move the survivors out of each dying frame, straight into the nearest surviving ancestor.
        for (int i = 0; i < dying.Count; i++)
        {
            FrameHandle frame = records[dying[i]].OwnedFrame;
            if (frame.IsNull)
                continue;
            FrameHandle target = SurvivingAncestor(frame);
            if (target.IsNull)
                continue;
            scratchSlots.Clear();
            if (frameMembers[frame.Index] is { } members)
                foreach (uint member in members.Items)
                    if ((flags[member] & SlotFlags.Dying) == 0)
                        scratchSlots.Add(member);
            foreach (uint member in scratchSlots)
                Rehome(member, frame, target);
            scratchFrames.Clear();
            Frames.GetChildren(frame, scratchFrames);
            foreach (FrameHandle child in scratchFrames)
            {
                if ((flags[child.Entity.Index] & SlotFlags.Dying) != 0)
                    continue;
                Frames.Reparent(child, target);
                MirrorFrameOwner(child);
            }
        }
        CommitTouchedSets();

        foreach (uint slot in dying)
            FreeEntity(slot);
        dying.Clear();
        CommitTouchedSets();
    }

    /// <summary>The nearest ancestor of a frame whose owner is not in the destroy batch, or null.</summary>
    private FrameHandle SurvivingAncestor(FrameHandle frame)
    {
        for (FrameHandle parent = Frames.Parent(frame); !parent.IsNull; parent = Frames.Parent(parent))
            if ((flags[parent.Entity.Index] & SlotFlags.Dying) == 0)
                return parent;
        return FrameHandle.Null;
    }

    /// <summary>Moves an entity from a dying frame into an ancestor frame, keeping its pose and velocity relative to everything else.</summary>
    private void Rehome(uint slot, FrameHandle from, FrameHandle to)
    {
        ref TransformRow row = ref backTransform[slot];
        DPose relative = Frames.Relative(from, to);
        Double3 position = row.Pose.Position;
        var moved = new TransformRow
        {
            Frame = to,
            Pose = DPose.Compose(relative, row.Pose),
            LinearVelocity = Frames.ConvertVelocity(from, to, position, row.LinearVelocity).ToVector3(),
            AngularVelocity = (relative.TransformDirection(row.AngularVelocity) + Frames.AngularVelocityRelative(from, to)).ToVector3(),
        };
        row = moved;
        MarkSyncDirty(slot, SlotFlags.TransformDirty);
        MoveMembership(slot, to, sendFrameChanged: true);

        // The body moves to the other physics system: physics destroys it before it creates the new one.
        if (records[slot].Body is { } body)
        {
            Entity entity = EntityAt((int)slot);
            physicsChanges.BodiesDestroyed.Add(entity);
            physicsChanges.BodiesCreated.Add(new BodyCreate(entity, to, body, moved.Pose, moved.LinearVelocity, moved.AngularVelocity));
        }
    }

    private void FreeEntity(uint slot)
    {
        ref EntityRecord rec = ref records[slot];
        Entity entity = EntityAt((int)slot);

        if (!rec.OwnedFrame.IsNull && Frames.IsAlive(rec.OwnedFrame))
        {
            FrameHandle frame = rec.OwnedFrame;
            physicsChanges.FramesDestroyed.Add(frame);
            if (frameTopicSubscribers[frame.Index] is { } subscribers)
            {
                Topic topic = frame.Topic;
                foreach (uint s in subscribers.Items)
                    records[s].Subscriptions?.Remove(topic);
                subscribers.Clear();
            }
            frameMembers[frame.Index]?.Clear();
            frameAt[frame.Index] = FrameHandle.Null;
            Frames.Destroy(frame);
        }
        if (rec.Body != null)
            physicsChanges.BodiesDestroyed.Add(entity);

        if (!rec.MemberFrame.IsNull)
            QueueSet(frameMembers[rec.MemberFrame.Index], slot, false);
        if (rec.Subscriptions is { } topics)
            foreach (Topic topic in topics)
                QueueSet(TopicSet(topic, create: false), slot, false);
        Detach(slot);
        if (rec.InGrid)
            grids[rec.GridFrame]?.Remove(rec.GridCell, slot);

        frontTransform[slot] = default;
        backTransform[slot] = default;
        frontRender[slot] = default;
        backRender[slot] = default;
        frontIntent[slot] = default;
        backIntent[slot] = default;
        masks[slot] = default;
        rec = default;
        liveCount--;
        chunks[slot / chunkSize].LiveCount--;
        FreeSlot(slot);
    }

    // ---- Components -----------------------------------------------------------------------------------------------

    private void InsertComponent(uint slot, IComponent component)
    {
        IComponent[] old = ComponentsOf((int)slot);
        foreach (IComponent existing in old)
            if (ReferenceEquals(existing, component))
                throw new InvalidOperationException($"{EntityAt((int)slot)} already has this {component.GetType().Name} instance.");
        int priority = component.Priority;
        int at = old.Length;
        while (at > 0 && old[at - 1].Priority > priority)
            at--;
        IComponent[] components = [.. old.AsSpan(0, at), component, .. old.AsSpan(at)];
        EventMask[] oldWants = records[slot].Wants ?? [];
        EventMask[] wants = [.. oldWants.AsSpan(0, at), component.Wants, .. oldWants.AsSpan(at)];
        records[slot].Components = components;
        records[slot].Wants = wants;
        masks[slot] = MaskOf(wants);
    }

    private bool DeleteComponent(uint slot, IComponent component)
    {
        IComponent[] old = ComponentsOf((int)slot);
        int at = Array.FindIndex(old, c => ReferenceEquals(c, component));
        if (at < 0)
            return false;
        EventMask[] oldWants = records[slot].Wants!;
        IComponent[] components = [.. old.AsSpan(0, at), .. old.AsSpan(at + 1)];
        EventMask[] wants = [.. oldWants.AsSpan(0, at), .. oldWants.AsSpan(at + 1)];
        records[slot].Components = components;
        records[slot].Wants = wants;
        masks[slot] = MaskOf(wants);
        return true;
    }

    /// <summary>Applies the component changes that the running entity made. Runs on the worker, right after the entity's run.</summary>
    private void ApplyComponentChanges(ChunkState chunk)
    {
        foreach (ComponentChange change in chunk.ComponentChanges)
        {
            try
            {
                if (change.Add)
                    InsertComponent(change.Slot, change.Component);
                else if (!DeleteComponent(change.Slot, change.Component))
                    chunk.Messages.Add(new ChunkMessage($"tick {TickNumber}: {EntityAt((int)change.Slot)}: RemoveComponent: the entity has no such {change.Component.GetType().Name} instance.", false));
            }
            catch (Exception ex)
            {
                chunk.Messages.Add(new ChunkMessage($"tick {TickNumber}: {EntityAt((int)change.Slot)}: {(change.Add ? "AddComponent" : "RemoveComponent")} {change.Component.GetType().FullName} failed: {ex.GetType().Name}: {ex.Message}", true));
            }
        }
        chunk.ComponentChanges.Clear();
    }

    // ---- Frames, membership and topics ----------------------------------------------------------------------------

    private void RegisterFrame(FrameHandle frame)
    {
        int index = frame.Index;
        if (index >= frameAt.Length)
        {
            int size = Math.Max(index + 1, frameAt.Length * 2);
            Array.Resize(ref frameAt, size);
            Array.Resize(ref frameMembers, size);
            Array.Resize(ref frameTopicSubscribers, size);
            Array.Resize(ref grids, size);
        }
        frameAt[index] = frame;
    }

    private bool FrameIndexAlive(int index) => (uint)index < (uint)frameAt.Length && !frameAt[index].IsNull;

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

    /// <summary>Moves an entity to the member set of <paramref name="to"/>. Optionally queues a <see cref="FrameChanged"/> for it.</summary>
    private void MoveMembership(uint slot, FrameHandle to, bool sendFrameChanged)
    {
        ref EntityRecord rec = ref records[slot];
        FrameHandle from = rec.MemberFrame;
        if (from == to)
            return;
        if (!from.IsNull && FrameIndexAlive(from.Index))
            QueueSet(frameMembers[from.Index], slot, false);
        if (!to.IsNull)
            QueueSet(frameMembers[to.Index] ??= new SubscriberList(), slot, true);
        rec.MemberFrame = to;
        if (sendFrameChanged)
        {
            ref Event e = ref engineEvents.AddRef();
            e = Event.Create(new Target(EntityAt((int)slot)), Entity.Null, new FrameChanged { From = from, To = to });
        }
    }

    private void QueueSet(SubscriberList? set, uint slot, bool member)
    {
        if (set != null && set.Queue(slot, member))
            touchedSets.Add(set);
    }

    private void CommitTouchedSets()
    {
        foreach (SubscriberList set in touchedSets)
            set.Commit();
        touchedSets.Clear();
    }

    /// <summary>The explicit subscriber set of a topic. Null for the topic of a frame that is not alive.</summary>
    private SubscriberList? TopicSet(Topic topic, bool create)
    {
        if (topic.IsFrameTopic)
        {
            int index = topic.FrameIndex;
            if (!FrameIndexAlive(index))
                return null;
            return create ? frameTopicSubscribers[index] ??= new SubscriberList() : frameTopicSubscribers[index];
        }
        int id = topic.Id;
        if (id >= namedTopics.Count)
        {
            if (!create)
                return null;
            CollectionsMarshal.SetCount(namedTopics, id + 1);
        }
        return create ? namedTopics[id] ??= new SubscriberList() : namedTopics[id];
    }

    private void SubscribeSlot(uint slot, Topic topic)
    {
        ref EntityRecord rec = ref records[slot];
        if (rec.Subscriptions?.Contains(topic) == true)
            return;
        SubscriberList? set = TopicSet(topic, create: true);
        if (set == null)
        {
            Report($"tick {TickNumber}: {EntityAt((int)slot)} cannot subscribe to {Topics.NameOf(topic)}: that frame is not alive.", isError: false);
            return;
        }
        (rec.Subscriptions ??= new List<Topic>(2)).Add(topic);
        QueueSet(set, slot, true);
    }

    private void UnsubscribeSlot(uint slot, Topic topic)
    {
        ref EntityRecord rec = ref records[slot];
        if (rec.Subscriptions?.Remove(topic) != true)
            return;
        QueueSet(TopicSet(topic, create: false), slot, false);
    }

    // ---- Attachments ----------------------------------------------------------------------------------------------

    private string? ValidateAttachParent(Entity parent, FrameHandle frame, bool throwOnError)
    {
        string? problem = null;
        if (!IsAlive(parent))
            problem = $"the attachment parent {parent} is not live.";
        else if (backTransform[parent.Index].Frame != frame)
            problem = $"the attachment parent {parent} is in {backTransform[parent.Index].Frame}, not in {frame}.";
        if (problem != null && throwOnError)
            throw new ArgumentException($"Cannot attach: {problem}", "desc");
        return problem;
    }

    private void Attach(uint slot, Entity parent, Pose offset)
    {
        ref EntityRecord parentRec = ref records[parent.Index];
        if (parentRec.AttachNode.IsNull)
            parentRec.AttachNode = attachTree.Add(PoseNode.Null, backTransform[parent.Index].Pose);
        List<uint> children = parentRec.AttachChildren ??= new List<uint>(2);
        int at = children.BinarySearch(slot);
        children.Insert(at < 0 ? ~at : at, slot);

        ref EntityRecord rec = ref records[slot];
        rec.AttachParent = parent;
        rec.AttachOffset = offset;
        rec.AttachNode = attachTree.Add(parentRec.AttachNode, SafePose(offset).ToDPose());
        // The transform is derived with every other attachment, after the touched sets are committed.
        QueueSet(attachedSlots, slot, true);
    }

    private void Detach(uint slot)
    {
        ref EntityRecord rec = ref records[slot];
        if (!rec.AttachParent.IsNull)
        {
            uint parent = rec.AttachParent.Index;
            ref EntityRecord parentRec = ref records[parent];
            parentRec.AttachChildren?.Remove(slot);
            QueueSet(attachedSlots, slot, false);
            // A chain root with no children left leaves the tree.
            if (parentRec.AttachChildren is { Count: 0 } && parentRec.AttachParent.IsNull && !parentRec.AttachNode.IsNull)
            {
                attachTree.Remove(parentRec.AttachNode);
                parentRec.AttachNode = PoseNode.Null;
            }
        }
        if (!rec.AttachNode.IsNull)
            attachTree.Remove(rec.AttachNode);
        rec.AttachNode = PoseNode.Null;
        rec.AttachParent = Entity.Null;
    }

    internal ref Pose WritableAttachOffset(int slot)
    {
        ref EntityRecord rec = ref records[slot];
        if (rec.AttachParent.IsNull)
            throw new InvalidOperationException($"{EntityAt(slot)} is not attached to a parent.");
        return ref rec.AttachOffset;
    }

    internal Entity AttachParentOf(int slot) => records[slot].AttachParent;

    /// <summary>
    /// Derives the transform of every attached entity from its chain root, parent before child. The chain root's
    /// row comes from the back table, so this runs after physics and the frame owners have been written.
    /// </summary>
    private void DeriveAttachments()
    {
        ReadOnlySpan<uint> attached = attachedSlots.Items;
        if (attached.IsEmpty)
            return;
        foreach (uint slot in attached)
        {
            ref EntityRecord rec = ref records[slot];
            attachTree.Local(rec.AttachNode) = SafePose(rec.AttachOffset).ToDPose();
            ref EntityRecord parentRec = ref records[rec.AttachParent.Index];
            if (parentRec.AttachParent.IsNull)
                attachTree.Local(parentRec.AttachNode) = backTransform[rec.AttachParent.Index].Pose;
        }
        attachTree.UpdateWorld();

        foreach (uint slot in attached)
        {
            ref EntityRecord rec = ref records[slot];
            uint root = rec.AttachParent.Index;
            while (!records[root].AttachParent.IsNull)
                root = records[root].AttachParent.Index;
            ref TransformRow rootRow = ref backTransform[root];
            DPose pose = attachTree.World(rec.AttachNode);
            Double3 lever = pose.Position - rootRow.Pose.Position;
            var derived = new TransformRow
            {
                Frame = rootRow.Frame,
                Pose = pose,
                LinearVelocity = rootRow.LinearVelocity + Double3.Cross(rootRow.AngularVelocity, lever).ToVector3(),
                AngularVelocity = rootRow.AngularVelocity,
            };
            if (!RowEquals(backTransform[slot], derived))
            {
                backTransform[slot] = derived;
                MarkSyncDirty(slot, SlotFlags.TransformDirty);
            }
            if (rec.MemberFrame != derived.Frame)
                MoveMembership(slot, derived.Frame, sendFrameChanged: true);
        }
    }

    // ---- Shared helpers -------------------------------------------------------------------------------------------

    /// <summary>Marks a back row as changed by the sync point itself.</summary>
    private void MarkSyncDirty(uint slot, SlotFlags bits)
    {
        SlotFlags f = flags[slot];
        if ((f & (SlotFlags.TransformDirty | SlotFlags.RenderDirty)) == 0)
            syncDirty.Add(slot);
        flags[slot] = f | bits;
    }

    private void QueueGridUpdate(uint slot)
    {
        if ((flags[slot] & SlotFlags.GridPending) != 0)
            return;
        flags[slot] |= SlotFlags.GridPending;
        gridPending.Add(slot);
    }

    /// <summary>Applies the effects of an outside-tick change that the sync point would otherwise apply.</summary>
    private void FinishOutsideTickChange()
    {
        CommitTouchedSets();
        DeriveAttachments();
        CommitTouchedSets();
        CopyDirtyRows();
        UpdateSpatialGrids();
        Frames.Update();
        FlushLog();
    }

    private static bool RowEquals(in TransformRow a, in TransformRow b) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<TransformRow>(in a)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<TransformRow>(in b)));

    /// <summary>A pose with a usable rotation: a zeroed quaternion, as in a defaulted struct, becomes the identity.</summary>
    private static Pose SafePose(Pose pose) =>
        pose.Rotation == default ? pose with { Rotation = Quaternion.Identity } : pose;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool BitEquals(in DPose a, in DPose b) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<DPose>(in a)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<DPose>(in b)));
}
