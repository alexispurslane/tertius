using Tertius.Frames.Rails;

namespace Tertius.Demo;

/// <summary>
/// The one law of the brake, the glide and the hold, in a frame whose origin is the planet's center, the center of its
/// point gravity, and which does not turn. It flies the shuttle to a target point fixed to the station and stops it there
/// (docs/plans/blue-danube-demo.md, "The approach guidance").
/// </summary>
/// <remarks>
/// <para>The law cancels the difference of the planet's gravity between the shuttle and the station's center. So the
/// shuttle moves relative to the target as a free mass in empty space does, and a simple law flies it exactly at every
/// distance. The target is the station's center plus a fixed distance along its axis, which keeps its direction, so the
/// target falls with the station's center.</para>
/// <para>The wanted velocity relative to the station points at the target. Its speed is the least of three limits: the
/// speed cap, the speed that stops the shuttle at the brake acceleration in the distance left, and near the target the
/// final rate times the distance, as the passenger's float slows (<see cref="Passenger.ApproachRate"/>). The push takes
/// the error of the velocity in the velocity time, as the trim of <see cref="AscentGuidance"/> takes the speed that is
/// left.</para>
/// </remarks>
/// <param name="Mu">The planet's G·M, in m³/s².</param>
/// <param name="BrakeAcceleration">The deceleration of the braking curve, in m/s².</param>
/// <param name="SpeedCap">The highest wanted speed, in m/s. Infinite for no cap.</param>
/// <param name="FinalRate">The wanted speed near the target per meter of distance, in 1/s.</param>
/// <param name="VelocityTime">The time in which the push takes the error of the velocity, in seconds.</param>
/// <param name="MaxAcceleration">The longest push, in m/s².</param>
public readonly record struct ApproachGuidance(
    double Mu, double BrakeAcceleration, double SpeedCap, double FinalRate, double VelocityTime, double MaxAcceleration)
{
    /// <summary>
    /// The law of the brake, which stops the shuttle at the arrival point with the main engine, and the translation
    /// thrusters for the rest. Its curve brakes at 2.4 m/s², 80 percent of the burn's <see cref="TransferGuidance.BurnAcceleration"/>,
    /// so the law has room to follow its curve. It has no speed cap. It takes the error of the velocity in 0.5 s.
    /// </summary>
    public static ApproachGuidance ForBrake(double mu) =>
        new(mu, 0.8 * TransferGuidance.BurnAcceleration, double.PositiveInfinity, 0.5, 0.5, TransferGuidance.BurnAcceleration);

    /// <summary>
    /// The law of the glide and the hold, which flies the shuttle along the axis to the hold point on the translation
    /// thrusters alone, at their 0.2 m/s² per axis. Its curve brakes at 0.05 m/s², a quarter of that, and it flies at
    /// 5 m/s at most. It takes the error of the velocity in 2 s.
    /// </summary>
    public static ApproachGuidance ForGlide(double mu) => new(mu, 0.05, 5, 0.1, 2, 0.2);

    /// <summary>
    /// The wanted speed toward the target at <paramref name="distance"/> meters from it, in m/s: the least of
    /// <see cref="SpeedCap"/>, the speed that stops the shuttle at <see cref="BrakeAcceleration"/> in the distance, and
    /// <see cref="FinalRate"/> times the distance.
    /// </summary>
    public double WantedSpeed(double distance) =>
        Math.Min(SpeedCap, Math.Min(Math.Sqrt(2 * BrakeAcceleration * distance), FinalRate * distance));

    /// <summary>
    /// The wanted velocity relative to the station, in m/s, for a shuttle at <paramref name="shuttlePosition"/>: the
    /// <see cref="WantedSpeed"/> toward <paramref name="target"/>. Zero at the target.
    /// </summary>
    public Double3 WantedVelocity(Double3 shuttlePosition, Double3 target)
    {
        Double3 offset = shuttlePosition - target;
        double distance = offset.Length;
        return distance > 0 ? offset * (-WantedSpeed(distance) / distance) : Double3.Zero;
    }

    /// <summary>
    /// The acceleration that the shuttle's thrust should give, in m/s², at most <see cref="MaxAcceleration"/> long, for a
    /// shuttle at <paramref name="shuttlePosition"/> that moves at <paramref name="shuttleVelocity"/>, toward
    /// <paramref name="target"/>, while the station's center is at <paramref name="stationPosition"/> and moves at
    /// <paramref name="stationVelocity"/>: the error of the velocity relative to the station over
    /// <see cref="VelocityTime"/>, less the difference of the planet's gravity between the shuttle and the station's center.
    /// </summary>
    public Double3 Push(Double3 shuttlePosition, Double3 shuttleVelocity, Double3 target, Double3 stationPosition, Double3 stationVelocity)
    {
        Double3 error = WantedVelocity(shuttlePosition, target) - (shuttleVelocity - stationVelocity);
        Double3 gravityDifference = KeplerOrbit.Acceleration(shuttlePosition, Mu) - KeplerOrbit.Acceleration(stationPosition, Mu);
        Double3 push = error / VelocityTime - gravityDifference;
        double length = push.Length;
        return length > MaxAcceleration ? push * (MaxAcceleration / length) : push;
    }
}
