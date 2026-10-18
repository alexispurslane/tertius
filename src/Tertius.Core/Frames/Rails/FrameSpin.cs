namespace Tertius.Frames.Rails;

/// <summary>
/// How a frame with the <see cref="MotionDriver.Spin"/> driver turns: it leans by <see cref="Tilt"/>, then it turns about
/// its own +Y, its pole, at <see cref="Rate"/>. Angles are radians, rates radians per second, times seconds. The default
/// spin holds the frame still, with its axes parallel to its parent's. <see cref="SpinRails"/> computes its motion.
/// </summary>
/// <param name="Tilt">
/// The rotation from the parent's axes to the frame's axes at a spin angle of 0. Only its direction counts, so it need not be
/// unit length. A zero quaternion means none.
/// </param>
/// <param name="Rate">How fast the frame turns about its pole, counterclockwise seen from the pole's tip.</param>
/// <param name="PhaseAtEpoch">The spin angle at <paramref name="Epoch"/>.</param>
/// <param name="Epoch">The time at which the spin angle is <paramref name="PhaseAtEpoch"/>.</param>
public record struct FrameSpin(DQuaternion Tilt, double Rate, double PhaseAtEpoch = 0, double Epoch = 0)
{
    /// <summary>True when every value is finite.</summary>
    public readonly bool IsValid =>
        double.IsFinite(Rate) && double.IsFinite(PhaseAtEpoch) && double.IsFinite(Epoch) &&
        double.IsFinite(Tilt.X) && double.IsFinite(Tilt.Y) && double.IsFinite(Tilt.Z) && double.IsFinite(Tilt.W);
}
