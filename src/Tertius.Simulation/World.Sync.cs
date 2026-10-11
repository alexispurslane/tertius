using System.Numerics;

namespace Tertius.Simulation;

public sealed partial class World
{
    // Entities whose back row physics wrote this sync, in the order of the writes. An entity that physics wrote twice
    // is here twice; its second entry finds its membership already moved.
    private readonly List<Entity> physicsTouched = new();

    // Entities whose hand-off this sync did not apply: its target frame is gone, or the frame tree refused to move the
    // entity's frame there. Physics reported their body states, and the states of the frames they own, relative to that
    // target, so the sync ignores those too.
    private readonly HashSet<Entity> refusedHandOffs = new();

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

        // 2. The hand-offs' reparents first, which decides the refused ones, then body states and hand-offs, then the
        //    physics frames' states and their owners' rows: a state is relative to the parent after the hand-offs, and
        //    must land before step 5 can destroy that parent or move the hull. Contacts last.
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
    /// Step 2: the frame owners' reparents of the hand-offs, then body states and hand-offs into the back table, then
    /// the new states of the physics-driven frames and their owners' rows, then contacts as Collision events. The
    /// contacts come after the frame states, so that a point converted between frames uses the frame poses after the
    /// step, like the bodies' own poses.
    /// </summary>
    private void ApplyPhysicsOutput()
    {
        physicsTouched.Clear();
        // Every hand-off is decided before any state lands, so a refused one changes nothing whatever the loops' order.
        MoveHandedOffFrames();

        foreach (BodyState state in stepOutput.Bodies)
        {
            Entity entity = state.Entity;
            if (!entities.IsAlive(entity) || entities.Body(entity) == null || refusedHandOffs.Contains(entity))
                continue;
            // The pose and velocities are relative to the reported frame, so a state in a frame that is gone is useless
            // in any other frame. The entity keeps its frame and its row, as with a hand-off to a stale frame.
            if (!state.Frame.IsNull && !Frames.IsAlive(state.Frame))
            {
                Report($"tick {TickNumber}: ignored the body state of {entity} in the stale frame {state.Frame}.", isError: true);
                continue;
            }
            entities.SetBackTransform(entity, new TransformRow
            {
                Frame = state.Frame.IsNull ? entities.BackRow(entity).Frame : state.Frame,
                Pose = state.Pose,
                LinearVelocity = state.LinearVelocity,
                AngularVelocity = state.AngularVelocity,
            });
            physicsTouched.Add(entity);
        }

        foreach (HandOff handOff in stepOutput.HandOffs)
        {
            Entity entity = handOff.Entity;
            if (!entities.IsAlive(entity) || refusedHandOffs.Contains(entity))
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

        // The first pass already put each frame under the parent that its state is relative to. The owner's row
        // mirrors the new state at once, so that a hull that the destroy batch moves to another physics system starts
        // from the state after the step.
        foreach (FrameState state in stepOutput.Frames)
        {
            if (!Frames.IsAlive(state.Frame) || refusedHandOffs.Contains(state.Frame.Entity))
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
    /// The first pass of step 2, before any body state or frame state lands: moves the frame of each handed-off frame
    /// owner under its new parent, in hand-off order, and fills <see cref="refusedHandOffs"/> with the entities whose
    /// hand-off is not applied. A hand-off to a frame that is gone is not applied, and neither is one that the frame
    /// tree refuses because it would put a frame under itself or its own descendant. The tree refuses before it changes
    /// anything, so a refused hand-off leaves the frame as it was.
    /// </summary>
    private void MoveHandedOffFrames()
    {
        refusedHandOffs.Clear();
        foreach (HandOff handOff in stepOutput.HandOffs)
        {
            Entity entity = handOff.Entity;
            if (!entities.IsAlive(entity))
                continue;
            if (!Frames.IsAlive(handOff.To))
            {
                Report($"tick {TickNumber}: ignored the hand-off of {entity} to the stale frame {handOff.To}.", isError: true);
                refusedHandOffs.Add(entity);
                continue;
            }
            FrameHandle own = entities.OwnedFrame(entity);
            if (own.IsNull || Frames.Parent(own) == handOff.To)
                continue;
            try
            {
                Frames.Reparent(own, handOff.To);
            }
            catch (InvalidOperationException ex)
            {
                Report($"tick {TickNumber}: refused the hand-off of {entity} from {handOff.From} to {handOff.To}: {ex.Message}", isError: true);
                refusedHandOffs.Add(entity);
            }
        }
    }

    /// <summary>Sends a Collision to each live entity of a contact, with the point and normal in that entity's frame.</summary>
    private void RouteContact(in ContactStarted contact)
    {
        if (entities.IsAlive(contact.A))
            AddCollision(contact.A, contact.B, contact.Frame, contact.Point, contact.Normal, contact.Impulse);
        if (entities.IsAlive(contact.B))
            AddCollision(contact.B, contact.A, contact.Frame, contact.Point, -contact.Normal, contact.Impulse);
    }

    private void AddCollision(Entity recipient, Entity other, FrameHandle contactFrame, Double3 point, Vector3 normal, float impulse)
    {
        FrameHandle frame = entities.BackRow(recipient).Frame;
        if (frame.IsNull)
            frame = entities.OwnedFrame(recipient);
        if (!contactFrame.IsNull && !frame.IsNull && frame != contactFrame && Frames.IsAlive(contactFrame))
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
