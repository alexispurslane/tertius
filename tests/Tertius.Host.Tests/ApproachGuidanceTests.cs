using Tertius.Demo;
using Tertius.Frames.Rails;

namespace Tertius.Host.Tests;

/// <summary>
/// ApproachGuidance: the one law of the brake, the glide, the hold and the docking, with the values of each phase from the
/// design. The law flies a point to its target and cancels the free acceleration that the caller gives it, so the point
/// moves relative to the target as a free mass in empty space does. The rendezvous gives the difference of Earth's gravity
/// between the shuttle and the station's center. The station flies the demo's circle, 6,771 km from Earth's center, with its
/// axis along the orbit's normal, +Y. Earth's gravity there is 8.694 m/s², so 150 m to the side of the orbit, at the hold
/// point, it differs from the gravity at the station's center by 8.694 × 150 / 6,771,000 = 1.93e-4 m/s². The docking flies
/// in the station's turning axes, and gives the centrifugal and Coriolis terms of the station's turn as well
/// (<see cref="ApproachGuidance.FreeAcceleration"/>).
/// </summary>
public sealed class ApproachGuidanceTests
{
    private const double Mu = 3.986004418e14, StationRadius = 6.771e6, HoldDistance = 150;

    /// <summary>The station's angular velocity in its own axes: 0.31321 rad/s about +Y.</summary>
    private static readonly Double3 Spin = Double3.UnitY * DemoScenes.SpinStation.Rate;

    private static readonly ApproachGuidance Brake = ApproachGuidance.ForBrake(), Glide = ApproachGuidance.ForGlide(), MoveOut = ApproachGuidance.ForMoveOut();
    private static readonly (Double3 Position, Double3 Velocity) Station = KeplerOrbit.Circular(StationRadius, phase: 1.1).StateAt(0, Mu);
    private static readonly Double3 HoldPoint = Station.Position + Double3.UnitY * HoldDistance;

    /// <summary>The difference of Earth's gravity between <paramref name="position"/> and the station's center, in m/s².</summary>
    private static Double3 GravityDifference(Double3 position) =>
        KeplerOrbit.Acceleration(position, Mu) - KeplerOrbit.Acceleration(Station.Position, Mu);

    [Fact]
    public void AtTheTargetAndAtRestThePushOnlyCancelsTheDifferenceOfGravity()
    {
        Double3 difference = GravityDifference(HoldPoint);

        Double3 push = Glide.Push(HoldPoint, Double3.Zero, HoldPoint, difference);

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
        Double3 velocity = Glide.WantedVelocity(shuttle, HoldPoint) - Double3.UnitY * 0.1;
        Double3 difference = GravityDifference(shuttle);

        Double3 push = Glide.Push(shuttle, velocity, HoldPoint, difference);

        Double3.Distance(push, Double3.UnitY * (0.1 / Glide.VelocityTime) - difference).Should().BeLessThan(1e-12);
    }

    /// <summary>
    /// Far, fast, and going the wrong way, with no free acceleration to cancel: the law never asks for more than the
    /// correction it may use.
    /// </summary>
    [Theory]
    [InlineData(1500, 60)]
    [InlineData(740, -60)]
    [InlineData(20, 5)]
    [InlineData(1350, -5)]
    public void ThePushNeverExceedsItsMaximum(double distance, double speedAway)
    {
        Double3 shuttle = HoldPoint + Double3.UnitX * distance;
        Double3 velocity = Double3.UnitX * speedAway;

        Brake.Push(shuttle, velocity, HoldPoint, Double3.Zero).Length.Should().BeLessThanOrEqualTo(Brake.MaxAcceleration + 1e-12);
        Glide.Push(shuttle, velocity, HoldPoint, Double3.Zero).Length.Should().BeLessThanOrEqualTo(Glide.MaxAcceleration + 1e-12);
    }

    /// <summary>
    /// At the parked center of mass, (-6.09, 3, -5) m in the station's axes, at rest there and at the target, the push is
    /// the pull toward the axis that keeps the hull on its co-rotating path: ω² times the distance from the axis, 0.773
    /// m/s², toward the axis.
    /// </summary>
    [Fact]
    public void AtRestOnACoRotatingPathThePushIsThePullTowardTheAxis()
    {
        var center = new Double3(-6.09, 3, -5);
        Double3 free = ApproachGuidance.FreeAcceleration(Spin, center, Double3.Zero, Double3.Zero);

        Double3 push = MoveOut.Push(center, Double3.Zero, center, free);

        double rate = DemoScenes.SpinStation.Rate, fromTheAxis = Math.Sqrt(center.X * center.X + center.Z * center.Z);
        Double3.Distance(push, -new Double3(center.X, 0, center.Z) * (rate * rate)).Should().BeLessThan(1e-12, "the push points at the axis");
        push.Length.Should().BeApproximately(rate * rate * fromTheAxis, 1e-12);
        push.Length.Should().BeApproximately(0.773, 0.001);
    }

    /// <summary>
    /// On the axis, where the centrifugal term is zero, a hull that moves at 0.5 m/s across the axis feels the Coriolis term
    /// -2 ω × u in the station's axes. The push differs from the push in axes that do not turn by 2 ω × u, 0.31 m/s², so it
    /// cancels that term and the hull keeps its path.
    /// </summary>
    [Fact]
    public void ThePushCancelsTheCoriolisTermOfTheVelocity()
    {
        var onTheAxis = new Double3(0, 3, 0);
        var velocity = new Double3(-0.5, 0, 0);
        var target = new Double3(-6.1, 3, -5.1);

        Double3 turning = MoveOut.Push(onTheAxis, velocity, target, ApproachGuidance.FreeAcceleration(Spin, onTheAxis, velocity, Double3.Zero));
        Double3 still = MoveOut.Push(onTheAxis, velocity, target, Double3.Zero);

        Double3.Distance(turning - still, 2 * Double3.Cross(Spin, velocity)).Should().BeLessThan(1e-12);
        (turning - still).Length.Should().BeApproximately(0.313, 0.001);
    }

    /// <summary>
    /// Outside the station's bubble, the planet's gravity pulls the shuttle and the station's center differently. The free
    /// acceleration adds that difference, turned into the station's axes by the caller, to the spin's terms, and the push
    /// cancels all of them.
    /// </summary>
    [Fact]
    public void OutsideTheBubbleThePushAlsoCancelsTheDifferenceOfGravity()
    {
        var center = new Double3(0.1, 150, -0.05);
        Double3 difference = GravityDifference(HoldPoint);
        Double3 spinOnly = ApproachGuidance.FreeAcceleration(Spin, center, Double3.Zero, Double3.Zero);

        Double3 push = Glide.Push(center, Double3.Zero, center, ApproachGuidance.FreeAcceleration(Spin, center, Double3.Zero, difference));

        Double3.Distance(push, -spinOnly - difference).Should().BeLessThan(1e-15);
        difference.Length.Should().BeGreaterThan(1e-4, "the difference of gravity is not negligible beside the spin's terms on the axis");
    }

    /// <summary>
    /// Far from the axis the spin's terms grow with the radius, and the law must always cancel them. So the cap takes only
    /// the correction: at the parked point, 1 m short of the landing point and moving away from it, the push exceeds the
    /// move out's <see cref="ApproachGuidance.MaxAcceleration"/> of 0.3 m/s² by the cancelled terms, and the push less the
    /// cancelled terms is at most that cap.
    /// </summary>
    [Fact]
    public void TheCapTakesOnlyTheCorrection()
    {
        var center = new Double3(-6, 3, -5);
        var velocity = new Double3(0.5, 0, 0.5);
        Double3 free = ApproachGuidance.FreeAcceleration(Spin, center, velocity, Double3.Zero);

        Double3 push = MoveOut.Push(center, velocity, new Double3(-6.7, 3, -5.7), free);

        push.Length.Should().BeGreaterThan(MoveOut.MaxAcceleration + 0.3, "the cancelled terms come on top of the cap");
        (push + free).Length.Should().BeApproximately(MoveOut.MaxAcceleration, 1e-12, "the correction is capped");
    }
}
