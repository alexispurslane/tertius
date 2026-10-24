namespace Tertius.Frames.Rails;

/// <summary>
/// How a frame on rails turns in place, for a <c>SpinMotion</c>: it leans by <see cref="Tilt"/>, then it turns about
/// its own +Y, its pole, at <see cref="Rate"/>. Angles are radians, rates radians per second, times seconds. The default
/// spin holds the frame still, with its axes parallel to its parent's. Its members are pure functions of the spin and the time.
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

    /// <summary>The frame's angular velocity in parent axes, in radians per second: <see cref="Rate"/> about the pole.</summary>
    public readonly Double3 AngularVelocity => Leaning.Rotate(Double3.UnitY) * Rate;

    private readonly DQuaternion Leaning => Tilt.LengthSquared == 0 ? DQuaternion.Identity : Tilt.Normalized();

    /// <summary>The frame's rotation relative to its parent at <paramref name="time"/>.</summary>
    public readonly DQuaternion RotationAt(double time) =>
        Leaning * DQuaternion.FromAxisAngle(Double3.UnitY, PhaseAtEpoch + Rate * (time - Epoch));

    /// <summary>
    /// The frame's motion over the physics step that ends at <paramref name="stepEnd"/>: it stands at
    /// <paramref name="place"/> in its parent, with <see cref="RotationAt"/> of <paramref name="stepEnd"/>, and turns
    /// steadily at <see cref="AngularVelocity"/>. The chord velocity, the velocity, the acceleration and the angular
    /// acceleration are zero. Unlike <see cref="KeplerOrbit.Sample"/>, it takes no step length: a frame that turns in
    /// place does not depend on it.
    /// </summary>
    public readonly MotionSample Sample(Double3 place, double stepEnd) =>
        new(Pose: new(place, RotationAt(stepEnd)), ChordVelocity: Double3.Zero, Velocity: Double3.Zero,
            Acceleration: Double3.Zero, AngularVelocity: AngularVelocity, AngularAcceleration: Double3.Zero);
}
