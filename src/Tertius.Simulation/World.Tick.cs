using DestroyEvent = Tertius.Destroy;
using TickEvent = Tertius.Tick;

namespace Tertius.Simulation;

public sealed partial class World
{
    // Each worker thread fills its own event list. A list never outlives one entity run.
    [ThreadStatic]
    private static EventBuffer? workerEvents;

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
                physicsWorker.Begin(new PhysicsStepContext(TickNumber, DeltaTime, Time, Frames, entities.FrontIntents, entities.IntentEntities));
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
        entities.BeginTick(options.SpawnPoolSize);
    }

    /// <summary>Hands this batch of structural changes to physics, which applies destroys before creates.</summary>
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

    private void RunGameplay()
    {
        int count = entities.Chunks.Length;
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

    /// <summary>Runs every entity of one chunk that has mail, wants Tick, or is new and wants Spawned. On a worker.</summary>
    private void RunChunk(int chunkIndex)
    {
        ChunkState chunk = entities.Chunks[chunkIndex];
        EventBuffer buffer = workerEvents ??= new EventBuffer(64);
        int tickType = EventType<TickEvent>.Id;
        int spawnedType = EventType<Spawned>.Id;
        foreach (Entity entity in entities.LiveEntitiesOfChunk(chunkIndex))
        {
            bool first = entities.TakeFirstTick(entity);
            bool wantsTick = entities.Accepts(entity, tickType);
            bool wantsSpawned = first && entities.Accepts(entity, spawnedType);
            if (router.InboxCount(entity) == 0 && !wantsTick && !wantsSpawned)
                continue;
            RunEntity(chunk, buffer, entity, wantsTick, wantsSpawned);
        }
    }

    private void RunEntity(ChunkState chunk, EventBuffer buffer, Entity entity, bool wantsTick, bool wantsSpawned)
    {
        EventList events = buffer.Begin(entity);
        if (wantsTick)
            events.Add(new TickEvent { Number = TickNumber, DeltaTime = DeltaTime });
        if (wantsSpawned)
            events.Add(new Spawned());
        // The per-recipient copy runs here, on the worker, not at the serial sync point.
        router.CopyInbox(entity, events);

        ReadOnlySpan<IComponent> components = entities.Components(entity);
        ReadOnlySpan<EventMask> wants = entities.ComponentWants(entity);
        var self = new Self(this, chunk, entity);
        var outbox = new Outbox(chunk, entity, TickNumber);
        for (int c = 0; c < components.Length; c++)
        {
            if (!wants[c].Overlaps(buffer.Mask))
                continue;
            try
            {
                components[c].Handle(self, ref events, ref outbox);
            }
            // A broken contract is a bug in the engine, not in the component, so it is not survived like one.
            catch (Exception ex) when (ex is not ContractException)
            {
                chunk.Messages.Add(new ChunkMessage(
                    $"tick {TickNumber}: {entity} {components[c].GetType().FullName}: {ex.GetType().Name}: {ex.Message}", true));
            }
        }

        // A Destroy that no component removed destroys the entity at the sync point.
        if (events.Contains<DestroyEvent>())
            RequestDestroy(chunk, entity);
        if (chunk.ComponentChanges.Count > 0)
            ApplyComponentChanges(chunk);
    }

    // ---- Writes for Self -------------------------------------------------------------------------------------------
    // The rules for who may write a transform or an intent span the entity table, the attachments and the frame tree,
    // so they live here. Every other write of Self goes straight to its subsystem.

    /// <summary>
    /// The running entity's back pose, for writing. Throws <see cref="InvalidOperationException"/> if physics, an
    /// attachment parent or a frame driver owns the transform, and <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    internal ref DPose WritableTransform(Entity entity)
    {
        if (entities.Body(entity) != null)
            throw new InvalidOperationException($"{entity} has a physics body, so physics owns its transform. Move it through Self.Body.");
        Entity parent = attachments.ParentOf(entity);
        if (!parent.IsNull)
            throw new InvalidOperationException($"{entity} is attached to {parent}, so its transform follows the parent. Change Self.AttachOffset instead.");
        FrameHandle own = entities.OwnedFrame(entity);
        if (!own.IsNull && Frames.Driver(own) != MotionDriver.Static)
            throw new InvalidOperationException($"{entity} owns a {Frames.Driver(own)} frame, so the frame's motion driver sets its pose.");
        return ref entities.WritableTransform(entity);
    }

    /// <summary>
    /// The running entity's intent for the next physics step, for writing. Throws <see cref="InvalidOperationException"/>
    /// if the entity has no body, and <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    internal ref PhysicsIntent WritableIntent(Entity entity)
    {
        if (entities.Body(entity) == null)
            throw new InvalidOperationException($"{entity} has no physics body.");
        return ref entities.WritableIntent(entity);
    }
}
