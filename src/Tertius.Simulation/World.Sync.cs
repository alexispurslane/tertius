using System.Diagnostics;
using System.Numerics;
using Tertius.Frames.Rails;

namespace Tertius.Simulation;

public sealed partial class World
{
    // Entities whose back row physics wrote this sync, in the order of the writes. An entity that physics wrote twice
    // is here twice; its second entry finds its membership already moved.
    private readonly List<Entity> physicsTouched = new();

    // The step record of each frame whose hull physics moved in the step, which the check did not skip, in the order of the
    // output. Step 2 fills it from the output and FollowOwners puts each frame on its record (see "One motion record per
    // frame" in docs/plans/rails-components.md), with the proper acceleration that physics measured for a dynamic hull. It
    // is empty without a physics backend and after a step that failed.
    private readonly Dictionary<FrameHandle, FrameStepRecord> stepRecords = new();

    // The entities without body or frame that took a step record at step 2 of this sync point, and those that took one at
    // the step 2 before, which FollowFramelessEntities swaps. An entity in the old list that takes no record now holds still
    // (see FollowFramelessEntities).
    private List<Entity> framelessMovers = new();
    private List<Entity> framelessMoversLast = new();

    // Scratch of step 4 (see ApplyMotionChanges): the owners whose change of motion the step that ended applied, then the
    // owners that wrote a change in the tick that ends.
    private readonly List<Entity> motionChanges = new();

    // Per entity: true when physics stood the character on walkable ground under gravity in its last step. The sync
    // point writes it from each step's character states, and gameplay reads it in the next tick. Nothing writes it
    // while gameplay runs, so it needs no second copy. The entity table clears it when it releases an entity.
    private readonly EntityTable.Column<bool> characterStanding;

    // The check of each step's output. It skips the whole physics update of every entity that physics reported something
    // about that only a bug in the backend produces. Their body states, their hand-offs, the states of the frames they
    // own, the contacts that name them and their character states are all ignored. A broken character state skips only
    // itself.
    private readonly PhysicsOutputCheck physicsOutputCheck = new();

    // The time of each step of the sync point (see SyncProfile). A build without SYNC_PROFILE compiles every lap away, but a
    // call that compiles away must still name a member, so the field stays: one array of a few longs per World.
    internal readonly SyncProfile Profile = new();

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
        Profile.Start();

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
        Profile.Lap(0);

        // 2. The falling numeric frames move over the step that physics made, with the acceleration that physics used.
        //    Then a check of the whole report and the hand-offs' reparents, which decides the skipped updates, then each
        //    orbit frame takes its anchor's sample, then body states and hand-offs, whose rows take the new places of their
        //    numeric homes, exactly. Then every frame takes its step record and every frame owner mirrors its frame (see
        //    FollowOwners): a record from physics is relative to the parent after the hand-offs, and every record must land
        //    before step 5 can destroy that parent or move the frame, so that a write in the tick in which a frame's parent
        //    dies converts with the frame. The motion intents that the step applied are still the front intents, which step
        //    4 swaps. Then each entity without body or frame takes its record on its transform row (see
        //    FollowFramelessEntities), before step 5 converts the rows out of dying frames and step 7 derives the attached
        //    children from their roots' rows. Contacts last, so that a point converted between frames uses the frame poses
        //    after the step.
        Frames.AdvanceNumericFrames(DeltaTime);
        Profile.Lap(1);
        stepRecords.Clear();
        physicsTouched.Clear();
        if (physics != null)
            ApplyPhysicsOutput();
        Profile.Lap(2);
        FollowOwners(stepFailed: physicsError != null);
        FollowFramelessEntities();
        Profile.Lap(25);
        if (physics != null)
            RouteStepContacts();
        Profile.Lap(6);

        // 3. Physics owns the transform of every entity with a body. The entity table refuses such writes, and checks
        //    that none got through among its invariants.
        if (options.DebugChecks)
            entities.AssertInvariants();
        Profile.Lap(7);

        // 4. The step applied the intents of the previous tick, so their collision groups and masks become the bodies' rows,
        //    which step 8 publishes, and their changes of motion become the stored motions of the hulls in the tree, also
        //    without a physics backend: like every state that the next tick reads, both describe the step that ended. The
        //    changes of motion of this tick plan the next step, which applies them: the frames and the intents drop what
        //    the new motion refuses. Then this tick's intents become the input of the next physics step. Without a physics
        //    backend the motion intents stay, because the sync point moves a frame without a body by them too.
        if (physics != null)
            entities.ApplyCollisionIntents();
        ApplyMotionChanges();
        entities.SwapIntents(keep: physics != null);
        Profile.Lap(8);

        // 5. Subscriptions first, while every entity that asked is still live, then the destroy batch.
        ApplySubscriptionChanges();
        DestroyRequestedEntities();
        Profile.Lap(9);

        // 6. New entities, after the destroy batch, so that a spawn into a frame that just died is dropped.
        MakeSpawnsLive();
        Profile.Lap(10);

        // 6c. Bodies move into numeric frames and out of them, after physics output, destroys and spawns, so that every
        //     body that is live at the end of the sync point is clustered where it is now. Every frame took its record in
        //     step 2, so the bodies' rows and the frames hold the end of the step, and the update tests them against bubbles
        //     of the same moment: a station that orbits at 7.5 km/s is not 126 m behind its bodies. The structural
        //     changes of this sync reach physics with the update's first batch, so that a spawn that joins a numeric frame
        //     is created in it, with its velocity converted in doubles.
        if (physics != null)
            UpdateNumericFrames(DeltaTime);
        FlushPhysicsChanges();
        Profile.Lap(11);

        // 7. The owner of every frame mirrors its frame again, after the numeric update, which can move a frame among numeric
        //    frames, unless nothing has changed the frame tree since the pass of step 2 (see MirrorFrameOwners). Then every
        //    attached entity follows.
        if (Frames.OwnerViewStamp != mirroredStamp)
            MirrorFrameOwners();
        Profile.Lap(12);
        DeriveAttachments();
        Profile.Lap(13);

        // 7b. The intents that the next step applies convert into the frames of the entities that this sync point moved:
        //     after the hand-offs of step 2 and the rehomes of step 5, which move entities, and after step 7, so that every
        //     entity is in the frame of the next tick, and every frame stands where the next step starts.
        //     The destroy batch of step 5 converted the intents that name its dying frames, which are gone by now. The
        //     motion intents convert with the rest of each intent.
        ConvertMovedIntents();
        Profile.Lap(14);

        // 8. The back tables become the front tables.
        SwapSnapshot();

        // 9. The physics queries of this tick, against Jolt at the end of the step with this sync's destroys and spawns, and
        //    against the new front snapshot, which describes the same moment: its component flags, collision groups,
        //    poses and frames. Their answers join the routing as engine events.
        queryRunner.Run(entities.Chunks, TickNumber);
        Profile.Lap(18);

        // 10. Messages, after every membership and subscription change of this sync, and after the answers of step 9.
        router.Route(TickNumber);
        Profile.Lap(19);

        // 11. The render snapshot of the new state.
        TickNumber++;
        Time = endTime;
        BuildRenderSnapshot();
        Profile.Lap(20);

        // 12. Ready for the next tick.
        entities.EndTick();
        if (options.DebugChecks)
            AssertInvariants();
        Profile.Lap(21);
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
    /// Step 2: the check of the report and the frame owners' reparents of the hand-offs, then the frames' step records and
    /// the orbit frames that their anchors' samples place, then body states and hand-offs into the back table, then whether
    /// physics stood each character. The frames take their records after this (see <see cref="FollowOwners"/>), and the
    /// contacts come last (see <see cref="RouteStepContacts"/>).
    /// </summary>
    private void ApplyPhysicsOutput()
    {
        // The states that only a bug produces, which the backend found and skipped during the step in a Release build.
        foreach (string error in stepOutput.Errors)
            Report($"tick {TickNumber}: {error}", isError: true);
        // Every update is decided before any state lands, so a skipped one changes nothing whatever the loops' order.
        SkipBrokenPhysicsUpdates();
        MoveHandedOffFrames();
        Profile.Lap(22);

        // The reparents already put each frame under the parent that its record is relative to. A frame that is gone has
        // its owner's update skipped.
        foreach (FrameStepRecord record in stepOutput.Frames)
            if (!physicsOutputCheck.IsSkipped(record.Frame.Entity))
                stepRecords[record.Frame] = record;

        // Each orbit frame takes its anchor's step plan before the rows of its members take the end of the step, so that
        // they are relative to its exact place. Physics aims a hull at the origin of its orbit frame, so the hull's record
        // is there, unless the hull teleported in the step: then the orbit frame jumps after the rows (see FollowOwners).
        foreach ((FrameHandle frame, FrameStepRecord record) in stepRecords)
            if (record.Motion.Pose.Position == Double3.Zero && Frames.InOrbitFrame(frame))
                Frames.PlaceFollowingFrame(frame);

        // The check put every entity that is not live, or has no body, among the skipped updates. The body of the owner of
        // a frame is its hull, whose frame's record moves the frame, and the owner's row mirrors the frame (see
        // FollowOwners), so its body state writes no row.
        foreach (BodyState state in stepOutput.Bodies)
        {
            Entity entity = state.Entity;
            if (physicsOutputCheck.IsSkipped(entity) || !entities.OwnedFrame(entity).IsNull)
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
    }

    /// <summary>
    /// Step 2, after the physics output: every frame takes its step record, in the tree's dense order (see "The step record
    /// of the last finished step" in docs/plans/rails-components.md), with the follow kind of the record (see
    /// <see cref="FrameTree.FollowKindInStep"/>). The owner's kind decides where the record comes from:
    /// <list type="bullet">
    /// <item>A hull, the body of the owner of a frame that has a parent: the record that physics released for it, relative to
    /// the frame's tree parent. Physics releases one when the hull is dynamic, kinematic and aimed by a motion intent, or
    /// static or kinematic without one and teleported in the step.</item>
    /// <item>No hull: the record of the owner's motion intent in the front intents, the intents of the step that ended,
    /// relative to the frame's semantic parent (see <see cref="FrameTree.StepRecordInStep"/>), and then a write of the
    /// owner's transform in the tick, which the sync point turns into a record at rest. The write comes last, so it wins for
    /// one tick, and its record is static. The next motion intent puts the frame back on its rails.</item>
    /// </list>
    /// Each frame takes the proper acceleration that its record gives, and a dynamic hull the one that physics measured
    /// beside its record (see <see cref="FrameTree.ProperAcceleration"/>). The rule reads the parent's value as the step
    /// found it, the value stored before step 2, so the tree keeps each new value aside in the pass and stores them all
    /// after it (see <see cref="FrameTree.StoreNewProperAccelerations"/>).
    /// A frame without a record holds still in its tree parent, unless its owner's update was skipped: the output check
    /// skipped it in a Release build (see <see cref="SkipBrokenPhysicsUpdates"/>), or the owner has a hull and the step
    /// failed (<paramref name="stepFailed"/>), so that physics released no record and may have moved the hull or not. Then
    /// the frame keeps its state, and the next step that works moves it on from Jolt's state. Then an orbit frame whose
    /// anchor's hull teleported jumps with it (see <see cref="FrameTree.JumpFollowingFrame"/>), and the rows of the bodies in
    /// it and the bodies in physics shift at once. Last, every frame owner mirrors its frame, whose state changed; a body
    /// state of a hull wrote no row.
    /// </summary>
    private void FollowOwners(bool stepFailed)
    {
        var follower = new OwnerFollower(this, stepFailed);
        Frames.VisitFrames(ref follower);
        // Every frame derived its proper acceleration with its parent's value as the step found it, so the new values go
        // into the rows only now.
        Frames.StoreNewProperAccelerations();

        Profile.Lap(3);
        // A hull that teleported in the step left the origin of its orbit frame, toward its step plan's place.
        bool jumped = false;
        foreach ((FrameHandle frame, FrameStepRecord record) in stepRecords)
        {
            if (record.Motion.Pose.Position == Double3.Zero || !Frames.InOrbitFrame(frame))
                continue;
            Frames.JumpFollowingFrame(frame, record.Motion, physicsChanges);
            jumped = true;
        }
        if (jumped)
        {
            // The rows of the bodies that stay lose the orbit frame's move, as for a drain, and physics shifts their bodies
            // and the hull in one batch.
            foreach (FrameShift shift in physicsChanges.FramesShifted)
                foreach (Entity entity in entities.LiveEntities)
                    if (entities.Body(entity) != null && attachments.ParentOf(entity).IsNull && entities.BackRow(entity).PhysicsHome == shift.Frame)
                        ShiftRow(entity, shift);
            FlushPhysicsChanges();
        }
        Profile.Lap(4);

        MirrorFrameOwners();
        Profile.Lap(5);
    }

    /// <summary>The pass of <see cref="FollowOwners"/> over every frame, in the tree's dense order.</summary>
    private readonly struct OwnerFollower(World world, bool stepFailed) : IFrameVisitor
    {
        public void Visit(in FrameLinks links) => world.FollowOwner(links, stepFailed);
    }

    /// <summary>One frame of <see cref="FollowOwners"/>: it takes its step record.</summary>
    private void FollowOwner(in FrameLinks links, bool stepFailed)
    {
        FrameHandle frame = links.Frame;
        Entity owner = frame.Entity;
        bool hull = !links.Parent.IsNull && entities.Body(owner) != null;
        // The intents of the step that ended, which step 4 swaps out after this.
        ReadOnlySpan<PhysicsIntent> stepIntents = entities.FrontIntents.Span;
        if (stepRecords.Count > 0 && stepRecords.TryGetValue(frame, out FrameStepRecord record))
            Frames.Follow(frame, links.TreeParent, record.Motion, Frames.FollowKindInStep(frame, stepIntents), record.ProperAcceleration, DeltaTime);
        // Nothing moved the tree pose of a frame without a hull since the step started, so the record starts there, as the
        // record that physics predicted for the frame's ghosts and hand-offs did.
        else if (!hull && Frames.StepRecordInStep(frame, stepIntents, DeltaTime, out MotionSample move))
            Frames.Follow(frame, links.Parent, move, Frames.FollowKindInStep(frame, stepIntents), measured: null, DeltaTime);
        // A skipped update changes nothing, so the frame keeps the velocities that it had before the step.
        else if (!(hull && stepFailed) && !physicsOutputCheck.IsSkipped(owner))
            Frames.HoldStill(frame, DeltaTime);

        // Gameplay can write any rotation, but the local pose of a frame must be unit length (see FrameTree), so the
        // world normalizes it here, and the owner's row follows the frame below (see MirrorFrameOwner). The write leaves
        // the frame at rest, so it feels the gravity at its new place reversed, as a frame that holds still, and it wins over
        // a motion intent of the same tick, so its record is static.
        if (!hull && entities.GameplayWroteTransform(owner))
        {
            ref readonly TransformRow row = ref entities.BackRow(owner);
            var write = new MotionSample(row.Pose.WithUnitRotation(), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero);
            Frames.Follow(frame, row.Frame, write, FollowKind.Static, measured: null, DeltaTime);
        }
    }

    /// <summary>
    /// Step 2, after <see cref="FollowOwners"/>: each entity without body or frame whose motion intent the step that ended
    /// applied takes the step record of that intent on its transform row (docs/plans/rails-components.md, "The transform row
    /// of an entity without body or frame"). Nothing moves such an entity but the sync point, so the record starts where the
    /// row stood when the step started (see <see cref="PhysicsIntent.StepRecord"/>), and the row takes the record's pose and
    /// its instant velocities narrowed to floats. A write of the entity's transform in the tick wins for one tick, as for a
    /// frame, and that write left the entity at rest (see <see cref="EntityTable.WritableTransform"/>). Then each entity that
    /// took a record at the step 2 before, and takes none now and wrote no transform, holds still: it gets zero velocities, as
    /// <see cref="FrameTree.HoldStill"/> gives a frame without a record, and as physics stops a kinematic body after its last
    /// motion intent. Physics skips an intent of an entity without a body, and the sync point moves it with or without a
    /// physics backend, also after a step that failed.
    /// </summary>
    private void FollowFramelessEntities()
    {
        (framelessMoversLast, framelessMovers) = (framelessMovers, framelessMoversLast);
        framelessMovers.Clear();
        // The intents of the step that ended, which step 4 swaps out after this.
        ReadOnlySpan<PhysicsIntent> stepIntents = entities.FrontIntents.Span;
        foreach (Entity entity in entities.IntentEntities.Span)
        {
            if (entities.Body(entity) != null || !entities.OwnedFrame(entity).IsNull)
                continue;
            ref readonly PhysicsIntent intent = ref stepIntents[(int)entity.Index];
            ref readonly TransformRow row = ref entities.BackRow(entity);
            // The guard lets no attached entity write a motion intent, and step 7b of the last sync point converted the intent
            // into the entity's frame, which is its home.
            Contract.Assert(intent.Frame == row.Frame && row.PhysicsHome == row.Frame);
            if (entities.GameplayWroteTransform(entity) || !intent.StepRecord(row.Pose, DeltaTime, out MotionSample record))
                continue;
            entities.SetBackTransform(entity, row with
            {
                Pose = record.Pose,
                LinearVelocity = record.Velocity.ToVector3(),
                AngularVelocity = record.AngularVelocity.ToVector3(),
            });
            framelessMovers.Add(entity);
        }
        foreach (Entity entity in framelessMoversLast)
        {
            if (!entities.IsAlive(entity) || stepIntents[(int)entity.Index].Move is EndPose or MotionSample || entities.GameplayWroteTransform(entity))
                continue;
            ref readonly TransformRow row = ref entities.BackRow(entity);
            entities.SetBackTransform(entity, row with { LinearVelocity = Vector3.Zero, AngularVelocity = Vector3.Zero });
        }
    }

    /// <summary>
    /// Step 2, after every frame took its record: each entity that physics moved to another frame changes topic and hears
    /// about it once, then the contacts become Collision events. The owner of a frame whose hull physics handed off moved
    /// already, in the pass of <see cref="MirrorFrameOwners"/> (see <see cref="MoveMembership"/>).
    /// </summary>
    private void RouteStepContacts()
    {
        foreach (Entity entity in physicsTouched)
            MoveMembership(entity, entities.BackRow(entity).Frame);

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
        physicsOutputCheck.Run(stepOutput, Frames, entities.FrontIntents.Span, new PhysicsOutputFacts(entities));
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

    // The owner view stamp of the frame tree (see FrameTree.OwnerViewStamp) at the end of the last pass of MirrorFrameOwners.
    // From that pass to the end of the sync point, only a write of the frame tree changes what the owners' rows must
    // mirror, so while the tree's stamp equals this one, every frame owner's row mirrors its frame. Gameplay writes the rows
    // of frames without a hull during the tick, so the pass of step 2 always runs.
    private long mirroredStamp = -1;

    /// <summary>
    /// Steps 2 and 7: the row of the owner of every frame mirrors its frame. In step 2 every frame took its record (see
    /// <see cref="FollowOwners"/>). In step 7 the destroy batch and the numeric update wrote the rows of the frames that they
    /// move or shift, but a numeric frame above the frame that moves under a nesting frame, or a nesting frame that jumps,
    /// changes no row, and it changes the sum that gives the frame's place in its semantic frame, which can then round to
    /// another double (see WorldFrameOwnerRowTests). So step 7 runs the pass whenever the frame tree changed since the pass
    /// of step 2, and only then: when nothing wrote the tree between them, as in a scene of frames on rails that no spawn,
    /// destroy or numeric update changed, the second pass found every row equal, at about 26 ns per frame.
    /// </summary>
    private void MirrorFrameOwners()
    {
        var mirror = new OwnerMirror(this);
        Frames.VisitOwnerViews(ref mirror);
        mirroredStamp = Frames.OwnerViewStamp;
    }

    /// <summary>
    /// The pass of <see cref="MirrorFrameOwners"/>: the owner of each frame mirrors it by the rule of
    /// <see cref="MirrorOwner"/>, which <see cref="MirrorFrameOwner"/> applies to one frame.
    /// </summary>
    private readonly struct OwnerMirror(World world) : IOwnerViewVisitor
    {
        public void Visit(in FrameOwnerView view) => world.MirrorOwner(view);
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
    /// Step 4, before the intents swap (docs/plans/rails-components.md, "Changes of motion"). First each hull whose change of
    /// motion the step that ended applied, from the front intents, stores that motion (see <see cref="FrameTree.HullMotion"/>),
    /// as the collision rows take their groups from the same intents: the stored motion describes the last finished step.
    /// Then each owner that wrote a change in this tick, which the next step applies, gets its frame planned for that step
    /// (see <see cref="FrameTree.PlanHullMotion"/>), and its intent loses the requests that the hull refuses in a tick under
    /// its motion in that step: a motion intent whose case the guard refuses (see <see cref="MotionIntentProblem"/>), so an
    /// end pose or a full sample of a hull that is not kinematic, and a steer target of a hull that is not dynamic, and the
    /// velocities of a kinematic hull (see <see cref="WritableMotionIntent"/>), which moves only by its motion intents and
    /// teleports. Its forces and impulses stay, because physics applies them only to a dynamic body. The motion intent that
    /// the running step applies stays, because the frame follows it at the next sync point. These drops follow the motion of
    /// the next step, whatever the motion of the step that ended: the guards of the tick judge each write by the motion at
    /// its moment, and a later change of the same tick can turn it.
    /// </summary>
    private void ApplyMotionChanges()
    {
        ReadOnlySpan<PhysicsIntent> applied = entities.FrontIntents.Span;
        motionChanges.Clear();
        entities.CollectFrontMotionChanges(motionChanges);
        foreach (Entity owner in motionChanges)
        {
            FrameHandle frame = entities.OwnedFrame(owner);
            // SetMotion refuses a body that is no hull, and in the World a frame never becomes a root after its spawn.
            Contract.Assert(!frame.IsNull && Frames.HullMotion(frame) is not null);
            Frames.SetHullMotion(frame, applied[(int)owner.Index].Motion);
        }

        motionChanges.Clear();
        entities.CollectMotionChanges(motionChanges);
        foreach (Entity owner in motionChanges)
        {
            ref PhysicsIntent intent = ref entities.WrittenIntent(owner);
            FrameHandle frame = entities.OwnedFrame(owner);
            Contract.Assert(!frame.IsNull && Frames.HullMotion(frame) is not null);
            Frames.PlanHullMotion(frame, intent.Motion);
            // Before the swap, the guard reads the change that this intent holds as the hull's motion in the next step.
            if (MotionIntentProblem(owner, intent.Move) is not null)
                intent.Move = default;
            if (intent.Motion == BodyMotion.Kinematic)
                intent.Flags &= ~(IntentFlags.SetVelocity | IntentFlags.SetAngularVelocity);
        }
    }

    /// <summary>
    /// Converts <paramref name="intent"/> from the frame that it names into <paramref name="to"/>, with the frames' relation
    /// now, for a body at <paramref name="position"/> in <paramref name="to"/>, and makes it name <paramref name="to"/>. The
    /// teleport's pose converts as a pose, the velocities of SetVelocity and SetAngularVelocity with the motion of one frame
    /// relative to the other, the motion intent by its case (see <see cref="MotionIntent.Converted"/>), and forces, torques,
    /// impulses and a character's walk as directions. The velocities are instant velocities at the start of the next step,
    /// the moment of the relation, so they convert exactly. An end pose and a steer target are places at the end of the
    /// step, so they miss by how far the two frames move relative to each other in a step.
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
        if (intent.Move.HasValue)
        {
            // The motion of the old frame in the new one now, by which a full sample converts as a body that hands off
            // between the two frames does. An end pose and a steer target read only its pose.
            var motion = new FrameMotion(relative, Frames.ConvertVelocity(from, to, Double3.Zero, Double3.Zero), Frames.AngularVelocityRelative(from, to));
            intent.Move = intent.Move.Converted(motion, Frames.AccelerationRelative(from, to));
        }
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
    /// spatial grids catch up with the new front state. The entity table and the spatial index run their large steps on
    /// the workers in both cases. The laps record only inside the sync point (see <see cref="SyncProfile"/>).
    /// </summary>
    private void SwapSnapshot()
    {
        entities.CopyDirtyRows();
        Profile.Lap(15);
        Frames.Update();
        Profile.Lap(16);
        spatial.Update();
        Profile.Lap(17);
    }
}
