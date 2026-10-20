using Tertius.Demo;
using Tertius.Frames.Rails;

namespace Tertius.Host.Tests;

/// <summary>
/// TransferGuidance: the guidance of the shuttle's transfer to the station, for Earth's G·M. The station flies the demo's
/// circle, 6,771 km from Earth's center, in the XZ plane, from +X toward -Z, so its orbit's normal is +Y, and its axis
/// lies along that normal. The shuttle flies the parking circle, 6,571 km from the center, in the same plane. The arrival
/// point lies 1,500 m along the axis from the station, as in the demo.
/// </summary>
public sealed class TransferGuidanceTests
{
    private const double Mu = 3.986004418e14, StationRadius = 6.771e6, ParkingRadius = 6.571e6;
    private const double TransferTime = 2560, ArrivalDistance = 1500;

    private static readonly TransferGuidance Guidance = new(Mu);
    private static readonly Double3 Axis = Double3.UnitY;

    private static double Degrees(double degrees) => degrees * Math.PI / 180;

    /// <summary>The state on the circle of <paramref name="radius"/> at <paramref name="degrees"/> along the motion from +X.</summary>
    private static (Double3 Position, Double3 Velocity) OnCircle(double radius, double degrees) =>
        KeplerOrbit.Circular(radius, phase: Degrees(degrees)).StateAt(0, Mu);

    /// <summary>
    /// The demo's window: the station 3.8 degrees ahead of the shuttle, so that its place after the transfer time, 166.2
    /// degrees further on, lies 170 degrees ahead of the shuttle.
    /// </summary>
    private static readonly (Double3 Position, Double3 Velocity) Station = OnCircle(StationRadius, 3.8);
    private static readonly (Double3 Position, Double3 Velocity) Shuttle = OnCircle(ParkingRadius, 0);

    [Fact]
    public void TheTransferAngleRunsTheWayOfTheOrbit()
    {
        Double3 normal = TransferGuidance.OrbitNormal(Station.Position, Station.Velocity);
        Double3 ahead = OnCircle(StationRadius, 30).Position, behind = OnCircle(StationRadius, -30).Position;

        TransferGuidance.TransferAngle(Shuttle.Position, ahead, normal).Should().BeApproximately(Degrees(30), 1e-12);
        TransferGuidance.TransferAngle(Shuttle.Position, behind, normal).Should().BeApproximately(Degrees(330), 1e-12);
    }

    [Fact]
    public void TheArrivalPointLiesAlongTheStationsAxisFromItsPlaceAfterTheTime()
    {
        (Double3 point, Double3 stationVelocity) = Guidance.Arrival(Station.Position, Station.Velocity, Axis, ArrivalDistance, TransferTime);

        (Double3 position, Double3 velocity) = KeplerOrbit.Circular(StationRadius, phase: Degrees(3.8)).StateAt(TransferTime, Mu);
        Double3.Distance(point, position + Axis * ArrivalDistance).Should().BeLessThan(1e-3);
        Double3.Distance(stationVelocity, velocity).Should().BeLessThan(1e-6);
    }

    [Fact]
    public void TheVelocityToGainPutsTheShuttleOnAPathThatReachesTheArrivalPointWhenTheTimeIsUp()
    {
        TransferSteering steering = Guidance.Steer(Shuttle.Position, Shuttle.Velocity, Station.Position, Station.Velocity, Axis, ArrivalDistance, TransferTime);

        (Double3 landing, _) = KeplerOrbit.Propagate(Shuttle.Position, Shuttle.Velocity + steering.ToGain, Mu, TransferTime);
        (Double3 point, _) = Guidance.Arrival(Station.Position, Station.Velocity, Axis, ArrivalDistance, TransferTime);
        Double3.Distance(landing, point).Should().BeLessThan(1e-3);
        steering.ToGain.Length.Should().BeInRange(55, 65, "the departure burn of the design costs 60.0 m/s");
    }

    [Fact]
    public void TheBrakesDirectionOpposesTheShuttlesVelocityRelativeToTheStationAtTheArrivalPoint()
    {
        TransferSteering steering = Guidance.Steer(Shuttle.Position, Shuttle.Velocity, Station.Position, Station.Velocity, Axis, ArrivalDistance, TransferTime);

        (_, Double3 atArrival) = KeplerOrbit.Propagate(Shuttle.Position, Shuttle.Velocity + steering.ToGain, Mu, TransferTime);
        (_, Double3 stationVelocity) = Guidance.Arrival(Station.Position, Station.Velocity, Axis, ArrivalDistance, TransferTime);
        Double3.Distance(steering.BrakeDirection, -(atArrival - stationVelocity).Normalized()).Should().BeLessThan(1e-9);
    }

    /// <summary>The parking circle's velocity is 60 m/s short of the transfer's, so the burn pushes at its limit along it.</summary>
    [Fact]
    public void FarFromTheNeededVelocityTheBurnPushesAtTheBurnAccelerationTowardIt()
    {
        TransferSteering steering = Guidance.Steer(Shuttle.Position, Shuttle.Velocity, Station.Position, Station.Velocity, Axis, ArrivalDistance, TransferTime);

        steering.Burns.Should().BeTrue();
        steering.Push.Length.Should().BeApproximately(TransferGuidance.BurnAcceleration, 1e-12);
        Double3.Distance(steering.Push.Normalized(), steering.ToGain.Normalized()).Should().BeLessThan(1e-12);
    }

    /// <summary>0.05 m/s short of the needed velocity, below the speed tolerance of 0.1 m/s, the trim takes it in 4 s.</summary>
    [Fact]
    public void NearTheNeededVelocityTheTrimTakesTheVelocityToGainInTheTrimTime()
    {
        TransferSteering steering = SteeringShortBy(0.05);

        steering.Burns.Should().BeFalse();
        steering.ToGain.Length.Should().BeApproximately(0.05, 1e-6);
        Double3.Distance(steering.Push, steering.ToGain / TransferGuidance.TrimTime).Should().BeLessThan(1e-15);
    }

    /// <summary>
    /// The longest velocity to gain that the trim takes is the speed tolerance. Its push, that velocity over the trim time,
    /// is at most half <see cref="FrameTree.ZeroGravityThreshold"/>, so the trim leaves the cabin clearly weightless, and a
    /// strapped person who touches its seat does not walk and turn its up to the trim's push.
    /// </summary>
    [Fact]
    public void TheTrimPushesAtMostHalfTheWeightlessThreshold()
    {
        TransferSteering steering = SteeringShortBy(TransferGuidance.SpeedTolerance - 1e-9);

        steering.Burns.Should().BeFalse();
        steering.Push.Length.Should().BeLessThanOrEqualTo(FrameTree.ZeroGravityThreshold / 2);
    }

    [Theory]
    [InlineData(0.09, false)]
    [InlineData(0.11, true)]
    public void TheShuttleBurnsWhileTheVelocityToGainIsLongerThanTheSpeedTolerance(double toGain, bool burns) =>
        new TransferSteering(new Double3(0, 0, toGain), Double3.Zero, Double3.UnitX).Burns.Should().Be(burns);

    /// <summary>The steering of the demo's window for a shuttle whose velocity lies <paramref name="short"/> m/s short of the needed velocity, along the velocity to gain.</summary>
    private static TransferSteering SteeringShortBy(double @short)
    {
        TransferSteering first = Guidance.Steer(Shuttle.Position, Shuttle.Velocity, Station.Position, Station.Velocity, Axis, ArrivalDistance, TransferTime);
        Double3 velocity = Shuttle.Velocity + first.ToGain - first.ToGain.Normalized() * @short;
        return Guidance.Steer(Shuttle.Position, velocity, Station.Position, Station.Velocity, Axis, ArrivalDistance, TransferTime);
    }
}
