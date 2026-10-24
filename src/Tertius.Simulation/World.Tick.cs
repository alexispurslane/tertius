using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Tertius.Frames.Rails;
using DestroyEvent = Tertius.Destroy;
using TickEvent = Tertius.Tick;

namespace Tertius.Simulation;

public sealed partial class World
{
    // Each worker thread fills its own event list. A list never outlives one entity run.
    [ThreadStatic]
    private static EventBuffer? workerEvents;

    /// <summary>
    /// The time that the sync points of this world took, in <see cref="Stopwatch"/> timestamp units, summed over
    /// <see cref="SyncCount"/> sync points. The benchmarks read it, because a sync point has no timer of its own.
    /// </summary>
    internal long SyncTimestampTotal { get; private set; }

    /// <summary>The number of sync points that <see cref="SyncTimestampTotal"/> sums.</summary>
    internal long SyncCount { get; private set; }

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
        // The physics worker while it runs the step of this tick.
        PhysicsWorker? stepping = null;
        try
        {
            PrepareTick();
            if (physicsWorker != null)
            {
                stepOutput.Clear();
                physicsWorker.Begin(
                    new(
                        tick: TickNumber,
                        deltaTime: DeltaTime,
                        time: Time,
                        frames: Frames,
                        intents: Entities.FrontIntents,
                        intentEntities: Entities.IntentEntities
                    )
                );
                stepping = physicsWorker;
            }

            RunGameplay();

            ExceptionDispatchInfo? physicsError = null;
            if (stepping is { } worker)
            {
                stepping = null;
                physicsError = worker.Wait();
            }

            // A broken contract is a bug in the backend, not a failed step, so the world does not survive it like one.
            // It escapes here, on the ticking thread, with the stack of the physics thread where it broke.
            if (physicsError?.SourceException is ContractException)
                physicsError.Throw();
            long syncStart = Stopwatch.GetTimestamp();
            Sync(physicsError?.SourceException);
            SyncTimestampTotal += Stopwatch.GetTimestamp() - syncStart;
            SyncCount++;
        }
        finally
        {
            stepping?.Wait();
            inTick = false;
        }

        FlushLog();
    }

    /// <summary>
    /// The simulation time at the start of tick <paramref name="tick"/>, in seconds: the same double that <see cref="Time"/>
    /// holds during that tick, and the same read as <see cref="Self.TimeAt"/>. Use it for a time in another tick, because a
    /// sum such as <c>Time + DeltaTime</c> can round to another double. Outside a tick, the step of the next tick ends at
    /// <c>TimeAt(TickNumber + 1)</c>, which is the end of the first step of an entity spawned now (see
    /// <see cref="SpawnDesc.FirstMove"/>).
    /// </summary>
    public double TimeAt(long tick) => Options.StartTime + tick * (double)DeltaTime;

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
        // Every frame fixes its turns for the step, which physics reads, the falling numeric frames and the following frames
        // their accelerations, which physics reads and the sync point follows, and each following frame its step plan, from
        // its anchor's motion intent in the intents of the step, while the tree stands where the step starts. After a step
        // that failed, the chords of the bodies may or may not come from the turns of that step, so every frame takes its new
        // turn as its last turn.
        Frames.BeginStep(Entities.FrontIntents.Span, DeltaTime, lastStepFailed);
        // The indexes that entity queries start from apply their queued changes, so that the workers only read them.
        topics.SettleMembers();
        Entities.BeginTick(Options.SpawnPoolSize);
    }

    /// <summary>Hands this batch of structural changes to physics (see <see cref="ApplyPhysicsChanges"/>), unless it holds no change.</summary>
    private void FlushPhysicsChanges()
    {
        PhysicsChanges changes = physicsChanges;
        if (
            changes.FramesCreated.Count == 0
            && changes.BodiesDestroyed.Count == 0
            && changes.FramesDestroyed.Count == 0
            && changes.FramesShifted.Count == 0
            && changes.BodiesMoved.Count == 0
            && changes.BodiesCreated.Count == 0
        )
            return;
        ApplyPhysicsChanges();
    }

    /// <summary>
    /// Hands this batch of structural changes to physics, also an empty one, and clears it. Physics applies destroys before
    /// creates. When it applies a batch, physics also follows the tree in other ways: for example it drops the ghosts whose
    /// place the numeric update withdrew (see <see cref="UpdateNumericFrames"/>).
    /// </summary>
    private void ApplyPhysicsChanges()
    {
        PhysicsChanges changes = physicsChanges;
        try
        {
            Physics?.ApplyChanges(changes, Frames);
        }
        // A broken contract is a bug in the backend, not a failure to apply changes, so it is not survived like one.
        catch (Exception ex) when (ex is not ContractException)
        {
            Report(
                $"tick {TickNumber}: the physics backend failed to apply changes: {ex.GetType().Name}: {ex.Message}",
                isError: true
            );
        }

        // The states that only a bug produces, which the backend skipped in a Release build.
        foreach (string error in changes.Errors)
            Report($"tick {TickNumber}: {error}", isError: true);
        changes.Clear();
    }

    private void RunGameplay()
    {
        int count = Entities.Chunks.Length;
        if (count == 0)
            return;
        if (Options.MaxDegreeOfParallelism == 1 || count == 1)
            for (int c = 0; c < count; c++)
                RunChunk(c);
        else
            Parallel.For(fromInclusive: 0, toExclusive: count, parallelOptions: parallelOptions, body: runChunk);
    }

    /// <summary>Runs every entity of one chunk that has mail, wants Tick, or is new and wants Spawned. On a worker.</summary>
    private void RunChunk(int chunkIndex)
    {
        ChunkState chunk = Entities.Chunks[chunkIndex];
        EventBuffer buffer = workerEvents ??= new(64);
        int tickType = EventType<TickEvent>.Id;
        int spawnedType = EventType<Spawned>.Id;
        foreach (Entity entity in Entities.LiveEntitiesOfChunk(chunkIndex))
        {
            bool first = Entities.TakeFirstTick(entity);
            bool wantsTick = Entities.Accepts(entity, tickType);
            bool wantsSpawned = first && Entities.Accepts(entity, spawnedType);
            if (router.InboxCount(entity) == 0 && !wantsTick && !wantsSpawned)
                continue;
            RunEntity(chunk: chunk, buffer: buffer, entity: entity, wantsTick: wantsTick, wantsSpawned: wantsSpawned);
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

        ReadOnlySpan<IComponent> components = Entities.Components(entity);
        ReadOnlySpan<EventMask> wants = Entities.ComponentWants(entity);
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
                    chunk.Messages.Add(
                        new(
                            $"tick {TickNumber}: {entity} {components[c].GetType().FullName}: {ex.GetType().Name}: {ex.Message}",
                            true
                        )
                    );
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
    /// The running entity's back pose, for writing. Throws <see cref="InvalidOperationException"/> if physics or an
    /// attachment parent owns the transform, and <see cref="StaleEntityException"/> for a stale handle. The write leaves the
    /// entity at rest (see <see cref="EntityTable.WritableTransform"/>). An entity without a body writes its transform whatever
    /// moves it otherwise: the sync point of the tick puts a frame on the write after its record, and skips the record of an
    /// entity without a frame, so the write wins for one tick (see <see cref="FollowOwners"/> and
    /// <see cref="FollowFramelessEntities"/>).
    /// </summary>
    internal ref DPose WritableTransform(Entity entity)
    {
        if (Entities.Body(entity) != null)
            throw new InvalidOperationException(
                $"{entity} has a physics body, so physics owns its transform. Move it through Self.Body."
            );
        Entity parent = Attachments.ParentOf(entity);
        if (!parent.IsNull)
            throw new InvalidOperationException(
                $"{entity} is attached to {parent}, so its transform follows the parent. Change Self.AttachOffset instead."
            );
        return ref Entities.WritableTransform(entity);
    }

    /// <summary>
    /// Queues a ray cast of the running entity for this tick's sync point, in the frame that the entity is in:
    /// <see cref="RayCastKind.Nearest"/> for <see cref="PhysicsQueries.CastRay"/> and <see cref="RayCastKind.All"/> for
    /// <see cref="PhysicsQueries.CastRayAll"/>, which give the arguments and the answer.
    /// </summary>
    internal void RequestRay(
        ChunkState chunk,
        Entity entity,
        RayCastKind kind,
        Double3 from,
        Double3 direction,
        double length,
        in EntityQuery query,
        int tag
    )
    {
        FrameHandle frame = QueryFrame(entity, "cast a ray");
        Double3 origin = CheckedOrigin(from);
        Double3 unit = CheckedSweep("A ray", direction, length);
        chunk.PhysicsRequests.Add(
            new(
                Asker: entity,
                Frame: frame,
                Question: new RayQuestion(Origin: origin, Direction: unit, Length: length, Kind: kind),
                Query: query,
                Tag: tag
            )
        );
    }

    /// <summary>
    /// Queues a shape cast of the running entity for this tick's sync point, in the frame that the entity is in. See
    /// <see cref="PhysicsQueries.CastShape"/> for the arguments and the answer.
    /// </summary>
    internal void RequestShapeCast(
        ChunkState chunk,
        Entity entity,
        in QueryShape shape,
        in DPose start,
        Double3 direction,
        double length,
        in EntityQuery query,
        int tag
    )
    {
        FrameHandle frame = QueryFrame(entity, "cast a shape");
        DPose pose = CheckedPose(shape: shape, pose: start, what: "A shape cast", parameter: nameof(start));
        Double3 unit = CheckedSweep("A shape cast", direction, length);
        chunk.PhysicsRequests.Add(
            new(
                Asker: entity,
                Frame: frame,
                Question: new ShapeCastQuestion(Shape: shape, Start: pose, Direction: unit, Length: length),
                Query: query,
                Tag: tag
            )
        );
    }

    /// <summary>
    /// Queues an overlap query of the running entity for this tick's sync point, in the frame that the entity is in. See
    /// <see cref="PhysicsQueries.Overlap"/> for the arguments and the answer.
    /// </summary>
    internal void RequestOverlap(
        ChunkState chunk,
        Entity entity,
        in QueryShape shape,
        in DPose pose,
        in EntityQuery query,
        int tag
    )
    {
        FrameHandle frame = QueryFrame(entity, "test an overlap");
        DPose checkedPose = CheckedPose(shape: shape, pose: pose, what: "An overlap query", parameter: nameof(pose));
        chunk.PhysicsRequests.Add(
            new(Asker: entity, Frame: frame, Question: new OverlapQuestion(shape, checkedPose), Query: query, Tag: tag)
        );
    }

    /// <summary>The frame that a query of <paramref name="entity"/> works in: the frame the entity is in.</summary>
    private FrameHandle QueryFrame(Entity entity, string toDo)
    {
        FrameHandle frame = Entities.FrontRow(entity).Frame;
        return frame.IsNull
            ? throw new InvalidOperationException(
                $"{entity} owns a root frame and is in no frame, so it has no frame to {toDo} in."
            )
            : frame;
    }

    private static Double3 CheckedOrigin(Double3 from) =>
        !double.IsFinite(from.X) || !double.IsFinite(from.Y) || !double.IsFinite(from.Z)
            ? throw new ArgumentException($"A ray needs a finite origin, not {from}.", nameof(from))
            : from;

    /// <summary>The unit vector along <paramref name="direction"/>, after the checks of a cast's direction and length.</summary>
    private static Double3 CheckedSweep(string what, Double3 direction, double length)
    {
        double size = direction.Length;
        if (!(size > 0) || !double.IsFinite(size))
            throw new ArgumentException(
                $"{what} needs a finite direction that is not zero, not {direction}.",
                nameof(direction)
            );
        if (!(length > 0) || !double.IsFinite(length))
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                $"{what} needs a finite length of more than 0 m."
            );
        return direction / size;
    }

    /// <summary>The pose of a shape query with a unit rotation, after the checks of its shape and pose.</summary>
    private static DPose CheckedPose(in QueryShape shape, in DPose pose, string what, string parameter)
    {
        if (shape.Problem() is { } problem)
            throw new ArgumentException(
                $"{what} needs a sphere, a box or a capsule of a usable size, and its shape {problem}.",
                nameof(shape)
            );
        Double3 p = pose.Position;
        DQuaternion q = pose.Rotation;
        if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z))
            throw new ArgumentException($"{what} needs a finite position, not {p}.", parameter);
        double rotationSize = q.LengthSquared;
        return !(rotationSize > 0) || !double.IsFinite(rotationSize)
            ? throw new ArgumentException($"{what} needs a finite rotation that is not zero, not {q}.", parameter)
            : new(p, q.Normalized());
    }

    /// <summary>
    /// The running entity's intent for the next physics step, for writing. Throws <see cref="InvalidOperationException"/>
    /// if the entity has no body, and <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    internal ref PhysicsIntent WritableIntent(Entity entity)
    {
        if (Entities.Body(entity) == null)
            throw new InvalidOperationException($"{entity} has no physics body.");
        return ref Entities.WritableIntent(entity);
    }

    /// <summary>
    /// The running entity's intent, for writing a push: a force, a torque, an impulse or an angular impulse
    /// (docs/plans/parts.md, "The public API"). For an entity with a body, its own intent. For a part, an attached entity
    /// whose chain root has a body, the part's own intent, which the sync point carries to the root's body at the part's
    /// place. The guard reads the part's link and the root's body description, which nothing writes during a tick, so the
    /// part's worker may read them while the root runs on another. Throws <see cref="InvalidOperationException"/> for an
    /// entity that has no body and is not part of a chain whose root has one, and <see cref="StaleEntityException"/> for a
    /// stale handle.
    /// </summary>
    internal ref PhysicsIntent WritablePush(Entity entity)
    {
        if (Entities.Body(entity) != null)
            return ref Entities.WritableIntent(entity);
        Entity root = Attachments.RootOf(entity);
        if (root.IsNull || Entities.Body(root) == null)
            throw new InvalidOperationException(
                $"{entity} has no physics body, and it is not part of a chain whose root has one."
            );
        return ref Entities.WritablePush(entity);
    }

    /// <summary>
    /// The running entity's intent, for writing a velocity of its body: <paramref name="call"/>, <c>SetVelocity</c> or
    /// <c>SetAngularVelocity</c>. Throws <see cref="InvalidOperationException"/> as <see cref="WritableIntent"/> does, and
    /// also when the body is kinematic in the step that applies the velocity (see <see cref="NextStepMotion"/>, which counts
    /// a change of motion that the entity wrote earlier in the tick), hull or plain: a kinematic body moves only by its
    /// motion intents and teleports, and holds still in a step without one (docs/plans/rails-components.md, "The flow per
    /// kind of entity").
    /// </summary>
    internal ref PhysicsIntent WritableMotionIntent(Entity entity, string call)
    {
        if (NextStepMotion(entity) == BodyMotion.Kinematic)
        {
            FrameHandle own = Entities.OwnedFrame(entity);
            string body = own.IsNull
                ? $"{entity} has a kinematic body"
                : $"{entity} owns {own}, whose body is a kinematic hull,";
            throw new InvalidOperationException(
                $"{body} so it cannot take {call}: a kinematic body moves only by its motion intents and teleports."
            );
        }

        return ref WritableIntent(entity);
    }

    /// <summary>
    /// The reason that <paramref name="entity"/> refuses the case of <paramref name="intent"/> in the next step, the step that
    /// applies the intents written in this tick, or null when it takes it: the one guard of every motion intent
    /// (docs/plans/rails-components.md, "The flow per kind of entity"). An attached entity and a character refuse every case.
    /// An end pose and a full sample need a body that is kinematic in the next step (see <see cref="NextStepMotion"/>), no
    /// body, or the owner of a root frame, whose body is only the frame's static interior. A steer target needs a body that
    /// is dynamic in the next step. No motion intent is never refused. The writers of a motion intent throw with this reason,
    /// step 4 drops a written motion intent by it, and a spawn judges its first motion intent by the same rules (see
    /// <see cref="MotionIntent.ProblemFor"/>). Throws <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    /// <remarks>
    /// It reads the change of motion that the entity wrote in this tick, so during a tick only the worker of the entity may
    /// ask it, as for <see cref="NextStepMotion"/>. At the sync point and outside a tick anyone may.
    /// </remarks>
    public string? MotionIntentProblem(Entity entity, in MotionIntent intent)
    {
        FrameHandle own = Entities.OwnedFrame(entity);
        bool ownsRootFrame = !own.IsNull && Frames.Parent(own).IsNull;
        return intent.ProblemFor(NextStepMotion(entity), attached: !Attachments.ParentOf(entity).IsNull, ownsRootFrame);
    }

    /// <summary>
    /// True when <paramref name="entity"/> takes the case of <paramref name="intent"/> in the next step: the guard of
    /// <see cref="MotionIntentProblem"/> gives no reason. It judges by the same rules and builds no reason, so it allocates
    /// nothing, and a rails component asks it in every tick. Throws <see cref="StaleEntityException"/> for a stale handle.
    /// During a tick only the worker of the entity may ask it, as for <see cref="MotionIntentProblem"/>.
    /// </summary>
    public bool TakesMotionIntent(Entity entity, in MotionIntent intent)
    {
        FrameHandle own = Entities.OwnedFrame(entity);
        bool ownsRootFrame = !own.IsNull && Frames.Parent(own).IsNull;
        return intent.RefusalFor(NextStepMotion(entity), attached: !Attachments.ParentOf(entity).IsNull, ownsRootFrame)
            == MotionIntentRefusal.None;
    }

    /// <summary>
    /// Writes <paramref name="move"/>, relative to the frame that the running entity is in during the tick, as the motion
    /// intent of its next step (see <see cref="PhysicsIntent.Move"/>), with its rotation made unit length. The last write
    /// in a tick wins, whatever its case. <paramref name="call"/> names the writer for the messages. It throws
    /// <see cref="ArgumentException"/> for a number that is not finite or a zero rotation (see
    /// <see cref="MotionIntentNumberProblem"/>), then <see cref="InvalidOperationException"/> with the reason of the guard
    /// when the entity refuses the case (see <see cref="MotionIntentProblem"/>), and <see cref="StaleEntityException"/> for a
    /// stale handle. The entity needs no body, so it writes through <see cref="EntityTable.WriteMotionIntent"/>.
    /// </summary>
    internal void WriteMotionIntent(Entity entity, string call, in MotionIntent move)
    {
        if (MotionIntentNumberProblem(move) is { } problem)
            throw new ArgumentException($"{call} {problem}.", nameof(move));
        if (MotionIntentProblem(entity, move) is { } refusal)
        {
            FrameHandle own = Entities.OwnedFrame(entity);
            string who = own.IsNull ? $"{entity}" : $"{entity}, which owns {own},";
            throw new InvalidOperationException($"{who} cannot take {call}: it {refusal}.");
        }

        Entities.WriteMotionIntent(entity, WithUnitRotation(move));
    }

    /// <summary>
    /// What is wrong with the numbers of <paramref name="move"/>, or null: a number that is not finite, or a rotation that
    /// is zero or not finite. The writers of a motion intent and the check of a spawn's first motion intent refuse such an
    /// intent.
    /// </summary>
    private static string? MotionIntentNumberProblem(in MotionIntent move) =>
        move switch
        {
            MotionSample sample => SampleProblem(sample),
            EndPose end => end.Pose.IsValid
                ? null
                : $"needs an end pose with a finite position and a finite rotation that is not zero, not {end.Pose}",
            SteerTarget steer => steer.Pose.IsValid
                ? null
                : $"needs a steer target with a finite position and a finite rotation that is not zero, not {steer.Pose}",
            null => null,
        };

    /// <summary>
    /// <paramref name="move"/> with the rotation of its pose made unit length. Gameplay can write any rotation that is not
    /// zero, but the local pose of a frame must be unit length (see <see cref="FrameTree"/>), and physics puts a body on the
    /// pose, so the World normalizes it, as it does for a write of a transform (see FollowOwners).
    /// </summary>
    private static MotionIntent WithUnitRotation(in MotionIntent move) =>
        move switch
        {
            MotionSample sample => new(sample with { Pose = sample.Pose.WithUnitRotation() }),
            EndPose end => new(new EndPose(end.Pose.WithUnitRotation())),
            SteerTarget steer => new(new SteerTarget(steer.Pose.WithUnitRotation())),
            null => move,
        };

    /// <summary>
    /// The motion that the hull of <paramref name="frame"/> has in the next step: the step that applies the intents written
    /// in this tick. That is the change of motion that the frame's owner wrote in this tick so far (see
    /// <see cref="BodyHandle.SetMotion"/>), or else the motion in the step that applies the front intents (see
    /// <see cref="FrameTree.HullMotionInStep"/>). During the tick, and at the sync point until step 4, that is the running
    /// step, which counts the change of the tick before. After step 4 swaps the intents, this tick's writes are the front
    /// intents, so that step is the next step itself, and the answer stays the same. Null for a frame without a hull.
    /// Throws <see cref="StaleEntityException"/> for a stale owner and <see cref="InvalidOperationException"/> for a stale
    /// frame.
    /// </summary>
    /// <remarks>
    /// The readers that ask about that step: the guards of a tick, which judge the tick's writes (see
    /// <see cref="WritableMotionIntent"/> and <see cref="MotionIntentProblem"/>); the rails components, on their first run
    /// (see <see cref="BodyHandle.NextStepMotion"/>); the numeric update, which plans that step; and the body that a rehome
    /// creates for it. During a tick only the worker of the frame's owner may ask it, because the owner writes its own
    /// intent; at the sync point and outside a tick anyone may.
    /// </remarks>
    internal BodyMotion? NextStepHullMotion(FrameHandle frame)
    {
        BodyMotion? inFrontStep = Frames.HullMotionInStep(frame, Entities.FrontIntents.Span);
        return inFrontStep is null ? null : Entities.WrittenMotionChange(frame.Entity) ?? inFrontStep;
    }

    /// <summary>
    /// The motion of the body of <paramref name="entity"/> in the next step (see <see cref="NextStepHullMotion"/>): null
    /// without a body, the motion of the description for a body that owns no frame, and static for the owner of a root
    /// frame, whose body is only the frame's static interior. Throws <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    internal BodyMotion? NextStepMotion(Entity entity)
    {
        if (Entities.Body(entity) is not { } body)
            return null;
        FrameHandle own = Entities.OwnedFrame(entity);
        return own.IsNull ? body.Motion : NextStepHullMotion(own) ?? BodyMotion.Static;
    }

    /// <summary>
    /// Writes a change of the running entity's hull motion to <paramref name="motion"/> into its intent, which the next step
    /// applies (see <see cref="EntityTable.WriteMotionChange"/> and <see cref="ApplyMotionChanges"/>). Throws
    /// <see cref="InvalidOperationException"/> if the entity's body is no hull: it has no body, owns no frame, or owns a root
    /// frame, whose owner's body is only the frame's static interior. Throws <see cref="ArgumentException"/> for a motion that
    /// the hull cannot take (see <see cref="BodyDesc.Problem"/> with <see cref="BodyRole.Hull"/>), and
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    internal void WriteHullMotion(Entity entity, BodyMotion motion)
    {
        FrameHandle own = Entities.OwnedFrame(entity);
        string owner = own.IsNull ? $"{entity}" : $"{entity}, which owns {own},";
        if (Entities.Body(entity) is not { } body)
            throw new InvalidOperationException(
                $"{owner} has no physics body, so it has no hull whose motion can change."
            );
        if (own.IsNull)
            throw new InvalidOperationException(
                $"{owner} owns no frame, so its body is no hull: only a hull can change its motion."
            );
        if (Frames.HullMotion(own) is null)
            throw new InvalidOperationException(
                $"{owner} has a root frame, so its body is only the frame's static interior, not a hull: only a hull can change its motion."
            );
        if ((body with { Motion = motion }).Problem(BodyRole.Hull) is { } problem)
            throw new ArgumentException(
                $"The hull of {owner} cannot take the motion {motion}: as a hull it {problem}.",
                nameof(motion)
            );
        Entities.WriteMotionChange(entity, motion);
    }

    /// <summary>
    /// Writes the running entity's full sample for the next physics step as its motion intent (see
    /// <see cref="Self.MoveOnRails"/> and <see cref="WriteMotionIntent"/>).
    /// </summary>
    internal void MoveOnRails(Entity entity, in MotionSample sample) =>
        WriteMotionIntent(entity, nameof(Self.MoveOnRails), new(sample));

    /// <summary>
    /// The pose in <paramref name="frame"/> at which <paramref name="entity"/> starts the next step (see
    /// <see cref="Self.PoseAtNextStepStartIn"/>): the end of its motion intent in the step that applies the front intents, an
    /// end pose or a full sample, which physics or the sync point puts the entity on exactly; else that step's teleport;
    /// else its pose now, in the front snapshot, which for a frame owner is its frame's pose in its parent, carried to the
    /// end of that step as <see cref="PoseNowAtRunningStepEnd"/> says. A steer target gives no exact end, so the read skips
    /// it. Each of them is in the frame that the entity is in, or for the owner of a root frame in the root's own space, the
    /// null frame. Throws <see cref="StaleEntityException"/> for a stale entity, and <see cref="ArgumentException"/> for a
    /// stale frame. During a tick, only the worker of the entity may ask it: it reads the entity's own front intent, which
    /// nothing writes during the tick.
    /// </summary>
    internal DPose PoseAtNextStepStartIn(Entity entity, FrameHandle frame)
    {
        ReadOnlySpan<PhysicsIntent> front = Entities.FrontIntents.Span;
        ref readonly PhysicsIntent intent = ref front[(int)entity.Index];
        (DPose pose, FrameHandle from) = intent.Move switch
        {
            EndPose end => (end.Pose, intent.Frame),
            MotionSample sample => (sample.Pose, intent.Frame),
            _ when (intent.Flags & IntentFlags.Teleport) != 0 => (intent.TeleportPose, intent.Frame),
            SteerTarget or null => PoseNowAtRunningStepEnd(entity, front),
        };
        return from == frame ? pose : DPose.Compose(Frames.Relative(from, LiveFrame(frame)), pose);
    }

    /// <summary>
    /// Where <paramref name="entity"/>, whose step beside the tick applies no end pose, full sample or teleport, stands at
    /// the end of that step, from its pose now, with the frame that the pose is in. A body that is kinematic in that step
    /// holds still in its home, hull or not: a hull in its frame's tree parent, a plain body in its physics home. So it ends
    /// the step where its home carries it, which a numeric home moves over the step (see
    /// <see cref="FrameTree.HomeMoveOver"/>). Its inputs, the numeric frames' places, velocities and step accelerations,
    /// stay fixed during the tick, because physics converts its kinematic targets with them while the step runs. Any other
    /// entity keeps its pose now: an entity without a body, a static body, an attached entity and a frame without a hull
    /// live in their semantic frame, and physics moves a dynamic body or a character, which the read does not predict.
    /// </summary>
    private (DPose Pose, FrameHandle From) PoseNowAtRunningStepEnd(Entity entity, ReadOnlySpan<PhysicsIntent> front)
    {
        FrameHandle own = Entities.OwnedFrame(entity);
        if (own.IsNull)
        {
            ref readonly TransformRow row = ref Entities.FrontRow(entity);
            if (Entities.Body(entity) is not { Motion: BodyMotion.Kinematic })
                return (row.Pose, row.Frame);
            Double3 move = Frames.HomeMoveOver(row.PhysicsHome, row.Frame, DeltaTime);
            return (new(row.Pose.Position + move, row.Pose.Rotation), row.Frame);
        }

        DPose local = Frames.LocalPose(own);
        FrameHandle parent = Frames.Parent(own);
        if (Frames.HullMotionInStep(own, front) != BodyMotion.Kinematic)
            return (local, parent);
        Double3 carried = Frames.HomeMoveOver(Frames.TreeParent(own), parent, DeltaTime);
        return (new(local.Position + carried, local.Rotation), parent);
    }

    /// <summary>
    /// What is wrong with the numbers of <paramref name="sample"/>, or null: a field that is not a finite number, or a rotation
    /// that is zero or not finite. The writers and the check of a spawn's first motion intent refuse such a sample.
    /// </summary>
    private static string? SampleProblem(in MotionSample sample)
    {
        if (
            !IsFinite(sample.Pose.Position)
            || !IsFinite(sample.ChordVelocity)
            || !IsFinite(sample.Velocity)
            || !IsFinite(sample.Acceleration)
            || !IsFinite(sample.AngularVelocity)
            || !IsFinite(sample.AngularAcceleration)
        )
            return $"needs fields that are finite numbers, not {sample}";
        double rotationSize = sample.Pose.Rotation.LengthSquared;
        return !(rotationSize > 0) || !double.IsFinite(rotationSize)
            ? $"needs a finite rotation that is not zero, not {sample.Pose.Rotation}"
            : null;
    }
}
