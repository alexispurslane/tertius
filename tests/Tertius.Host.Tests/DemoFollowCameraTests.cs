using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo surface's <c>follow-camera</c>, a <see cref="ChaseCamera"/> on the person, in one run of the scene on the real
/// World and the real Jolt backend: the person walks through the terminal, across the pad and into the shuttle to its
/// seat, and then rides the shuttle up its ramp. Every test of the class reads that one run.
/// </summary>
public sealed class DemoFollowCameraRun : IDisposable
{
    /// <summary>
    /// How many ticks the run goes on after the ignition: the ramp, the jolt at its foot, the flight past its top, and the
    /// climb through the long script's <c>climb-cabin</c> film, which ends 58 s after the ignition.
    /// </summary>
    public const int RideTicks = 3600;

    public DemoFollowCameraRun()
    {
        World = NewJoltWorld();
        Scene = DemoScenes.All.Single(s => s.Name == "demo-surface").Build(World, RenderResources.None);
        Entity person = Scene["person"], camera = Scene["follow-camera"];
        FrameHandle cabin = World.GetOwnedFrame(Scene["shuttle"]);
        Passenger passenger = World.GetComponent<Passenger>(person)!;
        while (passenger.State.Value is Boarding && World.TickNumber < 1500)
        {
            World.Tick();
            Ticks.Add(Read(person, camera, cabin, riding: false));
            if (World.TickNumber == 1)
                FirstBack = World.PoseIn(camera, World.GetFrame(person)).Position - World.PoseIn(person, World.GetFrame(person)).Position;
        }
        SeatedTick = World.TickNumber;
        ScriptRunner.Run(new Script("demo-surface", [new PostStep(new ToEntity("shuttle"), "Ignition")]), World, Scene);
        for (int tick = 0; tick < RideTicks; tick++)
        {
            World.Tick();
            Ticks.Add(Read(person, camera, cabin, riding: true));
        }
        ShuttleTravel = Double3.Distance(World.PoseIn(Scene["shuttle"], World.GetFrame(Scene["shuttle"])).Position, DemoSurface.ShuttleParked.Position);
    }

    public World World { get; }
    public SceneInstance Scene { get; }

    /// <summary>The camera's position less the person's after the first tick, in earth-surface, in meters.</summary>
    public Double3 FirstBack { get; }

    /// <summary>The tick at which the person shut its belt at the seat.</summary>
    public long SeatedTick { get; }

    /// <summary>How far the shuttle is from where it stood parked at the end of the run, in meters.</summary>
    public double ShuttleTravel { get; }

    public List<FollowTick> Ticks { get; } = new();

    // The person's up after the last tick, in its frame's axes.
    private Double3 lastUp = Double3.UnitY;

    private FollowTick Read(Entity person, Entity camera, FrameHandle cabin, bool riding)
    {
        FrameHandle frame = World.GetFrame(person);
        DPose view = World.PoseIn(camera, frame), body = World.PoseIn(person, frame);
        Double3 eye = body.TransformPoint((Double3)DemoSurface.Eye);
        Double3 look = view.Rotation.Rotate(-Double3.UnitZ), toEye = eye - view.Position, up = body.Rotation.Rotate(Double3.UnitY);
        var tick = new FollowTick(World.TickNumber, riding, Degrees(look, toEye), toEye.Length, Degrees(up, lastUp),
            World.GetFrame(camera) == cabin ? World.PoseIn(camera, cabin).Position : null, Reach(view, body), frame, view.Position);
        lastUp = up;
        return tick;
    }

    /// <summary>
    /// How far the corners of the person's box reach from the middle of the camera's picture, 1280 by 720 pixels with the
    /// world's vertical view angle: the largest of the corners' horizontal and vertical distances from the middle, as a
    /// share of the half width and the half height, or infinity when a corner lies behind the camera.
    /// </summary>
    private double Reach(DPose view, DPose body)
    {
        double tanY = Math.Tan(World.VerticalFov / 2), tanX = tanY * BlueDanube.Window.Width / BlueDanube.Window.Height;
        var half = (Double3)DemoSurface.PersonHalf;
        double reach = 0;
        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Double3((corner & 1) == 0 ? -half.X : half.X, (corner & 2) == 0 ? -half.Y : half.Y, (corner & 4) == 0 ? -half.Z : half.Z);
            Double3 seen = view.Rotation.Conjugate().Rotate(body.TransformPoint(local) - view.Position);
            if (seen.Z > -1e-3)
                return double.PositiveInfinity;
            reach = Math.Max(reach, Math.Max(Math.Abs(seen.X / -seen.Z) / tanX, Math.Abs(seen.Y / -seen.Z) / tanY));
        }
        return reach;
    }

    private static double Degrees(Double3 a, Double3 b) => Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1)) * 180 / Math.PI;

    public void Dispose() => World.Dispose();
}

/// <summary>The follow camera after one tick.</summary>
/// <param name="Tick">The number of completed ticks.</param>
/// <param name="Riding">True after the ignition.</param>
/// <param name="AngleToEye">The angle between the camera's view and the way from the camera to the person's eye, in degrees.</param>
/// <param name="Distance">How far the person's eye is from the camera, in meters.</param>
/// <param name="UpTurn">How far the person's up turned in the tick, in degrees.</param>
/// <param name="InCabin">The camera's position in the shuttle's frame while it is in that frame, or null.</param>
/// <param name="Reach">How far the corners of the person's box reach from the middle of the picture, as a share of its half width or half height (1 at its edge).</param>
/// <param name="Frame">The person's frame.</param>
/// <param name="Camera">The camera's position in the person's frame, in meters.</param>
public sealed record FollowTick(long Tick, bool Riding, double AngleToEye, double Distance, double UpTurn, Double3? InCabin, double Reach, FrameHandle Frame, Double3 Camera);

public sealed class DemoFollowCameraTests(DemoFollowCameraRun run) : IClassFixture<DemoFollowCameraRun>
{
    [Fact]
    public void ThePersonReachesItsSeatAndTheShuttleLeavesThePad()
    {
        run.SeatedTick.Should().BeLessThan(1500);
        run.ShuttleTravel.Should().BeGreaterThan(200, "the ride goes up the ramp");
    }

    /// <summary>The person starts its walk along -X, toward the terminal's entrance, and the camera starts behind it, along +X.</summary>
    [Fact]
    public void TheFollowCameraStartsBehindThePersonOnItsWayToTheEntrance()
    {
        run.FirstBack.X.Should().BeGreaterThan(3);
    }

    /// <summary>
    /// Through the walk and the ride, the person's eye stays within 20 degrees of the middle of the view, which is 60
    /// degrees high, and between 0.4 m and 4.5 m from the camera.
    /// </summary>
    [Fact]
    public void TheFollowCameraKeepsThePersonInItsFramingThroughTheWalkAndTheRide()
    {
        run.Ticks.Should().OnlyContain(tick => tick.AngleToEye < 20 && tick.Distance > 0.4 && tick.Distance < 4.5);
    }

    /// <summary>
    /// From the person's seat on, through the ramp and its jolt, the camera stays in the shuttle's frame, inside the cabin's
    /// walls, floor and roof, with 5 cm to spare. The engine turns the person's up at once when the cabin's gravity turns,
    /// as at the ignition, and the attachment turns the camera's offset with the person, so in the tick of such a turn the
    /// camera swings with the person once (see <see cref="ChaseCamera"/>). The test leaves out those ticks, in which the
    /// person's up turned by more than 5 degrees, and counts them: a few in the ride.
    /// </summary>
    [Fact]
    public void TheFollowCameraStaysInsideTheCabinAtTheSeatAndThroughTheRideOutsideTheTicksOfTheUpsTurns()
    {
        List<FollowTick> seated = [.. run.Ticks.Where(tick => tick.Tick >= run.SeatedTick)];
        List<FollowTick> steady = [.. seated.Where(tick => tick.UpTurn < 5)];

        seated.Should().HaveCount(DemoFollowCameraRun.RideTicks + 1);
        (seated.Count - steady.Count).Should().BeInRange(1, 5, "the person's up turns at the ignition and at the ramp's ends");
        steady.Should().OnlyContain(tick => tick.InCabin.HasValue
            && Math.Abs(tick.InCabin.Value.X) < Shuttle.Cabin.X - 0.05
            && Math.Abs(tick.InCabin.Value.Y) < Shuttle.Cabin.Y - 0.05
            && Math.Abs(tick.InCabin.Value.Z) < Shuttle.Cabin.Z - 0.05);
    }

    /// <summary>
    /// Through the walk and the ride, every corner of the person's box stays in the picture, 5 percent of the half width
    /// or half height inside its edges, outside the ticks in which the person's up turned by more than 5 degrees.
    /// </summary>
    [Fact]
    public void TheFollowCameraKeepsThePersonsWholeBodyInThePictureThroughTheWalkAndTheRide()
    {
        List<FollowTick> steady = [.. run.Ticks.Where(tick => tick.UpTurn < 5)];
        FollowTick worst = steady.MaxBy(tick => tick.Reach)!;

        worst.Reach.Should().BeLessThan(0.95, $"at tick {worst.Tick} the person's box reaches {worst.Reach:F3} of the half picture");
    }

    /// <summary>
    /// Through the walk, with its turns at the waypoints, the camera moves smoothly: its move in one tick differs from its
    /// move in the tick before by less than 10 cm. The person's turn at a waypoint changes its velocity at 3 m/s by up to 90
    /// degrees, 4.2 m/s, which no camera can foresee, so it moves the camera 7 cm off its way in the tick of the turn, and
    /// the camera's lag takes that move out from the next tick on (see <see cref="ChaseCamera"/>). The step up onto the
    /// hatch's sill, which the person's capsule climbs by up to 9 cm in a tick, moves the camera as far; the long script
    /// films that step with the apron camera. The camera jumps only
    /// where a wall comes into its arm, which it then shortens at once: once, as the person steps through the shuttle's
    /// hatch and the arm, which looked through the hatch, meets the hull's side. The test leaves out the ticks of that
    /// jump, in which the camera comes more than 0.2 m nearer the eye, and those in which the person changes frame.
    /// </summary>
    [Fact]
    public void TheFollowCameraMovesSmoothlyThroughTheTurnsOfTheWalk()
    {
        List<FollowTick> walk = [.. run.Ticks.Where(tick => !tick.Riding && tick.Tick > 2)];
        List<long> jumps = [];
        double worst = 0;
        long at = 0;
        for (int i = 2; i < walk.Count; i++)
        {
            if (walk[i].Distance < walk[i - 1].Distance - 0.2)
                jumps.Add(walk[i].Tick);
            if (walk[i].Frame != walk[i - 1].Frame || walk[i - 1].Frame != walk[i - 2].Frame
                || jumps.Contains(walk[i].Tick) || jumps.Contains(walk[i - 1].Tick))
                continue;
            double jerk = (walk[i].Camera - 2 * walk[i - 1].Camera + walk[i - 2].Camera).Length;
            if (jerk > worst)
                (worst, at) = (jerk, walk[i].Tick);
        }

        jumps.Should().ContainSingle("the arm meets the hull's side once, at the hatch");
        worst.Should().BeLessThan(0.1, $"at tick {at}");
    }

    /// <summary>
    /// The long script's boarding film, from the apron camera, lasts until the follow camera is inside the cabin
    /// (<see cref="BlueDanube.InsideTheCabin"/>), so the follow camera's cabin film starts after its jump at the hatch.
    /// </summary>
    [Fact]
    public void TheFollowCameraIsInsideTheCabinOnlyAfterItsJumpAtTheHatch()
    {
        List<FollowTick> walk = [.. run.Ticks.Where(tick => !tick.Riding)];
        long jump = walk.Zip(walk.Skip(1)).First(pair => pair.Second.Distance < pair.First.Distance - 0.2).Second.Tick;
        long inside = walk.First(tick => tick.InCabin is { } at && BlueDanube.InsideTheCabin(at)).Tick;

        inside.Should().BeGreaterThanOrEqualTo(jump);
    }
}
