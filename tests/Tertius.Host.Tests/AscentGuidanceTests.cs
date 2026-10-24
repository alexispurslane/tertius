using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>
/// AscentGuidance: the steering law of the shuttle's climb, for Earth's G·M, a circle 200 km above Earth's mean radius,
/// at a radius of 6,571 km, and an engine of three gravities, 29.43 m/s². The circle's speed there is 7,788.49 m/s and
/// its gravity 9.2316 m/s². The planet's center is the origin. The launch plane is that of the demo: it holds +Y and -Z,
/// and its normal is -X, so a ship on +Y has its up on +Y and its track along -Z.
/// </summary>
public sealed class AscentGuidanceTests
{
    private const double Mu = 3.986004418e14, OrbitRadius = 6.571e6, Full = 3 * 9.81;

    private static readonly Double3 PlaneNormal = -Double3.UnitX;

    private static readonly AscentGuidance Guidance = new(Mu: Mu, OrbitRadius: OrbitRadius, MaxAcceleration: Full, PlaneNormal: PlaneNormal);

    [Fact]
    public void TheCirclesSpeedIsThatOfACircularOrbit() =>
        Guidance.CircleSpeed.Should().BeApproximately(7788.488, 0.001);

    /// <summary>
    /// A ship at rest on the ground, or rising straight up, still has a track: the direction in the launch plane across its
    /// up. So it is told to climb and to gain speed along the track, with nothing across the plane.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    public void AShipWithNoSpeedAlongTheTrackIsToldToGainItInTheLaunchPlane(double climb)
    {
        Double3 a = Guidance.Acceleration(new(0, 6.371e6, 0), new(0, climb, 0));

        a.Y.Should().BeGreaterThan(0, "the ship climbs");
        a.Z.Should().BeLessThan(0, "the ship gains speed along the track");
        a.X.Should().Be(0);
        a.Length.Should().BeApproximately(Full, 1e-9);
    }

    /// <summary>
    /// On the circle with no vertical speed and 2 m/s short of its speed, the thrust takes the 2 m/s in the trim time of
    /// 4 s, 0.5 m/s² along the track, and holds the height against the gravity less the lift of the speed, 0.00474 m/s².
    /// </summary>
    [Fact]
    public void NearTheEndTheThrustTakesTheSpeedThatIsLeftInTheTrimTime()
    {
        Double3 a = Guidance.Acceleration(new(0, OrbitRadius, 0), new(0, 0, -(7788.488 - 2)));

        a.Z.Should().BeApproximately(-0.5, 1e-3);
        a.Y.Should().BeApproximately(0.00474, 1e-5);
        a.X.Should().Be(0);
    }

    /// <summary>
    /// Low and slow, fast and low, high and rising, and drifting across the plane: the law never asks for more than full
    /// throttle.
    /// </summary>
    [Theory]
    [InlineData(6.3711e6, 60, -100, 0)]
    [InlineData(6.40e6, 900, -1500, 0)]
    [InlineData(6.55e6, 300, -7000, 0)]
    [InlineData(6.40e6, -400, -7700, 0)]
    [InlineData(6.55e6, 0, -7700, 300)]
    [InlineData(OrbitRadius, 0, -7700, -300)]
    public void TheThrustNeverExceedsFullThrottle(double radius, double climb, double alongTrack, double acrossSpeed)
    {
        Double3 a = Guidance.Acceleration(new(0, radius, 0), new(acrossSpeed, climb, alongTrack));

        a.Length.Should().BeLessThanOrEqualTo(Full + 1e-9);
        a.Length.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// A ship 21 km below the circle that does not climb, at 2 km/s along the track, is told to thrust upward harder than
    /// the gravity there, 9.29 m/s², and to put the rest of the thrust along the track.
    /// </summary>
    [Fact]
    public void AShipBelowTheCircleIsToldToClimb()
    {
        Double3 a = Guidance.Acceleration(new(0, 6.55e6, 0), new(0, 0, -2000));

        a.Y.Should().BeGreaterThan(9.29);
        a.Z.Should().BeLessThan(0, "the rest of the thrust goes along the track");
    }

    /// <summary>
    /// On the circle, 88.5 m/s short of its speed, the time to go is the shortest, 8 s. A ship in the plane that drifts
    /// toward +X at 5 m/s, away from the plane's normal, is told to take that speed out in the time to go: 4 × 5 m/s / 8 s
    /// = 2.5 m/s² along the normal, toward -X.
    /// </summary>
    [Fact]
    public void AShipThatDriftsAcrossTheLaunchPlaneIsToldToPushBackTowardIt()
    {
        Double3 a = Guidance.Acceleration(new(0, OrbitRadius, 0), new(5, 0, -7700));

        a.X.Should().BeApproximately(-2.5, 1e-9);
    }

    /// <summary>
    /// On the circle, 88.5 m/s short of its speed, the time to go is the shortest, 8 s. A ship 10 m from the plane on the
    /// side of +X, with no speed across it, is told to reach the plane at the end of the time to go: 6 × 10 m / (8 s)² =
    /// 0.9375 m/s² across the plane, toward -X. The ship's up leans 1.5e-6 radians from the plane, so the part across the
    /// plane is across the up too, and the push along the normal is 0.9375 m/s² to within 1e-6 m/s².
    /// </summary>
    [Fact]
    public void AShipBesideTheLaunchPlaneIsToldToReturnToIt()
    {
        (Double3 position, Double3 velocity) = OnTheTrack(radius: OrbitRadius, across: 10, climb: 0, alongTrack: 7700);

        Double3 a = Guidance.Acceleration(position, velocity);

        Double3.Dot(a, PlaneNormal).Should().BeApproximately(0.9375, 1e-6);
    }

    /// <summary>
    /// A ship beside the plane, with no speed across it, is told to push up or down exactly as its twin in the plane, at
    /// the same height with the same speeds: 171 km below the circle and falling, at full throttle upward with no thrust left
    /// for the plane, 21 km below it, and near the circle, where the plane would take more than the whole thrust.
    /// </summary>
    [Theory]
    [InlineData(6.4e6, -100, 1000, 10)]
    [InlineData(6.55e6, 0, 2000, 10)]
    [InlineData(OrbitRadius, 0, 7700, 1000)]
    [InlineData(6.56e6, 20, 7000, -50)]
    public void ThePushAcrossThePlaneNeverTakesThrustFromTheClimb(double radius, double climb, double alongTrack,
        double across)
    {
        (Double3 position, Double3 velocity) = OnTheTrack(radius: radius, across: across, climb: climb, alongTrack: alongTrack);
        (Double3 twinPosition, Double3 twinVelocity) = OnTheTrack(radius: radius, across: 0, climb: climb, alongTrack: alongTrack);

        Double3 a = Guidance.Acceleration(position, velocity), twin = Guidance.Acceleration(twinPosition, twinVelocity);

        Double3.Dot(a, position.Normalized()).Should()
            .BeApproximately(Double3.Dot(twin, twinPosition.Normalized()), 1e-9);
    }

    /// <summary>
    /// A ship <paramref name="radius"/> from the planet's center and <paramref name="across"/> meters from the plane on the
    /// side of +X, the side away from its normal, that climbs at <paramref name="climb"/> and moves along its track at
    /// <paramref name="alongTrack"/>, with no speed across the plane.
    /// </summary>
    private static (Double3 Position, Double3 Velocity) OnTheTrack(double radius, double across, double climb,
        double alongTrack)
    {
        var position = new Double3(across, Math.Sqrt(radius * radius - across * across), 0);
        Double3 up = position.Normalized(), track = Double3.Cross(PlaneNormal, up).Normalized();
        return (position, up * climb + track * alongTrack);
    }

    [Theory]
    [InlineData(0, 7788.488, true)]
    [InlineData(0, 7788.46, true)]
    [InlineData(0, 7790, true)]
    [InlineData(0, 7788.3, true)]
    [InlineData(0, 7788.2, false)]
    [InlineData(0.4, 7788.488, true)]
    [InlineData(-0.6, 7788.488, false)]
    [InlineData(0.6, 7788.488, false)]
    public void TheClimbIsDoneOnceTheSpeedAlongTheTrackAndTheVerticalSpeedAreThoseOfTheCircle(double climb,
        double alongTrack, bool done) =>
        Guidance.Reached(new(0, OrbitRadius, 0), new(0, climb, -alongTrack)).Should().Be(done);
}
