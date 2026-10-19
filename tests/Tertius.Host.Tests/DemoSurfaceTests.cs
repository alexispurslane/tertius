using Tertius.Demo;
using Tertius.Host.Scripting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo surface: one headless run of the checked-in script scripts/demo-surface.json. The person's walker walks it
/// from 9 m outside the terminal's entrance through the hall, out of the gate and across the pad to the boarding point
/// outside the parked shuttle's bubble: 30 m at 3 m/s, which takes about 11 s with the slowdown at the end. The script
/// dumps the state in the hall at tick 314, on the pad at tick 544, and at the end at tick 780, after the person stopped.
/// Every test of the class reads that one run.
/// </summary>
public sealed class DemoSurfaceRun() : CheckedInRun("demo-surface");

[Collection(GpuCollection.Name)]
public sealed class DemoSurfaceTests(DemoSurfaceRun run) : IClassFixture<DemoSurfaceRun>
{
    private StateDump Dump(string name) => run.Result.Dump(name);

    private static double Across(Double3 a, Double3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    [Fact]
    public void ThePersonWalksThroughTheTerminal()
    {
        EntityRecord person = Dump("terminal").Entity("person");
        Double3 inHall = person.Pose.Position - DemoSurface.TerminalCenter;

        person.Pose.Frame.Should().Be("earth-surface");
        person.Standing.Should().BeTrue();
        Math.Abs(inHall.X).Should().BeLessThan(DemoSurface.TerminalHalf.X - 0.6, "the person is in the hall, away from its walls");
        Math.Abs(inHall.Z).Should().BeLessThan(DemoSurface.TerminalHalf.Z - 0.6, "the person is in the hall, away from its walls");
    }

    [Fact]
    public void ThePersonCrossesThePadTowardTheShuttle()
    {
        EntityRecord person = Dump("apron").Entity("person");

        person.Pose.Frame.Should().Be("earth-surface");
        person.Standing.Should().BeTrue();
        person.Pose.Position.X.Should().BeInRange(DemoSurface.BoardingPoint.X, DemoSurface.TerminalCenter.X - DemoSurface.TerminalHalf.X, "the person is between the gate and the boarding point");
    }

    [Fact]
    public void ThePersonStopsAtTheBoardingPointOutsideTheShuttlesBubble()
    {
        EntityRecord person = Dump("end").Entity("person");

        person.Pose.Frame.Should().Be("earth-surface");
        person.Standing.Should().BeTrue();
        Across(person.Pose.Position, DemoSurface.BoardingPoint).Should().BeLessThanOrEqualTo(Walker.StopRadius + 0.01);
        person.Velocity.Length.Should().BeLessThan(0.01);
    }

    [Fact]
    public void TheShuttleStaysParkedWhereItSpawned()
    {
        StateDump end = Dump("end");

        end.Frames.Single(frame => frame.Id == "shuttle").Follows.Should().Be("static");
        Double3.Distance(end.Entity("shuttle").Pose.Position, new Double3(0, DemoScenes.EarthRadius + Shuttle.Hull.Y, 0)).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void TheWorldLogsNoErrors()
    {
        Dump("end").Errors.Should().BeEmpty();
        run.ValidationMessages.Should().BeEmpty();
    }

    [Fact(Skip = "Waits for ticket ter-imi5, point 1: a static body that is not a hull gets no ghost in a child frame's bubble, so the person falls through the slab when it enters the shuttle's bubble. Then the walker's path goes on into the cabin, and the script runs on to the dump 'seated'.")]
    public void ThePersonEndsInTheShuttlesFrameAtTheSeat()
    {
        EntityRecord person = Dump("seated").Entity("person");

        person.Pose.Frame.Should().Be("shuttle");
        person.Standing.Should().BeTrue();
        Across(person.Pose.Position, Shuttle.SeatStand).Should().BeLessThanOrEqualTo(Walker.StopRadius + 0.01);
        Dump("seated").Errors.Should().BeEmpty();
    }
}
