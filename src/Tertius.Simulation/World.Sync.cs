using System.Numerics;

namespace Tertius.Simulation;

public sealed partial class World
{
    // Events that the sync point itself sends: FrameChanged and Collision. They join the routing first.
    private readonly RefList<Event> engineEvents = new(16);
    // Every routed message, stored once. Inboxes are index ranges into it. It stays valid through the next tick's run.
    private readonly RefList<Event> routed = new(256);
    private readonly Dictionary<long, RefList<Event>> timers = new();
    private readonly Stack<RefList<Event>> spareTimerLists = new();
    private uint[] pairRecipients = new uint[256];
    private int[] pairEvents = new int[256];
    private int pairCount;
    private int[] inboxStart = [];
    private int[] inboxFill = [];
    private int[] inboxEvents = new int[256];
    private int routedSlotCount;
    private int lastRoutedEvents, lastDeliveries;
    private readonly List<uint> physicsTouched = new();

    /// <summary>The sync point, steps 1 to 11 of ARCHITECTURE.md. One thread runs it; nothing else runs meanwhile.</summary>
    private void Sync(Exception? physicsError)
    {
        double endTime = TimeAt(TickNumber + 1);

        // 1. The gameplay chunks and the physics step are done.
        AdoptFallbackSlots();
        CollectChunkMessages();
        if (physicsError != null)
        {
            // A step that threw may have written only part of its output. Use none of it.
            Report($"tick {TickNumber}: the physics step failed and its results were dropped: {physicsError.GetType().Name}: {physicsError.Message}", isError: true);
            stepOutput.Clear();
        }

        // 2. Body states, hand-offs and contacts from physics.
        if (physics != null)
            ApplyPhysicsOutput();

        // 3. Physics owns the transform of every entity with a body.
        if (options.DebugChecks)
            CheckTransformOwnership();

        // 4. This tick's intents become the input of the next step.
        SwapIntents();

        // 5. Subscriptions, then dead entities.
        ApplySubscriptionChanges();
        CollectDestroyRequests();
        if (dying.Count > 0)
            DestroyDying();
        CompactIntentEntities();

        // 6. New entities, then one batch of structural changes for physics.
        MakeSpawnsLive(endTime);
        FlushPhysicsChanges();

        // 7. Frames: physics frames from the step, Kepler frames from time. Then the rows derived from them.
        AdvanceFrames(endTime);
        CommitTouchedSets();
        DeriveAttachments();
        CommitTouchedSets();

        // 8. Messages.
        QueueDelayedSends();
        Route();

        // 9. The back tables become the front tables.
        CopyDirtyRows();
        Frames.Update();
        UpdateSpatialGrids();

        // 10. The render snapshot of the new state.
        TickNumber++;
        Time = endTime;
        BuildRenderSnapshot();

        // 11. Ready for the next tick.
        int count = ChunkCountFor(runSlotCount);
        for (int c = 0; c < count; c++)
            chunks[c].EndTick();
    }

    /// <summary>Slots past the high-water mark that the spawn fallback took during the tick. The tables grow to cover them.</summary>
    private void AdoptFallbackSlots()
    {
        int end = Volatile.Read(ref slotCount);
        if (end <= runSlotCount)
            return;
        EnsureCapacity(end);
        for (int slot = runSlotCount; slot < end; slot++)
        {
            generation[slot] = 1;
            slotState[slot] = SlotState.Reserved;
        }
    }

    private void CollectChunkMessages()
    {
        int count = ChunkCountFor(runSlotCount);
        int fallback = 0;
        for (int c = 0; c < count; c++)
        {
            ChunkState chunk = chunks[c];
            foreach (ChunkMessage message in chunk.Messages)
                Report(message.Text, message.IsError);
            fallback += chunk.FallbackSpawns;
        }
        if (fallback > 0)
        {
            fallbackSpawnTotal += fallback;
            Report($"tick {TickNumber}: {fallback} spawns ran out of their chunk's reserved slot pool and took slots from the global fallback. " +
                   "Slot assignment for this tick depends on thread timing, so the run is no longer deterministic. Raise WorldOptions.SpawnPoolSize.", isError: false);
        }
    }

    private void ApplyPhysicsOutput()
    {
        physicsTouched.Clear();
        foreach (BodyState state in stepOutput.Bodies)
        {
            if (!IsAlive(state.Entity) || records[state.Entity.Index].Body == null)
                continue;
            uint slot = state.Entity.Index;
            ref TransformRow row = ref backTransform[slot];
            if (!state.Frame.IsNull)
                row.Frame = state.Frame;
            row.Pose = state.Pose;
            row.LinearVelocity = state.LinearVelocity;
            row.AngularVelocity = state.AngularVelocity;
            MarkSyncDirty(slot, SlotFlags.TransformDirty);
            TouchForPhysics(slot);
        }

        foreach (HandOff handOff in stepOutput.HandOffs)
        {
            if (!IsAlive(handOff.Entity))
                continue;
            if (!Frames.IsAlive(handOff.To))
            {
                Report($"tick {TickNumber}: ignored the hand-off of {handOff.Entity} to the stale frame {handOff.To}.", isError: true);
                continue;
            }
            uint slot = handOff.Entity.Index;
            backTransform[slot] = new TransformRow
            {
                Frame = handOff.To,
                Pose = handOff.Pose,
                LinearVelocity = handOff.LinearVelocity,
                AngularVelocity = handOff.AngularVelocity,
            };
            MarkSyncDirty(slot, SlotFlags.TransformDirty);
            TouchForPhysics(slot);

            FrameHandle own = records[slot].OwnedFrame;
            if (!own.IsNull && Frames.Parent(own) != handOff.To)
            {
                try
                {
                    Frames.Reparent(own, handOff.To);
                }
                catch (InvalidOperationException ex)
                {
                    Report($"tick {TickNumber}: cannot move frame {own} under {handOff.To}: {ex.Message}", isError: true);
                }
            }
        }

        // Each entity that physics moved to another frame changes topic and hears about it once.
        foreach (uint slot in physicsTouched)
        {
            flags[slot] &= ~SlotFlags.Listed;
            FrameHandle frame = backTransform[slot].Frame;
            if (frame != records[slot].MemberFrame)
                MoveMembership(slot, frame, sendFrameChanged: true);
        }
        CommitTouchedSets();

        foreach (ContactStarted contact in stepOutput.Contacts)
            RouteContact(contact);
    }

    private void TouchForPhysics(uint slot)
    {
        if ((flags[slot] & SlotFlags.Listed) != 0)
            return;
        flags[slot] |= SlotFlags.Listed;
        physicsTouched.Add(slot);
    }

    /// <summary>Sends a Collision to each live entity of a contact, with the point and normal in that entity's frame.</summary>
    private void RouteContact(in ContactStarted contact)
    {
        if (IsAlive(contact.A))
            AddCollision(contact.A, contact.B, contact.Frame, contact.Point, contact.Normal, contact.Impulse);
        if (IsAlive(contact.B))
            AddCollision(contact.B, contact.A, contact.Frame, contact.Point, -contact.Normal, contact.Impulse);
    }

    private void AddCollision(Entity recipient, Entity other, FrameHandle contactFrame, Double3 point, Vector3 normal, float impulse)
    {
        FrameHandle frame = backTransform[recipient.Index].Frame;
        if (frame.IsNull)
            frame = records[recipient.Index].OwnedFrame;
        if (!contactFrame.IsNull && !frame.IsNull && frame != contactFrame && Frames.IsAlive(contactFrame))
        {
            DPose toRecipient = Frames.Relative(contactFrame, frame);
            point = toRecipient.TransformPoint(point);
            normal = toRecipient.Rotation.Rotate(normal);
        }
        ref Event e = ref engineEvents.AddRef();
        e = Event.Create(new Target(recipient), Entity.Null, new Collision { Other = other, Point = point, Normal = normal, Impulse = impulse });
    }

    private void CheckTransformOwnership()
    {
        int count = ChunkCountFor(runSlotCount);
        for (int c = 0; c < count; c++)
            foreach (uint slot in chunks[c].DirtySlots)
                if ((flags[slot] & SlotFlags.GameplayWroteTransform) != 0 && records[slot].Body != null)
                    Report($"tick {TickNumber}: {EntityAt((int)slot)} has a physics body but gameplay wrote its transform.", isError: true);
    }

    private void SwapIntents()
    {
        foreach (Entity entity in frontIntentEntities.AsSpan())
            if (entity.Index < (uint)capacity)
                frontIntent[entity.Index] = default;
        frontIntentEntities.Clear();

        int count = ChunkCountFor(runSlotCount);
        for (int c = 0; c < count; c++)
        {
            foreach (uint slot in chunks[c].IntentSlots)
            {
                flags[slot] &= ~SlotFlags.IntentRegistered;
                // With no physics backend, intents are dropped: bodies do not move.
                if (physics != null && slotState[slot] == SlotState.Live)
                {
                    frontIntent[slot] = backIntent[slot];
                    frontIntentEntities.Add(EntityAt((int)slot));
                }
                backIntent[slot] = default;
            }
        }
    }

    private void CompactIntentEntities()
    {
        Span<Entity> entities = frontIntentEntities.AsSpan();
        int write = 0;
        foreach (Entity entity in entities)
            if (IsAlive(entity))
                entities[write++] = entity;
        frontIntentEntities.Truncate(write);
    }

    private void ApplySubscriptionChanges()
    {
        int count = ChunkCountFor(runSlotCount);
        for (int c = 0; c < count; c++)
        {
            foreach (SubscriptionChange change in chunks[c].SubscriptionChanges)
            {
                if (slotState[change.Slot] != SlotState.Live)
                    continue;
                if (change.Subscribe)
                    SubscribeSlot(change.Slot, change.Topic);
                else
                    UnsubscribeSlot(change.Slot, change.Topic);
            }
        }
        CommitTouchedSets();
    }

    private void CollectDestroyRequests()
    {
        int count = ChunkCountFor(runSlotCount);
        for (int c = 0; c < count; c++)
        {
            foreach (uint slot in chunks[c].Destroys)
            {
                flags[slot] &= ~SlotFlags.DestroyRequested;
                MarkDying(slot);
            }
        }
    }

    /// <summary>Step 7: physics frames from the step output, Kepler frames from time, then the owners' rows.</summary>
    private void AdvanceFrames(double endTime)
    {
        if (physics != null)
        {
            foreach (FrameState state in stepOutput.Frames)
            {
                if (!Frames.IsAlive(state.Frame))
                    continue;
                Frames.SetLocalPose(state.Frame, state.LocalPose);
                Frames.LinearVelocity(state.Frame) = state.LinearVelocity;
                Frames.AngularVelocity(state.Frame) = state.AngularVelocity;
                Frames.ProperAcceleration(state.Frame) = state.ProperAcceleration;
            }
        }

        int n = Frames.Count;
        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = Frames.AtDense(i);
            switch (Frames.Driver(frame))
            {
                case MotionDriver.Kepler:
                    ApplyKepler(frame, endTime);
                    break;
                case MotionDriver.Static:
                    // A static frame whose owner has no body moves when gameplay writes the owner's transform.
                    uint owner = frame.Entity.Index;
                    if ((flags[owner] & SlotFlags.GameplayWroteTransform) != 0)
                        Frames.SetLocalPose(frame, backTransform[owner].Pose);
                    break;
            }
            MirrorFrameOwner(frame);
        }
    }

    /// <summary>The owner's transform row mirrors the frame's local pose, which is the authoritative one.</summary>
    private void MirrorFrameOwner(FrameHandle frame)
    {
        uint owner = frame.Entity.Index;
        TransformRow row = FrameOwnerRow(frame);
        if (!RowEquals(backTransform[owner], row))
        {
            backTransform[owner] = row;
            MarkSyncDirty(owner, SlotFlags.TransformDirty);
        }
        if (records[owner].MemberFrame != row.Frame)
            MoveMembership(owner, row.Frame, sendFrameChanged: true);
    }

    private void QueueDelayedSends()
    {
        int count = ChunkCountFor(runSlotCount);
        for (int c = 0; c < count; c++)
        {
            RefList<DelayedEvent> delayed = chunks[c].Delayed;
            RefList<Event>? bucket = null;
            long bucketDue = 0;
            foreach (ref DelayedEvent d in delayed.AsSpan())
            {
                if (bucket is null || d.Due != bucketDue)
                {
                    bucketDue = d.Due;
                    bucket = TimerBucket(bucketDue);
                }
                bucket.Add(d.Event);
            }
            delayed.Clear();
        }
    }

    /// <summary>The messages due at <paramref name="tick"/>, in the order they were sent.</summary>
    private RefList<Event> TimerBucket(long tick)
    {
        if (!timers.TryGetValue(tick, out RefList<Event>? bucket))
        {
            bucket = spareTimerLists.Count > 0 ? spareTimerLists.Pop() : new RefList<Event>(8);
            timers.Add(tick, bucket);
        }
        return bucket;
    }

    /// <summary>
    /// Step 8. Joins the engine events, the chunk outboxes in chunk order, the delayed messages that are due (in the
    /// order they were sent) and the posted messages. Expands each target, drops recipients whose mask lacks the type,
    /// and builds the inboxes with a stable counting sort by recipient slot.
    /// </summary>
    private void Route()
    {
        routed.Clear();
        routed.AddRange(engineEvents.AsSpan());
        engineEvents.Clear();
        int count = ChunkCountFor(runSlotCount);
        for (int c = 0; c < count; c++)
        {
            RefList<Event> outbox = chunks[c].Outbox;
            routed.AddRange(outbox.AsSpan());
            outbox.Clear();
        }
        if (timers.Remove(TickNumber, out RefList<Event>? due))
        {
            routed.AddRange(due.AsSpan());
            due.Clear();
            spareTimerLists.Push(due);
        }
        while (posted.TryDequeue(out Event e))
            routed.Add(e);

        pairCount = 0;
        Event[] store = routed.Items;
        for (int i = 0; i < routed.Count; i++)
        {
            ref Event e = ref store[i];
            int type = e.Type;
            if (e.Target.TryGetValue(out Entity target))
            {
                if (IsAlive(target))
                    AddDelivery(target.Index, i, type);
            }
            else if (e.Target.TryGetValue(out Topic topic))
            {
                ExpandTopic(topic, i, type);
            }
            else if (e.Target.TryGetValue(out Broadcast _))
            {
                for (int slot = 0; slot < slotCount; slot++)
                    if (slotState[slot] == SlotState.Live)
                        AddDelivery((uint)slot, i, type);
            }
        }

        // Stable counting sort of the deliveries by recipient: each inbox keeps routing order.
        int n = slotCount;
        Span<int> start = inboxStart.AsSpan(0, n + 1);
        start.Clear();
        for (int p = 0; p < pairCount; p++)
            start[(int)pairRecipients[p] + 1]++;
        for (int s = 1; s <= n; s++)
            start[s] += start[s - 1];
        if (inboxFill.Length < n)
            inboxFill = new int[Math.Max(n, inboxFill.Length * 2)];
        start[..n].CopyTo(inboxFill);
        if (inboxEvents.Length < pairCount)
            inboxEvents = new int[Math.Max(pairCount, inboxEvents.Length * 2)];
        for (int p = 0; p < pairCount; p++)
            inboxEvents[inboxFill[pairRecipients[p]]++] = pairEvents[p];

        routedSlotCount = n;
        foreach (uint slot in staleInboxes)
            flags[slot] &= ~SlotFlags.InboxStale;
        staleInboxes.Clear();
        lastRoutedEvents = routed.Count;
        lastDeliveries = pairCount;
    }

    private void ExpandTopic(Topic topic, int eventIndex, int type)
    {
        if (!topic.IsFrameTopic)
        {
            if (topic.Id < namedTopics.Count && namedTopics[topic.Id] is { } set)
                foreach (uint slot in set.Items)
                    AddDelivery(slot, eventIndex, type);
            return;
        }

        int frame = topic.FrameIndex;
        if (!FrameIndexAlive(frame))
            return;
        // The frame's members and its explicit subscribers, merged in slot order without duplicates.
        ReadOnlySpan<uint> a = frameMembers[frame] is { } members ? members.Items : default;
        ReadOnlySpan<uint> b = frameTopicSubscribers[frame] is { } subscribers ? subscribers.Items : default;
        int i = 0, j = 0;
        while (i < a.Length || j < b.Length)
        {
            uint slot;
            if (j == b.Length || (i < a.Length && a[i] < b[j]))
                slot = a[i++];
            else if (i == a.Length || b[j] < a[i])
                slot = b[j++];
            else
            {
                slot = a[i++];
                j++;
            }
            AddDelivery(slot, eventIndex, type);
        }
    }

    private void AddDelivery(uint slot, int eventIndex, int type)
    {
        // Every mask contains Destroy, so a Destroy always gets through.
        if (slotState[slot] != SlotState.Live || !masks[slot].Contains(type))
            return;
        if (pairCount == pairRecipients.Length)
        {
            Array.Resize(ref pairRecipients, pairCount * 2);
            Array.Resize(ref pairEvents, pairCount * 2);
        }
        pairRecipients[pairCount] = slot;
        pairEvents[pairCount] = eventIndex;
        pairCount++;
    }

    /// <summary>Step 9: copies every dirty back row forward. Rows that did not change cost nothing.</summary>
    private void CopyDirtyRows()
    {
        int count = ChunkCountFor(runSlotCount);
        for (int c = 0; c < count; c++)
            foreach (uint slot in chunks[c].DirtySlots)
                CopyRow(slot);
        foreach (uint slot in syncDirty)
            CopyRow(slot);
        syncDirty.Clear();
    }

    private void CopyRow(uint slot)
    {
        SlotFlags f = flags[slot];
        if ((f & (SlotFlags.TransformDirty | SlotFlags.RenderDirty)) == 0)
            return;
        flags[slot] = f & ~(SlotFlags.TransformDirty | SlotFlags.RenderDirty | SlotFlags.GameplayWroteTransform);
        if (slotState[slot] != SlotState.Live)
            return;
        if ((f & SlotFlags.TransformDirty) != 0)
        {
            frontTransform[slot] = backTransform[slot];
            QueueGridUpdate(slot);
        }
        if ((f & SlotFlags.RenderDirty) != 0)
            frontRender[slot] = backRender[slot];
    }

    /// <summary>Moves each entity whose front transform changed into its current grid cell.</summary>
    private void UpdateSpatialGrids()
    {
        foreach (uint slot in gridPending)
        {
            flags[slot] &= ~SlotFlags.GridPending;
            if (slotState[slot] != SlotState.Live)
                continue;
            ref EntityRecord rec = ref records[slot];
            ref TransformRow row = ref frontTransform[slot];
            bool inGrid = !row.Frame.IsNull;
            int frame = inGrid ? row.Frame.Index : 0;
            CellKey cell = inGrid ? SpatialGrid.CellOf(row.Pose.Position) : default;
            if (rec.InGrid == inGrid && (!inGrid || (rec.GridFrame == frame && rec.GridCell == cell)))
                continue;
            if (rec.InGrid)
                grids[rec.GridFrame]?.Remove(rec.GridCell, slot);
            if (inGrid)
                (grids[frame] ??= new SpatialGrid()).Add(cell, slot);
            rec.InGrid = inGrid;
            rec.GridFrame = frame;
            rec.GridCell = cell;
        }
        gridPending.Clear();
    }

    internal void QueryRadius(FrameHandle frame, Double3 center, double radius, List<Entity> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        results.Clear();
        if (!Frames.IsAlive(frame))
            throw new InvalidOperationException($"{frame} is stale or null.");
        if (!(radius >= 0))
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "The radius must be zero or more.");
        if (frame.Index >= grids.Length || grids[frame.Index] is not { } grid)
            return;

        double r2 = radius * radius;
        long x0 = SpatialGrid.Coordinate(center.X - radius), x1 = SpatialGrid.Coordinate(center.X + radius);
        long y0 = SpatialGrid.Coordinate(center.Y - radius), y1 = SpatialGrid.Coordinate(center.Y + radius);
        long z0 = SpatialGrid.Coordinate(center.Z - radius), z1 = SpatialGrid.Coordinate(center.Z + radius);
        double boxCells = (double)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
        if (boxCells > grid.CellCount)
        {
            // A large radius: visiting the occupied cells is cheaper than visiting the box.
            foreach (KeyValuePair<CellKey, List<uint>> cell in grid.Cells)
                CollectWithin(cell.Value, center, r2, results);
        }
        else
        {
            for (long x = x0; x <= x1; x++)
                for (long y = y0; y <= y1; y++)
                    for (long z = z0; z <= z1; z++)
                        if (grid.TryGetCell(new CellKey(x, y, z), out List<uint>? slots))
                            CollectWithin(slots, center, r2, results);
        }
        results.Sort();
    }

    private void CollectWithin(List<uint> slots, Double3 center, double r2, List<Entity> results)
    {
        foreach (uint slot in slots)
            if ((frontTransform[slot].Pose.Position - center).LengthSquared <= r2)
                results.Add(EntityAt((int)slot));
    }
}
