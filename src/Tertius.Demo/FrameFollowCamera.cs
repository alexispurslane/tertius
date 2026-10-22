using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// A camera that follows a person at a fixed offset and with a fixed look in the axes of one frame, the reference, as a
/// camera on a rail beside a slide follows the rider. Its entity is attached to the person, the subject, so it goes with
/// the subject into every frame that the subject enters. The component writes only its own entity's offset from the
/// subject. In every tick it reads the subject's rotation in the reference's axes and turns the offset and the look into
/// the subject's axes, so the subject's own turns do not turn the view.
/// </summary>
/// <remarks>
/// The rotation that it reads is the one at the end of the last finished step, and the attachment applies the offset to
/// the subject's pose at the end of the step that runs beside the tick. So a turn of the subject in that step turns the
/// view with it for one tick. A sliding person whose up follows a steady spin gravity barely turns.
/// </remarks>
/// <param name="reference">The frame in whose axes the offset and the look hold.</param>
/// <param name="offset">The camera's place relative to the subject's center, in the reference's axes, in meters.</param>
/// <param name="forward">The way that the camera looks, in the reference's axes.</param>
/// <param name="up">The way that the top of the picture points, in the reference's axes. It must not lie along
/// <paramref name="forward"/>.</param>
public sealed class FrameFollowCamera(FrameHandle reference, Double3 offset, Double3 forward, Double3 up) : IComponent
{
    private readonly FrameHandle reference = reference;
    private readonly Double3 offset = offset, forward = forward.Normalized(), up = up.Normalized();

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Entity subject = self.AttachedTo;
        if (subject.IsNull || !self.Snapshot.Frames.IsAlive(reference) ||
            !self.Snapshot.TryGetPoseIn(subject, reference, out DPose pose))
            return;
        DQuaternion toSubject = pose.Rotation.Conjugate();
        self.AttachOffset = new(toSubject.Rotate(offset).ToVector3(),
            ChaseCamera.View(toSubject.Rotate(forward), toSubject.Rotate(up)));
    }
}
