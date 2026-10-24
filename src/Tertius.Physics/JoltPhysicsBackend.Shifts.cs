using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    /// <summary>
    /// Applies the <see cref="PhysicsChanges.FramesShifted"/> of a batch: each body in the system of a shifted numeric
    /// frame loses the frame's offset from its position and the frame's velocity change from its velocity, so that its
    /// place and velocity in the frame's parent stay the same. Jolt has no call that shifts a whole system, so this sets
    /// each body, one at a time. A body that loses no velocity keeps sleeping, and a static body loses only the offset. The
    /// velocity shadow of a body moves the same way.
    /// Nothing turns, because numeric frames do not rotate. The ghosts in the system move the same way, without waking
    /// what is near them (see <see cref="FrameWorld.ShiftGhosts"/>). The system keeps its own ghosts, so a shift visits
    /// only the members and the ghosts of the shifted system. Each velocity loses the frame's velocity change in doubles
    /// and only then narrows to a float, so that the system and the rows agree at orbital speed.
    /// </summary>
    private void ApplyShifts(List<FrameShift> shifts)
    {
        foreach (FrameShift shift in shifts)
        {
            if (!frames.IsAlive(shift.Frame) || !frames.IsNumeric(shift.Frame))
            {
                ReportBug(
                    $"Physics: cannot shift {shift.Frame}, which is not a live numeric frame. Only a bug sends such a shift, so it was skipped."
                );
                continue;
            }

            if (ExistingWorld(shift.Frame) is not { } world)
                continue;
            Double3 velocity = shift.Velocity;
            BodyInterface bodies = world.Bodies;
            foreach (BodyRecord record in world.Members)
            {
                // The velocity shadow moves with the body (see VelocityShadow.Moved).
                record.Shadow = record.Shadow.Moved(-shift.Offset, -velocity);
                if (record.Character is { } character)
                {
                    character.RPosition = (character.RPosition.ToDouble3() - shift.Offset).ToJolt();
                    character.LinearVelocity = (character.LinearVelocity - velocity).ToVector3();
                    continue;
                }

                BodySnapshot s = ReadPrimary(record);
                bodies.SetRPositionAndRotation(
                    bodyID: record.Body,
                    position: (s.Pose.Position - shift.Offset).ToJolt(),
                    rotation: s.Pose.Rotation.ToJolt(),
                    activationMode: Activation.DontActivate
                );
                // A nonzero velocity wakes a sleeping body, as it must: the body now moves in the system.
                if (record.IsMovable && velocity != Double3.Zero)
                    bodies.SetLinearVelocity(record.Body, (s.Linear - velocity).ToVector3());
            }

            world.ShiftGhosts(shift.Offset, velocity);
        }
    }
}
