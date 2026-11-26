using Tertius.Frames.Rails;

namespace Tertius.Demo;

/// <summary>
/// The ride of a lift's floor down a spoke of a spinning station, from rest at the top to rest at the foot
/// (https://github.com/alexispurslane/tertius/wiki/blue-danube-demo, "The lift"). A distance is the floor's distance from the axis along the spoke, in meters,
/// and a speed or an acceleration points outward. The ride has three phases. The boost accelerates outward at a share
/// c of the local spin gravity, c ω² d, so d = d₀ cosh(√c ω t) and a rider keeps the rest of its weight. The cruise holds
/// the cruise speed. The brake decelerates at a constant rate to rest at the foot. Every member is a pure function of the
/// time since the start, which <see cref="Of"/> makes from valid inputs only.
/// </summary>
public readonly record struct LiftProfile
{
    private LiftProfile(double rate, double top, double foot, double boostShare, double cruiseSpeed, double brake)
    {
        Rate = rate;
        Top = top;
        Foot = foot;
        BoostShare = boostShare;
        CruiseSpeed = cruiseSpeed;
        Brake = brake;
    }

    /// <summary>The station's rate about its axis, ω, in rad/s.</summary>
    public double Rate { get; }

    /// <summary>The floor's distance from the axis at the start, d₀, in meters.</summary>
    public double Top { get; }

    /// <summary>The floor's distance from the axis at the end, in meters.</summary>
    public double Foot { get; }

    /// <summary>The share c of the local spin gravity at which the boost accelerates, above 0 and below 1.</summary>
    public double BoostShare { get; }

    /// <summary>The speed of the cruise, in m/s.</summary>
    public double CruiseSpeed { get; }

    /// <summary>The deceleration of the brake, in m/s².</summary>
    public double Brake { get; }

    /// <summary>How long the boost takes, in seconds: until d₀ k sinh(k t) reaches the cruise speed, with k = √c ω.</summary>
    public double BoostTime => Math.Asinh(CruiseSpeed / (Top * Growth)) / Growth;

    /// <summary>When the brake starts, in seconds after the start: after the boost and the cruise.</summary>
    public double BrakeTime => BoostTime + (BrakeFrom - BoostTo) / CruiseSpeed;

    /// <summary>How long the whole ride takes, in seconds.</summary>
    public double Duration => BrakeTime + CruiseSpeed / Brake;

    /// <summary>The boost's rate of growth, k = √c ω, in 1/s.</summary>
    private double Growth => Math.Sqrt(BoostShare) * Rate;

    /// <summary>The floor's distance at the boost's end, in meters: d₀ cosh(k t) where d₀ k sinh(k t) is the cruise speed.</summary>
    private double BoostTo => Math.Sqrt(Top * Top + CruiseSpeed * CruiseSpeed / (Growth * Growth));

    /// <summary>The floor's distance where the brake starts, in meters: the brake's way, v² / (2 b), short of the foot.</summary>
    private double BrakeFrom => Foot - CruiseSpeed * CruiseSpeed / (2 * Brake);

    /// <summary>
    /// The profile of a ride at the station's rate <paramref name="rate"/> rad/s from <paramref name="top"/> to
    /// <paramref name="foot"/> meters from the axis: a boost at <paramref name="boostShare"/> of the spin gravity to
    /// <paramref name="cruiseSpeed"/> m/s, and a brake at <paramref name="brake"/> m/s². Throws
    /// <see cref="ArgumentException"/> for an input that is not a positive finite number, a share of 1 or more, or a way
    /// shorter than the boost and the brake together.
    /// </summary>
    public static LiftProfile Of(
        double rate,
        double top,
        double foot,
        double boostShare,
        double cruiseSpeed,
        double brake
    )
    {
        foreach (
            (double value, string name) in new[]
            {
                (rate, nameof(rate)),
                (top, nameof(top)),
                (foot, nameof(foot)),
                (boostShare, nameof(boostShare)),
                (cruiseSpeed, nameof(cruiseSpeed)),
                (brake, nameof(brake)),
            }
        )
            if (!(value > 0 && double.IsFinite(value)))
                throw new ArgumentException($"A lift's {name} must be a positive finite number, not {value}.", name);
        if (boostShare >= 1)
            throw new ArgumentException(
                $"A lift that boosts at {boostShare} of the spin gravity leaves its rider weightless or behind.",
                nameof(boostShare)
            );
        var profile = new LiftProfile(
            rate: rate,
            top: top,
            foot: foot,
            boostShare: boostShare,
            cruiseSpeed: cruiseSpeed,
            brake: brake
        );
        if (profile.BrakeFrom < profile.BoostTo)
            throw new ArgumentException(
                $"The way from {top} m to {foot} m is shorter than the boost to {cruiseSpeed} m/s and the brake from it.",
                nameof(foot)
            );
        return profile;
    }

    /// <summary>The floor's distance from the axis at <paramref name="elapsed"/> seconds after the start, in meters.</summary>
    public double Distance(double elapsed)
    {
        if (elapsed <= 0)
            return Top;
        if (elapsed < BoostTime)
            return Top * Math.Cosh(Growth * elapsed);
        if (elapsed < BrakeTime)
            return BoostTo + CruiseSpeed * (elapsed - BoostTime);
        if (elapsed >= Duration)
            return Foot;
        double left = Duration - elapsed;
        return Foot - Brake * left * left / 2;
    }

    /// <summary>The floor's outward speed at <paramref name="elapsed"/> seconds after the start, in m/s.</summary>
    public double Speed(double elapsed)
    {
        if (elapsed <= 0 || elapsed >= Duration)
            return 0;
        if (elapsed < BoostTime)
            return Top * Growth * Math.Sinh(Growth * elapsed);
        if (elapsed < BrakeTime)
            return CruiseSpeed;
        return Brake * (Duration - elapsed);
    }

    /// <summary>The floor's outward acceleration at <paramref name="elapsed"/> seconds after the start, in m/s².</summary>
    public double Acceleration(double elapsed)
    {
        if (elapsed <= 0 || elapsed >= Duration)
            return 0;
        if (elapsed < BoostTime)
            return Growth * Growth * Distance(elapsed);
        if (elapsed < BrakeTime)
            return 0;
        return -Brake;
    }

    /// <summary>
    /// The motion of a lift over the step of <paramref name="step"/> seconds that ends <paramref name="elapsed"/> seconds
    /// after the start, for a floor whose pose at the top is <paramref name="top"/> and which moves along the unit direction
    /// <paramref name="outward"/>, both in the frame that holds the lift: its pose, speed and velocity at the step's end, its
    /// chord over the step, and the mean change of its velocity. It does not turn.
    /// </summary>
    public MotionSample Sample(DPose top, Double3 outward, double elapsed, double step)
    {
        double start = elapsed - step;
        return new(
            Pose: new(top.Position + outward * (Distance(elapsed) - Top), top.Rotation),
            ChordVelocity: outward * ((Distance(elapsed) - Distance(start)) / step),
            Velocity: outward * Speed(elapsed),
            Acceleration: outward * ((Speed(elapsed) - Speed(start)) / step),
            AngularVelocity: Double3.Zero,
            AngularAcceleration: Double3.Zero
        );
    }
}
