using System.Numerics;

namespace Tertius.Simulation;

public sealed partial class World
{
    // Entities whose back row physics wrote this sync, in the order of the writes. An entity that physics wrote twice
    // is here twice; its second entry finds its membership already moved.
    private readonly List<Entity> physicsTouched = new();

    // Entities whose whole physics update this sync skips, because physics reported something about them that only a bug
    // in the backend produces. Their body states, their hand-offs, the states of the frames they own and the contacts
    // that name them are all ignored.
    private readonly HashSet<Entity> skippedPhysicsUpdates = new();

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
        if (physicsError != null)
        {
            // A step that threw may have written only part of its output. Use none of it.
            Report($"tick {TickNumber}: the physics step failed and its results were dropped: {physicsError.GetType().Name}: {physicsError.Message}", isError: true);
            stepOutput.Clear();
        }

        // 2. A check of the whole report and the hand-offs' reparents first, which decides the skipped updates, then body
        //    states and hand-offs, then the physics frames' states and their owners' rows: a state is relative to the
        //    parent after the hand-offs, and must land before step 5 can destroy that parent or move the hull. Contacts
        //    last.
        if (physics != null)
            ApplyPhysicsOutput();

        // 3. Physics owns the transform of every entity with a body. The entity table refuses such writes, and checks
        //    that none got through among its invariants.
        if (options.DebugChecks)
            entities.AssertInvariants();

        // 4. This tick's intents become the input of the next physics step.
        entities.SwapIntents(keep: physics != null);

        // 5. Subscriptions first, while every entity that asked is still live, then the destroy batch.
        ApplySubscriptionChanges();
        DestroyRequestedEntities();

        // 6. New entities, after the destroy batch, so that a spawn into a frame that just died is dropped. Then the
        //    structural changes of this sync reach physics as one batch.
        MakeSpawnsLive(endTime);
        FlushPhysicsChanges();

        // 7. Kepler frames advance to the end of the tick, and every frame owner, then every attached entity, follows.
        AdvanceFrames(endTime);
        DeriveAttachments();

        // 8. Messages, after every membership and subscription change of this sync.
        router.Route(TickNumber);

        // 9. The back tables become the front tables.
        SwapSnapshot();

        // 10. The render snapshot of the new state.
        TickNumber++;
        Time = endTime;
        BuildRenderSnapshot();

        // 11. Ready for the next tick.
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
    /// into the back table, then the new states of the physics-driven frames and their owners' rows, then contacts as
    /// Collision events. The contacts come after the frame states, so that a point converted between frames uses the
    /// frame poses after the step, like the bodies' own poses.
    /// </summary>
    private void ApplyPhysicsOutput()
    {
        physicsTouched.Clear();
        // Every update is decided before any state lands, so a skipped one changes nothing whatever the loops' order.
        SkipBrokenPhysicsUpdates();
        MoveHandedOffFrames();

        // The checks of the first pass put every entity that is not live, or has no body, among the skipped updates.
        foreach (BodyState state in stepOutput.Bodies)
        {
            Entity entity = state.Entity;
            if (skippedPhysicsUpdates.Contains(entity))
                continue;
            entities.SetBackTransform(entity, new TransformRow
            {
                Frame = state.Frame,
                Pose = state.Pose,
                LinearVelocity = state.LinearVelocity,
                AngularVelocity = state.AngularVelocity,
            });
            physicsTouched.Add(entity);
        }

        foreach (HandOff handOff in stepOutput.HandOffs)
        {
            Entity entity = handOff.Entity;
            if (skippedPhysicsUpdates.Contains(entity))
                continue;
            entities.SetBackTransform(entity, new TransformRow
            {
                Frame = handOff.To,
                Pose = handOff.Pose,
                LinearVelocity = handOff.LinearVelocity,
                AngularVelocity = handOff.AngularVelocity,
            });
            physicsTouched.Add(entity);
        }

        // The reparents already put each frame under the parent that its state is relative to. The owner's row mirrors
        // the new state at once, so that a hull that the destroy batch moves to another physics system starts from the
        // state after the step. A frame that is gone has its owner's update skipped.
        foreach (FrameState state in stepOutput.Frames)
        {
            if (skippedPhysicsUpdates.Contains(state.Frame.Entity))
                continue;
            Frames.SetLocalPose(state.Frame, state.LocalPose);
            Frames.LinearVelocity(state.Frame) = state.LinearVelocity;
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
    }

    /// <summary>
    /// The first pass of step 2, before any state lands. It checks each output of physics against what a correct backend
    /// keeps:
    /// <list type="bullet">
    /// <item>Entities and frames die in step 5 or outside a tick, and physics hears of each death before its next step.
    /// An entity keeps its body for life. So every body state, hand-off and contact names live entities with bodies.</item>
    /// <item>For the same reason, the frame of every body state and contact is alive, and so is the target of every
    /// hand-off. None of them is null.</item>
    /// <item>Physics follows the frame tree. So a hand-off leaves the frame that the entity is in at the start of the
    /// step (for the owner of a frame, the frame's parent in the tree) and enters another frame. The owner of a root frame
    /// is in no frame, so it never hands off.</item>
    /// <item>Physics drives a frame through the hull of the frame's owner. So a frame state names a live frame with the
    /// physics driver whose owner has a body.</item>
    /// <item>A contact names two different entities.</item>
    /// </list>
    /// Any other output comes only from a bug in the backend. It breaks a contract in Debug builds. In Release builds the
    /// sync skips, for this step, the whole physics update of every entity that the output names. It records exactly one
    /// error for each broken output, however many checks the output fails, and also when another broken output skipped
    /// the same entities already. For a frame state, that entity is the frame's owner. The skip does not change physics: the backend keeps each body
    /// where it put it. A frame hull goes back where the tree says at the start of the next step. A plain body that
    /// physics moved into a frame that is gone stays in that frame's system, so later outputs about it are broken too,
    /// until physics finds the frame gone and removes the body, with a warning.
    /// </summary>
    private void SkipBrokenPhysicsUpdates()
    {
        skippedPhysicsUpdates.Clear();
        foreach (BodyState state in stepOutput.Bodies)
        {
            // The pose and velocities are relative to the reported frame, so they are useless in any other frame.
            string? fault = BodyFault(state.Entity) ?? FrameFault(state.Frame);
            Contract.Assert(fault is null, fault);
            if (fault is not null)
                SkipPhysicsUpdate(state.Entity, $"ignored the body state of {state.Entity}, because {fault}");
        }
        foreach (HandOff handOff in stepOutput.HandOffs)
        {
            string? fault = BodyFault(handOff.Entity) ?? FrameFault(handOff.To) ?? MoveFault(handOff);
            Contract.Assert(fault is null, fault);
            if (fault is not null)
                SkipPhysicsUpdate(handOff.Entity, $"ignored the hand-off of {handOff.Entity}, because {fault}");
        }
        foreach (FrameState state in stepOutput.Frames)
        {
            string? fault = DrivenFrameFault(state.Frame);
            Contract.Assert(fault is null, fault);
            if (fault is not null)
                SkipPhysicsUpdate(state.Frame.Entity, $"ignored the state of {fault}");
        }
        foreach (ContactStarted contact in stepOutput.Contacts)
        {
            // The point and normal are relative to the contact's frame.
            string? fault = BodyFault(contact.A) ?? BodyFault(contact.B) ?? (contact.A == contact.B ? $"{contact.A} cannot touch itself" : null) ??
                FrameFault(contact.Frame);
            Contract.Assert(fault is null, fault);
            if (fault is not null)
                SkipPhysicsUpdates(contact.A, contact.B, $"ignored the contact of {contact.A} and {contact.B}, because {fault}");
        }
    }

    /// <summary>Why physics cannot name <paramref name="entity"/> in its output, or null when it can.</summary>
    private string? BodyFault(Entity entity) =>
        !entities.IsAlive(entity) ? $"{entity} no longer exists" :
        entities.Body(entity) == null ? $"{entity} has no body" :
        null;

    /// <summary>Why physics cannot report a state relative to <paramref name="frame"/>, or null when it can.</summary>
    private string? FrameFault(FrameHandle frame) =>
        frame.IsNull ? "it names no frame" :
        !Frames.IsAlive(frame) ? $"it names the stale frame {frame}" :
        null;

    /// <summary>
    /// Why a hand-off of a live entity into a live frame is not a move that physics makes, or null when it is. The entity
    /// is in its row's frame, or, if it owns a frame, in the frame's parent in the tree. Every write to the row of a body
    /// comes from physics, and the row of a frame owner mirrors its frame, so both are where the step began.
    /// </summary>
    private string? MoveFault(in HandOff handOff)
    {
        Entity entity = handOff.Entity;
        FrameHandle own = entities.OwnedFrame(entity);
        FrameHandle current = own.IsNull ? entities.BackRow(entity).Frame : Frames.Parent(own);
        return current.IsNull ? $"{entity} is in no frame, so it leaves none" :
            handOff.From != current ? $"{entity} is in {current}, not in {handOff.From}" :
            handOff.To == current ? $"it moves {entity} into {current}, which it is in already" :
            null;
    }

    /// <summary>
    /// Why physics cannot report the state of <paramref name="frame"/>, or null when it can. The fault names the frame,
    /// because a stale frame has no owner to blame.
    /// </summary>
    private string? DrivenFrameFault(FrameHandle frame) =>
        !Frames.IsAlive(frame) ? $"the stale frame {frame}" :
        Frames.Driver(frame) != MotionDriver.Physics ? $"{frame}, because physics does not drive it" :
        BodyFault(frame.Entity) is { } fault ? $"{frame}, because {fault}" :
        null;

    /// <summary>
    /// The second pass of step 2, before any state lands: moves the frame of each handed-off frame owner under its new
    /// parent, in hand-off order. A hand-off is a proposal of physics, and the frame tree decides. A correct backend
    /// never proposes one that the tree refuses, because its reconcile starts every step with each frame hull in the
    /// system of its frame's parent in the tree. From there a hull can only enter a sibling of its frame or
    /// leave for the parent's parent, never its own frame. It enters a sibling only when its bubble fits in the
    /// sibling's, and fitting is a strict order of size, so the entries of one step never close a loop. So no hand-off
    /// puts a frame under itself or its own descendant. One that does comes only from a bug in the backend. It breaks a
    /// contract in Debug builds. In Release builds the sync skips the whole physics update of that entity for this step
    /// and records one error, and the frame stays where it was. The check also covers the hand-off of an entity whose
    /// update another output skipped already, so that every broken output records its error.
    /// </summary>
    private void MoveHandedOffFrames()
    {
        foreach (HandOff handOff in stepOutput.HandOffs)
        {
            Entity entity = handOff.Entity;
            // A hand-off that the first pass found broken has its error already.
            if (BodyFault(entity) is not null || FrameFault(handOff.To) is not null || MoveFault(handOff) is not null)
                continue;
            FrameHandle own = entities.OwnedFrame(entity);
            if (own.IsNull)
                continue;
            bool intoItself = handOff.To == own || Frames.IsAncestorOf(own, handOff.To);
            Contract.Assert(!intoItself);
            if (intoItself)
            {
                SkipPhysicsUpdate(entity, $"refused the hand-off of {entity} from {handOff.From} to {handOff.To}, which would put {own} under itself or its own descendant");
                continue;
            }
            if (!skippedPhysicsUpdates.Contains(entity))
                Frames.Reparent(own, handOff.To);
        }
    }

    /// <summary>Skips the whole physics update of an entity for this step, and records one error for the broken output.</summary>
    private void SkipPhysicsUpdate(Entity entity, string error)
    {
        skippedPhysicsUpdates.Add(entity);
        Report($"tick {TickNumber}: {error}.", isError: true);
    }

    /// <summary>
    /// Skips the whole physics update of both entities of a broken contact for this step, and records one error for the
    /// contact.
    /// </summary>
    private void SkipPhysicsUpdates(Entity a, Entity b, string error)
    {
        skippedPhysicsUpdates.Add(a);
        skippedPhysicsUpdates.Add(b);
        Report($"tick {TickNumber}: {error}.", isError: true);
    }

    /// <summary>
    /// Sends a Collision to each entity of a contact, with the point and normal in that entity's frame. A contact is part
    /// of the update of both entities, so a contact that names a skipped update reaches neither. The first pass has
    /// checked that both entities are live and that the contact's frame is alive.
    /// </summary>
    private void RouteContact(in ContactStarted contact)
    {
        if (skippedPhysicsUpdates.Contains(contact.A) || skippedPhysicsUpdates.Contains(contact.B))
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
    /// Step 7: Kepler frames from time, static frames from their owners' gameplay writes, then every owner's row from its
    /// frame. Physics frames got their states in step 2.
    /// </summary>
    private void AdvanceFrames(double endTime)
    {
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
                    Entity owner = frame.Entity;
                    if (entities.GameplayWroteTransform(owner))
                        Frames.SetLocalPose(frame, entities.BackRow(owner).Pose);
                    break;
            }
            MirrorFrameOwner(frame);
        }
    }

    /// <summary>
    /// Step 9, and the end of an outside-tick change: the back rows become the front rows, and the frame tree and the
    /// spatial grids catch up with the new front state.
    /// </summary>
    private void SwapSnapshot()
    {
        entities.CopyDirtyRows();
        Frames.Update();
        spatial.Update();
    }
}
