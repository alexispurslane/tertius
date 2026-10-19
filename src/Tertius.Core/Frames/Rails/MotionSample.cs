namespace Tertius.Frames.Rails;

/// <summary>
/// The motion of a frame on rails over one physics step, in doubles: what <see cref="KeplerOrbit.Sample"/> and
/// <see cref="FrameSpin.Sample"/> give. Every field is relative to the frame's semantic parent, in the parent's axes. A root
/// frame has no parent, so its sample is in the root's own space. A sample holds no proper acceleration: the frame tree
/// derives a frame's proper acceleration from its step record and the gravity at its place (see
/// <see cref="FrameTree.ProperAcceleration"/>).
/// </summary>
/// <param name="Pose">The frame's pose at the end of the step: its place in m and its rotation.</param>
/// <param name="ChordVelocity">The change of the frame's place over the step, divided by the length of the step, in m/s.</param>
/// <param name="Velocity">The frame's instant velocity at the end of the step, in m/s.</param>
/// <param name="Acceleration">The frame's acceleration during the step, in m/s².</param>
/// <param name="AngularVelocity">The frame's angular velocity at the end of the step, in rad/s.</param>
/// <param name="AngularAcceleration">The frame's angular acceleration during the step, in rad/s².</param>
public readonly record struct MotionSample(
    DPose Pose, Double3 ChordVelocity, Double3 Velocity, Double3 Acceleration,
    Double3 AngularVelocity, Double3 AngularAcceleration)
{
    /// <summary>
    /// This sample in another frame, which <paramref name="motion"/> calls the parent: the motion of the sample's frame
    /// relative to it now. The pose, the chord velocity and the instant velocity at the sample's place at the end of its
    /// step, the angular velocity and the angular acceleration convert with that motion, by the rule of a hand-off (see
    /// <see cref="FrameMotion"/>). The acceleration turns into the parent's axes and adds
    /// <paramref name="relativeAcceleration"/>, the acceleration of the sample's frame relative to the parent, in the
    /// parent's axes (see <see cref="FrameTree.AccelerationRelative"/>).
    /// </summary>
    internal MotionSample Converted(in FrameMotion motion, Double3 relativeAcceleration)
    {
        Double3 place = Pose.Position;
        return new MotionSample(
            motion.PoseToParent(Pose),
            motion.VelocityToParent(place, ChordVelocity),
            motion.VelocityToParent(place, Velocity),
            motion.Pose.TransformDirection(Acceleration) + relativeAcceleration,
            motion.AngularToParent(AngularVelocity),
            motion.AngularAccelerationToParent(AngularAcceleration));
    }
}
