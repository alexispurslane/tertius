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
    /// Nothing turns, because numeric frames do not rotate. The ghosts in the system go, because their places came from
    /// the old origin: the ghost pass of the next step makes the ones that are still wanted.
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
            Vector3 velocity = shift.Velocity.ToVector3();
            BodyInterface bodies = world.Bodies;
            foreach (BodyRecord record in world.Members)
            {
                if (record.Character is { } character)
                {
                    character.RPosition = (character.RPosition.ToDouble3() - shift.Offset).ToJolt();
                    character.LinearVelocity -= velocity;
                    continue;
                }
                BodySnapshot s = ReadPrimary(record);
                bodies.SetRPositionAndRotation(record.Body, (s.Pose.Position - shift.Offset).ToJolt(), s.Pose.Rotation.ToJolt(), Activation.DontActivate);
                // A nonzero velocity wakes a sleeping body, as it must: the body now moves in the system.
                if (record.IsMovable && velocity != Vector3.Zero)
                    bodies.SetLinearVelocity(record.Body, s.Linear - velocity);
            }
            foreach (BodyRecord? record in records)
                if (record is not null)
                    RemoveGhostIn(record, world);
        }
    }
}
