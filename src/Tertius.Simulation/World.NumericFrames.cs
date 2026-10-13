using System.Runtime.InteropServices;

namespace Tertius.Simulation;

/// <summary>
/// The engine's operations on numeric frames (docs/plans/numeric-frames.md): frames that the engine makes around a group
/// of bodies, only for precision. Gameplay never sees them. The tree decides and changes the frames at each sync point
/// (<see cref="FrameTree.UpdateNumericFrames"/>); the World gathers the bodies for it and applies the moves to the rows.
/// The internal operations below do the same work by hand, outside a tick, for tests.
/// </summary>
public sealed partial class World
{
    // The bodies of the current numeric update, in entity slot order.
    private readonly List<NumericBody> numericBodies = new();
    private Action<List<NumericMove>>? applyNumericMoves;

    // True after a change outside a tick, until the next tick runs the numeric update for it.
    private bool numericUpdateDue;

    /// <summary>
    /// The frame whose physics system holds the body of <paramref name="entity"/>: its frame, or a numeric frame below it.
    /// Null for an entity that is not live.
    /// </summary>
    internal FrameHandle PhysicsHomeOf(Entity entity) => entities.IsAlive(entity) ? entities.BackRow(entity).PhysicsHome : FrameHandle.Null;

    /// <summary>
    /// Runs the rules of numeric frames once, over a sync point of <paramref name="dt"/> seconds, after the physics output,
    /// the destroys and the spawns have reached physics, on one thread. Its changes reach physics in two batches.
    /// </summary>
    private void UpdateNumericFrames(double dt)
    {
        // The update's shifts are the only ones in the batch, so that ApplyNumericMoves finds them there.
        Contract.Requires(physicsChanges.FramesShifted.Count == 0);
        numericBodies.Clear();
        foreach (Entity entity in entities.LiveEntities)
        {
            if (entities.Body(entity) is not { } desc || !attachments.ParentOf(entity).IsNull)
                continue;
            ref readonly TransformRow row = ref entities.BackRow(entity);
            if (row.Frame.IsNull)
                continue;
            FrameHandle own = entities.OwnedFrame(entity);
            numericBodies.Add(new NumericBody
            {
                Entity = entity,
                Semantic = row.Frame,
                Home = row.PhysicsHome,
                Position = row.Pose.Position,
                Velocity = Frames.HomeVelocity(row.PhysicsHome, row.Frame) + row.LinearVelocity,
                Radius = physics!.BoundingRadius(desc.Shape),
                Mass = desc.Mass > 0 ? desc.Mass : 1,
                Static = desc.Motion == BodyMotion.Static,
                Fixed = !own.IsNull && Frames.Driver(own) != MotionDriver.Physics,
                OwnedFrame = own,
            });
        }
        Frames.UpdateNumericFrames(CollectionsMarshal.AsSpan(numericBodies), dt, physicsChanges, applyNumericMoves ??= ApplyNumericMoves);
        FlushPhysicsChanges();
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
    /// Creates a numeric frame under the semantic frame <paramref name="parent"/>, at <paramref name="position"/> in it and
    /// moving at <paramref name="velocity"/> relative to it (zero for a resting frame). It has no bodies until
    /// <see cref="MoveBodies"/> moves some in. Outside a tick only. Throws <see cref="ArgumentException"/> for a parent that
    /// is not a live semantic frame, a state that is not finite, or a resting frame with a velocity.
    /// </summary>
    internal FrameHandle CreateNumericFrame(FrameHandle parent, Double3 position, Double3 velocity, NumericMode mode)
    {
        ThrowIfInTick(nameof(CreateNumericFrame));
        if (!Frames.IsAlive(parent) || Frames.IsNumeric(parent))
            throw new ArgumentException($"A numeric frame needs a live semantic parent, not {parent}.", nameof(parent));
        if (!IsFinite(position) || !IsFinite(velocity))
            throw new ArgumentException("The state of a numeric frame must be finite.");
        if (mode is not (NumericMode.Falling or NumericMode.Resting))
            throw new ArgumentException($"A numeric frame falls or rests, not {mode}.", nameof(mode));
        if (mode == NumericMode.Resting && velocity != Double3.Zero)
            throw new ArgumentException("A resting frame does not move in its parent.", nameof(velocity));

        FrameHandle frame = Frames.CreateNumeric(parent, position, velocity, mode);
        physicsChanges.FramesCreated.Add(frame);
        FlushPhysicsChanges();
        SwapSnapshot();
        return frame;
    }

    /// <summary>
    /// Moves the bodies of <paramref name="group"/>, a group that touch or are joined, from their physics homes into
    /// <paramref name="home"/>: a numeric frame, or the semantic frame of their numeric homes. Every body keeps its place
    /// and velocity in its semantic frame, converted in doubles, and a sleeping body stays asleep. Physics carries the
    /// contacts between the bodies of the group, so a contact that goes on is not reported again. The semantic frame stays
    /// the same, so no entity hears <see cref="FrameChanged"/> or changes topic. The owner of a frame moves its frame under
    /// <paramref name="home"/> in the tree, because a hull lives in the system of its frame's tree parent, and the frame's
    /// contents move with it. Outside a tick only. Throws <see cref="ArgumentException"/>, before anything changes, for a
    /// home that is not alive, and for an entity that is not live, has no body, is attached, is in another semantic frame,
    /// or owns a frame that physics does not drive.
    /// </summary>
    internal void MoveBodies(IReadOnlyList<Entity> group, FrameHandle home)
    {
        ThrowIfInTick(nameof(MoveBodies));
        ArgumentNullException.ThrowIfNull(group);
        if (!Frames.IsAlive(home))
            throw new ArgumentException($"Cannot move bodies into {home}: the frame is gone.", nameof(home));
        FrameHandle semantic = Frames.SemanticOf(home);
        foreach (Entity entity in group)
        {
            if (MoveProblem(entity, home, semantic) is { } problem)
                throw new ArgumentException($"Cannot move {entity} into {home}: {problem}.", nameof(group));
        }
        foreach (Entity entity in group)
            MoveBodyHome(entity, home, semantic);
        FlushPhysicsChanges();
        FinishOutsideTickChange();
    }

    /// <summary>
    /// Destroys a numeric frame that holds no body and no frame. Outside a tick only. Throws <see cref="ArgumentException"/>
    /// for a frame that is not a live numeric frame, or that is not empty.
    /// </summary>
    internal void DestroyNumericFrame(FrameHandle frame)
    {
        ThrowIfInTick(nameof(DestroyNumericFrame));
        if (!Frames.IsAlive(frame) || !Frames.IsNumeric(frame))
            throw new ArgumentException($"{frame} is not a live numeric frame.", nameof(frame));
        var children = new List<FrameHandle>();
        Frames.GetTreeChildren(frame, children);
        if (children.Count > 0)
            throw new ArgumentException($"{frame} still holds the frame {children[0]}.", nameof(frame));
        foreach (Entity entity in entities.LiveEntities)
        {
            if (entities.BackRow(entity).PhysicsHome == frame)
                throw new ArgumentException($"{frame} still holds {entity}.", nameof(frame));
        }
        Frames.DestroyNumeric(frame);
        physicsChanges.FramesDestroyed.Add(frame);
        FlushPhysicsChanges();
        SwapSnapshot();
    }

    /// <summary>Why <paramref name="entity"/> cannot move into <paramref name="home"/>, or null when it can.</summary>
    private string? MoveProblem(Entity entity, FrameHandle home, FrameHandle semantic)
    {
        if (!entities.IsAlive(entity))
            return "it is not live";
        if (entities.Body(entity) is null)
            return "it has no body";
        if (!attachments.ParentOf(entity).IsNull)
            return "it is attached, so it follows its parent";
        if (entities.BackRow(entity).Frame != semantic)
            return $"it is in {entities.BackRow(entity).Frame}, not in {semantic}";
        FrameHandle own = entities.OwnedFrame(entity);
        if (own.IsNull)
            return null;
        if (Frames.Driver(own) != MotionDriver.Physics)
            return $"physics does not drive {own}, the frame that it owns";
        return home == own || Frames.IsAncestorOf(own, home) ? $"{home} lies in {own}, the frame that it owns" : null;
    }

    /// <summary>
    /// Moves the body of <paramref name="entity"/>, which <see cref="MoveBodies"/> or a numeric update checked, into
    /// <paramref name="home"/>: the row and the change for physics. The owner of a frame moves its frame under the home,
    /// unless the update did that already. A row keeps its frame and its pose in that frame. Its linear velocity, which is
    /// relative to its physics home, gains the frame velocity of the old home and loses that of the new one, in doubles.
    /// </summary>
    private void MoveBodyHome(Entity entity, FrameHandle home, FrameHandle semantic)
    {
        FrameHandle own = entities.OwnedFrame(entity);
        if (!own.IsNull)
        {
            Frames.Reparent(own, home);
            entities.SetBackTransform(entity, FrameOwnerRow(own));
        }
        else
        {
            TransformRow row = entities.BackRow(entity);
            Double3 velocity = Frames.HomeVelocity(row.PhysicsHome, semantic) + row.LinearVelocity;
            // Through the homes' lowest common ancestor, so that no large number enters between two nearby homes.
            row.HomePosition = home == semantic ? row.Pose.Position : row.HomePose.Position + Frames.Relative(row.PhysicsHome, home).Position;
            row.PhysicsHome = home;
            row.LinearVelocity = (velocity - Frames.HomeVelocity(home, semantic)).ToVector3();
            entities.SetBackTransform(entity, row);
        }
        physicsChanges.BodiesMoved.Add(new BodyMove(entity, home));
    }
}
