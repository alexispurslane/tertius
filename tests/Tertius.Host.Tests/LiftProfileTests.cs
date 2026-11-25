using Tertius.Demo;
using Tertius.Frames.Rails;

namespace Tertius.Host.Tests;

/// <summary>
/// The profile of the lift's ride down a spoke, <see cref="LiftProfile"/> (docs/plans/blue-danube-demo.md, "The lift"), as
/// pure math. The lift's floor starts at rest at the top, boosts outward at a share of the local spin gravity, cruises at
/// a set speed and brakes to rest at the foot. Distances are the floor's distance from the axis along the spoke, in
/// meters, and speeds and accelerations point outward.
/// </summary>
public sealed class LiftProfileTests
{
    private const double Rate = 0.07,
        Top = 40,
        Foot = 1999.7,
        Share = 0.5,
        Cruise = 20,
        Brake = 1,
        Tick = 1.0 / 60;

    private static readonly LiftProfile Profile = LiftProfile.Of(
        rate: Rate,
        top: Top,
        foot: Foot,
        boostShare: Share,
        cruiseSpeed: Cruise,
        brake: Brake
    );

    /// <summary>The times from the start to past the end, one tick apart.</summary>
    private static IEnumerable<double> Ticks() =>
        Enumerable.Range(0, (int)Math.Ceiling((Profile.Duration + 2) / Tick)).Select(tick => tick * Tick);

    /// <summary>Before the start and at it, the floor rests at the top.</summary>
    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    public void TheFloorRestsAtTheTopUntilTheStart(double elapsed)
    {
        Profile.Distance(elapsed).Should().Be(Top);
        Profile.Speed(elapsed).Should().Be(0);
        Profile.Acceleration(elapsed).Should().Be(0);
    }

    /// <summary>
    /// In the boost, the floor accelerates outward at the boost's share of the spin gravity at its own distance, ω² d, so a
    /// rider keeps the rest of its weight.
    /// </summary>
    [Fact]
    public void TheBoostAcceleratesAtItsShareOfTheLocalSpinGravity()
    {
        Profile.BoostTime.Should().BePositive();
        foreach (double elapsed in Ticks().Skip(1).TakeWhile(t => t < Profile.BoostTime))
            Profile
                .Acceleration(elapsed)
                .Should()
                .BeApproximately(Share * Rate * Rate * Profile.Distance(elapsed), 1e-12, $"at {elapsed} s");
    }

    /// <summary>The boost ends at the cruise speed, which the floor holds until the brake starts.</summary>
    [Fact]
    public void TheFloorCruisesAtTheCruiseSpeedBetweenTheBoostAndTheBrake()
    {
        Profile.Speed(Profile.BoostTime).Should().BeApproximately(Cruise, 1e-9);
        foreach (double elapsed in Ticks().Where(t => t > Profile.BoostTime && t < Profile.BrakeTime))
        {
            Profile.Speed(elapsed).Should().Be(Cruise, $"at {elapsed} s");
            Profile.Acceleration(elapsed).Should().Be(0, $"at {elapsed} s");
        }
    }

    /// <summary>The brake slows the floor at the brake's deceleration, and the floor stops at the foot at the end.</summary>
    [Fact]
    public void TheBrakeStopsTheFloorAtTheFoot()
    {
        foreach (double elapsed in Ticks().Where(t => t > Profile.BrakeTime && t < Profile.Duration))
            Profile.Acceleration(elapsed).Should().Be(-Brake, $"at {elapsed} s");
        Profile.Distance(Profile.Duration).Should().BeApproximately(Foot, 1e-9);
        Profile.Speed(Profile.Duration).Should().BeApproximately(0, 1e-9);
    }

    /// <summary>After the end, the floor rests at the foot.</summary>
    [Fact]
    public void AfterTheRideTheFloorRestsAtTheFoot()
    {
        Profile.Distance(Profile.Duration + 5).Should().Be(Foot);
        Profile.Speed(Profile.Duration + 5).Should().Be(0);
        Profile.Acceleration(Profile.Duration + 5).Should().Be(0);
    }

    /// <summary>
    /// The speed is the change of the distance, and the acceleration the change of the speed, everywhere: the mean speed
    /// over each tick lies within the speeds at its ends, and the change of the speed over each tick lies within the
    /// accelerations at its ends, with a tolerance for the jumps of the acceleration at the phases' joints.
    /// </summary>
    [Fact]
    public void TheSpeedAndTheAccelerationAreTheChangesOfTheDistanceAndTheSpeed()
    {
        foreach (double start in Ticks())
        {
            double end = start + Tick;
            double meanSpeed = (Profile.Distance(end) - Profile.Distance(start)) / Tick,
                meanAcceleration = (Profile.Speed(end) - Profile.Speed(start)) / Tick;
            meanSpeed
                .Should()
                .BeInRange(
                    Math.Min(Profile.Speed(start), Profile.Speed(end)) - 1e-6,
                    Math.Max(Profile.Speed(start), Profile.Speed(end)) + 1e-6,
                    $"over the tick from {start} s"
                );
            meanAcceleration
                .Should()
                .BeInRange(
                    Math.Min(Math.Min(Profile.Acceleration(start), Profile.Acceleration(end)), -Brake) - 1e-6,
                    Math.Max(Profile.Acceleration(start), Profile.Acceleration(end)) + 1e-6,
                    $"over the tick from {start} s"
                );
        }
    }

    /// <summary>The floor never moves back toward the axis, and never passes the foot.</summary>
    [Fact]
    public void TheFloorMovesOnlyOutwardAndStopsAtTheFoot()
    {
        double last = Top;
        foreach (double elapsed in Ticks())
        {
            double distance = Profile.Distance(elapsed);
            distance.Should().BeGreaterThanOrEqualTo(last, $"at {elapsed} s");
            distance.Should().BeLessThanOrEqualTo(Foot + 1e-9, $"at {elapsed} s");
            last = distance;
        }
    }

    /// <summary>
    /// The phases' times follow from the inputs: the boost reaches the cruise speed when d₀ k sinh(k t) = v, with
    /// k = sqrt(c) ω; the brake takes v / b; the cruise covers the rest of the way at v.
    /// </summary>
    [Fact]
    public void ThePhasesTakeTheTimesThatTheirLawsGive()
    {
        double k = Math.Sqrt(Share) * Rate;
        double boost = Math.Asinh(Cruise / (Top * k)) / k,
            boostEnd = Math.Sqrt(Top * Top + Cruise * Cruise / (k * k)),
            brakeStart = Foot - Cruise * Cruise / (2 * Brake);
        Profile.BoostTime.Should().BeApproximately(boost, 1e-9);
        Profile.BrakeTime.Should().BeApproximately(boost + (brakeStart - boostEnd) / Cruise, 1e-9);
        Profile.Duration.Should().BeApproximately(Profile.BrakeTime + Cruise / Brake, 1e-9);
    }

    /// <summary>
    /// The sample of a step lies on the spoke from the top's place along the outward direction: its place and its velocity
    /// at the step's end, its chord the change of place over the step, and its acceleration the mean change of the speed.
    /// It does not turn.
    /// </summary>
    [Fact]
    public void TheSampleOfAStepLiesOnTheSpokeFromTheTopsPlace()
    {
        var top = new DPose(new Double3(-39.8, 0, 2.5), DQuaternion.Identity);
        var outward = new Double3(-1, 0, 0);
        double end = 70.5;

        MotionSample sample = Profile.Sample(top: top, outward: outward, elapsed: end, step: Tick);

        sample.Pose.Position.Should().Be(top.Position + outward * (Profile.Distance(end) - Top));
        sample.Pose.Rotation.Should().Be(top.Rotation);
        sample.Velocity.Should().Be(outward * Profile.Speed(end));
        sample.ChordVelocity.Should().Be(outward * ((Profile.Distance(end) - Profile.Distance(end - Tick)) / Tick));
        sample.Acceleration.Should().Be(outward * ((Profile.Speed(end) - Profile.Speed(end - Tick)) / Tick));
        sample.AngularVelocity.Should().Be(Double3.Zero);
        sample.AngularAcceleration.Should().Be(Double3.Zero);
    }

    /// <summary>A way too short for the boost to the cruise speed and the brake from it has no profile.</summary>
    [Fact]
    public void AWayTooShortForTheBoostAndTheBrakeHasNoProfile()
    {
        Action shortWay = () =>
            LiftProfile.Of(rate: Rate, top: Top, foot: 500, boostShare: Share, cruiseSpeed: Cruise, brake: Brake);
        shortWay.Should().Throw<ArgumentException>();
    }

    /// <summary>A rate, a top, a share, a cruise speed or a brake that is not a positive finite number has no profile.</summary>
    [Theory]
    [InlineData(0.0, Top, Share, Cruise, Brake)]
    [InlineData(Rate, 0.0, Share, Cruise, Brake)]
    [InlineData(Rate, Top, 0.0, Cruise, Brake)]
    [InlineData(Rate, Top, 1.0, Cruise, Brake)]
    [InlineData(Rate, Top, Share, 0.0, Brake)]
    [InlineData(Rate, Top, Share, Cruise, double.NaN)]
    [InlineData(Rate, Top, Share, double.PositiveInfinity, Brake)]
    public void AnInputOutOfItsRangeHasNoProfile(double rate, double top, double share, double cruise, double brake)
    {
        Action invalid = () =>
            LiftProfile.Of(rate: rate, top: top, foot: Foot, boostShare: share, cruiseSpeed: cruise, brake: brake);
        invalid.Should().Throw<ArgumentException>();
    }
}
