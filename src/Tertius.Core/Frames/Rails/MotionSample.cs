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
    /// This sample in another frame, which the two relations call the parent: <paramref name="start"/> and
    /// <paramref name="end"/>, the motion of the sample's frame relative to it where the step of <paramref name="step"/>
    /// seconds starts and where it ends (see <see cref="FrameTree.MotionRelativeInStep"/>). Each part converts by the moment
    /// that it describes (docs/plans/spin-gravity.md, "V7, the relative motion of two frames" and "The two conversion
    /// rules"). The pose, the instant velocity, the acceleration, the angular velocity and the angular acceleration describe
    /// the end of the step, so they convert by <paramref name="end"/>, by the rules of a point and of a body in a frame that
    /// moves, turns and spins up (see <see cref="FrameMotion"/>), which hold the frame's rotation terms. The chord is the
    /// move of the sample's place over the step, so it converts by where the frame stood at both ends of the step (see
    /// <see cref="StepPoses.ChordToParent"/>).
    /// </summary>
    internal MotionSample Converted(in FrameMotion start, in FrameMotion end, double step)
    {
        Double3 place = Pose.Position;
        return new MotionSample(
            end.PoseToParent(Pose),
            new StepPoses(start.Pose, end.Pose).ChordToParent(place, ChordVelocity, step),
            end.VelocityToParent(place, Velocity),
            end.AccelerationToParent(place, Velocity, Acceleration),
            end.AngularToParent(AngularVelocity),
            end.AngularAccelerationToParent(AngularAcceleration, AngularVelocity));
    }
}
