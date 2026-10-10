using System.Runtime.CompilerServices;
using DestroyEvent = Tertius.Destroy;
using TickEvent = Tertius.Tick;

namespace Tertius.Simulation;

public sealed partial class World
{
    private const int MaxSpawnPool = 1024;

    // Each worker thread fills its own event list. A list never outlives one entity run.
    [ThreadStatic]
    private static EventBuffer? workerEvents;

    // The slot count when the gameplay part of the tick started. Only these slots ran, so only their chunks hold results.
    private int runSlotCount;
    private readonly RefList<Entity> frontIntentEntities = new(64);

    /// <summary>
    /// Runs one tick. Physics steps on its own thread while the gameplay chunks run in parallel; both read only the
    /// front snapshot and the frame tree. Then the sync point merges the results. See ARCHITECTURE.md.
    /// </summary>
    public void Tick()
    {
        ThrowIfDisposed();
        if (inTick)
            throw new InvalidOperationException("World.Tick is not re-entrant.");
        inTick = true;
        bool stepping = false;
        try
        {
            PrepareTick();
            if (physicsWorker != null)
            {
                stepOutput.Clear();
                physicsWorker.Begin(new PhysicsStepContext(TickNumber, DeltaTime, Time, Frames, frontIntent, frontIntentEntities.AsMemory()));
                stepping = true;
            }

            RunGameplay();

            Exception? physicsError = null;
            if (stepping)
            {
                stepping = false;
                physicsError = physicsWorker!.Wait();
            }
            Sync(physicsError);
        }
        finally
        {
            if (stepping)
                physicsWorker!.Wait();
            inTick = false;
        }
        FlushLog();
    }

    private double TimeAt(long tick) => options.StartTime + tick * (double)DeltaTime;

    private void PrepareTick()
    {
        // Spawns and destroys made outside the tick reach physics before the step that may touch them.
        FlushPhysicsChanges();
        Frames.Update();
        RefillSpawnPools();
        runSlotCount = slotCount;
    }

    private void FlushPhysicsChanges()
    {
        PhysicsChanges changes = physicsChanges;
        if (changes.FramesCreated.Count == 0 && changes.BodiesDestroyed.Count == 0 &&
            changes.FramesDestroyed.Count == 0 && changes.BodiesCreated.Count == 0)
            return;
        try
        {
            physics?.ApplyChanges(changes, Frames);
        }
        catch (Exception ex)
        {
            Report($"tick {TickNumber}: the physics backend failed to apply changes: {ex.GetType().Name}: {ex.Message}", isError: true);
        }
        changes.Clear();
    }

    /// <summary>
    /// Reserves free slots for each chunk with live entities, in chunk order, so that spawns during the next tick get
    /// the same handles in every run. A chunk with no live entities returns its pool.
    /// </summary>
    private void RefillSpawnPools()
    {
        int count = ChunkCountFor(slotCount);
        for (int c = 0; c < count; c++)
        {
            ChunkState chunk = chunks[c];
            if (chunk.LiveCount == 0 || options.SpawnPoolSize == 0)
            {
                while (chunk.SpawnPool.TryDequeue(out uint slot))
                {
                    slotState[slot] = SlotState.Free;
                    freeSlots.Enqueue(slot);
                }
                continue;
            }
            int target = Math.Min(Math.Max(options.SpawnPoolSize, chunk.LastSpawnCount * 2), MaxSpawnPool);
            while (chunk.SpawnPool.Count < target)
                chunk.SpawnPool.Enqueue(AllocateSlot());
        }
    }

    private void RunGameplay()
    {
        int count = ChunkCountFor(runSlotCount);
        if (count == 0)
            return;
        if (options.MaxDegreeOfParallelism == 1 || count == 1)
        {
            for (int c = 0; c < count; c++)
                RunChunk(c);
        }
        else
        {
            Parallel.For(0, count, parallelOptions, runChunk);
        }
    }

    private void RunChunk(int chunkIndex)
    {
        ChunkState chunk = chunks[chunkIndex];
        if (chunk.LiveCount == 0)
            return;
        EventBuffer buffer = workerEvents ??= new EventBuffer(64);
        int tickType = EventType<TickEvent>.Id;
        int spawnedType = EventType<Spawned>.Id;
        int start = chunkIndex * chunkSize;
        int end = Math.Min(start + chunkSize, runSlotCount);
        for (int slot = start; slot < end; slot++)
        {
            if (slotState[slot] != SlotState.Live)
                continue;
            SlotFlags f = flags[slot];
            bool first = (f & SlotFlags.FirstTick) != 0;
            if (first)
                flags[slot] = f & ~SlotFlags.FirstTick;

            int inboxBegin = 0, inboxEnd = 0;
            if (slot < routedSlotCount && (f & SlotFlags.InboxStale) == 0)
            {
                inboxBegin = inboxStart[slot];
                inboxEnd = inboxStart[slot + 1];
            }
            ref EventMask mask = ref masks[slot];
            bool wantsTick = mask.Contains(tickType);
            bool wantsSpawned = first && mask.Contains(spawnedType);
            if (inboxBegin == inboxEnd && !wantsTick && !wantsSpawned)
                continue;
            RunEntity(chunk, buffer, slot, inboxBegin, inboxEnd, wantsTick, wantsSpawned);
        }
    }

    private void RunEntity(ChunkState chunk, EventBuffer buffer, int slot, int inboxBegin, int inboxEnd, bool wantsTick, bool wantsSpawned)
    {
        var entity = new Entity((uint)slot, generation[slot]);
        EventList events = buffer.Begin(entity);
        if (wantsTick)
            events.Add(new TickEvent { Number = TickNumber, DeltaTime = DeltaTime });
        if (wantsSpawned)
            events.Add(new Spawned());
        Event[] store = routed.Items;
        for (int i = inboxBegin; i < inboxEnd; i++)
            events.Add(in store[inboxEvents[i]]);

        ref EntityRecord rec = ref records[slot];
        IComponent[] components = rec.Components ?? [];
        EventMask[] wants = rec.Wants ?? [];
        var self = new Self(this, chunk, slot, entity);
        var outbox = new Outbox(chunk, entity, TickNumber);
        for (int c = 0; c < components.Length; c++)
        {
            if (!wants[c].Overlaps(buffer.Mask))
                continue;
            try
            {
                components[c].Handle(self, ref events, ref outbox);
            }
            catch (Exception ex)
            {
                chunk.Messages.Add(new ChunkMessage(
                    $"tick {TickNumber}: {entity} {components[c].GetType().FullName}: {ex.GetType().Name}: {ex.Message}", true));
            }
        }

        // A Destroy that no component removed destroys the entity at the sync point.
        if (events.Contains<DestroyEvent>())
            RequestDestroy(chunk, slot);
        if (chunk.ComponentChanges.Count > 0)
            ApplyComponentChanges(chunk);
    }

    // ---- Writable rows for Self ----------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkDirty(ChunkState chunk, int slot, SlotFlags bits)
    {
        SlotFlags f = flags[slot];
        if ((f & (SlotFlags.TransformDirty | SlotFlags.RenderDirty)) == 0)
            chunk.DirtySlots.Add((uint)slot);
        flags[slot] = f | bits;
    }

    internal ref DPose WritableTransform(ChunkState chunk, int slot)
    {
        ref EntityRecord rec = ref records[slot];
        if (rec.Body != null)
            throw new InvalidOperationException($"{EntityAt(slot)} has a physics body, so physics owns its transform. Move it through Self.Body.");
        if (!rec.AttachParent.IsNull)
            throw new InvalidOperationException($"{EntityAt(slot)} is attached to {rec.AttachParent}, so its transform follows the parent. Change Self.AttachOffset instead.");
        if (!rec.OwnedFrame.IsNull && Frames.Driver(rec.OwnedFrame) != MotionDriver.Static)
            throw new InvalidOperationException($"{EntityAt(slot)} owns a {Frames.Driver(rec.OwnedFrame)} frame, so the frame's motion driver sets its pose.");
        MarkDirty(chunk, slot, SlotFlags.TransformDirty | SlotFlags.GameplayWroteTransform);
        return ref backTransform[slot].Pose;
    }

    internal ref RenderData WritableRender(ChunkState chunk, int slot)
    {
        MarkDirty(chunk, slot, SlotFlags.RenderDirty);
        return ref backRender[slot];
    }

    internal ref PhysicsIntent WritableIntent(ChunkState chunk, int slot)
    {
        if (records[slot].Body == null)
            throw new InvalidOperationException($"{EntityAt(slot)} has no physics body.");
        SlotFlags f = flags[slot];
        if ((f & SlotFlags.IntentRegistered) == 0)
        {
            flags[slot] = f | SlotFlags.IntentRegistered;
            chunk.IntentSlots.Add((uint)slot);
        }
        return ref backIntent[slot];
    }

    internal PhysicsIntent PendingIntent(int slot) => backIntent[slot];
}
