namespace Tertius;

/// <summary>
/// The motion of a frame relative to another frame, which the methods call the parent, in the parent's axes: the pose,
/// the velocity of the frame's origin and the angular velocity. It is the one rule by which the motion of a body converts
/// between two frames: physics converts a body that hands off with it, and the sample table converts the samples of a
/// frame whose relative frame changes (see <see cref="MotionSampleTable.ConvertInto"/>). The conversions follow
/// ARCHITECTURE.md, hand-off rule 5: v' = R⁻¹(v − v_frame − ω × r).
/// </summary>
internal readonly record struct FrameMotion(DPose Pose, Double3 Velocity, Double3 AngularVelocity)
{
    public DPose PoseToParent(in DPose local) => DPose.Compose(Pose, local);

    public DPose PoseFromParent(in DPose parentPose) => DPose.Compose(Pose.Inverse(), parentPose);

    /// <summary>The velocity in the parent of a point that moves with <paramref name="velocity"/> in the frame.</summary>
    public Double3 VelocityToParent(Double3 pointInFrame, Double3 velocity) =>
        Pose.TransformDirection(velocity) + Velocity + Double3.Cross(AngularVelocity, Pose.TransformDirection(pointInFrame));

    /// <summary>The velocity in the frame of a point that moves with <paramref name="velocity"/> in the parent.</summary>
    public Double3 VelocityFromParent(Double3 pointInParent, Double3 velocity) =>
        Pose.InverseTransformDirection(velocity - Velocity - Double3.Cross(AngularVelocity, pointInParent - Pose.Position));

    public Double3 AngularToParent(Double3 omega) => Pose.TransformDirection(omega) + AngularVelocity;

    public Double3 AngularFromParent(Double3 omega) => Pose.InverseTransformDirection(omega - AngularVelocity);

    /// <summary>
    /// The angular acceleration in the parent of a body whose angular acceleration in the frame is <paramref name="alpha"/>:
    /// the same direction in the parent's axes. The full rule, the change of <see cref="AngularToParent"/> over time, also
    /// adds the frame's angular acceleration and the cross product of <see cref="AngularVelocity"/> with the body's angular
    /// velocity in the parent's axes. Spin gravity brings those terms here, for every conversion at once
    /// (docs/plans/spin-gravity.md, "Conversions between frames").
    /// </summary>
    public Double3 AngularAccelerationToParent(Double3 alpha) => Pose.TransformDirection(alpha);

    /// <summary>The angular acceleration in the frame of a body whose angular acceleration in the parent is <paramref name="alpha"/>, by the rule of <see cref="AngularAccelerationToParent"/>.</summary>
    public Double3 AngularAccelerationFromParent(Double3 alpha) => Pose.InverseTransformDirection(alpha);
}
