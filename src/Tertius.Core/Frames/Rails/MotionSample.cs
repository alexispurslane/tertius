using System.Numerics;

namespace Tertius.Frames.Rails;

/// <summary>
/// The motion of a frame on rails over one physics step, in doubles: what <see cref="KeplerOrbit.Sample"/> and
/// <see cref="FrameSpin.Sample"/> give. Every field except <see cref="ProperAcceleration"/> is relative to the frame's
/// semantic parent, in the parent's axes. <see cref="ProperAcceleration"/> is in the frame's own axes. A root frame has no
/// parent, so its sample is in the root's own space.
/// </summary>
/// <param name="Pose">The frame's pose at the end of the step: its place in m and its rotation.</param>
/// <param name="ChordVelocity">The change of the frame's place over the step, divided by the length of the step, in m/s.</param>
/// <param name="Velocity">The frame's instant velocity at the end of the step, in m/s.</param>
/// <param name="Acceleration">The frame's acceleration during the step, in m/s².</param>
/// <param name="AngularVelocity">The frame's angular velocity at the end of the step, in rad/s.</param>
/// <param name="AngularAcceleration">The frame's angular acceleration during the step, in rad/s².</param>
/// <param name="ProperAcceleration">
/// The frame's proper acceleration, in m/s², in the frame's own axes. The effective gravity of the frame's contents is the
/// frame's field minus this value.
/// </param>
public readonly record struct MotionSample(
    DPose Pose, Double3 ChordVelocity, Double3 Velocity, Double3 Acceleration,
    Double3 AngularVelocity, Double3 AngularAcceleration, Vector3 ProperAcceleration);
