namespace Tertius.Frames.Rails;

/// <summary>
/// The motion of a frame that turns in place: a pure function of a <see cref="FrameSpin"/> and the time. It holds no state.
/// </summary>
public static class SpinRails
{
    /// <summary>The frame's rotation relative to its parent at <paramref name="time"/>.</summary>
    public static DQuaternion RotationAt(in FrameSpin spin, double time) => Leaning(spin) * DQuaternion.FromAxisAngle(Double3.UnitY, spin.PhaseAtEpoch + spin.Rate * (time - spin.Epoch));

    /// <summary>The frame's angular velocity in parent axes, in radians per second: <see cref="FrameSpin.Rate"/> about the pole.</summary>
    public static Double3 AngularVelocity(in FrameSpin spin) => Leaning(spin).Rotate(Double3.UnitY) * spin.Rate;

    private static DQuaternion Leaning(in FrameSpin spin) => spin.Tilt.LengthSquared == 0 ? DQuaternion.Identity : spin.Tilt.Normalized();
}
