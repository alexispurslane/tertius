using System.Numerics;
using Tertius.Demo;
using Tertius.Frames.Rails;
using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The checked-in long script, scripts/blue-danube.json, through its film <see cref="LastFilm"/>, in one run on the real
/// World and the real Jolt backend with no renderer (docs/plans/blue-danube-demo.md, "The station and the rendezvous", "The
/// docking", "The exit" and "The demo in the Earth system"): from the terminal at the north pole, in the turning
/// earth-surface, through the hand-off of the climb into earth, the rendezvous in earth and the docking to the person on
/// the wheel's floor and the coda's throws. The run keeps a log of every tick from the first tick of the parking orbit, and at each frame it records
/// what the frame's camera sees of the station and of the person. It runs without the world's invariant checks: with the
/// station, they cost 6.6 ms a tick, and the run takes about 300,000 ticks.
/// </summary>
public sealed class BlueDanubeRendezvousRun : IDisposable
{
    /// <summary>
    /// The name of the test collection whose classes share this one run: <see cref="BlueDanubeRendezvousTests"/>,
    /// <see cref="BlueDanubeDockingTests"/>, <see cref="BlueDanubeExitTests"/> and <see cref="BlueDanubeCodaTests"/>.
    /// </summary>
    public const string Collection = "Blue Danube long run";

    /// <summary>The last film of the run, the coda's last, which ends the script.</summary>
    public const string LastFilm = "antispinward";

    /// <summary>The exit's last film, after which the coda's sections start.</summary>
    public const string FloorFilm = "floor";

    /// <summary>The docking's last film, after which the exit's sections start.</summary>
    public const string DockedFilm = "docked";

    public BlueDanubeRendezvousRun()
    {
        Script full = Script.Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json"));
        int last = full.Steps.ToList().FindIndex(step => step.Value is FilmStep { Name: LastFilm });
        if (last < 0)
            throw new InvalidOperationException($"scripts/blue-danube.json has no film '{LastFilm}'.");
        World = new(new() { MaxDegreeOfParallelism = 1, DebugChecks = false, DisposePhysics = true },
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced));
        World.Log += Logged.Add;
        Scene = DemoScenes.All.Single(s => s.Name == full.Scene).Build(World, RenderResources.None);
        Earth = World.GetOwnedFrame(Scene["earth"]);
        Mu = KeplerOrbit.MuFrom(World.Frames.Gravity(Earth));
        var recorder = new Recorder(this);
        ScriptRunner.Run(full with { Steps = [.. full.Steps.Take(last + 1)] }, World, Scene, outputFolder: null,
            recorder);
        // The playback asks before each tick, so the run records the state after the last tick itself.
        recorder.WaitForTick();
    }

    public World World { get; }
    public SceneInstance Scene { get; }

    /// <summary>The number of completed ticks at the last frame of the film <see cref="DockedFilm"/>.</summary>
    public long DockedTick { get; private set; } = -1;

    /// <summary>The ticks from the first tick of the parking orbit to the end of the film <see cref="DockedFilm"/>, the docking's end.</summary>
    public IEnumerable<RendezvousTick> ThroughDocked => Ticks.TakeWhile(tick => tick.Tick <= DockedTick);

    /// <summary>The state at the end of the film <see cref="DockedFilm"/>, the docking's end state.</summary>
    public RendezvousTick DockedEnd => Ticks.Last(tick => tick.Tick == DockedTick);

    /// <summary>The ticks after the film <see cref="DockedFilm"/>: the exit and the coda, from the order to the end of the run.</summary>
    public IEnumerable<RendezvousTick> AfterDocked => Ticks.SkipWhile(tick => tick.Tick <= DockedTick);

    /// <summary>The number of completed ticks at the last frame of the film <see cref="FloorFilm"/>.</summary>
    public long FloorTick { get; private set; } = -1;

    /// <summary>The ticks after the film <see cref="FloorFilm"/>: the coda, from the order to play to the end of the run.</summary>
    public IEnumerable<RendezvousTick> AfterFloor => Ticks.SkipWhile(tick => tick.Tick <= FloorTick);

    /// <summary>
    /// The center of each box of the coda's stack, in the order of <see cref="DemoScenes.WheelCoda.Stack"/>, in the
    /// station's frame, at the last frame of the film <see cref="FloorFilm"/>, before the order to play.
    /// </summary>
    public Double3[] StackAtFloor { get; private set; } = [];

    /// <summary>
    /// The frame earth, which does not turn, in which the station flies, and the shuttle from the hand-off of the climb
    /// until it enters the station's frame in the docking.
    /// </summary>
    public FrameHandle Earth { get; }

    /// <summary>The station's frame, which the shuttle enters in the docking.</summary>
    public FrameHandle StationFrame => World.GetOwnedFrame(Scene["station"]);

    /// <summary>Earth's G·M in earth, from its point gravity, in m³/s².</summary>
    public double Mu { get; }

    /// <summary>One <see cref="RendezvousTick"/> for each tick from the first tick of the parking orbit, in order.</summary>
    public List<RendezvousTick> Ticks { get; } = [];

    /// <summary>One <see cref="RendezvousFrame"/> for each frame of the run, in order.</summary>
    public List<RendezvousFrame> Frames { get; } = [];

    /// <summary>The lines that the world logged.</summary>
    public List<string> Logged { get; } = [];

    public void Dispose() => World.Dispose();

    private void RecordTick()
    {
        Entity shuttle = Scene["shuttle"], person = Scene["person"], station = Scene["station"];
        if (World.GetComponent<AscentAutopilot>(shuttle)!.State.Value is not Orbit)
            return;
        if (Ticks.Count > 0 && Ticks[^1].Tick == World.TickNumber)
            return;
        FrameHandle cabin = World.GetOwnedFrame(shuttle);
        DPose pose = World.PoseIn(shuttle, Earth), stationPose = World.PoseIn(station, Earth);
        PassengerState passenger = World.GetComponent<Passenger>(person)!.State;
        DPose personPose = World.PoseIn(person, cabin);
        Double3 inStation = World.PoseIn(person, StationFrame).Position;
        Ticks.Add(new(World.TickNumber, World.Time, pose.Position, World.VelocityIn(shuttle, Earth),
            pose.Rotation.Rotate(-Double3.UnitZ),
            stationPose.Position, World.VelocityIn(station, Earth), stationPose.Rotation.Rotate(Double3.UnitY),
            World.GetComponent<RendezvousAutopilot>(shuttle)!.State, passenger, World.Frames.ProperAcceleration(cabin),
            World.GetFrame(shuttle),
            PenFromHand(passenger, cabin), personPose.Rotation.Rotate(Double3.UnitY),
            BlueDanube.InEarthsShadow(World, Scene),
            World.PoseIn(Scene["sun"], Earth).Position.Normalized(),
            World.GetComponent<DockingAutopilot>(shuttle)!.State,
            World.PoseIn(shuttle, StationFrame), personPose.Position, World.IsStanding(person),
            World.Frames.Parent(cabin), World.Frames.HullMotion(cabin),
            World.GetComponent<Disembark>(person)!.State, World.GetFrame(person), inStation,
            World.VelocityIn(person, StationFrame),
            World.Frames.FeltGravity(StationFrame, inStation).Length,
            WalkHeading.Yaw(World.GetRender(person).ViewOffset.Rotation),
            World.GetComponent<BoxPlay>(person)!.State));
    }

    /// <summary>
    /// How far the drawn copy of the pen is from the person's hand, at <see cref="Passenger.PenHold"/> in its view, in
    /// meters, while the passenger holds a living copy, or null.
    /// </summary>
    private double? PenFromHand(PassengerState passenger, FrameHandle cabin)
    {
        Entity copy = CopyOf(passenger);
        if (copy.IsNull || !World.IsAlive(copy))
            return null;
        Entity person = Scene["person"];
        Double3 hand = World.PoseIn(person, cabin)
            .TransformPoint(World.GetRender(person).ViewOffset.TransformPoint(Passenger.PenHold));
        return Double3.Distance(hand, World.PoseIn(copy, cabin).Position);
    }

    /// <summary>The drawn copy of the pen that the passenger holds, or <see cref="Entity.Null"/>.</summary>
    private static Entity CopyOf(PassengerState passenger) => passenger.Value switch
    {
        Holding holding => holding.Copy,
        Returning returning => returning.Copy,
        Strapped strapped => strapped.Copy,
        Unstrapped unstrapped => unstrapped.Copy,
        _ => Entity.Null
    };

    private RendezvousFrame RecordFrame(string name)
    {
        Entity person = Scene["person"];
        FrameHandle cabin = World.GetOwnedFrame(Scene["shuttle"]);
        DPose personPose = World.PoseIn(person, cabin);
        Entity copy = CopyOf(World.GetComponent<Passenger>(person)!.State);
        return new(name, World.PoseIn(World.Camera, cabin), World.PoseIn(Scene["station"], cabin).Position, personPose,
            personPose.TransformPoint(World.GetRender(person).ViewOffset.Position),
            !copy.IsNull && World.IsAlive(copy) ? World.PoseIn(copy, cabin).Position : null, World.VerticalFov,
            World.PoseIn(Scene["sun"], cabin).Position, World.PoseIn(Scene["moon"], cabin).Position,
            World.PoseIn(Scene["earth"], cabin).Position,
            World.PoseIn(World.Camera, StationFrame), World.PoseIn(Scene["shuttle"], StationFrame));
    }

    /// <summary>A playback that records the state before each tick and what each frame's camera sees, and draws nothing.</summary>
    private sealed class Recorder(BlueDanubeRendezvousRun run) : IPlayback
    {
        public bool WaitForTick()
        {
            run.RecordTick();
            return true;
        }

        public bool Capture(World world, string name)
        {
            run.Frames.Add(run.RecordFrame(name));
            if (name.StartsWith(DockedFilm + "-", StringComparison.Ordinal))
                run.DockedTick = world.TickNumber;
            if (name.StartsWith(FloorFilm + "-", StringComparison.Ordinal))
            {
                run.FloorTick = world.TickNumber;
                run.StackAtFloor =
                [
                    .. DemoScenes.WheelCoda.Stack()
                        .Select(box => world.PoseIn(run.Scene[box.Id], run.StationFrame).Position)
                ];
            }

            return true;
        }
    }
}

/// <summary>
/// The test classes that read the one run of <see cref="BlueDanubeRendezvousRun"/>. The run takes about a minute, so they
/// share one, and they run one after another.
/// </summary>
[CollectionDefinition(BlueDanubeRendezvousRun.Collection)]
public sealed class BlueDanubeLongRunCollection : ICollectionFixture<BlueDanubeRendezvousRun>;

/// <summary>The state of the shuttle, the station and the person before one tick of the long script.</summary>
/// <param name="Tick">The number of completed ticks.</param>
/// <param name="Time">The world's time, in seconds.</param>
/// <param name="Position">The shuttle's position in earth, in meters from Earth's center.</param>
/// <param name="Velocity">The shuttle's velocity in earth, in m/s.</param>
/// <param name="Nose">The shuttle's nose, its -Z, in earth's axes.</param>
/// <param name="StationCenter">The station's center in earth, in meters.</param>
/// <param name="StationVelocity">The station's velocity in earth, in m/s.</param>
/// <param name="Axis">The station's axis, its +Y, in earth's axes.</param>
/// <param name="Rendezvous">What the rendezvous autopilot does.</param>
/// <param name="Passenger">What the person does.</param>
/// <param name="CabinAcceleration">The proper acceleration of the shuttle's frame in the last finished step, in m/s², in its axes.</param>
/// <param name="ShuttleFrame">The shuttle's frame.</param>
/// <param name="PenFromHand">How far the drawn copy of the pen is from the person's hand, in meters, while the person holds it, or null.</param>
/// <param name="PersonUp">The person's up, the +Y of its pose, in the axes of the shuttle's frame.</param>
/// <param name="InShadow">True while the cue <see cref="BlueDanube.InEarthsShadow"/> holds: the shuttle is in Earth's shadow.</param>
/// <param name="ToSun">The direction from Earth's center to the Sun's, a unit vector in earth's axes.</param>
/// <param name="Docking">What the docking autopilot does.</param>
/// <param name="InStation">The shuttle's pose in the station's frame.</param>
/// <param name="PersonInCabin">The person's center in the shuttle's frame, in meters.</param>
/// <param name="PersonStanding">True when physics stood the person in the last finished step.</param>
/// <param name="CabinParent">The parent of the shuttle's own frame, the cabin's.</param>
/// <param name="HullMotion">The hull's motion in the last finished step.</param>
/// <param name="Exit">What the person's exit component does.</param>
/// <param name="PersonFrame">The person's frame.</param>
/// <param name="PersonInStation">The person's center in the station's frame, in meters.</param>
/// <param name="PersonVelocityInStation">The person's velocity in the station's frame, in m/s.</param>
/// <param name="PersonFeltGravity">The gravity that the person feels at its center, in the station's frame, in m/s².</param>
/// <param name="PersonHeading">The person's heading: the turn of its view about its up, in radians, as
/// <see cref="WalkHeading.Yaw"/> gives it.</param>
/// <param name="Play">What the person's <see cref="BoxPlay"/> of the coda does.</param>
public sealed record RendezvousTick(
    long Tick,
    double Time,
    Double3 Position,
    Double3 Velocity,
    Double3 Nose,
    Double3 StationCenter,
    Double3 StationVelocity,
    Double3 Axis,
    RendezvousState Rendezvous,
    PassengerState Passenger,
    Vector3 CabinAcceleration,
    FrameHandle ShuttleFrame,
    double? PenFromHand,
    Double3 PersonUp,
    bool InShadow,
    Double3 ToSun,
    DockingState Docking,
    DPose InStation,
    Double3 PersonInCabin,
    bool PersonStanding,
    FrameHandle CabinParent,
    BodyMotion? HullMotion,
    DisembarkState Exit,
    FrameHandle PersonFrame,
    Double3 PersonInStation,
    Double3 PersonVelocityInStation,
    double PersonFeltGravity,
    double PersonHeading,
    BoxPlayState Play)
{
    /// <summary>The shuttle's place relative to the station's center, in meters, in earth's axes.</summary>
    public Double3 FromStation => Position - StationCenter;

    /// <summary>The shuttle's velocity relative to the station, in m/s.</summary>
    public Double3 Relative => Velocity - StationVelocity;
}

/// <summary>What the camera of one frame of the long script sees, in the shuttle's frame.</summary>
/// <param name="Name">The frame's name, such as <c>brake-000012</c>.</param>
/// <param name="View">The camera's pose in the shuttle's frame.</param>
/// <param name="Station">The station's center in the shuttle's frame, in meters.</param>
/// <param name="Person">The person's pose in the shuttle's frame.</param>
/// <param name="Eye">The person's eye in the shuttle's frame, in meters.</param>
/// <param name="Pen">The center of the drawn copy of the pen in the person's hand, in the shuttle's frame, while the person holds it, or null.</param>
/// <param name="VerticalFov">The world's vertical view angle, in radians.</param>
/// <param name="Sun">The Sun's center in the shuttle's frame, in meters.</param>
/// <param name="Moon">The Moon's center in the shuttle's frame, in meters.</param>
/// <param name="EarthCenter">Earth's center in the shuttle's frame, in meters.</param>
/// <param name="ViewInStation">The camera's pose in the station's frame.</param>
/// <param name="ShuttleInStation">The shuttle's pose in the station's frame.</param>
public sealed record RendezvousFrame(
    string Name,
    DPose View,
    Double3 Station,
    DPose Person,
    Double3 Eye,
    Double3? Pen,
    double VerticalFov,
    Double3 Sun,
    Double3 Moon,
    Double3 EarthCenter,
    DPose ViewInStation,
    DPose ShuttleInStation)
{
    /// <summary>True when the frame belongs to the film <paramref name="film"/>.</summary>
    public bool Of(string film) =>
        Name.StartsWith(film + "-", StringComparison.Ordinal) && Name.Length == film.Length + 7;
}

[Collection(BlueDanubeRendezvousRun.Collection)]
public sealed class BlueDanubeRendezvousTests(BlueDanubeRendezvousRun run)
{
    /// <summary>The ticks of the rendezvous: from the first of the parking orbit until the docking autopilot starts its spin-up.</summary>
    private IReadOnlyList<RendezvousTick> Log { get; } =
        [.. run.Ticks.TakeWhile(tick => tick.Docking.Value is Standby or Waiting)];

    private RendezvousTick Window => Log.First(tick => tick.Rendezvous.Value is Transfer);

    /// <summary>The ticks from the first in which the autopilot waits for its window, after the order.</summary>
    private IEnumerable<RendezvousTick> AfterTheOrder => Log.SkipWhile(tick => tick.Rendezvous.Value is Standby);

    private static double Degrees(Double3 a, Double3 b) =>
        Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1)) * 180 / Math.PI;

    /// <summary>
    /// The angle along the orbit, φ, in degrees from 0 to 360: about the launch plane's normal, from the pad's up, +Y of
    /// earth, toward the ramp's incline at the departure, -Z of the departure axes.
    /// </summary>
    private static double AlongTheOrbit(Double3 position)
    {
        Double3 ahead = Double3.Cross(DemoSurface.LaunchPlaneNormal, Double3.UnitY);
        double degrees = Math.Atan2(Double3.Dot(position, ahead), position.Y) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private RendezvousTick FirstOf<T>() => Log.First(tick => tick.Rendezvous.Value is T);

    /// <summary>
    /// The climb ends in the launch plane, and nothing pushes the shuttle until the transfer. So in every tick from the
    /// first of the parking orbit to the window, the shuttle is within 100 m of the plane, and slower than 0.1 m/s across it.
    /// </summary>
    [Fact]
    public void TheParkingOrbitLiesInTheLaunchPlane()
    {
        RendezvousTick[] parking = [.. Log.TakeWhile(tick => tick.Rendezvous.Value is not Transfer)];

        parking.Should().HaveCountGreaterThan(60 * 60 * 20, "the shuttle waits about 26 minutes for its window");
        parking.Max(tick => Math.Abs(Double3.Dot(tick.Position, DemoSurface.LaunchPlaneNormal))).Should()
            .BeLessThan(100);
        parking.Max(tick => Math.Abs(Double3.Dot(tick.Velocity, DemoSurface.LaunchPlaneNormal))).Should()
            .BeLessThan(0.1);
    }

    /// <summary>
    /// The script tells the person to fasten its belt before it orders the rendezvous. So at the first tick after the order
    /// in which the cabin is not weightless, the first push of the transfer's burn, the passenger is strapped at its seat.
    /// </summary>
    [Fact]
    public void ThePassengerIsStrappedBeforeTheFirstBurn()
    {
        RendezvousTick firstPush =
            AfterTheOrder.First(tick => tick.CabinAcceleration.Length() >= FrameTree.ZeroGravityThreshold);

        firstPush.Rendezvous.Value.Should()
            .BeOfType<Transfer>("the first push of the parking orbit is the transfer's burn");
        firstPush.Passenger.Value.Should().BeOfType<Strapped>();
    }

    /// <summary>
    /// The burn tips the strapped person with its head toward the nose. Once the burn's thrust leaves the cabin weightless,
    /// the trim's push stays at most half <see cref="FrameTree.ZeroGravityThreshold"/>, and the nose turns to the brake's
    /// direction at the demo plan's <see cref="RendezvousPlan.WeightlessTurnRate"/>, so the turn's felt gravity at the seat stays below
    /// half the threshold too. The cabin stays weightless, and a character's up does not turn there. So from the first
    /// weightless tick after the burn's first push to the first tick of the brake, the person's up in the cabin's axes stays
    /// within 1 degree of its up in that tick.
    /// </summary>
    [Fact]
    public void TheStrappedPassengerKeepsItsUpThroughTheTrimAfterTheBurn()
    {
        RendezvousTick[] afterTheBurn =
        [
            .. AfterTheOrder.SkipWhile(tick => tick.CabinAcceleration.Length() < FrameTree.ZeroGravityThreshold)
                .SkipWhile(tick => tick.CabinAcceleration.Length() >= FrameTree.ZeroGravityThreshold)
                .TakeWhile(tick => tick.Rendezvous.Value is Transfer)
        ];

        afterTheBurn.Should().HaveCountGreaterThan(60 * 60 * 30, "the trim and the coast last about 40 minutes");
        afterTheBurn.Should().OnlyContain(tick => tick.Passenger.Value is Strapped);
        afterTheBurn.Max(tick => Degrees(tick.PersonUp, afterTheBurn[0].PersonUp)).Should().BeLessThan(1);
    }

    /// <summary>
    /// The slow turn of the trim and the coast still ends in time: in the first tick of the brake, the nose lies within
    /// 3 degrees of the coast's direction, against the velocity relative to the station, so the brake starts nearly
    /// without a turn. It lies 2.1 degrees off (measured on 2026-10-20): at about 3,300 s the brake's direction passes 9
    /// degrees from straight down, the roof, away from Earth across the nose, swings about the nose there, and at
    /// the demo plan's <see cref="RendezvousPlan.WeightlessTurnRate"/> the nose catches up only slowly afterward.
    /// </summary>
    [Fact]
    public void TheSlowTurnBringsTheNoseToTheBrakesDirectionBeforeTheBrake()
    {
        RendezvousTick brake = FirstOf<Brake>();

        Degrees(brake.Nose, -brake.Relative).Should().BeLessThan(3);
    }

    /// <summary>
    /// <see cref="DemoScenes.DemoStation.OverPadAt"/> sets the window so that the shuttle arrives where the sun lights the
    /// station and Earth's limb below it: the transfer starts with the shuttle between 115 and 125 degrees along the orbit.
    /// </summary>
    [Fact]
    public void TheWindowOpensBetween115And125DegreesAlongTheOrbit()
    {
        AlongTheOrbit(Window.Position).Should().BeInRange(115, 125);
    }

    /// <summary>
    /// The burn puts the shuttle on the two-body path that reaches the arrival point at the arrival time, one transfer time
    /// after the window. The brake starts in the coast, where its braking curve meets the shuttle, d / u before the arrival
    /// time for a shuttle d meters from the arrival point that closes on it at u m/s. So the time from the window to the
    /// brake is the transfer time less that lead, within 2 s.
    /// </summary>
    [Fact]
    public void TheTransferTakesTheTimeThatTwoBodyMotionPredicts()
    {
        RendezvousTick window = Window, brake = FirstOf<Brake>();
        Double3 arrivalPoint = brake.StationCenter + brake.Axis * RendezvousPlan.Demo.ArrivalDistance;
        double lead = (brake.Position - arrivalPoint).Length / brake.Relative.Length;

        lead.Should().BeInRange(0, TransferGuidance.CoastTime, "the brake starts in the coast");
        (brake.Time - window.Time).Should().BeApproximately(RendezvousPlan.Demo.TransferTime - lead, 2);
    }

    /// <summary>
    /// The two-body cost of the transfer, from the shuttle's state at the window to the station's velocity at the arrival
    /// point (see <see cref="TransferCost"/>), is about 120 m/s. The burn, the trim and the brake, from the window to the
    /// glide, change the shuttle's velocity by the integral of the cabin's proper acceleration, which lies within 10
    /// percent of that cost.
    /// </summary>
    [Fact]
    public void TheTotalChangeOfVelocityIsWithin10PercentOfTheTwoBodyCost()
    {
        double cost = TransferCost(Window);
        double flown = Log.SkipWhile(tick => tick.Rendezvous.Value is not Transfer)
            .TakeWhile(tick => tick.Rendezvous.Value is not Glide)
            .Sum(tick => (double)tick.CabinAcceleration.Length()) / 60;

        cost.Should().BeInRange(100, 140, "the plan's transfer costs about 120 m/s");
        flown.Should().BeApproximately(cost, cost * 0.1);
    }

    /// <summary>
    /// The two-body cost of the transfer from the shuttle's state at <paramref name="window"/>: the velocity to gain onto
    /// Lambert's path to the arrival point one transfer time later, plus the velocity relative to the station at its end,
    /// in m/s.
    /// </summary>
    private double TransferCost(RendezvousTick window)
    {
        var guidance = new TransferGuidance(run.Mu);
        (Double3 arrival, Double3 stationAtArrival) = guidance.Arrival(window.StationCenter, window.StationVelocity,
            window.Axis,
            RendezvousPlan.Demo.ArrivalDistance, RendezvousPlan.Demo.TransferTime);
        (Double3 departure, Double3 atArrival) = KeplerOrbit.Lambert(window.Position, arrival,
            RendezvousPlan.Demo.TransferTime, run.Mu,
            TransferGuidance.OrbitNormal(window.StationCenter, window.StationVelocity));
        return (departure - window.Velocity).Length + (atArrival - stationAtArrival).Length;
    }

    /// <summary>
    /// The hold point lies outside the station's bubble, so the shuttle never enters the station's frame, and no orbit of
    /// the step comes back into earth-surface's bubble: in every tick of the step, from the parking orbit to the end of the
    /// hold, the shuttle is in earth.
    /// </summary>
    [Fact]
    public void TheShuttleStaysInEarthThroughTheWholeStep()
    {
        Log.Should().NotBeEmpty();
        Log.Should().OnlyContain(tick => tick.ShuttleFrame == run.Earth);
        Log[^1].Rendezvous.Value.Should().BeOfType<Hold>("the run ends in the hold");
    }

    /// <summary>The brake ends slower than 0.1 m/s, so the glide stays within 5 m of the station's axis all the way.</summary>
    [Fact]
    public void TheGlideStaysWithin5MOfTheAxis()
    {
        RendezvousTick[] glide = [.. Log.Where(tick => tick.Rendezvous.Value is Glide)];

        glide.Should().HaveCountGreaterThan(60 * 60, "the glide takes several minutes");
        glide.Max(tick => (tick.FromStation - tick.Axis * Double3.Dot(tick.FromStation, tick.Axis)).Length).Should()
            .BeLessThan(5);
    }

    /// <summary>
    /// In every tick of the hold, the shuttle is within 0.5 m of the hold point, slower than 0.02 m/s relative to the
    /// station, and its nose lies within 1 degree of minus the station's axis, at the slot.
    /// </summary>
    [Fact]
    public void TheShuttleHoldsAtTheHoldPointWithItsNoseAtTheSlot()
    {
        RendezvousTick[] hold = [.. Log.SkipWhile(tick => tick.Rendezvous.Value is not Hold)];

        hold.Should()
            .HaveCountGreaterThanOrEqualTo(60 * 8, "the films arrival and hold go on 8 s after the hold starts");
        hold.Should().OnlyContain(tick => tick.Rendezvous.Value is Hold);
        hold.Max(tick => (tick.FromStation - tick.Axis * RendezvousPlan.Demo.HoldDistance).Length).Should()
            .BeLessThan(RendezvousAutopilot.HoldDistanceTolerance);
        hold.Max(tick => tick.Relative.Length).Should().BeLessThan(RendezvousAutopilot.HoldSpeedTolerance);
        hold.Max(tick => Degrees(tick.Nose, -tick.Axis)).Should().BeLessThan(1);
    }

    /// <summary>
    /// In every frame of the film <c>brake</c>, from <c>port-window-camera</c>, the station's center lies in the picture,
    /// and the line from the camera to it crosses both skins inside the holes of <c>pane-port-7</c>, the aft port pane. So
    /// does the line from <c>window-camera</c> through <c>pane-nose</c> in every frame of <c>glide-window</c>. Changed on
    /// purpose by ter-4xkz: the panes of the glass canopy replace the small windows.
    /// </summary>
    [Theory]
    [InlineData("brake", "pane-port-7")]
    [InlineData("glide-window", "pane-nose")]
    public void TheStationLiesInsideTheWindowInEveryFrameOfTheWindowFilms(string film, string pane)
    {
        HullPane glass = Shuttle.Glass.Single(p => p.Id == pane);
        RendezvousFrame[] frames = [.. run.Frames.Where(frame => frame.Of(film))];

        frames.Should().NotBeEmpty();
        foreach (RendezvousFrame frame in frames)
        {
            Picture.Contains(Picture.Project(frame.View, frame.Station, frame.VerticalFov)).Should()
                .BeTrue($"the station is in the picture of {frame.Name}");
            PaneChecks.Through(glass, frame.View.Position, frame.Station).Should()
                .BeTrue($"the station lies inside {pane} in {frame.Name}");
        }
    }

    /// <summary>
    /// The film <c>hold-chase</c>, from <c>chase-camera</c> on the shuttle's starboard quarter, shows the person through the
    /// glass with the station ahead: in every frame of it, and of the first 60 frames of <c>phasing</c>, the person's eye
    /// lies in the picture, and the line from the camera to it crosses both skins inside the holes of one pane.
    /// </summary>
    [Fact]
    public void TheChaseCameraSeesThePersonThroughTheGlassInHoldChase()
    {
        RendezvousFrame[] frames =
        [
            .. run.Frames.Where(frame => frame.Of("hold-chase")),
            .. run.Frames.Where(frame => frame.Of("phasing")).Take(60)
        ];

        frames.Should().HaveCountGreaterThan(60);
        foreach (RendezvousFrame frame in frames)
        {
            Picture.Contains(Picture.Project(frame.View, frame.Eye, frame.VerticalFov)).Should()
                .BeTrue($"the person's eye is in the picture of {frame.Name}");
            Shuttle.Glass.Should().Contain(pane => PaneChecks.Through(pane, frame.View.Position, frame.Eye),
                $"the camera sees the person's eye through the glass in {frame.Name}");
        }
    }

    /// <summary>The component of <paramref name="v"/> along the axis X, Y or Z, numbered 0, 1 or 2.</summary>
    private static double Along(Double3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    /// <summary>
    /// The film <c>closing</c>, from <c>bow-camera</c> ahead of the nose, shows the shuttle and the station behind it: the
    /// station's center lies in the picture in every frame.
    /// </summary>
    [Fact]
    public void TheStationIsInThePictureOfEveryFrameOfClosing()
    {
        RendezvousFrame[] closing = [.. run.Frames.Where(frame => frame.Of("closing"))];

        closing.Should().NotBeEmpty();
        closing.Should().OnlyContain(frame =>
            Picture.Contains(Picture.Project(frame.View, frame.Station, frame.VerticalFov)));
    }

    /// <summary>
    /// The strapped passenger keeps the drawn copy of the pen in its hand: in every tick from the first push of the burn to
    /// the end of the brake, the copy lives, and it lies within 5 cm of the hand, which it follows one tick late.
    /// </summary>
    [Fact]
    public void ThePenStaysInThePassengersHandThroughTheBurns()
    {
        RendezvousTick[] burns =
        [
            .. AfterTheOrder.SkipWhile(tick => tick.CabinAcceleration.Length() < FrameTree.ZeroGravityThreshold)
                .TakeWhile(tick => tick.Rendezvous.Value is not Glide)
        ];

        burns.Should().NotBeEmpty();
        burns.Should().OnlyContain(tick => tick.Passenger.Value is Strapped);
        burns.Should().OnlyContain(tick => tick.PenFromHand != null && tick.PenFromHand < 0.05);
    }

    /// <summary>
    /// <c>cockpit-camera</c>, aft of the seat and above it, films the strapped person at its seat with the pen, and the nose
    /// window beyond it: in every frame of its films, the person's eye, the pen in its hand and the middle of
    /// <c>pane-nose</c> lie in the picture, and the person's box covers at most half of it. The person floats upside down from the take, and
    /// the burn tips it with its head toward the nose, by the up rule of every character. The trim keeps that up, and so
    /// does the glide, whose push along the nose turns it back after the turn to the slot. So the camera frames both: in
    /// every frame, the line from the camera to the pen passes outside the person's box.
    /// </summary>
    [Fact]
    public void TheCockpitCameraShowsTheStrappedPersonWithThePenAndTheNoseWindow()
    {
        Double3 noseWindow = Shuttle.Glass.Single(pane => pane.Id == "pane-nose").Center, half = DemoSurface.PersonHalf;
        RendezvousFrame[] cockpit =
        [
            .. run.Frames.Where(frame =>
                frame.Of("night") || frame.Of("burn") || frame.Of("transfer-night") || frame.Of("arrival"))
        ];

        cockpit.Should().NotBeEmpty();
        foreach (RendezvousFrame frame in cockpit)
        {
            frame.Pen.Should().NotBeNull($"the person holds the pen in {frame.Name}");
            Picture.Contains(Picture.Project(frame.View, frame.Eye, frame.VerticalFov)).Should()
                .BeTrue($"the person's eye is in the picture of {frame.Name}");
            Picture.Contains(Picture.Project(frame.View, frame.Pen!.Value, frame.VerticalFov)).Should()
                .BeTrue($"the pen is in the picture of {frame.Name}");
            Picture.Contains(Picture.Project(frame.View, noseWindow, frame.VerticalFov)).Should()
                .BeTrue($"the nose window is in the picture of {frame.Name}");
            CrossesTheBox(frame.Person.InverseTransformPoint(frame.View.Position),
                    frame.Person.InverseTransformPoint(frame.Pen!.Value), half)
                .Should().BeFalse($"the person's body hides the pen in {frame.Name}");
            Picture.Cover(frame.View, frame.Person, half, frame.VerticalFov).Should()
                .BeLessThanOrEqualTo(0.5, frame.Name);
        }
    }

    /// <summary>
    /// True when the segment from <paramref name="from"/> to <paramref name="to"/> passes through the box with half extents
    /// <paramref name="half"/> round the origin, by the slab test.
    /// </summary>
    private static bool CrossesTheBox(Double3 from, Double3 to, Double3 half)
    {
        double enter = 0, leave = 1;
        Double3 line = to - from;
        for (int axis = 0; axis < 3; axis++)
        {
            double start = Along(from, axis), step = Along(line, axis), extent = Along(half, axis);
            if (Math.Abs(step) < 1e-12)
            {
                if (Math.Abs(start) > extent)
                    return false;
                continue;
            }

            double a = (-extent - start) / step, b = (extent - start) / step;
            (enter, leave) = (Math.Max(enter, Math.Min(a, b)), Math.Min(leave, Math.Max(a, b)));
        }

        return enter <= leave;
    }

    /// <summary>
    /// The transfer's burn falls in Earth's night (docs/plans/blue-danube-demo.md, "The station's plane"): in every tick of
    /// the burn, from the first push after the order to the first weightless tick after it, the shuttle is in Earth's
    /// shadow.
    /// </summary>
    [Fact]
    public void TheBurnFallsInEarthsShadow()
    {
        RendezvousTick[] burn =
        [
            .. AfterTheOrder.SkipWhile(tick => tick.CabinAcceleration.Length() < FrameTree.ZeroGravityThreshold)
                .TakeWhile(tick => tick.CabinAcceleration.Length() >= FrameTree.ZeroGravityThreshold)
        ];

        burn.Should().HaveCountGreaterThan(60, "the burn lasts more than a second");
        burn.Should().OnlyContain(tick => tick.InShadow);
    }

    /// <summary>
    /// The arrival falls in the day: at the first tick of the brake, the station lies outside Earth's shadow, and the Sun
    /// stands at least 30 degrees above the station's horizon. The model of the design gives 55 degrees.
    /// </summary>
    [Fact]
    public void TheArrivalFallsInSunlight()
    {
        RendezvousTick brake = FirstOf<Brake>();
        double towardTheSun = Double3.Dot(brake.StationCenter, brake.ToSun);
        bool shadowed = towardTheSun < 0 &&
                        (brake.StationCenter - brake.ToSun * towardTheSun).Length < DemoScenes.EarthRadius;

        shadowed.Should().BeFalse("the station is in sunlight at the arrival");
        (90 - Degrees(brake.StationCenter, brake.ToSun)).Should()
            .BeGreaterThan(30, "the Sun stands high over the station's horizon");
    }

    /// <summary>
    /// The rim camera turns with the station and sweeps a cone round its axis, which passes near the Sun and the Moon: the
    /// Moon's center lies in the picture of some frame of <c>glide-rim</c>, and the Sun's in some frame. In those frames,
    /// the line from the camera to each misses Earth's sphere.
    /// </summary>
    [Fact]
    public void TheMoonAndTheSunPassThroughTheRimCameraInGlideRim()
    {
        RendezvousFrame[] rim = [.. run.Frames.Where(frame => frame.Of("glide-rim"))];

        rim.Should().NotBeEmpty();
        rim.Should().Contain(frame => Shows(frame, frame.Moon), "the Moon passes through the rim camera's picture");
        rim.Should().Contain(frame => Shows(frame, frame.Sun), "the Sun passes through the rim camera's picture");
    }

    /// <summary>
    /// True when <paramref name="body"/>, in the shuttle's frame, lies in the picture of <paramref name="frame"/>, and the
    /// line from the camera to it passes outside Earth's sphere.
    /// </summary>
    private static bool Shows(RendezvousFrame frame, Double3 body)
    {
        if (!Picture.Contains(Picture.Project(frame.View, body, frame.VerticalFov)))
            return false;
        Double3 from = frame.View.Position, line = body - from;
        double along = Math.Clamp(Double3.Dot(frame.EarthCenter - from, line) / line.LengthSquared, 0, 1);
        return (from + line * along - frame.EarthCenter).Length > DemoScenes.EarthRadius;
    }

    /// <summary>
    /// The world records no error, and the rendezvous autopilot logs only its halt at the hand-off into the station's
    /// frame in the docking (see <see cref="BlueDanubeDockingTests"/>).
    /// </summary>
    [Fact]
    public void TheWorldLogsNoErrors()
    {
        run.World.Errors.Should().BeEmpty();
        run.Logged.Where(line => line.Contains(nameof(RendezvousAutopilot))).Should().ContainSingle()
            .Which.Should()
            .EndWith($"{nameof(RendezvousAutopilot)} halts: the shuttle is not in the frame that holds its station.");
        Log[^1].Rendezvous.Value.Should().BeOfType<Hold>("the rendezvous holds until the docking takes over");
    }
}
