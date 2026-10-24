using System.Numerics;
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
    public DemoLaunchRun() : this(new())
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
/// <param name="Frame">The shuttle's home frame: earth-surface up to the hand-off, then earth.</param>
/// <param name="Position">The shuttle's position in earth, in meters from Earth's center.</param>
/// <param name="Velocity">The shuttle's velocity in earth, in m/s.</param>
/// <param name="Rotation">The shuttle's rotation in earth.</param>
/// <param name="AngularVelocity">The shuttle's angular velocity in earth, in rad/s, in earth's axes.</param>
/// <param name="GroundPosition">The shuttle's position in earth-surface, the turning ground, in meters from Earth's center.</param>
/// <param name="GroundVelocity">The shuttle's velocity in earth-surface, in m/s.</param>
/// <param name="GroundRotation">earth-surface's rotation in earth: the turn from the ground's axes to earth's.</param>
/// <param name="Ascent">What the ascent autopilot does.</param>
/// <param name="CabinAcceleration">The proper acceleration of the shuttle's frame, in m/s², in its axes.</param>
/// <param name="PersonFrame">The person's frame.</param>
/// <param name="PersonInCabin">The person's position in the shuttle's frame, in meters.</param>
/// <param name="Passenger">What the person does.</param>
/// <param name="Pen">
/// The loose pen's position, velocity and rotation in the shuttle's frame, in meters and m/s, while the loose pen lives,
/// or null.
/// </param>
/// <param name="PenFromShuttle">
/// The loose pen's place relative to the shuttle's center, in earth's axes, which do not turn, in meters, while the loose
/// pen lives, or null.
/// </param>
/// <param name="Clip">What the pen's clip does, while the loose pen lives, or null.</param>
/// <param name="Held">The pose of the drawn copy of the pen in the person's hand, in the shuttle's frame, while the person holds it, or null.</param>
/// <param name="PersonHeading">The person's heading: the turn of its view about its up, in radians, as
/// <see cref="WalkHeading.Yaw"/> gives it.</param>
internal sealed record LaunchTick(
    long Tick,
    FrameHandle Frame,
    Double3 Position,
    Double3 Velocity,
    DQuaternion Rotation,
    Double3 AngularVelocity,
    Double3 GroundPosition,
    Double3 GroundVelocity,
    DQuaternion GroundRotation,
    AscentState Ascent,
    Vector3 CabinAcceleration,
    FrameHandle PersonFrame,
    Double3 PersonInCabin,
    PassengerState Passenger,
    (Double3 Position, Double3 Velocity, DQuaternion Rotation)? Pen,
    Double3? PenFromShuttle,
    ClipState? Clip,
    DPose? Held,
    double PersonHeading);

/// <summary>
/// A ball at rest in the cabin, 7 s after the engines stop, and where it is after 5 s. The attitude autopilot turns the
/// shuttle from its climb to face the planet for about 5 s after the engines stop, at up to 0.1 rad/s. A free body keeps
/// its inertial motion while the cabin's turn changes under it (docs/plans/spin-gravity.md), so a ball put there 2 s after
/// the engines stop leaves its place by about 1 m in 5 s. From 7 s the cabin turns at the orbit's rate, about 1.2e-3 rad/s,
/// and its rate changes by less than 4e-5 rad/s more, which moves the ball by about 1 mm in 5 s.
/// </summary>
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

    public List<LaunchTick> Ticks { get; } = [];

    public FloatingBall? Ball { get; private set; }

    /// <summary>The first tick at which the autopilot was in its orbit phase, or -1.</summary>
    public long OrbitTick { get; private set; } = -1;

    public void Record(World world, SceneInstance scene)
    {
        Entity shuttle = scene["shuttle"], person = scene["person"], pen = scene["pen"];
        FrameHandle earth = world.GetOwnedFrame(scene["earth"]),
            surface = world.GetOwnedFrame(scene["earth-surface"]),
            cabin = world.GetOwnedFrame(shuttle);
        AscentState ascent = world.GetComponent<AscentAutopilot>(shuttle)!.State;
        PassengerState passenger = world.GetComponent<Passenger>(person)!.State;
        bool penLives = world.IsAlive(pen);
        DPose? held = passenger.TryGetValue(out Holding holding) && world.IsAlive(holding.Copy)
            ? world.PoseIn(holding.Copy, cabin)
            : null;
        DPose inEarth = world.PoseIn(shuttle, earth);
        Ticks.Add(new(Tick: world.TickNumber, Frame: world.GetFrame(shuttle), Position: inEarth.Position, Velocity: world.VelocityIn(shuttle, earth),
            Rotation: inEarth.Rotation,
            AngularVelocity: world.AngularVelocityIn(shuttle, earth), GroundPosition: world.PoseIn(shuttle, surface).Position,
            GroundVelocity: world.VelocityIn(shuttle, surface),
            GroundRotation: world.PoseIn(scene["earth-surface"], earth).Rotation, Ascent: ascent,
            CabinAcceleration: world.Frames.ProperAcceleration(cabin), PersonFrame: world.GetFrame(person), PersonInCabin: world.PoseIn(person, cabin).Position,
            Passenger: passenger,
            Pen: penLives
                ? (world.PoseIn(pen, cabin).Position, world.VelocityIn(pen, cabin), world.PoseIn(pen, cabin).Rotation)
                : null,
            PenFromShuttle: penLives ? world.PoseIn(pen, earth).Position - inEarth.Position : null,
            Clip: penLives ? world.GetComponent<Clip>(pen)!.State : null, Held: held,
            PersonHeading: WalkHeading.Yaw(world.GetRender(person).ViewOffset.Rotation)));
        if (ascent.Value is Orbit && OrbitTick < 0)
            OrbitTick = world.TickNumber;
        if (OrbitTick >= 0 && ball.IsNull && world.TickNumber == OrbitTick + 420)
        {
            ball = world.Spawn(new() { Frame = cabin, Pose = new(BallStart, DQuaternion.Identity), Body = Ball(0.1f) });
            ballTick = world.TickNumber;
        }

        if (!ball.IsNull && world.TickNumber == ballTick + 300)
            Ball = new(BallStart, world.PoseIn(ball, cabin).Position, world.VelocityIn(ball, cabin));
    }
}

public sealed class DemoLaunchTests(DemoLaunchRun run) : IClassFixture<DemoLaunchRun>
{
    /// <summary>The normal of the incline's surface, up and toward the foot.</summary>
    private static readonly Double3 InclineNormal = new(0, Math.Cos(LaunchRamp.Angle), Math.Sin(LaunchRamp.Angle));

    private List<LaunchTick> Log => run.Log.Ticks;

    /// <summary>The index in the log of the first tick after which the shuttle is in earth, the hand-off out of the turning ground.</summary>
    private int HandOff
    {
        get
        {
            FrameHandle earth = run.World.GetOwnedFrame(run.Scene["earth"]);
            int handOff = Log.FindIndex(tick => tick.Frame == earth);
            handOff.Should().BeGreaterThan(10, "the shuttle leaves earth-surface in the run");
            return handOff;
        }
    }

    /// <summary>The indices in the log from 10 before the hand-off to 10 after it.</summary>
    private IEnumerable<int> AroundTheHandOff => Enumerable.Range(HandOff - 10, 21);

    private StateDump Dump(string name) => run.Result.Dump(name);

    /// <summary>The periapsis and the apoapsis of the orbit of a state, in meters above Earth's mean radius.</summary>
    private static (double Periapsis, double Apoapsis) Apsides(Double3 position, Double3 velocity)
    {
        double r = position.Length,
            energy = velocity.LengthSquared / 2 - DemoScenes.EarthMu / r,
            a = -DemoScenes.EarthMu / (2 * energy);
        double h = Double3.Cross(position, velocity).Length;
        double e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h * h / (DemoScenes.EarthMu * DemoScenes.EarthMu)));
        return (a * (1 - e) - DemoScenes.EarthRadius, a * (1 + e) - DemoScenes.EarthRadius);
    }

    /// <summary>The angle between two directions, in degrees.</summary>
    private static double Degrees(Double3 a, Double3 b) =>
        Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1)) * 180 / Math.PI;

    /// <summary>How far up the incline the shuttle's center has come, along it, in meters from the foot.</summary>
    private static double AlongTheRamp(LaunchTick tick) =>
        Double3.Dot(tick.GroundPosition - LaunchRamp.Foot, LaunchRamp.Up);

    [Fact]
    public void TheShuttleStaysParkedUntilItsIgnition()
    {
        StateDump seated = Dump("seated");

        seated.Frames.Single(frame => frame.Id == "shuttle").Follows.Should().Be("static");
        Double3.Distance(seated.Entity("shuttle").Pose.Position, DemoSurface.ShuttleParked.Position).Should()
            .BeLessThan(1e-9);
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
            Double3.Dot(tick.GroundPosition - LaunchRamp.Foot, InclineNormal).Should()
                .BeApproximately(Shuttle.Hull.Y, 0.2, $"at tick {tick.Tick}");
            Degrees(tick.GroundVelocity, LaunchRamp.Up).Should().BeLessThan(1, $"at tick {tick.Tick}");
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

        Degrees(off!.GroundVelocity, LaunchRamp.Up).Should().BeLessThan(2);
        off.GroundVelocity.Length.Should().BeGreaterThan(100);
    }

    [Fact]
    public void TheAutopilotReachesAnOrbitWhosePeriapsisIsAboveTheTopOfTheAir()
    {
        EntityRecord shuttle = Dump("orbit").Entity("shuttle");

        shuttle.Pose.Frame.Should().Be("earth");
        (double periapsis, double apoapsis) = Apsides(shuttle.Pose.Position, shuttle.Velocity);
        periapsis.Should().BeGreaterThan(DemoScenes.AtmosphereTop);
        periapsis.Should().BeGreaterThan(Shuttle.ParkingOrbitAltitude - 5e3, "the orbit is the parking circle");
        apoapsis.Should().BeLessThan(Shuttle.ParkingOrbitAltitude + 5e3, "the orbit is the parking circle");
    }

    /// <summary>
    /// The climb steers into the launch plane, the plane through Earth's center that holds the pad's up and the ramp's
    /// incline at the departure, fixed in earth. So the parking orbit lies in it: at the first tick of the orbit, the
    /// shuttle is within 100 m of the plane in earth, and slower than 0.1 m/s across it.
    /// </summary>
    [Fact]
    public void TheClimbEndsInTheLaunchPlane()
    {
        LaunchTick first = Log.First(tick => tick.Ascent.Value is Orbit);

        Math.Abs(Double3.Dot(first.Position, DemoSurface.LaunchPlaneNormal)).Should().BeLessThan(100);
        Math.Abs(Double3.Dot(first.Velocity, DemoSurface.LaunchPlaneNormal)).Should().BeLessThan(0.1);
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
        var ball = run.Log.Ball.Should().NotBeNull().And.Subject.As<FloatingBall>();

        Double3.Distance(ball.End, ball.Start).Should().BeLessThan(0.005);
        ball.EndVelocity.Length.Should().BeLessThan(0.001);
    }

    /// <summary>
    /// From the ignition to the orbit, three gravities and the ramp press the person aft, and the belt holds it at its
    /// seat in the shuttle's frame. The foot of the ramp jolts it once: when the hull's tail reaches the foot, at about
    /// 27 m/s, the hull stops turning onto the incline at once, and the belt's push against the cabin's gravity lags that
    /// jolt by a step, so the person swings about 0.17 m and back. In the climb, the thrust changes smoothly, and the
    /// person stays within 8 cm of its seat. Changed on purpose by ter-1b21: the bound of the climb was 1 cm. A character's
    /// up now follows its gravity in the air too, at most at the air rate (docs/plans/spin-gravity.md, "Characters", fork
    /// F5, the user's choice of 2026-10-20), and nothing holds the belted person's up to the seat. At the end of the climb
    /// the engine throttles down to about 0.2 m/s², and the felt gravity leans 14 degrees from the cabin's aft axis toward
    /// its ceiling. So the person's head, toward the nose, dips onto the seat in front of it, and the seat lifts the person
    /// by 66.9 mm, 310 s into the climb. Changed on purpose by ter-4xkz: the bound of the climb was 7 cm, 3 mm above the
    /// 66.9 mm of the box hull. The tram hull's mass properties, with its center of mass 9 cm low and 36 cm aft, give a
    /// lift of 72.9 mm, 311 s into the climb. The bound of 8 cm still fails for a person who leaves the seat.
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
        flight.Where(tick => tick.Ascent.Value is Climb)
            .Max(tick => Double3.Distance(tick.PersonInCabin, Shuttle.SeatStand)).Should().BeLessThan(0.08);
    }

    /// <summary>
    /// The person sits at its seat from the ignition, so its heading holds (docs/plans/blue-danube-demo.md, "The person's
    /// walk"): in every tick from the ignition to the end of the run, it lies within 0.01 radians of its heading at the
    /// ignition. This covers the jolt at the ramp's foot, the climb, where the seat lifts the person, and the fetch of the
    /// pen in orbit, where the person floats.
    /// </summary>
    [Fact]
    public void ThePersonsHeadingHoldsFromTheIgnitionToTheEndOfTheRun()
    {
        List<LaunchTick> flight = [.. Log.SkipWhile(tick => tick.Ascent.Value is Parked)];
        double ignition = flight[0].PersonHeading;

        flight.Should().HaveCountGreaterThan(10_000);
        flight.Max(tick => Math.Abs(Math.IEEERemainder(tick.PersonHeading - ignition, 2 * Math.PI))).Should()
            .BeLessThan(0.01);
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
        List<double> fromClip =
            [.. held.Select(tick => Double3.Distance(tick.Pen!.Value.Position, DemoSurface.PenClip.Position))];

        held.Should().HaveCountGreaterThan(20_000);
        held.Should().OnlyContain(tick => tick.Clip!.Value.Value is Clipped);
        fromClip.Max().Should().BeLessThan(0.15);
        fromClip.Count(distance => distance > 0.015).Should()
            .BeLessThanOrEqualTo(1, "only the step of the jolt at the ramp's foot moves the pen more");
    }

    /// <summary>
    /// In orbit the clip lets the pen go. From the step that applies its push to the person's take, nothing touches the
    /// pen, so it moves on a straight line in inertial space, and when the person takes it, it floats more than half a
    /// meter from every face of the cabin. The line is measured from the shuttle's center in earth's axes: the shuttle and
    /// the pen fall together, and those axes do not turn. The clip reads the felt gravity with
    /// the rotation terms of the cabin's turn (see <see cref="FrameTree.FeltGravity"/>), so it lets go about 2.9 s after the
    /// engines stop, once the change of the turn that the attitude autopilot makes has settled. The autopilot turns the
    /// cabin for about 5 s after the engines stop, so the cabin still turns then, and in the cabin's turning axes the pen's
    /// velocity changes by the rotation terms (docs/plans/spin-gravity.md, "The model from scratch"). Before the rotation
    /// terms, the test asked for the push's velocity in the cabin's axes.
    /// </summary>
    [Fact]
    public void InOrbitThePenFloatsFreeInTheMiddleOfTheCabinUntilThePersonTakesIt()
    {
        // The push of the tick on which the clip lets go acts in the step beside the next tick, so the snapshots of the
        // first two logged ticks after the release show the pen before the push.
        List<LaunchTick> free = [.. Log.Where(tick => tick.Clip?.Value is Released).Skip(2)];
        double dt = run.World.DeltaTime;

        free.Should().HaveCountGreaterThan(120);
        // The line is that of the first two places, in earth's axes, which do not turn. Without the rotation terms of the
        // cabin's turn the pen leaves the line by more than 0.5 mm within 0.2 s.
        Double3 start = free[0].PenFromShuttle!.Value, velocity = (free[1].PenFromShuttle!.Value - start) / dt;
        for (int k = 0; k < free.Count; k++)
            Double3.Distance(free[k].PenFromShuttle!.Value, start + velocity * (k * dt)).Should()
                .BeLessThan(5e-4, $"{k} ticks after the push");
        velocity.Length.Should().BeGreaterThan(0.15, "the push sets the pen moving");
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
        live.Where(entity => world.GetComponent<HeldInView>(entity) != null).Should()
            .ContainSingle("the person holds the pen");
        Double3.Distance(Dump("holding").Entity("person").Pose.Position, Shuttle.SeatStand).Should()
            .BeGreaterThan(1, "the person left its seat");
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
        (take.Pen.Value.Rotation.AngularVelocityTo(take.Held.Value.Rotation, 1).Length * 180 / Math.PI).Should()
            .BeLessThan(1);
    }

    /// <summary>
    /// At the tick of the ignition, the first in which the autopilot leaves its parking, the ramp's incline, turned by the
    /// ground's rotation into earth's axes, lies within 0.01 degrees of the launch plane, the station's plane. The script
    /// ignites 0.8 s before the departure, and the ground turns 0.0042 degrees in a second.
    /// </summary>
    [Fact]
    public void TheRampLiesInTheStationsPlaneAtTheIgnition()
    {
        LaunchTick ignition = Log.First(tick => tick.Ascent.Value is not Parked);
        Double3 incline = ignition.GroundRotation.Rotate(LaunchRamp.Up);

        (Math.Asin(Math.Abs(Double3.Dot(incline, DemoSurface.LaunchPlaneNormal))) * 180 / Math.PI).Should()
            .BeLessThan(0.01);
    }

    /// <summary>
    /// The shuttle is in earth-surface before one tick of the climb and in earth from that tick on, and never comes back.
    /// It leaves where the engine's rule of the hand-off puts the exit (ARCHITECTURE.md, "Hand-off", rule 2): its center
    /// lies at most 20 m above <see cref="TurningGroundHandOffRun.ExitRadius"/>, 178.4 km up, and it climbs at most 17 m
    /// in a tick there.
    /// </summary>
    [Fact]
    public void TheShuttleLeavesEarthSurfaceOnceAtTheExitOfItsBubbleInTheClimb()
    {
        int handOff = HandOff;
        FrameHandle surface = run.World.GetOwnedFrame(run.Scene["earth-surface"]),
            earth = run.World.GetOwnedFrame(run.Scene["earth"]);

        Log.Take(handOff).Should().OnlyContain(tick => tick.Frame == surface);
        Log.Skip(handOff).Should().OnlyContain(tick => tick.Frame == earth);
        Log[handOff].Ascent.Value.Should().BeOfType<Climb>("the shuttle hands off in the climb");
        Log[handOff - 1].Position.Length.Should().BeLessThanOrEqualTo(TurningGroundHandOffRun.ExitRadius);
        Log[handOff].Position.Length.Should()
            .BeInRange(TurningGroundHandOffRun.ExitRadius, TurningGroundHandOffRun.ExitRadius + 20);
    }

    /// <summary>
    /// In each tick from 10 before the hand-off to 10 after it, the shuttle's acceleration in earth, the second difference
    /// of its center over the tick, lies within 0.01 m/s² of Earth's gravity at its place plus the thrust of the step that
    /// the difference spans: the cabin's proper acceleration of that step, turned from the hull's axes into earth's. The
    /// axes of earth-surface and of earth differ by about 143 degrees, so a thrust that a step applies in the wrong axes
    /// misses by more than the thrust itself.
    /// </summary>
    [Fact]
    public void TheShuttlesAccelerationInEarthStaysSmoothThroughTheHandOff()
    {
        double dt = run.World.DeltaTime;
        foreach (int k in AroundTheHandOff)
        {
            LaunchTick tick = Log[k], next = Log[k + 1];
            Double3 measured = (next.Position - 2 * tick.Position + Log[k - 1].Position) / (dt * dt);
            Double3 gravity = -tick.Position * (DemoScenes.EarthMu / Math.Pow(tick.Position.Length, 3));
            Double3 expected = gravity + next.Rotation.Rotate((Double3)next.CabinAcceleration);
            (measured - expected).Length.Should().BeLessThan(0.01,
                $"at tick {tick.Tick}, {k - HandOff} from the hand-off: measured {measured}, expected {expected}");
        }
    }

    /// <summary>In each tick from 10 before the hand-off to 10 after it, the cabin's proper acceleration changes by less than 0.01 m/s².</summary>
    [Fact]
    public void TheCabinsProperAccelerationStaysSmoothThroughTheHandOff()
    {
        foreach (int k in AroundTheHandOff)
            (Log[k].CabinAcceleration - (Double3)Log[k - 1].CabinAcceleration).Length
                .Should().BeLessThan(0.01,
                    $"at tick {Log[k].Tick}, {k - HandOff} from the hand-off: {Log[k - 1].CabinAcceleration} to {Log[k].CabinAcceleration}");
    }

    /// <summary>
    /// In each tick from 10 before the hand-off to 10 after it, the hull's angular velocity in earth changes by less than
    /// 3e-5 rad/s. The ground turns at 7.3e-5 rad/s, so a hand-off that loses or doubles the ground's turn fails it. The
    /// design's bound was 1e-4 rad/s, which such a hand-off passes. On 2026-10-20 the largest change was 7.2e-6 rad/s, two
    /// ticks after the hand-off, where the attitude autopilot answers the change of its frame.
    /// </summary>
    [Fact]
    public void TheNoseTurnsSmoothlyInEarthThroughTheHandOff()
    {
        foreach (int k in AroundTheHandOff)
            (Log[k].AngularVelocity - Log[k - 1].AngularVelocity).Length
                .Should().BeLessThan(3e-5, $"at tick {Log[k].Tick}, {k - HandOff} from the hand-off");
    }

    /// <summary>
    /// The belted person and the clipped pen stay in the cabin's frame through the hand-off, and across each tick from 10
    /// before it to 10 after it the place of each in the cabin changes by less than 1 mm more than in the tick before.
    /// </summary>
    [Fact]
    public void ThePersonAndThePenKeepTheirPlacesInTheCabinThroughTheHandOff()
    {
        FrameHandle cabin = run.World.GetOwnedFrame(run.Scene["shuttle"]);
        foreach (int k in AroundTheHandOff)
        {
            Log[k].PersonFrame.Should().Be(cabin);
            Log[k].Clip!.Value.Value.Should().BeOfType<Clipped>();
            double person = Double3.Distance(Log[k + 1].PersonInCabin, Log[k].PersonInCabin),
                personBefore = Double3.Distance(Log[k].PersonInCabin, Log[k - 1].PersonInCabin);
            person.Should().BeLessThan(personBefore + 1e-3,
                $"the person at tick {Log[k].Tick}, {k - HandOff} from the hand-off");
            double pen = Double3.Distance(Log[k + 1].Pen!.Value.Position, Log[k].Pen!.Value.Position),
                penBefore = Double3.Distance(Log[k].Pen!.Value.Position, Log[k - 1].Pen!.Value.Position);
            pen.Should().BeLessThan(penBefore + 1e-3,
                $"the pen at tick {Log[k].Tick}, {k - HandOff} from the hand-off");
        }
    }

    [Fact]
    public void TheWorldLogsNoErrors()
    {
        Dump("holding").Errors.Should().BeEmpty();
        run.ValidationMessages.Should().BeEmpty();
    }
}
