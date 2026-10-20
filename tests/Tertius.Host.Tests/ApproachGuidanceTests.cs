using Tertius.Demo;
using Tertius.Frames.Rails;

namespace Tertius.Host.Tests;

/// <summary>
/// ApproachGuidance: the one law of the brake, the glide and the hold, for Earth's G·M, with the values of the brake and
/// the glide from the design. The station flies the demo's circle, 6,771 km from Earth's center, with its axis along the
/// orbit's normal, +Y. Earth's gravity there is 8.694 m/s², so 150 m to the side of the orbit, at the hold point, it differs
/// from the gravity at the station's center by 8.694 × 150 / 6,771,000 = 1.93e-4 m/s².
/// </summary>
public sealed class ApproachGuidanceTests
{
    private const double Mu = 3.986004418e14, StationRadius = 6.771e6, HoldDistance = 150;

    private static readonly ApproachGuidance Brake = ApproachGuidance.ForBrake(Mu), Glide = ApproachGuidance.ForGlide(Mu);
    private static readonly (Double3 Position, Double3 Velocity) Station = KeplerOrbit.Circular(StationRadius, phase: 1.1).StateAt(0, Mu);
    private static readonly Double3 HoldPoint = Station.Position + Double3.UnitY * HoldDistance;

    [Fact]
    public void AtTheTargetAndAtRestThePushOnlyCancelsTheDifferenceOfGravity()
    {
        Double3 push = Glide.Push(HoldPoint, Station.Velocity, HoldPoint, Station.Position, Station.Velocity);

        Double3 difference = KeplerOrbit.Acceleration(HoldPoint, Mu) - KeplerOrbit.Acceleration(Station.Position, Mu);
        Double3.Distance(push, -difference).Should().BeLessThan(1e-18);
        push.Length.Should().BeApproximately(1.93e-4, 1e-6);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(1350)]
    [InlineData(1e5)]
    public void TheWantedSpeedNeverExceedsTheCap(double distance) =>
        Glide.WantedSpeed(distance).Should().Be(Glide.SpeedCap);

    /// <summary>740 m before the arrival point, where the brake starts, the brake's curve asks for sqrt(2 × 2.4 × 740) m/s.</summary>
    [Fact]
    public void TheWantedSpeedStopsTheShuttleAtTheBrakeAccelerationInTheDistanceLeft() =>
        Brake.WantedSpeed(740).Should().BeApproximately(Math.Sqrt(2 * 2.4 * 740), 1e-12);

    /// <summary>Within 5 m of the hold point, the glide's final rate of 0.1 /s asks for less than its braking curve.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(1)]
    [InlineData(0.1)]
    public void NearTheTargetTheWantedSpeedFallsWithTheDistance(double distance) =>
        Glide.WantedSpeed(distance).Should().BeApproximately(0.1 * distance, 1e-15);

    [Fact]
    public void TheWantedVelocityPointsAtTheTarget()
    {
        Double3 shuttle = HoldPoint + new Double3(30, 40, 0);

        Double3 wanted = Glide.WantedVelocity(shuttle, HoldPoint);

        Double3.Distance(wanted, new Double3(-0.6, -0.8, 0) * Glide.WantedSpeed(50)).Should().BeLessThan(1e-12);
        Glide.WantedVelocity(HoldPoint, HoldPoint).Should().Be(Double3.Zero);
    }

    /// <summary>1 m from the hold point and 0.1 m/s faster toward it than the law wants, the push takes the 0.1 m/s in 2 s.</summary>
    [Fact]
    public void ThePushTakesTheVelocityErrorInTheVelocityTime()
    {
        Double3 shuttle = HoldPoint + Double3.UnitY;
        Double3 velocity = Station.Velocity + Glide.WantedVelocity(shuttle, HoldPoint) - Double3.UnitY * 0.1;

        Double3 push = Glide.Push(shuttle, velocity, HoldPoint, Station.Position, Station.Velocity);

        Double3 difference = KeplerOrbit.Acceleration(shuttle, Mu) - KeplerOrbit.Acceleration(Station.Position, Mu);
        Double3.Distance(push, Double3.UnitY * (0.1 / Glide.VelocityTime) - difference).Should().BeLessThan(1e-12);
    }

    /// <summary>Far, fast, and going the wrong way: the law never asks for more than the thrust it may use.</summary>
    [Theory]
    [InlineData(1500, 60)]
    [InlineData(740, -60)]
    [InlineData(20, 5)]
    [InlineData(1350, -5)]
    public void ThePushNeverExceedsItsMaximum(double distance, double speedAway)
    {
        Double3 shuttle = HoldPoint + Double3.UnitX * distance;
        Double3 velocity = Station.Velocity + Double3.UnitX * speedAway;

        Brake.Push(shuttle, velocity, HoldPoint, Station.Position, Station.Velocity).Length.Should().BeLessThanOrEqualTo(Brake.MaxAcceleration + 1e-12);
        Glide.Push(shuttle, velocity, HoldPoint, Station.Position, Station.Velocity).Length.Should().BeLessThanOrEqualTo(Glide.MaxAcceleration + 1e-12);
    }
}
