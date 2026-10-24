namespace Tertius.Demo;

/// <summary>
/// The one law of the brake, the glide, the hold and the docking. It flies a point to a target point and stops it there
/// (docs/plans/blue-danube-demo.md, "The approach guidance" and "The docking", "The law"). The positions and the velocity are
/// in one set of axes that moves with the target, such as the station's: the rendezvous flies in the axes of the frame that
/// holds the station, relative to the station's center, and the docking flies in the station's turning axes.
/// </summary>
/// <remarks>
/// <para>The law cancels the free acceleration that the caller gives it: the acceleration, in the law's axes, of a point
/// that does not thrust, relative to those axes. The rendezvous gives the difference of the planet's gravity between the
/// shuttle and the station's center. The docking adds the centrifugal and Coriolis terms of the station's turn (see
/// <see cref="FreeAcceleration"/>). So the point moves relative to the target as a free mass in empty space does, and a
/// simple law flies it exactly at every distance.</para>
/// <para>The wanted velocity relative to the target points at the target. Its speed is the least of three limits: the
/// speed cap, the speed that stops the point at the brake acceleration in the distance left, and near the target the final
/// rate times the distance, as the passenger's float slows (<see cref="Passenger.ApproachRate"/>). The correction takes the
/// error of the velocity in the velocity time, as the trim of <see cref="AscentGuidance"/> takes the speed that is left.
/// The cap takes only the correction: the free acceleration can grow with the distance from the station's axis, and the
/// law must always cancel it.</para>
/// </remarks>
/// <param name="BrakeAcceleration">The deceleration of the braking curve, in m/s².</param>
/// <param name="SpeedCap">The highest wanted speed, in m/s. Infinite for no cap.</param>
/// <param name="FinalRate">The wanted speed near the target per meter of distance, in 1/s.</param>
/// <param name="VelocityTime">The time in which the correction takes the error of the velocity, in seconds.</param>
/// <param name="MaxAcceleration">The longest correction, in m/s².</param>
public readonly record struct ApproachGuidance(
    double BrakeAcceleration,
    double SpeedCap,
    double FinalRate,
    double VelocityTime,
    double MaxAcceleration)
{
    /// <summary>
    /// The law of the brake, which stops the shuttle at the arrival point with the main engine, and the translation
    /// thrusters for the rest. Its curve brakes at 2.4 m/s², 80 percent of the burn's
    /// <see cref="TransferGuidance.BurnAcceleration"/>,
    /// so the law has room to follow its curve. It has no speed cap. It takes the error of the velocity in 0.5 s.
    /// </summary>
    public static ApproachGuidance ForBrake() =>
        new(BrakeAcceleration: 0.8 * TransferGuidance.BurnAcceleration, SpeedCap: double.PositiveInfinity,
            FinalRate: 0.5, VelocityTime: 0.5,
            MaxAcceleration: TransferGuidance.BurnAcceleration);

    /// <summary>
    /// The law of the glide and the hold, which flies the shuttle along the axis to the hold point on the translation
    /// thrusters alone, with a correction of at most 0.2 m/s². Its curve brakes at 0.05 m/s², and it flies at 5 m/s at
    /// most. It takes the error of the velocity in 2 s. The docking's spin-up holds the hold point with it.
    /// </summary>
    public static ApproachGuidance ForGlide() => new(BrakeAcceleration: 0.05, SpeedCap: 5, FinalRate: 0.1,
        VelocityTime: 2, MaxAcceleration: 0.2);

    /// <summary>
    /// The law of the docking's entry, which flies the shuttle along the station's axis through the slot to the entry's
    /// end: 2 m/s at most, a braking curve of 0.1 m/s², a final rate of 0.2 /s, the error of the velocity taken in 1 s, and
    /// a correction of at most 0.3 m/s².
    /// </summary>
    public static ApproachGuidance ForEntry() => new(BrakeAcceleration: 0.1, SpeedCap: 2, FinalRate: 0.2,
        VelocityTime: 1, MaxAcceleration: 0.3);

    /// <summary>
    /// The law of the docking's move out, which flies the shuttle from the axis to the landing point in the corner of the
    /// deck: 0.5 m/s at most, a braking curve of 0.05 m/s², a final rate of 0.2 /s, the error of the velocity taken in 1 s,
    /// and a correction of at most 0.3 m/s².
    /// </summary>
    public static ApproachGuidance ForMoveOut() => new(BrakeAcceleration: 0.05, SpeedCap: 0.5, FinalRate: 0.2,
        VelocityTime: 1, MaxAcceleration: 0.3);

    /// <summary>
    /// The free acceleration, in m/s², in axes that turn at <paramref name="rate"/> about their origin, of a body whose
    /// center of mass lies at <paramref name="centerOfMass"/> and moves at <paramref name="velocity"/> relative to those axes,
    /// all in those axes: the centrifugal term −ω × (ω × c), the Coriolis term −2 ω × u, and
    /// <paramref name="gravityDifference"/>, the difference of gravity between the body and the axes' origin, which the
    /// caller turns into the axes. The turn must be steady, so it has no Euler term. With no turn, it is the difference of
    /// gravity alone.
    /// </summary>
    public static Double3 FreeAcceleration(Double3 rate, Double3 centerOfMass, Double3 velocity,
        Double3 gravityDifference) =>
        -Double3.Cross(rate, Double3.Cross(rate, centerOfMass)) - 2 * Double3.Cross(rate, velocity) + gravityDifference;

    /// <summary>
    /// The wanted speed toward the target at <paramref name="distance"/> meters from it, in m/s: the least of
    /// <see cref="SpeedCap"/>, the speed that stops the point at <see cref="BrakeAcceleration"/> in the distance, and
    /// <see cref="FinalRate"/> times the distance.
    /// </summary>
    public double WantedSpeed(double distance) =>
        Math.Min(SpeedCap, Math.Min(Math.Sqrt(2 * BrakeAcceleration * distance), FinalRate * distance));

    /// <summary>
    /// The wanted velocity relative to the target, in m/s, for a point at <paramref name="position"/>: the
    /// <see cref="WantedSpeed"/> toward <paramref name="target"/>. Zero at the target.
    /// </summary>
    public Double3 WantedVelocity(Double3 position, Double3 target)
    {
        Double3 offset = position - target;
        double distance = offset.Length;
        return distance > 0 ? offset * (-WantedSpeed(distance) / distance) : Double3.Zero;
    }

    /// <summary>
    /// The acceleration that the thrust should give, in m/s², for a point at <paramref name="position"/> that moves at
    /// <paramref name="velocity"/> relative to the target's axes, toward <paramref name="target"/>, while a point that does
    /// not thrust accelerates at <paramref name="free"/> relative to those axes: the error of the velocity over
    /// <see cref="VelocityTime"/>, at most <see cref="MaxAcceleration"/> long, less the free acceleration.
    /// </summary>
    public Double3 Push(Double3 position, Double3 velocity, Double3 target, Double3 free)
    {
        Double3 correction = (WantedVelocity(position, target) - velocity) / VelocityTime;
        double length = correction.Length;
        return (length > MaxAcceleration ? correction * (MaxAcceleration / length) : correction) - free;
    }
}
