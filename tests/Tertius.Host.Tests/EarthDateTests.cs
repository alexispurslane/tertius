using Tertius.Demo;
using EarthDate = Tertius.Demo.DemoScenes.EarthDate;

namespace Tertius.Host.Tests;

/// <summary>
/// The date of the Earth system (docs/plans/blue-danube-demo.md, "The date and the ground's turn"): where Earth stands on
/// its orbit at the world's time 0, and how far the ground, <c>earth-surface</c>, is turned then. Pure math, with no World.
/// </summary>
public sealed class EarthDateTests
{
    /// <summary>The dot product of two quaternions: ±1 for the same rotation.</summary>
    private static double Dot(DQuaternion a, DQuaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    /// <summary>
    /// A date's ground stands at time 0 turned by the date's ground turn about +Y, earth's north pole. A quarter of a
    /// sidereal day later it has turned a quarter turn more, and a whole sidereal day later it stands as at time 0.
    /// </summary>
    [Fact]
    public void ADatesGroundStartsAtItsTurnAndTurnsAtTheSiderealRate()
    {
        var date = new EarthDate(EarthDate.JuneSolstice.Longitude, GroundTurn: 1.0);
        double day = 2 * Math.PI / DemoScenes.SiderealRate;

        Math.Abs(Dot(date.Ground.RotationAt(0), DQuaternion.FromAxisAngle(Double3.UnitY, 1.0)))
            .Should()
            .BeApproximately(1, 1e-12, "the ground starts at its turn");
        Math.Abs(Dot(date.Ground.RotationAt(day / 4), DQuaternion.FromAxisAngle(Double3.UnitY, 1.0 + Math.PI / 2)))
            .Should()
            .BeApproximately(1, 1e-12, "the ground turns a quarter turn in a quarter of a day");
        Math.Abs(Dot(date.Ground.RotationAt(day), DQuaternion.FromAxisAngle(Double3.UnitY, 1.0)))
            .Should()
            .BeApproximately(1, 1e-12, "the ground turns once per sidereal day");
        (date.Ground.AngularVelocity - Double3.UnitY * DemoScenes.SiderealRate)
            .Length.Should()
            .BeLessThan(1e-15, "the ground turns about the pole");
    }

    /// <summary>
    /// The March equinox and the June solstice keep the ground's turn 0, so the scenes that take them, the orbit scene, the
    /// station scene and their tests, keep the ground of the time before the date had a ground turn.
    /// </summary>
    [Fact]
    public void TheEquinoxAndTheSolsticeKeepTheGroundTurnZero()
    {
        EarthDate.MarchEquinox.GroundTurn.Should().Be(0);
        EarthDate.JuneSolstice.GroundTurn.Should().Be(0);
    }
}
