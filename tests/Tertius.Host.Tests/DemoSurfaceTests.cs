using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo surface: one headless run of the checked-in script scripts/demo-surface.json. The person's walker walks it
/// from 9 m outside the terminal's entrance through the hall, out of the gate, across the pad, through the parked
/// shuttle's hatch and along the cabin to the place at the seat: about 44 m at 3 m/s, which takes about 16 s with the
/// slowdown at the end. The script dumps the state in the hall ("terminal"), on the pad ("apron"), in the hatch
/// ("boarding"), at the seat after the person stopped ("at-the-seat"), and two seconds later ("end"). Every test of the
/// class reads that one run.
/// </summary>
public sealed class DemoSurfaceRun() : CheckedInRun("demo-surface");

public sealed class DemoSurfaceTests(DemoSurfaceRun run) : IClassFixture<DemoSurfaceRun>
{
    private StateDump Dump(string name) => run.Result.Dump(name);

    private static double Across(Double3 a, Double3 b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    [Fact]
    public void ThePersonWalksThroughTheTerminal()
    {
        EntityRecord person = Dump("terminal").Entity("person");
        Double3 inHall = person.Pose.Position - DemoSurface.TerminalCenter;

        person.Pose.Frame.Should().Be("earth-surface");
        person.Standing.Should().BeTrue();
        Math.Abs(inHall.X).Should().BeLessThan(DemoSurface.TerminalHalf.X - 0.6,
            "the person is in the hall, away from its walls");
        Math.Abs(inHall.Z).Should().BeLessThan(DemoSurface.TerminalHalf.Z - 0.6,
            "the person is in the hall, away from its walls");
    }

    /// <summary>The shuttle stands unturned at the origin, so the x of earth-surface is the x of the shuttle's axes.</summary>
    [Fact]
    public void ThePersonCrossesThePadTowardTheShuttle()
    {
        EntityRecord person = Dump("apron").Entity("person");

        person.Pose.Frame.Should().Be("earth-surface");
        person.Standing.Should().BeTrue();
        person.Pose.Position.X.Should().BeInRange(Shuttle.OutsideHatch.X,
            DemoSurface.TerminalCenter.X - DemoSurface.TerminalHalf.X, "the person is between the gate and the hatch");
    }

    /// <summary>
    /// The person steps through the hatch in the shuttle's frame: between 1 m outside the hull's starboard side and the
    /// waypoint inside the hatch, with its whole capsule, 0.3 m in radius, within the hatch's width of 1.2 m.
    /// </summary>
    [Fact]
    public void ThePersonWalksThroughTheHatchInTheShuttlesFrame()
    {
        EntityRecord person = Dump("boarding").Entity("person");

        person.Pose.Frame.Should().Be("shuttle");
        person.Pose.Position.X.Should().BeInRange(Shuttle.InsideHatch.X, Shuttle.Hull.X + 1);
        Math.Abs(person.Pose.Position.Z - Shuttle.Hatch.Center.Z).Should().BeLessThan(Shuttle.Hatch.Extents.Z - 0.3);
    }

    [Fact]
    public void TheShuttleStaysParkedWhereItSpawned()
    {
        StateDump end = Dump("end");

        end.Frames.Single(frame => frame.Id == "shuttle").Follows.Should().Be("static");
        Double3.Distance(end.Entity("shuttle").Pose.Position, DemoSurface.ShuttleParked.Position).Should()
            .BeLessThan(1e-9);
    }

    [Fact]
    public void TheWorldLogsNoErrors()
    {
        Dump("end").Errors.Should().BeEmpty();
        run.ValidationMessages.Should().BeEmpty();
    }

    /// <summary>The person stands still on the cabin floor at the seat: its capsule's center 0.9 m above the floor.</summary>
    [Fact]
    public void ThePersonEndsInTheShuttlesFrameAtTheSeat()
    {
        EntityRecord person = Dump("at-the-seat").Entity("person");

        person.Pose.Frame.Should().Be("shuttle");
        person.Standing.Should().BeTrue();
        Across(person.Pose.Position, Shuttle.SeatStand).Should().BeLessThanOrEqualTo(Walker.StopRadius + 0.01);
        person.Pose.Position.Y.Should()
            .BeApproximately(Shuttle.SeatStand.Y, 0.05, "the person stands on the cabin floor");
        person.Velocity.Length.Should().BeLessThan(0.01);
        Dump("at-the-seat").Errors.Should().BeEmpty();
    }

    [Fact]
    public void ThePersonStaysAtTheSeat()
    {
        EntityRecord atTheSeat = Dump("at-the-seat").Entity("person");
        EntityRecord end = Dump("end").Entity("person");

        end.Pose.Frame.Should().Be("shuttle");
        end.Standing.Should().BeTrue();
        Double3.Distance(end.Pose.Position, atTheSeat.Pose.Position).Should().BeLessThan(0.005);
    }
}

/// <summary>
/// The scene <c>demo-surface</c> in the Earth system (docs/plans/blue-danube-demo.md, "The demo in the Earth system"),
/// built without a script, on the World and the real Jolt backend: the ground, the shuttle and the person start in the
/// turning <c>earth-surface</c> at the north pole, and the ground's turn puts the ramp in the station's plane at the
/// departure.
/// </summary>
public sealed class DemoSurfaceSceneTests : IDisposable
{
    private readonly World world = HostTesting.NewJoltWorld();
    private readonly SceneInstance scene;
    private readonly FrameHandle sun, earth, surface;

    public DemoSurfaceSceneTests()
    {
        scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(world, RenderResources.None);
        sun = world.GetOwnedFrame(scene["sun"]);
        earth = world.GetOwnedFrame(scene["earth"]);
        surface = world.GetOwnedFrame(scene["earth-surface"]);
    }

    public void Dispose() => world.Dispose();

    /// <summary>
    /// The slab, the terminal, the ramp, the tower, the shuttle and the person are in earth-surface, which is in earth,
    /// which is in sun. The parked shuttle stands over the pole, the axis of the ground's turn.
    /// </summary>
    [Fact]
    public void TheGroundAndTheShuttleStartInEarthSurfaceAtTheNorthPole()
    {
        foreach (string id in new[] { "floor", "terminal", "ramp", "tower", "shuttle", "person" })
            world.GetFrame(scene[id]).Should().Be(surface, $"the {id} starts in earth-surface");
        world.Frames.Parent(surface).Should().Be(earth);
        world.Frames.Parent(earth).Should().Be(sun);
        DPose parked = world.PoseIn(scene["shuttle"], surface);
        Double3.Distance(parked.Position, DemoSurface.ShuttleParked.Position).Should().BeLessThan(1e-9);
        Math.Sqrt(parked.Position.X * parked.Position.X + parked.Position.Z * parked.Position.Z).Should()
            .BeLessThan(1e-9, "the shuttle stands on the axis of the turn");
    }

    /// <summary>
    /// The world runs to the departure, <see cref="DemoSurface.DepartureAt"/>. Then the ramp's incline, turned by
    /// earth-surface's rotation in earth, lies within 0.001 degrees of the station's plane, the launch plane.
    /// </summary>
    [Fact]
    public void EarthSurfaceTurnsSoThatTheRampLiesInTheStationsPlaneAtTheDeparture()
    {
        int ticks = (int)Math.Round(DemoSurface.DepartureAt / world.DeltaTime);
        for (int tick = 0; tick < ticks; tick++)
            world.Tick();

        world.Time.Should().BeApproximately(DemoSurface.DepartureAt, 1e-5);
        Double3 incline = world.PoseIn(scene["earth-surface"], earth).Rotation.Rotate(DemoScenes.LaunchRamp.Up);
        (Math.Asin(Math.Abs(Double3.Dot(incline, DemoSurface.LaunchPlaneNormal))) * 180 / Math.PI).Should()
            .BeLessThan(0.001);
    }
}
