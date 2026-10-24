namespace Tertius.Demo;

/// <summary>
/// The profile of the docking's spin-up, in the station's axes (docs/plans/blue-danube-demo.md, "The spin-up and the
/// roll"): one turn about the station's axis, +Y, from still among the stars to rest in the station, with a constant
/// angular acceleration of <paramref name="Rate"/> / <paramref name="Duration"/>. The attitude target trails
/// <paramref name="Docked"/> by the lag λ(τ) = ω (T - τ)² / (2T), which falls to zero at the end, and turns relative to the
/// station at dλ/dτ. Every member is a pure function of the time since the start.
/// </summary>
/// <remarks>
/// The roll is folded into the start. At the start the target trails the docked rotation by ω T / 2, 269.2 degrees for
/// the demo. The hull holds still among the stars, so in the station's axes its roof turns backward at ω, and the roll
/// angle to the first target passes zero once in each turn of the station. The spin-up starts in the one tick of each
/// pass in which the roll angle lies within half a tick's turn of zero (<see cref="InRollWindow"/>), so no roll is left to
/// fly after it.
/// </remarks>
/// <param name="Rate">The station's rate about its axis, ω, in rad/s.</param>
/// <param name="Duration">How long the spin-up takes, T, in seconds.</param>
/// <param name="Docked">The shuttle's rotation at rest in the station, where the spin-up ends, in the station's axes.</param>
public readonly record struct SpinUpProfile(double Rate, double Duration, DQuaternion Docked)
{
    /// <summary>The lag λ of the target behind <see cref="Docked"/> about +Y at <paramref name="elapsed"/> seconds into the spin-up, in radians.</summary>
    public double Lag(double elapsed)
    {
        double left = Duration - Math.Clamp(elapsed, 0, Duration);
        return Rate * left * left / (2 * Duration);
    }

    /// <summary>
    /// The target's turn about +Y relative to the station at <paramref name="elapsed"/> seconds into the spin-up, dλ/dτ, in
    /// rad/s: -ω at the start, where the target holds still among the stars, and zero from the end on.
    /// </summary>
    public double TurnRate(double elapsed) => -Rate * (Duration - Math.Clamp(elapsed, 0, Duration)) / Duration;

    /// <summary>The attitude target in the station's axes at <paramref name="elapsed"/> seconds into the spin-up:
    /// <see cref="Docked"/> turned by the lag about +Y.</summary>
    public DQuaternion Target(double elapsed) => DQuaternion.FromAxisAngle(Double3.UnitY, Lag(elapsed)) * Docked;

    /// <summary>
    /// The roll angle of a hull with <paramref name="rotation"/>, in the station's axes: the angle about +Y from the hull's
    /// roof to the roof of the first target, <see cref="Target"/> at 0, both taken across the axis, from -π to π.
    /// </summary>
    public double RollAngle(DQuaternion rotation)
    {
        Double3 roof = Across(rotation.Rotate(Double3.UnitY)),
            first = Across(Target(0).Rotate(Double3.UnitY));
        return Math.Atan2(Double3.Dot(Double3.UnitY, Double3.Cross(roof, first)), Double3.Dot(roof, first));
    }

    /// <summary>
    /// True when <paramref name="rollAngle"/> lies within half the station's turn in one tick of <paramref name="deltaTime"/>
    /// seconds of zero, from -ω dt / 2 up to but not including ω dt / 2, so one tick of each turn holds it.
    /// </summary>
    public bool InRollWindow(double rollAngle, double deltaTime) =>
        rollAngle >= -Rate * deltaTime / 2 && rollAngle < Rate * deltaTime / 2;

    /// <summary><paramref name="direction"/> less its part along +Y, the axis.</summary>
    private static Double3 Across(Double3 direction) => direction with { Y = 0 };
}
