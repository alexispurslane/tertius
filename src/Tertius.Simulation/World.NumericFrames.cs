using System.Runtime.InteropServices;

namespace Tertius.Simulation;

/// <summary>
/// The engine's part in numeric frames (docs/plans/numeric-frames.md): frames that the engine makes around a group of
/// bodies, only for precision. Gameplay never sees them. The tree decides and changes the frames at each sync point
/// (<see cref="FrameTree.UpdateNumericFrames"/>), and nothing else changes them; the World gathers the bodies for it and
/// applies the moves to the rows.
/// </summary>
public sealed partial class World
{
    // The bodies of the current numeric update, in entity slot order: a frame owner's interior, then its hull.
    private readonly List<NumericBody> numericBodies = new();
    private Action<List<NumericMove>>? applyNumericMoves;

    // The bodies of the current batch that physics has not created yet: the index of each one's creation in the batch.
    private readonly Dictionary<Entity, int> pendingCreates = new();

    // True after a change outside a tick, until the next tick runs the numeric update for it.
    private bool numericUpdateDue;

    /// <summary>
    /// The frame whose physics system holds the body of <paramref name="entity"/>: its frame, or a numeric frame below it.
    /// Null for an entity that is not live.
    /// </summary>
    internal FrameHandle PhysicsHomeOf(Entity entity) => entities.IsAlive(entity) ? entities.BackRow(entity).PhysicsHome : FrameHandle.Null;

    /// <summary>
    /// Runs the rules of numeric frames once, over a sync point of <paramref name="dt"/> seconds, after the physics output,
    /// the destroys, the spawns and the frames that physics does not drive have reached the end of the tick, on one thread. The structural changes that wait in the batch for physics reach it with
    /// the update's first batch, so that a body that physics has not created yet is created in its numeric frame, with its
    /// state converted in doubles. The update's changes reach physics in two batches. The last one goes also when it is
    /// empty: physics drops the ghosts in each home that this update stops naming to keep a ghost of an anchor (see
    /// <see cref="FrameTree.AnchorGhosts"/>) at this sync point, so that it keeps a ghost only where the tree allows one.
    /// That costs physics one empty batch per sync point, and the World needs no rule of its own for when physics needs one.
    /// </summary>
    private void UpdateNumericFrames(double dt)
    {
        // The update's shifts are the only ones in the batch, so that ApplyNumericMoves finds them there.
        Contract.Requires(physicsChanges.FramesShifted.Count == 0);
        pendingCreates.Clear();
        for (int i = 0; i < physicsChanges.BodiesCreated.Count; i++)
            pendingCreates[physicsChanges.BodiesCreated[i].Entity] = i;
        numericBodies.Clear();
        foreach (Entity entity in entities.BodyEntities)
        {
            if (!attachments.ParentOf(entity).IsNull)
                continue;
            BodyDesc desc = entities.Body(entity)!;
            FrameHandle own = entities.OwnedFrame(entity);
            // The interior of a frame owner is a body at rest at the frame's origin, in the frame's own system, which no
            // numeric update moves. Physics keeps it static while the hull is static and kinematic while the hull can move,
            // and the update counts it as a static anchor in either motion, which fixes each group that it links in that
            // system (docs/plans/numeric-frames.md, "Anchors"). A root frame has an interior and no hull.
            if (!own.IsNull)
                numericBodies.Add(new NumericBody
                {
                    Entity = entity,
                    Semantic = own,
                    Home = own,
                    Radius = entities.BodyInteriorBoundingRadius(entity),
                    Mass = desc.Mass > 0 ? desc.Mass : 1,
                    Static = true,
                    Fixed = true,
                });
            ref readonly TransformRow row = ref entities.BackRow(entity);
            if (row.Frame.IsNull)
                continue;
            // The body of a frame owner is its frame's hull here, because its frame has a parent. The update plans the next
            // step, so it reads the hull's motion in that step, which counts a change written in the tick that ends.
            BodyMotion motion = own.IsNull ? desc.Motion : NextStepHullMotion(own)!.Value;
            double mass = desc.Mass > 0 ? desc.Mass : 1;
            numericBodies.Add(new NumericBody
            {
                Entity = entity,
                Semantic = row.Frame,
                Home = row.PhysicsHome,
                Position = row.Pose.Position,
                HomePosition = row.HomePose.Position,
                Velocity = SemanticVelocity(entity, row, own),
                Radius = entities.BodyBoundingRadius(entity),
                Mass = mass,
                Character = motion == BodyMotion.Character,
                ThrustAcceleration = ThrustAcceleration(entity, motion, mass),
                Static = motion == BodyMotion.Static,
                Kinematic = motion == BodyMotion.Kinematic,
                // A hull that is not dynamic moves only as its frame's step records say, and a kinematic one follows the
                // motion intents of its owner, so it lives in an orbit frame of its own when they move it fast or far.
                Fixed = !own.IsNull && motion != BodyMotion.Dynamic,
                Orbits = !own.IsNull && motion == BodyMotion.Kinematic,
                Sensor = desc.IsSensor,
                OwnedFrame = own,
            });
        }
        Profile.Lap(23);
        Frames.UpdateNumericFrames(CollectionsMarshal.AsSpan(numericBodies), dt, DeltaTime, physicsChanges, applyNumericMoves ??= ApplyNumericMoves);
        Profile.Lap(24);
        ApplyPhysicsChanges();
        pendingCreates.Clear();
    }

    /// <summary>
    /// The acceleration in m/s² that the force and impulse intents of <paramref name="entity"/> ask of its body, whose
    /// motion is <paramref name="motion"/> and mass <paramref name="mass"/> kg, in the step after this sync point:
    /// (|force| + |impulse| / step) / mass, from the intent that the step applies. Physics gives a force or an impulse only
    /// to a dynamic body or a character, so any other body gets zero, and so does the hull of a frame that physics does not
    /// drive, whose owner's intent acts on that hull. Gravity is no intent, and a torque does not move the body, so neither
    /// counts. The thrust clock of the numeric update reads it (see <see cref="NumericFrameRules.ThrustClock"/>).
    /// </summary>
    private double ThrustAcceleration(Entity entity, BodyMotion motion, double mass)
    {
        if (motion is not (BodyMotion.Dynamic or BodyMotion.Character))
            return 0;
        ref readonly PhysicsIntent intent = ref entities.NextStepIntent(entity);
        return (intent.Force.Length() + intent.Impulse.Length() / (double)DeltaTime) / mass;
    }

    /// <summary>
    /// The velocity of the body of <paramref name="entity"/> relative to its semantic frame, in doubles: the one that its
    /// creation carries when physics has not created it yet, the one of the frame that it owns, or else the row's, which is
    /// relative to its physics home. A row narrows its velocity to a float, which at orbital speed steps by 0.5 mm/s.
    /// </summary>
    private Double3 SemanticVelocity(Entity entity, in TransformRow row, FrameHandle own)
    {
        if (!own.IsNull)
            return Frames.HomeVelocity(row.PhysicsHome, row.Frame) + Frames.TreeLinearVelocity(own);
        // Most sync points create no body, and a lookup in an empty dictionary still hashes the entity: this runs for every body.
        if (pendingCreates.Count > 0 && pendingCreates.TryGetValue(entity, out int created))
            return physicsChanges.BodiesCreated[created].LinearVelocity;
        return Frames.HomeVelocity(row.PhysicsHome, row.Frame) + row.LinearVelocity;
    }

    /// <summary>
    /// Applies the frame shifts and the moves of a numeric update to the rows, then hands them to physics, while the frames
    /// that the moves leave are still in the tree. A member of a shifted frame keeps its place and velocity in the frame's
    /// parent, so its row loses the shift; a moved body's row converts into its new home.
    /// </summary>
    private void ApplyNumericMoves(List<NumericMove> moves)
    {
        foreach (FrameShift shift in physicsChanges.FramesShifted)
            foreach (NumericBody body in numericBodies)
                if (body.Home == shift.Frame)
                    ShiftRow(body.Entity, shift);
        foreach (NumericMove move in moves)
            MoveBodyHome(move.Entity, move.To, Frames.SemanticOf(move.To));
        FlushPhysicsChanges();
    }

    /// <summary>The row of an entity whose physics home moved by <paramref name="shift"/>.</summary>
    private void ShiftRow(Entity entity, in FrameShift shift)
    {
        FrameHandle own = entities.OwnedFrame(entity);
        if (!own.IsNull)
        {
            entities.SetBackTransform(entity, FrameOwnerRow(own));
            return;
        }
        // The row's pose is in the semantic frame, which the shift does not move. In the frame, the body moves back.
        TransformRow row = entities.BackRow(entity);
        row.HomePosition -= shift.Offset;
        if (entities.Body(entity) is not { Motion: BodyMotion.Static })
            row.LinearVelocity = ((Double3)row.LinearVelocity - shift.Velocity).ToVector3();
        entities.SetBackTransform(entity, row);
    }

    /// <summary>
    /// Moves the body of <paramref name="entity"/>, which a numeric update moved, into <paramref name="home"/>: the row and
    /// the change for physics. The update has moved the frame that a frame owner owns under the home already. A row keeps
    /// its frame and its pose in that frame. Its linear velocity, which is relative to its physics home, gains the frame
    /// velocity of the old home and loses that of the new one, in doubles. A body that physics has not created yet is
    /// created in the home instead, and physics converts its state into the home in doubles.
    /// </summary>
    private void MoveBodyHome(Entity entity, FrameHandle home, FrameHandle semantic)
    {
        FrameHandle own = entities.OwnedFrame(entity);
        bool pending = pendingCreates.TryGetValue(entity, out int created);
        if (!own.IsNull)
        {
            entities.SetBackTransform(entity, FrameOwnerRow(own));
        }
        else
        {
            TransformRow row = entities.BackRow(entity);
            Double3 velocity = SemanticVelocity(entity, row, own);
            // Through the homes' lowest common ancestor, so that no large number enters between two nearby homes.
            row.HomePosition = home == semantic ? row.Pose.Position : row.HomePose.Position + Frames.Relative(row.PhysicsHome, home).Position;
            row.PhysicsHome = home;
            row.LinearVelocity = (velocity - Frames.HomeVelocity(home, semantic)).ToVector3();
            entities.SetBackTransform(entity, row);
        }
        if (pending)
            physicsChanges.BodiesCreated[created] = physicsChanges.BodiesCreated[created] with { Frame = home };
        else
            physicsChanges.BodiesMoved.Add(new BodyMove(entity, home));
    }
}
