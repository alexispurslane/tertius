using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using LaunchRamp = Tertius.Demo.DemoScenes.LaunchRamp;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo launch: one headless run of the checked-in script scripts/demo-launch.json, in the scene demo-surface, with a
/// log of every tick. The person walks to its seat in the parked shuttle. The script posts the ignition. The shuttle slides
/// up its ramp and climbs to a circle 200 km up, with the person belted at its seat and the pen in its clip on the port
/// wall. In orbit, the clip lets the pen go, and the person floats to the drifting pen and takes it. Once the engines have
/// been off for 2 s, the log puts a ball at rest in the
/// cabin, forward and to port, away from the person's way, and follows it for 5 s. Every test of the class reads that one
/// run.
/// </summary>
public sealed class DemoLaunchRun : CheckedInRun
{
    public DemoLaunchRun() : this(new LaunchLog())
    {
    }

    private DemoLaunchRun(LaunchLog log) : base("demo-launch", log.Record)
    {
        Log = log;
    }

    internal LaunchLog Log { get; }
}

/// <summary>The state of the shuttle and of the person before one tick.</summary>
/// <param name="Tick">The number of completed ticks.</param>
/// <param name="Position">The shuttle's position in earth-surface, in meters from Earth's center.</param>
/// <param name="Velocity">The shuttle's velocity in earth-surface, in m/s.</param>
/// <param name="Ascent">What the ascent autopilot does.</param>
/// <param name="CabinAcceleration">The proper acceleration of the shuttle's frame, in m/s², in its axes.</param>
/// <param name="PersonFrame">The person's frame.</param>
/// <param name="PersonInCabin">The person's position in the shuttle's frame, in meters.</param>
/// <param name="Passenger">What the person does.</param>
/// <param name="Pen">
/// The loose pen's position, velocity and rotation in the shuttle's frame, in meters and m/s, while the loose pen lives,
/// or null.
/// </param>
/// <param name="Clip">What the pen's clip does, while the loose pen lives, or null.</param>
/// <param name="Held">The pose of the drawn copy of the pen in the person's hand, in the shuttle's frame, while the person holds it, or null.</param>
internal sealed record LaunchTick(long Tick, Double3 Position, Double3 Velocity, AscentState Ascent, System.Numerics.Vector3 CabinAcceleration,
    FrameHandle PersonFrame, Double3 PersonInCabin, PassengerState Passenger, (Double3 Position, Double3 Velocity, DQuaternion Rotation)? Pen,
    ClipState? Clip, DPose? Held);

/// <summary>A ball at rest in the cabin, 2 s after the engines stop, and where it is after 5 s.</summary>
/// <param name="Start">Where the ball was put, in the shuttle's frame, in meters.</param>
/// <param name="End">Where it is 5 s later.</param>
/// <param name="EndVelocity">Its velocity relative to the shuttle's frame then, in m/s.</param>
internal sealed record FloatingBall(Double3 Start, Double3 End, Double3 EndVelocity);

/// <summary>What the run does tick by tick, which the dumps show too seldom, and the ball in the weightless cabin.</summary>
internal sealed class LaunchLog
{
    /// <summary>Where the ball is put, in the shuttle's axes: forward and to port, 1.2 m above the floor.</summary>
    public static readonly Double3 BallStart = new(-2, -0.6, -6);

    private Entity ball;
    private long ballTick = -1;
    private long orbitTick = -1;

    public List<LaunchTick> Ticks { get; } = new();

    public FloatingBall? Ball { get; private set; }

    /// <summary>The first tick at which the autopilot was in its orbit phase, or -1.</summary>
    public long OrbitTick => orbitTick;

    public void Record(World world, SceneInstance scene)
    {
        Entity shuttle = scene["shuttle"], person = scene["person"], pen = scene["pen"];
        FrameHandle surface = world.GetFrame(shuttle), cabin = world.GetOwnedFrame(shuttle);
        AscentState ascent = world.GetComponent<AscentAutopilot>(shuttle)!.State;
        PassengerState passenger = world.GetComponent<Passenger>(person)!.State;
        bool penLives = world.IsAlive(pen);
        DPose? held = passenger.TryGetValue(out Holding holding) && world.IsAlive(holding.Copy) ? world.PoseIn(holding.Copy, cabin) : null;
        Ticks.Add(new LaunchTick(world.TickNumber, world.PoseIn(shuttle, surface).Position, world.VelocityIn(shuttle, surface), ascent,
            world.Frames.ProperAcceleration(cabin), world.GetFrame(person), world.PoseIn(person, cabin).Position, passenger,
            penLives ? (world.PoseIn(pen, cabin).Position, world.VelocityIn(pen, cabin), world.PoseIn(pen, cabin).Rotation) : null,
            penLives ? world.GetComponent<Clip>(pen)!.State : null, held));
        if (ascent.Value is Orbit && orbitTick < 0)
            orbitTick = world.TickNumber;
        if (orbitTick >= 0 && ball.IsNull && world.TickNumber == orbitTick + 120)
        {
            ball = world.Spawn(new SpawnDesc { Frame = cabin, Pose = new DPose(BallStart, DQuaternion.Identity), Body = Ball(0.1f) });
            ballTick = world.TickNumber;
        }
        if (!ball.IsNull && world.TickNumber == ballTick + 300)
            Ball = new FloatingBall(BallStart, world.PoseIn(ball, cabin).Position, world.VelocityIn(ball, cabin));
    }
}

[Collection(GpuCollection.Name)]
public sealed class DemoLaunchTests(DemoLaunchRun run) : IClassFixture<DemoLaunchRun>
{
    private StateDump Dump(string name) => run.Result.Dump(name);

    private IReadOnlyList<LaunchTick> Log => run.Log.Ticks;

    /// <summary>The periapsis and the apoapsis of the orbit of a state, in meters above Earth's mean radius.</summary>
    private static (double Periapsis, double Apoapsis) Apsides(Double3 position, Double3 velocity)
    {
        double r = position.Length, energy = velocity.LengthSquared / 2 - DemoScenes.EarthMu / r, a = -DemoScenes.EarthMu / (2 * energy);
        double h = Double3.Cross(position, velocity).Length;
        double e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h * h / (DemoScenes.EarthMu * DemoScenes.EarthMu)));
        return (a * (1 - e) - DemoScenes.EarthRadius, a * (1 + e) - DemoScenes.EarthRadius);
    }

    /// <summary>The angle between two directions, in degrees.</summary>
    private static double Degrees(Double3 a, Double3 b) => Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1)) * 180 / Math.PI;

    /// <summary>The normal of the incline's surface, up and toward the foot.</summary>
    private static readonly Double3 InclineNormal = new(0, Math.Cos(LaunchRamp.Angle), Math.Sin(LaunchRamp.Angle));

    /// <summary>How far up the incline the shuttle's center has come, along it, in meters from the foot.</summary>
    private static double AlongTheRamp(LaunchTick tick) => Double3.Dot(tick.Position - LaunchRamp.Foot, LaunchRamp.Up);

    [Fact]
    public void TheShuttleStaysParkedUntilItsIgnition()
    {
        StateDump seated = Dump("seated");

        seated.Frames.Single(frame => frame.Id == "shuttle").Follows.Should().Be("static");
        Double3.Distance(seated.Entity("shuttle").Pose.Position, DemoSurface.ShuttleParked.Position).Should().BeLessThan(1e-9);
        Log.Where(tick => tick.Tick <= seated.Tick).Should().OnlyContain(tick => tick.Ascent.Value is Parked);
    }

    /// <summary>
    /// From 20 m up the incline to 10 m before its top, the hull lies on the incline: its center is half the hull's height
    /// above the surface, and it moves along the incline.
    /// </summary>
    [Fact]
    public void TheShuttleSlidesUpTheRampOnIt()
    {
        List<LaunchTick> onTheRamp = [.. Log.Where(tick => AlongTheRamp(tick) is > 20 and < LaunchRamp.Length - 10)];

        onTheRamp.Should().HaveCountGreaterThan(60);
        foreach (LaunchTick tick in onTheRamp)
        {
            Double3.Dot(tick.Position - LaunchRamp.Foot, InclineNormal).Should().BeApproximately(Shuttle.Hull.Y, 0.2, $"at tick {tick.Tick}");
            Degrees(tick.Velocity, LaunchRamp.Up).Should().BeLessThan(1, $"at tick {tick.Tick}");
        }
    }

    /// <summary>
    /// The first tick at which the hull's tail has passed the ramp's top: the shuttle flies on along the incline, faster
    /// than 100 m/s.
    /// </summary>
    [Fact]
    public void TheShuttleLeavesTheRampAtItsTopAlongIt()
    {
        LaunchTick? off = Log.FirstOrDefault(tick => AlongTheRamp(tick) > LaunchRamp.Length + Shuttle.Hull.Z);

        off.Should().NotBeNull("the shuttle passes the ramp's top");

        Degrees(off!.Velocity, LaunchRamp.Up).Should().BeLessThan(2);
        off.Velocity.Length.Should().BeGreaterThan(100);
    }

    [Fact]
    public void TheAutopilotReachesAnOrbitWhosePeriapsisIsAboveTheTopOfTheAir()
    {
        EntityRecord shuttle = Dump("orbit").Entity("shuttle");

        shuttle.Pose.Frame.Should().Be("earth-surface");
        (double periapsis, double apoapsis) = Apsides(shuttle.Pose.Position, shuttle.Velocity);
        periapsis.Should().BeGreaterThan(DemoScenes.AtmosphereTop);
        periapsis.Should().BeGreaterThan(Shuttle.ParkingOrbitAltitude - 5e3, "the orbit is the parking circle");
        apoapsis.Should().BeLessThan(Shuttle.ParkingOrbitAltitude + 5e3, "the orbit is the parking circle");
    }

    /// <summary>
    /// After the engines stop, nothing pushes the shuttle: its frame falls freely, so the cabin is weightless, and the orbit
    /// at the end of the run is the orbit at the stop.
    /// </summary>
    [Fact]
    public void TheEnginesStopOnceTheShuttleIsInOrbit()
    {
        run.Log.OrbitTick.Should().BeGreaterThan(0);
        List<LaunchTick> coasting = [.. Log.Where(tick => tick.Tick > run.Log.OrbitTick + 2)];

        coasting.Should().HaveCountGreaterThan(300);
        coasting.Should().OnlyContain(tick => tick.Ascent.Value is Orbit);
        coasting.Should().OnlyContain(tick => tick.CabinAcceleration.Length() < FrameTree.ZeroGravityThreshold);
        (double startPeri, double startApo) = Apsides(coasting[0].Position, coasting[0].Velocity);
        (double endPeri, double endApo) = Apsides(coasting[^1].Position, coasting[^1].Velocity);
        endPeri.Should().BeApproximately(startPeri, 100);
        endApo.Should().BeApproximately(startApo, 100);
    }

    [Fact]
    public void ABallInTheCabinFloatsRelativeToTheCabinOnceTheEnginesStop()
    {
        FloatingBall ball = run.Log.Ball.Should().NotBeNull().And.Subject.As<FloatingBall>();

        Double3.Distance(ball.End, ball.Start).Should().BeLessThan(0.005);
        ball.EndVelocity.Length.Should().BeLessThan(0.001);
    }

    /// <summary>
    /// From the ignition to the orbit, three gravities and the ramp press the person aft, and the belt holds it at its
    /// seat in the shuttle's frame. The foot of the ramp jolts it once: when the hull's tail reaches the foot, at about
    /// 27 m/s, the hull stops turning onto the incline at once, and the belt's push against the cabin's gravity lags that
    /// jolt by a step, so the person swings about 0.17 m and back. In the climb, the thrust changes smoothly, and the
    /// person stays within millimeters of its seat.
    /// </summary>
    [Fact]
    public void ThePassengerStaysAtItsSeatThroughTheClimb()
    {
        List<LaunchTick> flight = [.. Log.Where(tick => tick.Ascent.Value is Ramp or Climb)];
        FrameHandle cabin = run.World.GetOwnedFrame(run.Scene["shuttle"]);

        flight.Should().HaveCountGreaterThan(10_000);
        flight.Should().OnlyContain(tick => tick.PersonFrame == cabin);
        flight.Should().OnlyContain(tick => tick.Passenger.Value is Seated);
        flight.Max(tick => Double3.Distance(tick.PersonInCabin, Shuttle.SeatStand)).Should().BeLessThan(0.25);
        flight.Where(tick => tick.Ascent.Value is Climb).Max(tick => Double3.Distance(tick.PersonInCabin, Shuttle.SeatStand)).Should().BeLessThan(0.01);
    }

    /// <summary>
    /// From the start to the orbit, through the walk, the ramp and the climb, the clip holds the pen on the cabin's port
    /// wall: gravity moves it at most about 8 mm in a step at three gravities, before the clip brings it back. The foot of
    /// the ramp jolts it once: when the hull's tail reaches the foot, at about 27 m/s, the hull stops turning onto the
    /// incline at once, and in that one step the pen drops about 0.1 m, which the next step takes back.
    /// </summary>
    [Fact]
    public void TheClipHoldsThePenOnTheWallUntilTheOrbit()
    {
        List<LaunchTick> held = [.. Log.Where(tick => tick.Ascent.Value is not Orbit)];
        List<double> fromClip = [.. held.Select(tick => Double3.Distance(tick.Pen!.Value.Position, DemoSurface.PenClip.Position))];

        held.Should().HaveCountGreaterThan(20_000);
        held.Should().OnlyContain(tick => tick.Clip!.Value.Value is Clipped);
        fromClip.Max().Should().BeLessThan(0.15);
        fromClip.Count(distance => distance > 0.015).Should().BeLessThanOrEqualTo(1, "only the step of the jolt at the ramp's foot moves the pen more");
    }

    /// <summary>
    /// In orbit the clip lets the pen go. From the step that applies its push to the person's take, nothing touches the
    /// pen, so it keeps the push's velocity, and when the person takes it, it floats more than half a meter from every face
    /// of the cabin.
    /// </summary>
    [Fact]
    public void InOrbitThePenFloatsFreeInTheMiddleOfTheCabinUntilThePersonTakesIt()
    {
        // The push of the tick on which the clip lets go acts in the step beside the next tick, so the snapshots of the
        // first two logged ticks after the release show the pen before the push.
        List<LaunchTick> free = [.. Log.Where(tick => tick.Clip?.Value is Released).Skip(2)];

        free.Should().HaveCountGreaterThan(120);
        free.Should().OnlyContain(tick => (tick.Pen!.Value.Velocity - DemoSurface.PenPush).Length < 1e-3);
        PenClipTests.Clearance(free[^1].Pen!.Value.Position).Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void InOrbitThePassengerFloatsToThePenAndHoldsIt()
    {
        World world = run.World;
        var live = new List<Entity>();
        world.CopyLiveEntities(live);

        world.GetComponent<Passenger>(run.Scene["person"])!.State.Value.Should().BeOfType<Holding>();
        world.IsAlive(run.Scene["pen"]).Should().BeFalse("the person took the loose pen");
        live.Where(entity => world.GetComponent<HeldInView>(entity) != null).Should().ContainSingle("the person holds the pen");
        Double3.Distance(Dump("holding").Entity("person").Pose.Position, Shuttle.SeatStand).Should().BeGreaterThan(1, "the person left its seat");
        Log.Should().Contain(tick => tick.Passenger.Value is Fetching);
    }

    /// <summary>
    /// The person's hand takes the drifting, tumbling pen where and as it floats: in the first tick that shows the drawn
    /// copy in the hand, the loose pen still shows, for its last tick, and the two lie within a centimeter and a degree of
    /// each other. So the pen does not jump into the hand.
    /// </summary>
    [Fact]
    public void InOrbitTheHandTakesThePenWhereAndAsItFloats()
    {
        LaunchTick take = Log.First(tick => tick.Held != null);

        take.Pen.Should().NotBeNull("the loose pen goes one tick after the copy shows");
        Double3.Distance(take.Held!.Value.Position, take.Pen!.Value.Position).Should().BeLessThan(0.01);
        (take.Pen.Value.Rotation.AngularVelocityTo(take.Held.Value.Rotation, 1).Length * 180 / Math.PI).Should().BeLessThan(1);
    }

    [Fact]
    public void TheWorldLogsNoErrors()
    {
        Dump("holding").Errors.Should().BeEmpty();
        run.ValidationMessages.Should().BeEmpty();
    }
}
