using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo surface's <c>follow-camera</c>, a <see cref="ChaseCamera"/> on the person, in one run of the checked-in long
/// script, scripts/blue-danube.json, from its start through its film <see cref="LastFilm"/>, on the real World and the real
/// Jolt backend, with no renderer: the person walks through the terminal, across the pad and into the shuttle to its
/// seat, the script posts <c>Ignition</c>, and the shuttle rides up its ramp and climbs. Every test of the class reads that
/// one run, one <see cref="FollowTick"/> after each tick.
/// </summary>
public sealed class DemoFollowCameraRun : IDisposable
{
    /// <summary>The last film of the run: the follow camera's film of the climb.</summary>
    public const string LastFilm = "climb-cabin";

    // The person's up after the last tick, in its frame's axes.
    private Double3 lastUp = Double3.UnitY;

    public DemoFollowCameraRun()
    {
        Script full = Script.Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json"));
        int last = full.Steps.ToList().FindIndex(step => step.Value is FilmStep { Name: LastFilm });
        if (last < 0)
            throw new InvalidOperationException($"scripts/blue-danube.json has no film '{LastFilm}'.");
        World = NewJoltWorld();
        Scene = DemoScenes.All.Single(s => s.Name == full.Scene).Build(World, RenderResources.None);
        ScriptRunner.Run(script: full with { Steps = [.. full.Steps.Take(last + 1)] }, world: World, scene: Scene,
            outputFolder: null,
            playback: new Recorder(this));
        Record();
        SeatedTick = Ticks.First(tick => tick.Seated).Tick;
        // A posted event joins the routing of the next tick, and the shuttle handles it in the tick after that.
        IgnitionTick = Ticks.First(tick => tick.Riding).Tick - 2;
        ShuttleTravel =
            Double3.Distance(World.PoseIn(Scene["shuttle"], World.GetOwnedFrame(Scene["earth-surface"])).Position,
                DemoSurface.ShuttleParked.Position);
    }

    public World World { get; }
    public SceneInstance Scene { get; }

    /// <summary>The camera's position less the person's after the first tick, in earth-surface, in meters.</summary>
    public Double3 FirstBack { get; private set; }

    /// <summary>The number of ticks that ran before the run posted <c>Ignition</c> to the shuttle: two less than the first tick after which the shuttle's autopilot is not parked.</summary>
    public long IgnitionTick { get; }

    /// <summary>The tick at which the person shut its belt at the seat.</summary>
    public long SeatedTick { get; }

    /// <summary>How far the shuttle is from where it stood parked at the end of the run, in meters.</summary>
    public double ShuttleTravel { get; }

    public List<FollowTick> Ticks { get; } = [];

    /// <summary>Reads the follow camera after the tick that ran last, unless no tick ran yet or that tick is read already.</summary>
    private void Record()
    {
        if (World.TickNumber == 0 || (Ticks.Count > 0 && Ticks[^1].Tick == World.TickNumber))
            return;
        Entity person = Scene["person"], camera = Scene["follow-camera"];
        Ticks.Add(Read(person, camera, World.GetOwnedFrame(Scene["shuttle"])));
        if (World.TickNumber == 1)
            FirstBack = World.PoseIn(camera, World.GetFrame(person)).Position -
                        World.PoseIn(person, World.GetFrame(person)).Position;
    }

    private FollowTick Read(Entity person, Entity camera, FrameHandle cabin)
    {
        bool riding = World.GetComponent<AscentAutopilot>(Scene["shuttle"])!.State is not Parked;
        bool seated = World.GetComponent<Passenger>(person)!.State.Value is not Boarding;
        FrameHandle frame = World.GetFrame(person);
        DPose view = World.PoseIn(camera, frame), body = World.PoseIn(person, frame);
        Double3 eye = body.TransformPoint(DemoSurface.Eye);
        Double3 look = view.Rotation.Rotate(-Double3.UnitZ),
            toEye = eye - view.Position,
            up = body.Rotation.Rotate(Double3.UnitY);
        var tick = new FollowTick(Tick: World.TickNumber, Riding: riding, Seated: seated,
            AngleToEye: Degrees(look, toEye), Distance: toEye.Length,
            UpTurn: Degrees(up, lastUp),
            InCabin: World.GetFrame(camera) == cabin ? World.PoseIn(camera, cabin).Position : null,
            Reach: Reach(view, body), Frame: frame,
            Camera: view.Position, EyeInPicture: InPicture(view, eye), Eye: eye);
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
            var local = new Double3((corner & 1) == 0 ? -half.X : half.X, (corner & 2) == 0 ? -half.Y : half.Y,
                (corner & 4) == 0 ? -half.Z : half.Z);
            Double3 seen = view.Rotation.Conjugate().Rotate(body.TransformPoint(local) - view.Position);
            if (seen.Z > -1e-3)
                return double.PositiveInfinity;
            reach = Math.Max(reach, Math.Max(Math.Abs(seen.X / -seen.Z) / tanX, Math.Abs(seen.Y / -seen.Z) / tanY));
        }

        return reach;
    }

    /// <summary>
    /// Where <paramref name="point"/> lies in the camera's picture, 1280 by 720 pixels with the world's vertical view angle:
    /// its distances right of and above the middle, both as a share of the half height, so that one unit is 360 pixels.
    /// </summary>
    private Double3 InPicture(DPose view, Double3 point)
    {
        Double3 seen = view.Rotation.Conjugate().Rotate(point - view.Position);
        double tanY = Math.Tan(World.VerticalFov / 2);
        return new(seen.X / -seen.Z / tanY, seen.Y / -seen.Z / tanY, 0);
    }

    private static double Degrees(Double3 a, Double3 b) =>
        Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1)) * 180 / Math.PI;

    public void Dispose() => World.Dispose();

    /// <summary>A playback that reads the follow camera before each tick, after the tick before it, and draws nothing.</summary>
    private sealed class Recorder(DemoFollowCameraRun run) : IPlayback
    {
        public bool WaitForTick()
        {
            run.Record();
            return true;
        }

        public bool Capture(World world, string name) => true;
    }
}

/// <summary>The follow camera after one tick.</summary>
/// <param name="Tick">The number of completed ticks.</param>
/// <param name="Riding">True once the shuttle's autopilot has taken the ignition.</param>
/// <param name="Seated">True once the person has shut its belt at the seat.</param>
/// <param name="AngleToEye">The angle between the camera's view and the way from the camera to the person's eye, in degrees.</param>
/// <param name="Distance">How far the person's eye is from the camera, in meters.</param>
/// <param name="UpTurn">How far the person's up turned in the tick, in degrees.</param>
/// <param name="InCabin">The camera's position in the shuttle's frame while it is in that frame, or null.</param>
/// <param name="Reach">How far the corners of the person's box reach from the middle of the picture, as a share of its half width or half height (1 at its edge).</param>
/// <param name="Frame">The person's frame.</param>
/// <param name="Camera">The camera's position in the person's frame, in meters.</param>
/// <param name="Eye">The person's eye in its frame, in meters.</param>
/// <param name="EyeInPicture">Where the person's eye lies in the camera's picture: right of and above the middle, as a share of the half height (360 pixels), with Z zero.</param>
public sealed record FollowTick(
    long Tick,
    bool Riding,
    bool Seated,
    double AngleToEye,
    double Distance,
    double UpTurn,
    Double3? InCabin,
    double Reach,
    FrameHandle Frame,
    Double3 Camera,
    Double3 EyeInPicture,
    Double3 Eye);

public sealed class DemoFollowCameraTests(DemoFollowCameraRun run) : IClassFixture<DemoFollowCameraRun>
{
    /// <summary>
    /// The run is the checked-in long script up to the end of its film <c>climb-cabin</c>, so its ticks are the ticks that
    /// the script films: it posts <c>Ignition</c> after as many ticks as the script runs before its post, and its last tick
    /// is the last tick of <c>climb-cabin</c>.
    /// </summary>
    [Fact]
    public void TheRunIsTheCheckedInScriptThroughTheFilmClimbCabin()
    {
        Script script = Script.Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json"));
        int ignition = script.Steps.ToList().FindIndex(step => step.Value is PostStep { Event: nameof(Ignition) });
        int climbCabin = script.Steps.ToList().FindIndex(step => step.Value is FilmStep { Name: "climb-cabin" });

        run.IgnitionTick.Should().Be(TicksOf(script.Steps.Take(ignition)));
        run.Ticks[^1].Tick.Should().Be(TicksOf(script.Steps.Take(climbCabin + 1)));
    }

    /// <summary>How many ticks <paramref name="steps"/> run: the ticks of each run step, and the frames times the ticks per frame of each film.</summary>
    private static long TicksOf(IEnumerable<ScriptStep> steps) => steps.Sum(step => step.Value switch
    {
        RunStep run => run.Ticks,
        FilmStep film => (long)film.Frames * film.Every,
        _ => 0L
    });

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

        seated.Should().HaveCount((int)(run.Ticks[^1].Tick - run.SeatedTick + 1));
        (seated.Count - steady.Count).Should()
            .BeInRange(1, 5, "the person's up turns at the ignition and at the ramp's ends");
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

        worst.Reach.Should().BeLessThan(0.95,
            $"at tick {worst.Tick} the person's box reaches {worst.Reach:F3} of the half picture");
    }

    /// <summary>
    /// Through the walk, with its turns at the waypoints, the camera moves smoothly. The person's turn at a waypoint changes
    /// its velocity at once, which no camera can foresee, so the camera's way bends with the person's in the tick of the
    /// turn (see <see cref="ChaseCamera"/>). Two rules hold in every tick of the walk:
    /// <list type="bullet">
    /// <item>The camera does not zigzag: no second difference of its position longer than 1 cm is followed, in the next
    /// tick, by one that takes back more than half of it, unless the person's eye zigzags so itself, as at the step onto
    /// the hatch's sill.</item>
    /// <item>The person's eye moves in the picture by less than 2 percent of the half picture's height in a tick, 7.2
    /// pixels at 720p. A camera that moves back toward its old course after a turn moves the person by about 12 pixels in
    /// one tick. The arm's growth after the jump at the hatch pans the person down the picture by up to 5 pixels a tick.</item>
    /// </list>
    /// The camera jumps only where a wall comes into its arm, which it then shortens at once: once, as the person steps
    /// through the shuttle's hatch and the arm, which looked through the hatch, meets the hull's side. The test leaves out
    /// the ticks of that jump, in which the camera comes more than 0.2 m nearer the eye, and those in which the person
    /// changes frame.
    /// </summary>
    [Fact]
    public void TheFollowCameraMovesSmoothlyThroughTheTurnsOfTheWalk()
    {
        List<FollowTick> walk = [.. run.Ticks.Where(tick => !tick.Seated && tick.Tick > 2)];
        List<long> jumps = [];
        for (int i = 3; i < walk.Count; i++)
        {
            if (walk[i].Distance < walk[i - 1].Distance - 0.2)
                jumps.Add(walk[i].Tick);
            if (Enumerable.Range(i - 3, 4).Any(j => walk[j].Frame != walk[i].Frame || jumps.Contains(walk[j].Tick)))
                continue;
            (walk[i].EyeInPicture - walk[i - 1].EyeInPicture).Length.Should()
                .BeLessThan(0.02, $"at tick {walk[i].Tick} the person moved in the picture");
            bool cameraZigzags = Zigzags(a: walk[i - 3].Camera, b: walk[i - 2].Camera, c: walk[i - 1].Camera,
                d: walk[i].Camera);
            bool eyeZigzags = Zigzags(a: walk[i - 3].Eye, b: walk[i - 2].Eye, c: walk[i - 1].Eye, d: walk[i].Eye);
            (cameraZigzags && !eyeZigzags).Should().BeFalse($"at tick {walk[i].Tick} the camera zigzagged");
        }

        jumps.Should().ContainSingle("the arm meets the hull's side once, at the hatch");
    }

    /// <summary>
    /// True when the way through the four positions zigzags: its second difference at <paramref name="c"/> is longer than
    /// 1 cm, and the one at the next position takes back more than half of it.
    /// </summary>
    private static bool Zigzags(Double3 a, Double3 b, Double3 c, Double3 d)
    {
        Double3 before = c - 2 * b + a, now = d - 2 * c + b;
        return before.Length > 0.01 && Double3.Dot(before, now) < -0.5 * Double3.Dot(before, before);
    }

    /// <summary>
    /// Through the walk on the ground, through the terminal's entrance and gate, the camera stays out of the terminal's
    /// walls, roof, jambs and lintels: in every tick it lies at least 0.1 m outside each box of the terminal's shape, twice
    /// the renderer's near plane, so no wall cuts the picture. The doorways are 3 m tall and 2.5 m wide, and the camera,
    /// 2.45 m above the floor, passes through them behind the person at its full arm, below the lintels; the walk's turns
    /// before the gate and after it swing the arm, which must stay clear of the jambs while it swings.
    /// </summary>
    [Fact]
    public void TheFollowCameraStaysOutOfTheTerminalsWallsThroughTheWalk()
    {
        FrameHandle surface = run.World.GetFrame(run.Scene["shuttle"]);
        List<FollowTick> onTheGround = [.. run.Ticks.Where(tick => !tick.Seated && tick.Frame == surface)];
        BoundingBox[] terminal = [.. DemoSurface.TerminalShape()];

        onTheGround.Should().HaveCountGreaterThan(400, "the walk through the terminal lies on the ground");
        foreach (FollowTick tick in onTheGround)
        {
            Double3 local = tick.Camera - DemoSurface.TerminalCenter;
            foreach (BoundingBox box in terminal)
                Outside(local, box).Should().BeGreaterThan(0.1,
                    $"at tick {tick.Tick} the camera at {local} in the terminal's axes is near the box {box}");
        }
    }

    /// <summary>How far <paramref name="point"/> lies outside <paramref name="box"/>, in meters, or 0 inside it.</summary>
    private static double Outside(Double3 point, BoundingBox box)
    {
        double x = Math.Max(Math.Max(box.Min.X - point.X, point.X - box.Max.X), 0);
        double y = Math.Max(Math.Max(box.Min.Y - point.Y, point.Y - box.Max.Y), 0);
        double z = Math.Max(Math.Max(box.Min.Z - point.Z, point.Z - box.Max.Z), 0);
        return Math.Sqrt(x * x + y * y + z * z);
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
