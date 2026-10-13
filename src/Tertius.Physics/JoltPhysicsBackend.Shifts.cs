using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    /// <summary>
    /// Applies the <see cref="PhysicsChanges.FramesShifted"/> of a batch: each body in the system of a shifted numeric
    /// frame loses the frame's offset from its position and the frame's velocity change from its velocity, so that its
    /// place and velocity in the frame's parent stay the same. Jolt has no call that shifts a whole system, so this sets
    /// each body, one at a time. A body that loses no velocity keeps sleeping, and a static body loses only the offset.
    /// Nothing turns, because numeric frames do not rotate. The ghosts in the system move the same way, without waking
    /// what is near them. Each velocity loses the frame's velocity change in doubles and only then narrows to a float, so
    /// that the system and the rows agree at orbital speed.
    /// </summary>
    private void ApplyShifts(List<FrameShift> shifts)
    {
        foreach (FrameShift shift in shifts)
        {
            if (!frames.IsAlive(shift.Frame) || !frames.IsNumeric(shift.Frame))
            {
                ReportBug($"Physics: cannot shift {shift.Frame}, which is not a live numeric frame. Only a bug sends such a shift, so it was skipped.");
                continue;
            }
            if (ExistingWorld(shift.Frame) is not { } world)
                continue;
            Double3 velocity = shift.Velocity;
            BodyInterface bodies = world.Bodies;
            foreach (BodyRecord record in world.Members)
            {
                if (record.Character is { } character)
                {
                    character.RPosition = (character.RPosition.ToDouble3() - shift.Offset).ToJolt();
                    character.LinearVelocity = ((Double3)character.LinearVelocity - velocity).ToVector3();
                    continue;
                }
                BodySnapshot s = ReadPrimary(record);
                bodies.SetRPositionAndRotation(record.Body, (s.Pose.Position - shift.Offset).ToJolt(), s.Pose.Rotation.ToJolt(), Activation.DontActivate);
                // A nonzero velocity wakes a sleeping body, as it must: the body now moves in the system.
                if (record.IsMovable && velocity != Double3.Zero)
                    bodies.SetLinearVelocity(record.Body, ((Double3)s.Linear - velocity).ToVector3());
            }
            foreach (BodyRecord? record in records)
            {
                if (record?.Ghosts is not { Count: > 0 } ghosts)
                    continue;
                foreach (GhostBody ghost in ghosts)
                    if (ghost.World == world)
                        ShiftGhost(bodies, ghost.Body, shift);
            }
        }
    }

    /// <summary>
    /// Moves a ghost by the shift of its system's frame, like a member: its place and velocity in the frame's parent stay
    /// the same. Jolt does not simulate the move, and the ghost moves with the bodies around it, so nothing wakes.
    /// </summary>
    private static void ShiftGhost(BodyInterface bodies, BodyID ghost, in FrameShift shift)
    {
        bodies.GetRPosition(ghost, out RVector3 position);
        Quaternion rotation = bodies.GetRotation(ghost);
        bodies.SetRPositionAndRotation(ghost, (position.ToDouble3() - shift.Offset).ToJolt(), rotation, Activation.DontActivate);
        if (shift.Velocity != Double3.Zero && bodies.GetMotionType(ghost) != MotionType.Static)
            bodies.SetLinearVelocity(ghost, ((Double3)bodies.GetLinearVelocity(ghost) - shift.Velocity).ToVector3());
    }
}
