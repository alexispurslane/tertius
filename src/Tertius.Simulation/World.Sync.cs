using System.Numerics;

namespace Tertius.Simulation;

public sealed partial class World
{
    // Entities whose back row physics wrote this sync, in the order of the writes. An entity that physics wrote twice
    // is here twice; its second entry finds its membership already moved.
    private readonly List<Entity> physicsTouched = new();

    // Per entity: true when physics stood the character on walkable ground under gravity in its last step. The sync
    // point writes it from each step's character states, and gameplay reads it in the next tick. Nothing writes it
    // while gameplay runs, so it needs no second copy. The entity table clears it when it releases an entity.
    private readonly EntityTable.Column<bool> characterStanding;

    // The check of each step's output. It skips the whole physics update of every entity that physics reported something
    // about that only a bug in the backend produces. Their body states, their hand-offs, the states of the frames they
    // own, the contacts that name them and their character states are all ignored. A broken character state skips only
    // itself.
    private readonly PhysicsOutputCheck physicsOutputCheck = new();

    /// <summary>What the entity table knows about an entity, for the check of the physics output.</summary>
    private readonly struct PhysicsOutputFacts(EntityTable entities) : IPhysicsOutputFacts
    {
        public bool IsAlive(Entity entity) => entities.IsAlive(entity);

        public bool HasBody(Entity entity) => entities.Body(entity) != null;

        public bool IsCharacter(Entity entity) => entities.Body(entity) is { Motion: BodyMotion.Character };

        // Every write to the row of an entity with a body comes from physics, so the row is where the step began.
        public FrameHandle FrameOf(Entity entity) => entities.BackRow(entity).PhysicsHome;
    }

    /// <summary>
    /// The sync point, the steps of "Threads and the tick" in ARCHITECTURE.md. One thread runs it; nothing else runs
    /// meanwhile. Each step calls into the subsystems, which keep their own bookkeeping; the comments say why a step
    /// needs what came before it.
    /// </summary>
    private void Sync(Exception? physicsError)
    {
        double endTime = TimeAt(TickNumber + 1);

        // 1. The gameplay chunks and the physics step are done: Tick waited for both.
        int fallbackSpawns = entities.EndGameplay();
        ReportChunkMessages(fallbackSpawns);
        InitializeAddedComponentTypes();
        if (physicsError != null)
        {
            // A step that threw may have written only part of its output. Use none of it.
            Report($"tick {TickNumber}: the physics step failed and its results were dropped: {physicsError.GetType().Name}: {physicsError.Message}", isError: true);
            stepOutput.Clear();
        }

        // 2. The falling numeric frames move over the step that physics made, with the acceleration that physics used.
        //    Then a check of the whole report and the hand-offs' reparents, which decides the skipped updates, then body
        //    states and hand-offs, whose rows take the new places of their numeric homes, then the physics frames' states
        //    and their owners' rows: a state is relative to the parent after the hand-offs, and must land before step 5
        //    can destroy that parent or move the hull. Contacts last.
        Frames.AdvanceNumericFrames(DeltaTime);
        if (physics != null)
            ApplyPhysicsOutput();

        // 3. Physics owns the transform of every entity with a body. The entity table refuses such writes, and checks
        //    that none got through among its invariants.
        if (options.DebugChecks)
            entities.AssertInvariants();

        // 4. The step applied the intents of the previous tick, so their collision groups and masks become the bodies' rows,
        //    which step 8 publishes. Then this tick's intents become the input of the next physics step.
        if (physics != null)
            entities.ApplyCollisionIntents();
        entities.SwapIntents(keep: physics != null);

        // 5. Subscriptions first, while every entity that asked is still live, then the destroy batch.
        ApplySubscriptionChanges();
        DestroyRequestedEntities();

        // 6. New entities, after the destroy batch, so that a spawn into a frame that just died is dropped.
        MakeSpawnsLive(endTime);

        // 6b. Bodies move into numeric frames and out of them, after physics output, destroys and spawns, so that every
        //     body that is live at the end of the sync point is clustered where it is now. The structural changes of this
        //     sync reach physics with the update's first batch, so that a spawn that joins a numeric frame is created in
        //     it, with its velocity converted in doubles.
        if (physics != null)
            UpdateNumericFrames(DeltaTime);
        FlushPhysicsChanges();

        // 7. Kepler and Spin frames advance to the end of the tick, and every frame owner, then every attached entity, follows.
        AdvanceFrames(endTime);
        DeriveAttachments();

        // 8. The back tables become the front tables.
        SwapSnapshot();

        // 9. The ray casts of this tick, against Jolt at the end of the step with this sync's destroys and spawns, and
        //    against the new front snapshot, which describes the same moment: its component flags, collision groups,
        //    poses and frames. Their answers join the routing as engine events.
        rayQueries.Run(entities.Chunks, TickNumber);

        // 10. Messages, after every membership and subscription change of this sync, and after the answers of step 9.
        router.Route(TickNumber);

        // 11. The render snapshot of the new state.
        TickNumber++;
        Time = endTime;
        BuildRenderSnapshot();

        // 12. Ready for the next tick.
        entities.EndTick();
        if (options.DebugChecks)
            AssertInvariants();
    }

    /// <summary>Reports the chunks' log lines and errors in chunk order, then the spawns that missed their pool.</summary>
    private void ReportChunkMessages(int fallbackSpawns)
    {
        foreach (ChunkState chunk in entities.Chunks)
            foreach (ChunkMessage message in chunk.Messages)
                Report(message.Text, message.IsError);
        if (fallbackSpawns > 0)
        {
            fallbackSpawnTotal += fallbackSpawns;
            Report($"tick {TickNumber}: {fallbackSpawns} spawns ran out of their chunk's reserved slot pool and took slots from the global fallback. " +
                   "Slot assignment for this tick depends on thread timing, so the run is no longer deterministic. Raise WorldOptions.SpawnPoolSize.", isError: false);
        }
    }

    /// <summary>
    /// Step 2: the check of the report and the frame owners' reparents of the hand-offs, then body states and hand-offs
    /// into the back table, then whether physics stood each character, then the new states of the physics-driven frames
    /// and their owners' rows, then contacts as Collision events. The contacts come after the frame states, so that a
    /// point converted between frames uses the frame poses after the step, like the bodies' own poses.
    /// </summary>
    private void ApplyPhysicsOutput()
    {
        physicsTouched.Clear();
        // The states that only a bug produces, which the backend found and skipped during the step in a Release build.
        foreach (string error in stepOutput.Errors)
            Report($"tick {TickNumber}: {error}", isError: true);
        // Every update is decided before any state lands, so a skipped one changes nothing whatever the loops' order.
        SkipBrokenPhysicsUpdates();
        MoveHandedOffFrames();

        // The check put every entity that is not live, or has no body, among the skipped updates.
        foreach (BodyState state in stepOutput.Bodies)
        {
            Entity entity = state.Entity;
            if (physicsOutputCheck.IsSkipped(entity))
                continue;
            entities.SetBackTransform(entity, PhysicsRow(state.Frame, state.Pose, state.LinearVelocity, state.AngularVelocity));
            physicsTouched.Add(entity);
        }

        foreach (HandOff handOff in stepOutput.HandOffs)
        {
            Entity entity = handOff.Entity;
            if (physicsOutputCheck.IsSkipped(entity))
                continue;
            entities.SetBackTransform(entity, PhysicsRow(handOff.To, handOff.Pose, handOff.LinearVelocity, handOff.AngularVelocity));
            physicsTouched.Add(entity);
        }

        // The check skipped every character state that is broken, and the state of every entity whose update it skipped.
        for (int i = 0; i < stepOutput.Characters.Count; i++)
        {
            if (!physicsOutputCheck.IsCharacterStateSkipped(i))
                characterStanding[stepOutput.Characters[i].Entity] = stepOutput.Characters[i].Standing;
        }

        // The reparents already put each frame under the parent that its state is relative to. The owner's row mirrors
        // the new state at once, so that a hull that the destroy batch moves to another physics system starts from the
        // state after the step. A frame that is gone has its owner's update skipped.
        foreach (FrameState state in stepOutput.Frames)
        {
            if (physicsOutputCheck.IsSkipped(state.Frame.Entity))
                continue;
            Frames.SetTreeLocalPose(state.Frame, state.LocalPose);
            Frames.TreeLinearVelocity(state.Frame) = state.LinearVelocity;
            Frames.AngularVelocity(state.Frame) = state.AngularVelocity;
            Frames.ProperAcceleration(state.Frame) = state.ProperAcceleration;
            Entity owner = state.Frame.Entity;
            entities.SetBackTransform(owner, FrameOwnerRow(state.Frame));
            physicsTouched.Add(owner);
        }

        // Each entity that physics moved to another frame changes topic and hears about it once.
        foreach (Entity entity in physicsTouched)
            MoveMembership(entity, entities.BackRow(entity).Frame, sendFrameChanged: true);

        foreach (ContactStarted contact in stepOutput.Contacts)
            RouteContact(contact);
        // After the starts, so that a contact that starts and ends in one step reaches each entity in that order.
        foreach (ContactEnded ended in stepOutput.ContactsEnded)
            RouteContactEnd(ended);
    }

    /// <summary>
    /// The row of a body that physics reports in the system of <paramref name="home"/>, with <paramref name="pose"/> and
    /// the velocities relative to the home. The row frame is the home's semantic frame, and the pose moves into it in
    /// doubles: numeric frames do not rotate, so that is one addition of positions. The linear velocity stays relative to
    /// the home (see <see cref="TransformRow.LinearVelocity"/>).
    /// </summary>
    private TransformRow PhysicsRow(FrameHandle home, in DPose pose, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        FrameHandle semantic = Frames.SemanticOf(home);
        return new TransformRow
        {
            Frame = semantic,
            PhysicsHome = home,
            Pose = home == semantic ? pose : new DPose(Frames.InSemantic(home, semantic, pose.Position), pose.Rotation),
            HomePosition = pose.Position,
            LinearVelocity = linearVelocity,
            AngularVelocity = angularVelocity,
        };
    }

    /// <summary>
    /// The first pass of step 2, before any state lands: the check of every output of physics (see
    /// <see cref="PhysicsOutputCheck"/> for the rules). An output that breaks a rule comes only from a bug in the backend.
    /// It breaks a contract in Debug builds. In Release builds the sync skips, for this step, the whole physics update of
    /// every entity that the output names, and it records exactly one error for each broken output: however many rules
    /// the output breaks, and also when another broken output skipped the same entities already. For a frame state, that
    /// entity is the frame's owner. A broken character state skips only itself, and the rest of the entity's update lands.
    /// The skip does not change physics: the backend keeps each body where it put it. A frame hull goes back where the
    /// tree says at the start of the next step. A plain body that physics moved into a frame that is gone stays in that
    /// frame's system, so later outputs about it are broken too, until physics finds the frame gone and removes the body,
    /// with a warning.
    /// </summary>
    private void SkipBrokenPhysicsUpdates()
    {
        physicsOutputCheck.Run(stepOutput, Frames, new PhysicsOutputFacts(entities));
        foreach (PhysicsOutputFault fault in physicsOutputCheck.Faults)
        {
            // Only a bug in the backend produces a broken output.
            Contract.Assert(false, fault.Message);
            Report($"tick {TickNumber}: {fault.Message}.", isError: true);
        }
    }

    /// <summary>
    /// The second pass of step 2, before any state lands: moves the frame of each handed-off frame owner under its new
    /// parent, in hand-off order. A hand-off is a proposal of physics, and the frame tree decides. The check of the first
    /// pass refused every hand-off that would put a frame under itself or its own descendant, with the earlier hand-offs
    /// of the step applied, so every move here is one that the tree takes.
    /// </summary>
    private void MoveHandedOffFrames()
    {
        foreach (HandOff handOff in stepOutput.HandOffs)
        {
            Entity entity = handOff.Entity;
            if (physicsOutputCheck.IsSkipped(entity))
                continue;
            FrameHandle own = entities.OwnedFrame(entity);
            if (!own.IsNull)
                Frames.Reparent(own, handOff.To);
        }
    }

    /// <summary>
    /// Sends a Collision to each entity of a contact, with the point and normal in that entity's frame. A contact is part
    /// of the update of both entities, so a contact that names a skipped update reaches neither. The first pass has
    /// checked that both entities are live and that the contact's frame is alive.
    /// </summary>
    private void RouteContact(in ContactStarted contact)
    {
        if (physicsOutputCheck.IsSkipped(contact.A) || physicsOutputCheck.IsSkipped(contact.B))
            return;
        AddCollision(contact.A, contact.B, contact.Frame, contact.Point, contact.Normal, contact.Impulse);
        AddCollision(contact.B, contact.A, contact.Frame, contact.Point, -contact.Normal, contact.Impulse);
    }

    private void AddCollision(Entity recipient, Entity other, FrameHandle contactFrame, Double3 point, Vector3 normal, float impulse)
    {
        // The owner of a root frame is in no frame. Its only body is its interior, in its own frame.
        FrameHandle frame = entities.BackRow(recipient).Frame;
        if (frame.IsNull)
            frame = entities.OwnedFrame(recipient);
        if (frame != contactFrame)
        {
            DPose toRecipient = Frames.Relative(contactFrame, frame);
            point = toRecipient.TransformPoint(point);
            normal = toRecipient.Rotation.Rotate(normal);
        }
        router.AddEngineEvent(Event.Create(new Target(recipient), Entity.Null, new Collision { Other = other, Point = point, Normal = normal, Impulse = impulse }));
    }

    /// <summary>
    /// Sends a CollisionEnded to each entity of an ended contact. Destroying a body ends its contacts, so one of the two
    /// can be gone, and routing drops the event of an entity that is gone, as it drops every event to a stale handle. Like
    /// a start, an end is part of the update of both entities, so an end that names a skipped update reaches neither.
    /// </summary>
    private void RouteContactEnd(in ContactEnded ended)
    {
        if (physicsOutputCheck.IsSkipped(ended.A) || physicsOutputCheck.IsSkipped(ended.B))
            return;
        router.AddEngineEvent(Event.Create(new Target(ended.A), Entity.Null, new CollisionEnded { Other = ended.B }));
        router.AddEngineEvent(Event.Create(new Target(ended.B), Entity.Null, new CollisionEnded { Other = ended.A }));
    }

    /// <summary>Applies the subscribe and unsubscribe requests of the chunks, in chunk order, for entities that are still live.</summary>
    private void ApplySubscriptionChanges()
    {
        foreach (ChunkState chunk in entities.Chunks)
        {
            foreach (SubscriptionChange change in chunk.SubscriptionChanges)
            {
                if (!entities.IsAlive(change.Entity))
                    continue;
                if (change.Subscribe)
                    SubscribeEntity(change.Entity, change.Topic);
                else
                    topics.Unsubscribe(change.Entity, change.Topic);
            }
        }
    }

    /// <summary>Destroys every entity that asked to be destroyed this tick, in chunk order, with everything that dies with it.</summary>
    private void DestroyRequestedEntities()
    {
        foreach (ChunkState chunk in entities.Chunks)
            foreach (Entity entity in chunk.Destroys)
                MarkDying(entity);
        if (dying.Count > 0)
            DestroyDying();
    }

    /// <summary>
    /// Step 7: Kepler and Spin frames from time, static frames from their owners' gameplay writes, then every owner's row
    /// from its frame. Physics frames got their states in step 2.
    /// </summary>
    private void AdvanceFrames(double endTime)
    {
        int n = Frames.Count;
        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = Frames.AtDense(i);
            // A numeric frame moved in step 2, and it has no owner to follow it.
            if (Frames.IsNumeric(frame))
                continue;
            switch (Frames.Driver(frame))
            {
                case MotionDriver.Kepler:
                    ApplyKepler(frame, endTime);
                    break;
                case MotionDriver.Spin:
                    ApplySpin(frame, endTime);
                    break;
                case MotionDriver.Static:
                    // A static frame whose owner has no body moves when gameplay writes the owner's transform. Gameplay can
                    // write any rotation, but the local pose of a frame must be unit length (see FrameTree), so the world
                    // normalizes it here, and the owner's row follows the frame (see MirrorFrameOwner).
                    Entity owner = frame.Entity;
                    if (entities.GameplayWroteTransform(owner))
                        Frames.SetLocalPose(frame, entities.BackRow(owner).Pose.WithUnitRotation());
                    break;
            }
            MirrorFrameOwner(frame);
        }
    }

    /// <summary>
    /// Step 8, and the end of an outside-tick change: the back rows become the front rows, and the frame tree and the
    /// spatial grids catch up with the new front state.
    /// </summary>
    private void SwapSnapshot()
    {
        entities.CopyDirtyRows();
        Frames.Update();
        spatial.Update();
    }
}
