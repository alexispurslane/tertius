namespace Tertius;

/// <summary>
/// The motion of a frame relative to another frame, which the methods call the parent, in the parent's axes: the pose,
/// the velocity of the frame's origin, the angular velocity, and the two accelerations. It is the one rule by which the
/// motion of a body converts between two frames: physics converts a body that hands off with it, and a motion intent
/// converts a full sample with it (see <see cref="Frames.Rails.MotionSample.Converted"/>) when its entity changes frame
/// (see <c>World.ConvertIntent</c>). The conversions follow ARCHITECTURE.md, hand-off rule 5: v' = R⁻¹(v − v_frame − ω × r).
/// The rules of the accelerations, with the frame's rotation terms, are those of docs/plans/spin-gravity.md, "V7, the
/// relative motion of two frames". Every producer states both accelerations: the motion of a frame at the start of a step
/// states an explicit zero for each, because a value left out would drop a record's accelerations without a word.
/// </summary>
/// <param name="Pose">The frame's pose in the parent: R, its rotation into the parent's axes, and the place of its origin.</param>
/// <param name="Velocity">The velocity of the frame's origin in the parent, in m/s.</param>
/// <param name="AngularVelocity">ω, the frame's angular velocity relative to the parent, in the parent's axes, in rad/s.</param>
/// <param name="Acceleration">A, the acceleration of the frame's origin in the parent, in the parent's axes, in m/s².</param>
/// <param name="AngularAcceleration">α, the frame's angular acceleration relative to the parent, in the parent's axes, in rad/s².</param>
internal readonly record struct FrameMotion(DPose Pose, Double3 Velocity, Double3 AngularVelocity,
    Double3 Acceleration, Double3 AngularAcceleration)
{
    public DPose PoseToParent(in DPose local) => DPose.Compose(Pose, local);

    /// <summary>
    /// The motion in the parent of an inner frame whose motion in this frame is <paramref name="local"/>: its pose composed
    /// with this pose, its origin's velocity and acceleration by the rules of a point (<see cref="VelocityToParent"/> and
    /// <see cref="AccelerationToParent"/>), and its angular velocity and angular acceleration by the rules of a body
    /// (<see cref="AngularToParent"/> and <see cref="AngularAccelerationToParent"/>). A path of frames
    /// composes level by level with it, from the inner end, as the pose tree composes poses
    /// (see <see cref="FrameTree.MotionRelativeInStep"/>).
    /// </summary>
    public FrameMotion MotionToParent(in FrameMotion local) => new(
        PoseToParent(local.Pose),
        VelocityToParent(local.Pose.Position, local.Velocity),
        AngularToParent(local.AngularVelocity),
        AccelerationToParent(local.Pose.Position, local.Velocity, local.Acceleration),
        AngularAccelerationToParent(local.AngularAcceleration, local.AngularVelocity));

    /// <summary>
    /// The motion of the parent in this frame, in the frame's axes: the inverse pose, and the motion of the parent's origin
    /// and axes, which stand still in the parent, by the rules from the parent (<see cref="VelocityFromParent"/>,
    /// <see cref="AccelerationFromParent"/>, <see cref="AngularFromParent"/> and
    /// <see cref="AngularAccelerationFromParent"/>). So <c>m.Inverse().MotionToParent(m)</c> is the frame
    /// at rest in itself.
    /// </summary>
    public FrameMotion Inverse() => new(
        Pose.Inverse(),
        VelocityFromParent(Double3.Zero, Double3.Zero),
        AngularFromParent(Double3.Zero),
        AccelerationFromParent(Double3.Zero, Double3.Zero, Double3.Zero),
        AngularAccelerationFromParent(Double3.Zero, Double3.Zero));

    public DPose PoseFromParent(in DPose parentPose) => DPose.Compose(Pose.Inverse(), parentPose);

    /// <summary>The velocity in the parent of a point that moves with <paramref name="velocity"/> in the frame.</summary>
    public Double3 VelocityToParent(Double3 pointInFrame, Double3 velocity) =>
        Pose.TransformDirection(velocity) + Velocity + Double3.Cross(AngularVelocity, Pose.TransformDirection(pointInFrame));

    /// <summary>The velocity in the frame of a point that moves with <paramref name="velocity"/> in the parent.</summary>
    public Double3 VelocityFromParent(Double3 pointInParent, Double3 velocity) =>
        Pose.InverseTransformDirection(velocity - Velocity - Double3.Cross(AngularVelocity, pointInParent - Pose.Position));

    /// <summary>
    /// The acceleration in the parent of a point at <paramref name="pointInFrame"/> that moves with
    /// <paramref name="velocity"/> and accelerates at <paramref name="acceleration"/> in the frame:
    /// a' = R a + A + α × r + ω × (ω × r) + 2 ω × (R v), with r = R x the point's place relative to the frame's origin in the
    /// parent's axes.
    /// </summary>
    public Double3 AccelerationToParent(Double3 pointInFrame, Double3 velocity, Double3 acceleration)
    {
        Double3 r = Pose.TransformDirection(pointInFrame);
        return Pose.TransformDirection(acceleration) + Acceleration + Double3.Cross(AngularAcceleration, r) +
               Double3.Cross(AngularVelocity, Double3.Cross(AngularVelocity, r)) +
               2 * Double3.Cross(AngularVelocity, Pose.TransformDirection(velocity));
    }

    /// <summary>
    /// The acceleration in the frame of a point at <paramref name="pointInParent"/> that moves with
    /// <paramref name="velocity"/> and accelerates at <paramref name="acceleration"/> in the parent, the inverse of
    /// <see cref="AccelerationToParent"/>: a = R⁻¹ (a' − A − α × r − ω × (ω × r) − 2 ω × u), with
    /// u = v' − <see cref="Velocity"/> − ω × r the point's velocity relative to the frame, in the parent's axes.
    /// </summary>
    public Double3 AccelerationFromParent(Double3 pointInParent, Double3 velocity, Double3 acceleration)
    {
        Double3 r = pointInParent - Pose.Position;
        Double3 relative = velocity - Velocity - Double3.Cross(AngularVelocity, r);
        return Pose.InverseTransformDirection(acceleration - Acceleration - Double3.Cross(AngularAcceleration, r) -
                                              Double3.Cross(AngularVelocity, Double3.Cross(AngularVelocity, r)) -
                                              2 * Double3.Cross(AngularVelocity, relative));
    }

    public Double3 AngularToParent(Double3 omega) => Pose.TransformDirection(omega) + AngularVelocity;

    public Double3 AngularFromParent(Double3 omega) => Pose.InverseTransformDirection(omega - AngularVelocity);

    /// <summary>
    /// The angular acceleration in the parent of a body that turns at <paramref name="angularVelocity"/> and spins up at
    /// <paramref name="alpha"/> in the frame, both in the frame's axes: the change of <see cref="AngularToParent"/> over
    /// time, α' = R α_b + α + ω × (R ω_b).
    /// </summary>
    public Double3 AngularAccelerationToParent(Double3 alpha, Double3 angularVelocity) =>
        Pose.TransformDirection(alpha) + AngularAcceleration + Double3.Cross(AngularVelocity, Pose.TransformDirection(angularVelocity));

    /// <summary>
    /// The angular acceleration in the frame of a body that turns at <paramref name="angularVelocity"/> and spins up at
    /// <paramref name="alpha"/> in the parent, both in the parent's axes, the inverse of
    /// <see cref="AngularAccelerationToParent"/>: α_b = R⁻¹ (α' − α − ω × Ω').
    /// </summary>
    public Double3 AngularAccelerationFromParent(Double3 alpha, Double3 angularVelocity) =>
        Pose.InverseTransformDirection(alpha - AngularAcceleration - Double3.Cross(AngularVelocity, angularVelocity));
}
