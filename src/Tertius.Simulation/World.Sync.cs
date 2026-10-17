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

        // 6b. Kepler and Spin frames advance to the end of the tick, and static frames take their owners' gameplay writes,
        //     with their owners' rows: every frame that physics does not drive reaches its place at the end of the tick.
        //     The bodies' rows and the frames that physics drives hold the end of the step since step 2, so the numeric
        //     update of 6c tests them against bubbles of the same moment: a station that orbits at 7.5 km/s would else be
        //     126 m behind its bodies. A Kepler frame in its orbit frame puts that frame on the orbit, which step 2 moved
        //     there to its rounding, so the frame's place in its semantic parent is the orbit's to the last bit at every
        //     sync point, and the update of 6c makes a new orbit frame from the orbit's place at the end of the tick, the
        //     moment at which it clusters every body. After the destroy batch and the spawns, because this step mirrors the
        //     owners of the frames that they move and create, and because a frame whose orbit frame lost it to a destroy
        //     places itself in its new tree parent. Nothing between this step and step 7 reads these frames at the start of
        //     the tick: the update and the flush of 6c read them at its end, as physics does in its next step.
        AdvanceDrivenFrames(endTime);

        // 6c. Bodies move into numeric frames and out of them, after physics output, destroys, spawns and the frames of
        //     6b, so that every body that is live at the end of the sync point is clustered where it is now. The structural
        //     changes of this sync reach physics with the update's first batch, so that a spawn that joins a numeric frame
        //     is created in it, with its velocity converted in doubles.
        if (physics != null)
            UpdateNumericFrames(DeltaTime);
        FlushPhysicsChanges();

        // 7. The owner of every frame that physics drives mirrors its frame, after the numeric update, which can move such
        //    a frame among numeric frames. Then every attached entity follows.
        MirrorPhysicsFrameOwners();
        DeriveAttachments();

        // 7b. The intents that the next step applies convert into the frames of the entities that this sync point moved:
        //     after the hand-offs of step 2 and the rehomes of step 5, which move entities, and after step 7, so that every
        //     entity is in the frame of the next tick; after 6b, so that every frame stands where the next step starts.
        //     The destroy batch of step 5 converted the intents that name its dying frames, which are gone by now.
        ConvertMovedIntents();

        // 8. The back tables become the front tables.
        SwapSnapshot();

        // 9. The physics queries of this tick, against Jolt at the end of the step with this sync's destroys and spawns, and
        //    against the new front snapshot, which describes the same moment: its component flags, collision groups,
        //    poses and frames. Their answers join the routing as engine events.
        queryRunner.Run(entities.Chunks, TickNumber);

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
    /// Step 6b: Kepler and Spin frames from time, static frames from their owners' gameplay writes, then each one's owner's
    /// row from its frame. These frames hang in semantic frames, or a Kepler frame in its orbit frame, which it places on
    /// the orbit with itself (see <see cref="FrameTree.PlaceOnOrbit"/>); the loop skips the orbit frame, which has no owner.
    /// The numeric update moves such a frame only into or out of its orbit frame, which keeps its place, and it writes the
    /// owner's row when it does, so the owners' rows hold after it.
    /// </summary>
    private void AdvanceDrivenFrames(double endTime)
    {
        int n = Frames.Count;
        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = Frames.AtDense(i);
            // A numeric frame moved in step 2, and it has no owner to follow it. A physics frame got its state in step 2.
            if (Frames.IsNumeric(frame) || Frames.Driver(frame) == MotionDriver.Physics)
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
    /// Step 7: the row of the owner of every frame that physics drives mirrors its frame. Step 2 wrote it from the frame's
    /// state, and the destroy batch and the numeric update write the rows of the frames that they move or shift. A numeric
    /// frame above the frame that moves under a nesting frame, or a nesting frame that jumps, changes no row, but it changes
    /// the sum that gives the frame's place in its semantic frame, which can then round to another double.
    /// </summary>
    private void MirrorPhysicsFrameOwners()
    {
        int n = Frames.Count;
        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = Frames.AtDense(i);
            if (!Frames.IsNumeric(frame) && Frames.Driver(frame) == MotionDriver.Physics)
                MirrorFrameOwner(frame);
        }
    }

    /// <summary>
    /// Step 7b: gameplay wrote each intent that the next step applies relative to the frame that its entity was in during
    /// the tick, which the intent names, and this sync point may have moved the entity since: a hand-off of the step that
    /// ran during the tick, or a rehome out of a frame that died. Physics applies an intent in the entity's frame, so each
    /// such intent converts into it here, for a body where its row is now. Without this, a kinematic body that gameplay
    /// moved in every tick and that a passing bubble took in got a target of its old frame in the new one, jumped out of
    /// the bubble and back, and changed frames in every tick (ter-dhkf).
    /// </summary>
    private void ConvertMovedIntents()
    {
        foreach (Entity entity in entities.IntentEntities.Span)
        {
            ref PhysicsIntent intent = ref entities.NextStepIntent(entity);
            ref readonly TransformRow row = ref entities.BackRow(entity);
            if (intent.Frame != row.Frame)
                ConvertIntent(ref intent, row.Frame, row.Pose.Position);
        }
    }

    /// <summary>
    /// Converts <paramref name="intent"/> from the frame that it names into <paramref name="to"/>, with the frames' relation
    /// now, for a body at <paramref name="position"/> in <paramref name="to"/>, and makes it name <paramref name="to"/>. The
    /// poses convert as poses, the velocities of SetVelocity and SetAngularVelocity with the motion of one frame relative
    /// to the other, and forces, torques, impulses and a character's walk as directions. The velocities are instant
    /// velocities at the start of the next step, the moment of the relation, so they convert exactly. A kinematic target
    /// is a place at the end of the step, so it misses by how far the two frames move relative to each other in a step.
    /// </summary>
    private void ConvertIntent(ref PhysicsIntent intent, FrameHandle to, Double3 position)
    {
        FrameHandle from = intent.Frame;
        Contract.Requires(Frames.IsAlive(from) && Frames.IsAlive(to));
        DPose relative = Frames.Relative(from, to);
        IntentFlags flags = intent.Flags;
        if ((flags & IntentFlags.SetVelocity) != 0)
        {
            // A teleport in the same intent puts the body at its pose before the velocity applies.
            Double3 at = (flags & IntentFlags.Teleport) != 0 ? intent.TeleportPose.Position : relative.InverseTransformPoint(position);
            intent.Velocity = Frames.ConvertVelocity(from, to, at, intent.Velocity);
        }
        if ((flags & IntentFlags.SetAngularVelocity) != 0)
            intent.AngularVelocity = (relative.TransformDirection(intent.AngularVelocity) + Frames.AngularVelocityRelative(from, to)).ToVector3();
        if ((flags & IntentFlags.KinematicTarget) != 0)
            intent.Target = DPose.Compose(relative, intent.Target);
        if ((flags & IntentFlags.Teleport) != 0)
            intent.TeleportPose = DPose.Compose(relative, intent.TeleportPose);
        intent.Force = relative.TransformDirection(intent.Force).ToVector3();
        intent.Torque = relative.TransformDirection(intent.Torque).ToVector3();
        intent.Impulse = relative.TransformDirection(intent.Impulse).ToVector3();
        intent.AngularImpulse = relative.TransformDirection(intent.AngularImpulse).ToVector3();
        intent.CharacterVelocity = relative.TransformDirection(intent.CharacterVelocity).ToVector3();
        intent.Frame = to;
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
