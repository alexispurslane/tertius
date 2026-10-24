using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    // Scratch for a batch of moves.
    private readonly List<PendingMove> pendingMoves = [];

    // Scratch for MoveGhostsThatStickOut: the ghosts in the old home of a moved hull.
    private readonly List<BodyID> stickOutScratch = [];

    /// <summary>
    /// A body of a move batch, with its state read before any body of the batch left its system. <see cref="Offset"/> is
    /// the old home's origin in the new home, and <see cref="HomeVelocity"/> the old home's velocity relative to the new
    /// home, which the body's state gains.
    /// </summary>
    private readonly record struct PendingMove(
        BodyRecord Record,
        FrameHandle To,
        DPose Pose,
        Vector3 Linear,
        Vector3 Angular,
        bool Awake,
        Double3 Offset,
        Double3 HomeVelocity);

    /// <summary>
    /// Applies the <see cref="PhysicsChanges.BodiesMoved"/> of a batch: each body leaves the system of its physics home for
    /// the system of its new home, a numeric frame below the same semantic frame or that semantic frame. Its pose and
    /// velocity move by the difference of the two homes' offsets and frame velocities, in doubles; numeric frames do not
    /// rotate, so nothing turns. Its velocity shadow moves the same way (see <see cref="VelocityShadow.Moved"/>). A sleeping
    /// rigid body stays asleep. A character is created again, as in a hand-off, and
    /// looks up its contacts. A frame's hull keeps its body state too, and the simulation has already put the frame under
    /// the new home. A moved body keeps its ghosts in the systems of other frames: a move stays within one semantic frame,
    /// and every frame where a ghost may be (see <see cref="GhostAllowed"/>) depends only on that semantic frame, so the
    /// ghost there still stands for the body, with the same velocity in its system. The ghost pass of the next step puts it
    /// at the body's new state. Before ticket ter-dhkf every ghost of a moved body went until that ghost pass, so a fast ship
    /// whose hull joined the system of a station's hull, an anchor, while their bubbles overlapped had no ghost in the
    /// station for a step. A ghost in the new home goes, because a system never holds a body and its ghost. A moved hull
    /// also takes along the ghost of each body that sticks out of its frame, from its old home into its new one (see
    /// <see cref="MoveGhostsThatStickOut"/>). The contacts of the moved body wait until the next ghost pass (see
    /// <see cref="ResolveLooseContacts"/>). A contact goes on without a
    /// new report where a body of each entity can still touch the other, such as two touching bodies that move to the same
    /// system. Every other contact of a moved body ends. The bodies left behind around a moved body wake, and so do the
    /// moved bodies that Jolt simulated.
    /// </summary>
    private void ApplyMoves(List<BodyMove> moves)
    {
        if (moves.Count == 0)
            return;
        pendingMoves.Clear();

        // Every state is read before any body leaves, because a removal wakes the bodies around it.
        foreach (BodyMove move in moves)
        {
            if (Find(move.Entity) is not { HasPrimary: true } record || !frames.IsAlive(move.To) ||
                frames.SemanticOf(move.To) != frames.SemanticOf(record.Frame) ||
                (record.IsHull && frames.TreeParent(record.OwnedFrame) != move.To))
            {
                ReportBug(
                    $"Physics: cannot move the body of {move.Entity} into {move.To}. Only a bug sends such a move, so it was skipped.");
                continue;
            }

            if (record.Frame == move.To)
                continue;
            FrameHandle semantic = frames.SemanticOf(move.To);
            // The old home's origin in the new home, through their lowest common ancestor: numeric frames do not rotate, and
            // two nearby homes far from the semantic frame's origin add no large number.
            Double3 offset = frames.Relative(record.Frame, move.To).Position;
            Double3 velocity = frames.HomeVelocity(record.Frame, semantic) - frames.HomeVelocity(move.To, semantic);
            BodySnapshot s = ReadPrimary(record);
            // A sleeping body has no velocity in its old home, so it has the homes' difference in the new one. It sleeps
            // on only when that is zero: else Jolt would carry it along with the new home.
            bool awake = record.Character is not null || record.World.Bodies.IsActive(record.Body) ||
                         velocity != Double3.Zero;
            pendingMoves.Add(new(Record: record, To: move.To, Pose: new(s.Pose.Position + offset, s.Pose.Rotation),
                Linear: (s.Linear + velocity).ToVector3(), Angular: s.Angular, Awake: awake, Offset: offset,
                HomeVelocity: velocity));
        }

        for (int i = 0; i < pendingMoves.Count; i++)
        {
            PendingMove move = pendingMoves[i];
            BodyRecord record = move.Record;
            // The first loop read the body's state, so it has one.
            Contract.Assert(record.HasPrimary);
            // The body keeps its ghosts, but not one in its new home, so that no system holds a body and its ghost. Only an
            // anchor's ghost lies in a home of the body's semantic frame: a kinematic hull whose owner changes its motion
            // can join the home of a group that kept its ghost a sync point before. The update stops naming that home at
            // the same sync point, but the batch drops withdrawn ghosts only after the moves and the reconcile (see
            // DropWithdrawnAnchorGhosts).
            RemoveGhostIn(record, WorldFor(move.To));
            if (record.IsHull)
                MoveGhostsThatStickOut(frame: record.OwnedFrame, from: record.World, to: move.To, offset: move.Offset,
                    velocity: move.HomeVelocity);
            // The body's velocity shadow moves with it, as its state does.
            VelocityShadow shadow = record.Shadow;
            RemovePrimary(record);
            CreatePrimary(record: record, frame: move.To, pose: move.Pose, linearVelocity: move.Linear,
                angularVelocity: move.Angular, upFromGravity: record.Standing,
                awake: move.Awake);
            record.Shadow = shadow.Moved(move.Offset, move.HomeVelocity);
            Contract.Assert(record.HasPrimary);
            if (record.Character is { } character)
                character.RefreshContacts(layer: PrimaryLayer(record), physicsSystem: record.World.System);
        }

        pendingMoves.Clear();
    }

    /// <summary>
    /// Moves the ghost of each body that sticks out of <paramref name="frame"/> from <paramref name="from"/>, the system of
    /// the old home of the frame's hull, into <paramref name="to"/>, the hull's new home. The simulation has put the frame
    /// under that home already, so it is the frame's new tree parent, the one frame of the semantic parent that the tree
    /// allows such a ghost in (see <see cref="GhostAllowed"/>). Each ghost keeps its place and velocity in the semantic
    /// frame: its pose gains <paramref name="offset"/>, the old home's origin in the new home, and its velocity gains
    /// <paramref name="velocity"/>, the old home's velocity relative to the new home, as the hull's own state does. So a
    /// body of the new home meets the ghost in the step after the move, as a moved body keeps its own ghosts. Before, the
    /// reconcile dropped the ghost, and the ghost pass made the new one only at the end of that step (review of ter-4psc).
    /// A body that has a ghost in the new home already, beside a hull that lived apart from its group, keeps that ghost,
    /// which takes the moved one's state. The contacts of a moved ghost wait for the next ghost pass, as those of a moved
    /// body do (see <see cref="ResolveLooseContacts"/>). Visits every ghost in the old home's system.
    /// </summary>
    private void MoveGhostsThatStickOut(FrameHandle frame, FrameWorld from, FrameHandle to, Double3 offset,
        Double3 velocity)
    {
        stickOutScratch.Clear();
        from.CopyGhosts(stickOutScratch);
        foreach (BodyID body in stickOutScratch)
        {
            // A system holds only the ghosts that the records keep in it (see AssertInvariants).
            BodyRecord? record = RecordFor(from.Bodies.GetUserData(body));
            Contract.Assert(record?.Ghosts is not null);
            // Only a body of the frame, or of a numeric frame below it, sticks out of the frame's bubble (see BubbleOf).
            if (record.Frame.IsNull || BubbleOf(record.Frame) != frame)
                continue;
            List<GhostBody> ghosts = record.Ghosts;
            int index = ghosts.Count - 1;
            while (index >= 0 && !(ghosts[index].Body == body && ghosts[index].World == from))
                index--;
            Contract.Assert(index >= 0);
            BodyInterface bodies = from.Bodies;
            bodies.GetRPosition(body, out RVector3 position);
            bodies.GetLinearAndAngularVelocity(body, out Vector3 linear, out Vector3 angular);
            var pose = new DPose(position.ToDouble3() + offset, bodies.GetRotation(body).ToDouble());
            BodyLayer role = ghosts[index].Role;
            RemoveGhostAt(ghosts, index);
            WantGhost(record: record, frame: to, pose: pose, linearVelocity: linear + velocity,
                angularVelocity: angular, role: role);
        }

        stickOutScratch.Clear();
    }

    /// <summary>The ID of the body that stands for a record in contact pairs: its rigid body, or its character's inner body.</summary>
    private static BodyID PrimaryId(BodyRecord record) =>
        record.Character is { } character ? character.InnerBodyID : record.Body;
}
