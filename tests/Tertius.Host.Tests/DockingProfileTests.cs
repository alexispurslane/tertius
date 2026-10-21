using Tertius.Demo;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;

namespace Tertius.Host.Tests;

/// <summary>
/// The profile of the docking's spin-up, <see cref="SpinUpProfile"/> (docs/plans/blue-danube-demo.md, "The spin-up and the
/// roll"), as pure math, in the station's axes. The spin-up turns the shuttle about the station's axis, +Y, from still among
/// the stars to the station's rate in 30 s, with a constant angular acceleration. Its attitude target trails the docked
/// rotation by the lag λ(τ) = ω (T - τ)² / (2T), which falls to zero. The roll is folded into the start: the spin-up starts
/// in the one tick of each turn of the station in which the shuttle's roof lies within half a tick's turn of the roof of
/// the spin-up's first target.
/// </summary>
public sealed class DockingProfileTests
{
    private const double Rate = DemoScenes.SpinStation.Rate, Duration = 30, Tick = 1.0 / 60;

    private static readonly SpinUpProfile Profile = new(Rate, Duration, DemoStation.DockedRotation);

    /// <summary>
    /// The lag starts at ω T / 2, 269.2 degrees for the station's rate and 30 s, falls as the square of the time left, and
    /// is zero from the end of the spin-up on.
    /// </summary>
    [Fact]
    public void TheLagFallsFromHalfTheStationsTurnInTheSpinUpTimeToZero()
    {
        (Profile.Lag(0) * 180 / Math.PI).Should().BeApproximately(269.2, 0.1);
        Profile.Lag(0).Should().BeApproximately(Rate * Duration / 2, 1e-12);
        Profile.Lag(Duration / 2).Should().BeApproximately(Rate * Duration / 8, 1e-12);
        Profile.Lag(Duration).Should().Be(0);
        Profile.Lag(Duration + 5).Should().Be(0);
        Profile.Target(Duration).AngularVelocityTo(DemoStation.DockedRotation, 1).Length.Should().BeLessThan(1e-12, "at the end the target is the docked rotation");
    }

    /// <summary>
    /// The feed-forward, the target's turn about +Y relative to the station, is -ω at the start, so the target holds still
    /// among the stars as the hull does. It is zero at the end, at rest in the station. In between, the turn relative to the
    /// stars, ω plus the feed-forward, grows in proportion to the time: a constant angular acceleration of ω / T, 0.0104
    /// rad/s². It is the derivative of the lag, so the target's rotation and its feed-forward agree.
    /// </summary>
    [Fact]
    public void TheFeedForwardRisesSteadilyFromStillAmongTheStarsToTheStationsRate()
    {
        Profile.TurnRate(0).Should().BeApproximately(-Rate, 1e-15);
        Profile.TurnRate(Duration).Should().Be(0);
        Profile.TurnRate(Duration + 5).Should().Be(0);
        foreach (double elapsed in new[] { 1.0, 7.5, 15, 22.5, 29 })
        {
            (Rate + Profile.TurnRate(elapsed)).Should().BeApproximately(Rate * elapsed / Duration, 1e-15, "the turn among the stars grows steadily");
            double derivative = (Profile.Lag(elapsed + 1e-4) - Profile.Lag(elapsed - 1e-4)) / 2e-4;
            Profile.TurnRate(elapsed).Should().BeApproximately(derivative, 1e-8, "the feed-forward is the derivative of the lag");
        }
        (Rate / Duration).Should().BeApproximately(0.0104, 0.0001);
    }

    /// <summary>
    /// At the hold, the shuttle does not turn among the stars, so in the station's axes its roof turns backward at ω, and
    /// the roll angle to the spin-up's first target grows by ω times a tick in each tick. From several starting phases, the
    /// window catches exactly one tick in each pass of the angle through zero: in 5,000 ticks it catches 4 or 5, never two
    /// ticks in a row, and between two catches lie the ticks of one turn of the station.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2.5)]
    [InlineData(-3)]
    public void TheRollWindowCatchesExactlyOneTickInEachTurnOfTheStation(double phase)
    {
        DQuaternion first = Profile.Target(0);
        var caught = new List<int>();

        for (int tick = 0; tick < 5000; tick++)
        {
            DQuaternion hull = DQuaternion.FromAxisAngle(Double3.UnitY, phase - Rate * Tick * tick) * first;
            if (Profile.InRollWindow(Profile.RollAngle(hull), Tick))
                caught.Add(tick);
        }

        caught.Count.Should().BeInRange(4, 5);
        double turn = 2 * Math.PI / (Rate * Tick);
        caught.Zip(caught.Skip(1), (a, b) => b - a).Should().OnlyContain(gap => gap >= Math.Floor(turn) && gap <= Math.Ceiling(turn));
    }

    /// <summary>
    /// The roll angle is the angle about +Y from the hull's roof to the first target's roof, both across the axis, from -π
    /// to π. A hull turned by 0.3 radians backward about the axis from the first target has a roll angle of 0.3, and its nose
    /// does not change it.
    /// </summary>
    [Fact]
    public void TheRollAngleIsTheTurnAboutTheAxisFromTheHullsRoofToTheFirstTargetsRoof()
    {
        DQuaternion first = Profile.Target(0);

        Profile.RollAngle(DQuaternion.FromAxisAngle(Double3.UnitY, -0.3) * first).Should().BeApproximately(0.3, 1e-12);
        Profile.RollAngle(DQuaternion.FromAxisAngle(Double3.UnitY, 3) * first).Should().BeApproximately(-3, 1e-12);
        Profile.RollAngle(first).Should().BeApproximately(0, 1e-12);
        Profile.RollAngle(DQuaternion.FromAxisAngle(Double3.UnitX, 0.01) * first).Should().BeApproximately(0, 1e-3, "a tilt of the nose leaves the roll");
    }
}
