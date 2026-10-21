using Tertius.Demo;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using SpinStation = Tertius.Demo.DemoScenes.SpinStation;
using StationExit = Tertius.Demo.DemoScenes.StationExit;
using WheelCoda = Tertius.Demo.DemoScenes.WheelCoda;

namespace Tertius.Host.Tests;

/// <summary>
/// The places of the coda on the wheel's floor (docs/plans/blue-danube-demo.md, "The coda"), from the scene's constants,
/// with no World: the stack, the stand, the lane of the throws, the marks and the cameras.
/// </summary>
public sealed class WheelCodaGeometryTests
{
    private static WheelFloor Floor => DemoStation.WheelFloor;

    private const double VerticalFov = Math.PI / 3;

    /// <summary>The predicted landing of throw <paramref name="index"/> of the design: where the straight line of its release lands it.</summary>
    private static ThrowLanding Landing(int index) => WheelCoda.DesignRelease(index, WheelCoda.PersonBody).Landing(SpinStation.Rate, Floor, WheelCoda.BoxHalf);

    [Fact]
    public void TheStackStandsOnTheFloorWithItsMiddleInThePersonsWayAndItsSidesClearOfIt()
    {
        (string Id, DPose Pose)[] stack = [.. WheelCoda.Stack()];
        stack.Should().HaveCount(6);
        stack.Take(3).Should().OnlyContain(box => Math.Abs(Floor.Height(box.Pose.Position) - WheelCoda.BoxHalf) < 1e-9, "the lowest row rests on the floor");
        Waypoint[] shove = WheelCoda.Shove(default);
        double Across(Double3 place) => Floor.Spinward(shove[^1].Position, place);
        Math.Abs(Across(stack[1].Pose.Position)).Should().BeLessThan(1e-9, "the person walks into the middle box");
        foreach (int side in new[] { 0, 2 })
            Math.Abs(Across(stack[side].Pose.Position)).Should().BeGreaterThan(WheelCoda.BoxHalf + StationExit.PersonRadius, "the side boxes stand clear of the person's way");
        shove[^1].Position.Y.Should().BeGreaterThan(WheelCoda.StackY + WheelCoda.BoxHalf, "the walk goes through the stack");
    }

    /// <summary>
    /// The throws' lane runs 2 m from the line of the chute's trough, whose rails reach 0.7 m from the middle, so a box
    /// 0.87 m across its corners passes 0.86 m clear. Each throw starts and lands inside the lit stretch of the floor round
    /// the chute's end, z from -12 to 16 m, and passes below the ceiling, 5 m above the floor.
    /// </summary>
    [Fact]
    public void TheDesignsThrowsRunInTheLaneUnderTheCeilingInTheLight()
    {
        (Math.Abs(WheelCoda.LaneY) - StationExit.Chute.HalfWidth - StationExit.PlateThickness - WheelCoda.BoxHalf * Math.Sqrt(3)).Should().BeGreaterThan(0.8);
        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            ThrowRelease release = WheelCoda.DesignRelease(i, WheelCoda.PersonBody);
            ThrowLanding landing = Landing(i);
            double apex = Enumerable.Range(0, (int)(landing.Time * 1000)).Max(ms => Floor.Height(release.PlaceAt(ms / 1000.0, SpinStation.Rate)));
            apex.Should().BeLessThan(5 - 0.5 - WheelCoda.BoxHalf * Math.Sqrt(3), $"throw {i} passes below the ceiling");
            landing.Place.Z.Should().BeInRange(-12, 16);
            release.Place.Z.Should().BeInRange(-12, 16);
        }
    }

    /// <summary>The boxes to throw rest in front of the stand, clear of the person's capsule and within its reach, and clear of the lane.</summary>
    [Fact]
    public void TheBoxesToThrowRestInFrontOfTheStandWithinReach()
    {
        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            Double3 box = WheelCoda.ThrowBoxPose(i).Position;
            double apart = Math.Sqrt(Math.Pow(Floor.Spinward(WheelCoda.Stand, box), 2) + Math.Pow(box.Y - WheelCoda.Stand.Y, 2));
            apart.Should().BeInRange(StationExit.PersonRadius + WheelCoda.BoxHalf * Math.Sqrt(2) + 0.1, 1.2);
            Floor.Height(box).Should().BeApproximately(WheelCoda.BoxHalf, 1e-9);
        }
    }

    /// <summary>
    /// The person faces -Y on the floor, as the exit leaves it, so a box at the side of the person's way to the stand does
    /// not lie on that way: the walk to the stand ends at the stand, and both boxes lie beyond it.
    /// </summary>
    [Fact]
    public void TheWalkToTheStandEndsBeforeTheBoxesToThrow()
    {
        Waypoint[] toStand = WheelCoda.ToStand(default);
        toStand[^1].Position.Should().Be(WheelCoda.Stand);
        for (int i = 0; i < BoxPlayState.Throws; i++)
            WheelCoda.ThrowBoxPose(i).Position.Y.Should().BeLessThan(WheelCoda.Stand.Y - StationExit.PersonRadius - WheelCoda.BoxHalf);
    }

    /// <summary>Each mark lies on the floor where a flat field of ω² R lands the design's throw.</summary>
    [Fact]
    public void EachMarkLiesOnTheFloorWhereAFlatFieldLandsTheDesignsThrow()
    {
        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            ThrowRelease release = WheelCoda.DesignRelease(i, WheelCoda.PersonBody);
            Double3 mark = WheelCoda.FlatMark(i, WheelCoda.PersonBody);
            Floor.Height(mark).Should().BeApproximately(0, 1e-9);
            Floor.Spinward(release.Place, mark).Should().BeApproximately(release.FlatLanding(SpinStation.Rate * SpinStation.Rate * Floor.Radius, Floor, WheelCoda.BoxHalf).Spinward, 1e-9);
            mark.Y.Should().Be(WheelCoda.LaneY);
        }
    }

    /// <summary>
    /// The cameras see their places, with a tenth of the half picture to spare: <c>stack-camera</c> the stack and the
    /// person's way into it, and each throw's camera the person at the stand, the throw's release, its predicted landing
    /// and its mark.
    /// </summary>
    [Fact]
    public void TheCamerasSeeTheirPlaces()
    {
        void Sees(DPose view, Double3 point, string what)
        {
            Double3 at = Picture.Project(view, point, VerticalFov);
            (at.Z > 0.5 && Math.Abs(at.X) <= 0.9 && Math.Abs(at.Y) <= 0.9).Should().BeTrue($"the camera sees {what} at {at}");
        }
        foreach ((string id, DPose pose) in WheelCoda.Stack())
            Sees(WheelCoda.StackCamera, pose.Position, id);
        foreach (Waypoint waypoint in WheelCoda.Shove(default))
            Sees(WheelCoda.StackCamera, waypoint.Position, "the walk into the stack");
        DPose[] throws = [WheelCoda.SpinwardCamera, WheelCoda.AntispinwardCamera];
        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            Sees(throws[i], WheelCoda.Stand, "the person at the stand");
            Sees(throws[i], WheelCoda.Stand + WheelFloor.UpAt(WheelCoda.Stand) * 0.9, "the person's head at the stand");
            Sees(throws[i], WheelCoda.DesignRelease(i, WheelCoda.PersonBody).Place, $"the release of throw {i}");
            Sees(throws[i], Landing(i).Place, $"the landing of throw {i}");
            Sees(throws[i], WheelCoda.FlatMark(i, WheelCoda.PersonBody), $"the mark of throw {i}");
        }
    }
}
