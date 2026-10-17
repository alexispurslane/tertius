using System.Runtime.ExceptionServices;
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
    /// <remarks>
    /// An exception that the physics backend throws is reported in <see cref="Errors"/>, and the tick goes on without
    /// that step's output or that change batch. A <see cref="ContractException"/> is different: it is a bug, so it
    /// escapes this call, in every build, with the stack where it was thrown.
    /// </remarks>
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

            ExceptionDispatchInfo? physicsError = null;
            if (stepping)
            {
                stepping = false;
                physicsError = physicsWorker!.Wait();
            }
            // A broken contract is a bug in the backend, not a failed step, so the world does not survive it like one.
            // It escapes here, on the ticking thread, with the stack of the physics thread where it broke.
            if (physicsError?.SourceException is ContractException)
                physicsError.Throw();
            Sync(physicsError?.SourceException);
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
        // Spawns and destroys made outside the tick reach physics before the step that may touch them. The bodies that
        // they changed join their numeric frames first, as the bodies spawned in a tick do at the sync point, so that a
        // spawn that joins a numeric frame is created in it. No time passes, so no split comes due.
        if (numericUpdateDue)
        {
            numericUpdateDue = false;
            UpdateNumericFrames(0);
            DeriveAttachments();
            SwapSnapshot();
        }
        FlushPhysicsChanges();
        Frames.Update();
        // The falling numeric frames and the orbit frames fix their accelerations for the step, which physics reads and the
        // sync point follows.
        Frames.BeginNumericStep();
        // The indexes that entity queries start from apply their queued changes, so that the workers only read them.
        topics.SettleMembers();
        entities.BeginTick(options.SpawnPoolSize);
    }

    /// <summary>
    /// Hands this batch of structural changes to physics, which applies destroys before creates. An empty batch goes only
    /// when <paramref name="evenIfEmpty"/> is true: physics also follows the tree in other ways when it applies a batch, for
    /// example it drops the ghosts whose place the numeric update withdrew (see <see cref="UpdateNumericFrames"/>).
    /// </summary>
    private void FlushPhysicsChanges(bool evenIfEmpty = false)
    {
        PhysicsChanges changes = physicsChanges;
        if (!evenIfEmpty && changes.FramesCreated.Count == 0 && changes.BodiesDestroyed.Count == 0 &&
            changes.FramesDestroyed.Count == 0 && changes.FramesShifted.Count == 0 && changes.BodiesMoved.Count == 0 &&
            changes.BodiesCreated.Count == 0)
            return;
        try
        {
            physics?.ApplyChanges(changes, Frames);
        }
        // A broken contract is a bug in the backend, not a failure to apply changes, so it is not survived like one.
        catch (Exception ex) when (ex is not ContractException)
        {
            Report($"tick {TickNumber}: the physics backend failed to apply changes: {ex.GetType().Name}: {ex.Message}", isError: true);
        }
        // The states that only a bug produces, which the backend skipped in a Release build.
        foreach (string error in changes.Errors)
            Report($"tick {TickNumber}: {error}", isError: true);
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
        // The marker lets the code that must not run in a component's Handle call, such as building the heap part of an
        // entity query, refuse to.
        GameplayThread.BeginEntityRun();
        try
        {
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
        }
        finally
        {
            GameplayThread.EndEntityRun();
        }

        // A Destroy that no component removed destroys the entity at the sync point.
        if (events.Of<DestroyEvent>().Any())
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
    /// Queues a ray cast or a shape cast of the running entity for this tick's sync point, in the frame that the entity is
    /// in. A ray passes the default shape and a start pose with the identity rotation. See <see cref="PhysicsQueries"/> for
    /// the arguments and the answer.
    /// </summary>
    internal void RequestCast(ChunkState chunk, Entity entity, PhysicsRequestKind kind, in QueryShape shape, in DPose start, Double3 direction,
        double length, in EntityQuery query, int tag)
    {
        bool ray = kind is PhysicsRequestKind.Ray or PhysicsRequestKind.RayAll;
        string what = ray ? "A ray" : "A shape cast";
        FrameHandle frame = QueryFrame(entity, ray ? "cast a ray" : "cast a shape");
        DPose pose = ray ? CheckedOrigin(start.Position) : CheckedPose(shape, start, what, nameof(start));
        double size = direction.Length;
        if (!(size > 0) || !double.IsFinite(size))
            throw new ArgumentException($"{what} needs a finite direction that is not zero, not {direction}.", nameof(direction));
        if (!(length > 0) || !double.IsFinite(length))
            throw new ArgumentOutOfRangeException(nameof(length), length, $"{what} needs a finite length of more than 0 m.");
        chunk.PhysicsRequests.Add(new PhysicsRequest(entity, frame, kind, shape, pose, direction / size, length, query, tag));
    }

    /// <summary>
    /// Queues an overlap query of the running entity for this tick's sync point, in the frame that the entity is in. See
    /// <see cref="PhysicsQueries.Overlap"/> for the arguments and the answer.
    /// </summary>
    internal void RequestOverlap(ChunkState chunk, Entity entity, in QueryShape shape, in DPose pose, in EntityQuery query, int tag)
    {
        FrameHandle frame = QueryFrame(entity, "test an overlap");
        DPose checkedPose = CheckedPose(shape, pose, "An overlap query", nameof(pose));
        chunk.PhysicsRequests.Add(new PhysicsRequest(entity, frame, PhysicsRequestKind.Overlap, shape, checkedPose, Double3.Zero, 0, query, tag));
    }

    /// <summary>The frame that a query of <paramref name="entity"/> works in: the frame the entity is in.</summary>
    private FrameHandle QueryFrame(Entity entity, string toDo)
    {
        FrameHandle frame = entities.FrontRow(entity).Frame;
        if (frame.IsNull)
            throw new InvalidOperationException($"{entity} owns a root frame and is in no frame, so it has no frame to {toDo} in.");
        return frame;
    }

    private static DPose CheckedOrigin(Double3 from)
    {
        if (!double.IsFinite(from.X) || !double.IsFinite(from.Y) || !double.IsFinite(from.Z))
            throw new ArgumentException($"A ray needs a finite origin, not {from}.", nameof(from));
        return new DPose(from, DQuaternion.Identity);
    }

    /// <summary>The pose of a shape query with a unit rotation, after the checks of its shape and pose.</summary>
    private static DPose CheckedPose(in QueryShape shape, in DPose pose, string what, string parameter)
    {
        if (shape.Problem() is { } problem)
            throw new ArgumentException($"{what} needs a sphere, a box or a capsule of a usable size, and its shape {problem}.", nameof(shape));
        Double3 p = pose.Position;
        DQuaternion q = pose.Rotation;
        if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z))
            throw new ArgumentException($"{what} needs a finite position, not {p}.", parameter);
        double rotationSize = q.LengthSquared;
        if (!(rotationSize > 0) || !double.IsFinite(rotationSize))
            throw new ArgumentException($"{what} needs a finite rotation that is not zero, not {q}.", parameter);
        return new DPose(p, q.Normalized());
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
