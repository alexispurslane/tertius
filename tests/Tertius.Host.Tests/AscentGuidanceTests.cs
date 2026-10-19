using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>
/// AscentGuidance: the steering law of the shuttle's climb, for Earth's G·M, a circle 200 km above Earth's mean radius,
/// at a radius of 6,571 km, and an engine of three gravities, 29.43 m/s². The circle's speed there is 7,788.49 m/s and
/// its gravity 9.2316 m/s². The planet's center is the origin, and the ships here lie on +Y, so their up is +Y.
/// </summary>
public sealed class AscentGuidanceTests
{
    private const double Mu = 3.986004418e14, OrbitRadius = 6.571e6, Full = 3 * 9.81;

    private static readonly AscentGuidance Guidance = new(Mu, OrbitRadius, Full);

    [Fact]
    public void TheCirclesSpeedIsThatOfACircularOrbit() =>
        Guidance.CircleSpeed.Should().BeApproximately(7788.488, 0.001);

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    public void AShipWithNoSpeedAlongTheGroundIsToldToClimbStraightUpAtFullThrottle(double climb)
    {
        Double3 a = Guidance.Acceleration(new Double3(0, 6.371e6, 0), new Double3(0, climb, 0));

        a.Y.Should().BeApproximately(Full, 1e-9);
        a.X.Should().Be(0);
        a.Z.Should().Be(0);
    }

    /// <summary>
    /// On the circle with no vertical speed and 2 m/s short of its speed, the thrust takes the 2 m/s in the trim time of
    /// 4 s, 0.5 m/s² along the track, and holds the height against the gravity less the lift of the speed, 0.00474 m/s².
    /// </summary>
    [Fact]
    public void NearTheEndTheThrustTakesTheSpeedThatIsLeftInTheTrimTime()
    {
        Double3 a = Guidance.Acceleration(new Double3(0, OrbitRadius, 0), new Double3(0, 0, -(7788.488 - 2)));

        a.Z.Should().BeApproximately(-0.5, 1e-3);
        a.Y.Should().BeApproximately(0.00474, 1e-5);
        a.X.Should().Be(0);
    }

    /// <summary>Low and slow, fast and low, high and rising: the law never asks for more than full throttle.</summary>
    [Theory]
    [InlineData(6.3711e6, 60, -100)]
    [InlineData(6.40e6, 900, -1500)]
    [InlineData(6.55e6, 300, -7000)]
    [InlineData(6.40e6, -400, -7700)]
    public void TheThrustNeverExceedsFullThrottle(double radius, double climb, double alongTrack)
    {
        Double3 a = Guidance.Acceleration(new Double3(0, radius, 0), new Double3(0, climb, alongTrack));

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
        Double3 a = Guidance.Acceleration(new Double3(0, 6.55e6, 0), new Double3(0, 0, -2000));

        a.Y.Should().BeGreaterThan(9.29);
        a.Z.Should().BeLessThan(0, "the rest of the thrust goes along the track");
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
    public void TheClimbIsDoneOnceTheSpeedAlongTheTrackAndTheVerticalSpeedAreThoseOfTheCircle(double climb, double alongTrack, bool done) =>
        Guidance.Reached(new Double3(0, OrbitRadius, 0), new Double3(0, climb, -alongTrack)).Should().Be(done);
}
