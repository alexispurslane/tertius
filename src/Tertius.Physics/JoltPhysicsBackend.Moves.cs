using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    /// <summary>A body of a move batch, with its state read before any body of the batch left its system.</summary>
    private readonly record struct PendingMove(BodyRecord Record, FrameHandle To, DPose Pose, Vector3 Linear, Vector3 Angular, bool Awake);

    // Scratch for a batch of moves.
    private readonly List<PendingMove> pendingMoves = new();

    /// <summary>
    /// Applies the <see cref="PhysicsChanges.BodiesMoved"/> of a batch: each body leaves the system of its physics home for
    /// the system of its new home, a numeric frame below the same semantic frame or that semantic frame. Its pose and
    /// velocity move by the difference of the two homes' offsets and frame velocities, in doubles; numeric frames do not
    /// rotate, so nothing turns. A sleeping rigid body stays asleep. A character is created again, as in a hand-off, and
    /// looks up its contacts. A frame's hull keeps its body state too, and the simulation has already put the frame under
    /// the new home. Every contact of a moved body with another moved body that goes to the same system goes on there
    /// without a new report, and every other contact of a moved body ends (see <see cref="ResolveLooseContacts"/>). The
    /// bodies left behind around a moved body wake, and so do the moved bodies that Jolt simulated. A moved body's ghosts
    /// go: the ghost pass of the next step makes the ones that its new place wants.
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
                frames.SemanticOf(move.To) != frames.SemanticOf(record.Frame) || (record.IsHull && frames.TreeParent(record.OwnedFrame) != move.To))
            {
                ReportBug($"Physics: cannot move the body of {move.Entity} into {move.To}. Only a bug sends such a move, so it was skipped.");
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
            bool awake = record.Character is not null || record.World!.Bodies.IsActive(record.Body) || velocity != Double3.Zero;
            pendingMoves.Add(new PendingMove(record, move.To, new DPose(s.Pose.Position + offset, s.Pose.Rotation),
                ((Double3)s.Linear + velocity).ToVector3(), s.Angular, awake));
        }

        for (int i = 0; i < pendingMoves.Count; i++)
        {
            PendingMove move = pendingMoves[i];
            BodyRecord record = move.Record;
            RemoveGhosts(record);
            RemovePrimary(record);
            CreatePrimary(record, move.To, move.Pose, move.Linear, move.Angular, upFromGravity: record.Standing, awake: move.Awake);
            if (record.Character is { } character)
                character.RefreshContacts(PrimaryLayer(record), record.World!.System);
        }

        pendingMoves.Clear();
    }

    /// <summary>The ID of the body that stands for a record in contact pairs: its rigid body, or its character's inner body.</summary>
    private static BodyID PrimaryId(BodyRecord record) => record.Character is { } character ? character.InnerBodyID : record.Body;
}
