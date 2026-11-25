using System.Numerics;
using Tertius.Demo;
using Tertius.Physics;
using Tertius.Simulation;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;
using StationExit = Tertius.Demo.DemoScenes.StationExit;

namespace Tertius.Host.Tests;

/// <summary>What the exit's run recorded at the end of one tick. Places and velocities are in the station's frame.</summary>
/// <param name="Tick">The tick's number, from 1 after the order.</param>
/// <param name="Time">The world's time after the tick, in seconds.</param>
/// <param name="InCabin">True while the person is in the cabin's frame.</param>
/// <param name="InStation">True while the person is in the station's frame.</param>
/// <param name="Position">The person's center in the station's axes.</param>
/// <param name="InCabinAxes">The person's center in the cabin's axes.</param>
/// <param name="Velocity">The person's velocity in the station's frame.</param>
/// <param name="Standing">Whether physics stood the person on walkable ground in the step.</param>
/// <param name="Exit">The case of the person's <see cref="Disembark"/>.</param>
/// <param name="Belt">The case of the person's <see cref="Passenger"/>.</param>
/// <param name="FeltGravity">The spin gravity of the station at the person's center, in m/s².</param>
/// <param name="CopyHeld">True while the pen's drawn copy lives and is attached to the person.</param>
/// <param name="Rotation">The person's rotation in the station's axes.</param>
/// <param name="Facing">The way that the person faces, the -Z of its view, in the station's axes.</param>
/// <param name="Lift">The case of the lift's <see cref="Demo.Lift"/>.</param>
/// <param name="LiftPosition">The lift's pose's place, the middle of its floor's top face, in the station's axes.</param>
/// <param name="LiftVelocity">The lift's velocity in the station's frame.</param>
/// <param name="LiftInStation">True while the lift is in the station's frame.</param>
/// <param name="RideView">The pose in the station's frame of the camera that films the section <c>ride</c>.</param>
public sealed record ExitSample(
    long Tick,
    double Time,
    bool InCabin,
    bool InStation,
    Double3 Position,
    Double3 InCabinAxes,
    Double3 Velocity,
    bool Standing,
    DisembarkState Exit,
    PassengerState Belt,
    double FeltGravity,
    bool CopyHeld,
    DQuaternion Rotation,
    Double3 Facing,
    LiftState Lift,
    Double3 LiftPosition,
    Double3 LiftVelocity,
    bool LiftInStation,
    DPose RideView
);

/// <summary>
/// One run of the scene <c>demo-exit</c> from the docking's end state: the order <see cref="LeaveShip"/> before the first
/// tick, and then ticks until the exit is over and 1 s more, or 300 s at most, on the real Jolt backend without the world's
/// invariant checks. It records every tick.
/// </summary>
public sealed class ExitRun : IDisposable
{
    public ExitRun()
    {
        World = new(
            new()
            {
                MaxDegreeOfParallelism = 1,
                DebugChecks = false,
                DisposePhysics = true,
            },
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced)
        );
        World.Log += Log.Add;
        Scene = DemoScenes.DemoExit.Build(World, RenderResources.None);
        Station = World.GetOwnedFrame(Scene["station"]);
        Cabin = World.GetOwnedFrame(Scene["shuttle"]);
        Start = Record(0);
        Entity person = Scene["person"];
        World.Post(Event.Create(target: person, sender: Entity.Null, payload: new LeaveShip()));
        int after = -1;
        for (long tick = 1; tick <= 300 * 60 && after != 0; tick++)
        {
            World.Tick();
            ExitSample sample = Record(tick);
            Samples.Add(sample);
            if (after < 0 && sample.Exit.Value is Arrived or Halted)
                after = 60;
            else if (after > 0)
                after--;
        }
    }

    public World World { get; }
    public SceneInstance Scene { get; }
    public FrameHandle Station { get; }
    public FrameHandle Cabin { get; }
    public List<ExitSample> Samples { get; } = [];
    public List<string> Log { get; } = [];

    /// <summary>The state before the order and before the first tick.</summary>
    public ExitSample Start { get; }

    /// <summary>The samples from the first tick of the ride to the last tick before the stroll.</summary>
    public IEnumerable<ExitSample> Ride =>
        Samples.SkipWhile(s => s.Exit.Value is not Riding).TakeWhile(s => s.Exit.Value is Riding);

    private ExitSample Record(long tick)
    {
        Entity person = Scene["person"],
            lift = Scene["lift"];
        FrameHandle frame = World.GetFrame(person);
        DPose pose = World.PoseIn(person, Station);
        Entity copy = World.GetComponent<Passenger>(person)!.State.Value switch
        {
            Strapped strapped => strapped.Copy,
            Unstrapped unstrapped => unstrapped.Copy,
            _ => Entity.Null,
        };
        return new(
            Tick: tick,
            Time: World.Time,
            InCabin: frame == Cabin,
            InStation: frame == Station,
            Position: pose.Position,
            InCabinAxes: World.PoseIn(person, Cabin).Position,
            Velocity: World.VelocityIn(person, Station),
            Standing: World.IsStanding(person),
            Exit: World.GetComponent<Disembark>(person)!.State,
            Belt: World.GetComponent<Passenger>(person)!.State,
            FeltGravity: World.Frames.FeltGravity(Station, pose.Position).Length,
            CopyHeld: !copy.IsNull && World.IsAlive(copy) && World.Attachments.ParentOf(copy) == person,
            Rotation: pose.Rotation,
            Facing: pose.Rotation.Rotate(
                (Double3)Vector3.Transform(-Vector3.UnitZ, World.GetRender(person).ViewOffset.Rotation)
            ),
            Lift: World.GetComponent<Lift>(lift)!.State,
            LiftPosition: World.PoseIn(lift, Station).Position,
            LiftVelocity: World.VelocityIn(lift, Station),
            LiftInStation: World.GetFrame(lift) == Station,
            RideView: World.PoseIn(Scene["ride-camera"], Station)
        );
    }

    public void Dispose() => World.Dispose();
}

/// <summary>
/// The exit on the real World (docs/plans/blue-danube-demo.md, "The exit" and "The lift"): from the docking's end state,
/// the person opens its belt, walks out of the hatch and across the deck onto the lift, rides it standing down the spoke,
/// and walks off it onto the wheel's floor. Every place is in the station's axes unless it names the cabin's.
/// </summary>
public sealed class DisembarkTests(ExitRun run) : IClassFixture<ExitRun>
{
    /// <summary>When the person arrives at the stroll's end in this run, in seconds after the order: measured on 2026-11-24, and pinned so that the long run can match it.</summary>
    public const double ArrivalAfterOrder = 171.2;

    private static StationLayout Layout => DemoStation.Layout;

    /// <summary>The first sample in which <paramref name="holds"/> holds.</summary>
    private ExitSample First(Func<ExitSample, bool> holds) => run.Samples.First(holds);

    /// <summary>
    /// The person faces its way (docs/plans/blue-danube-demo.md, "The person's walk"): in the ticks of the walk out and of
    /// the stroll in which it stands and moves across its up at 0.9 m/s or more, it faces within 10 degrees of that motion
    /// in 9 ticks of 10 or more. The rest are the ticks of a turn at a waypoint. It starts facing the nose, and its way out
    /// leads aft, so without a turn it would face its back to the walk.
    /// </summary>
    [Fact]
    public void ThePersonFacesItsWayOnTheWalkOutAndTheStroll()
    {
        List<double> angles = [];
        foreach (
            ExitSample sample in run.Samples.Where(sample =>
                sample.Standing && sample.Exit.Value is Walking or Strolling
            )
        )
        {
            Double3 up = sample.Rotation.Rotate(Double3.UnitY);
            Double3 across = sample.Velocity - up * Double3.Dot(sample.Velocity, up);
            if (across.Length < 0.9)
                continue;
            Double3 facing = sample.Facing - up * Double3.Dot(sample.Facing, up);
            angles.Add(
                Math.Acos(Math.Clamp(Double3.Dot(across.Normalized(), facing.Normalized()), -1, 1)) * 180 / Math.PI
            );
        }

        angles.Should().HaveCountGreaterThan(600, "the walk out and the stroll take more than 10 s at 1 m/s");
        angles.Count(angle => angle < 10).Should().BeGreaterThanOrEqualTo(angles.Count * 9 / 10);
    }

    /// <summary>
    /// The scene starts at the docking's end state: the hull is static at <see cref="StationLayout.ParkedPose"/>, its cabin's
    /// frame is a child of the station's, and the person is <see cref="Strapped"/> at its seat with the pen in its hand,
    /// waiting <see cref="Aboard"/>, with its up against the felt gravity. The felt gravity at the seat stand is 0.494 m/s²,
    /// ω² times the seat's 98.2 m from the axis, leaning 1.7 degrees toward port.
    /// </summary>
    [Fact]
    public void TheSceneStartsAtTheDockingsEndState()
    {
        World world = run.World;
        Entity shuttle = run.Scene["shuttle"];
        world.GetFrame(shuttle).Should().Be(run.Station);
        world.Frames.Parent(run.Cabin).Should().Be(run.Station, "the cabin's frame is a child of the station's");
        world.Frames.HullMotion(run.Cabin).Should().Be(BodyMotion.Static, "the docked hull is static");
        (world.PoseIn(shuttle, run.Station).Position - Layout.ParkedPose.Position).Length.Should().BeLessThan(1e-6);
        ExitSample start = run.Start;
        start.InCabin.Should().BeTrue();
        Double3 felt = world.Frames.FeltGravity(run.Cabin, Shuttle.SeatStand);
        (start.InCabinAxes - DemoScenes.DemoExit.StandAtSeat.Position)
            .Length.Should()
            .BeLessThan(0.01, "the person stands at its seat");
        (DemoScenes.DemoExit.FeltAtSeat - felt)
            .Length.Should()
            .BeLessThan(1e-3, "the scene's felt gravity at the seat is the frame tree's");
        Double3 up = start.Rotation.Rotate(Double3.UnitY),
            inCabin = world.Frames.Relative(run.Station, run.Cabin).Rotation.Rotate(up);
        (inCabin + felt.Normalized())
            .Length.Should()
            .BeLessThan(0.01, "the person's up stands against the felt gravity");
        start.Belt.Value.Should().BeOfType<Strapped>();
        start.CopyHeld.Should().BeTrue("the person holds the pen");
        start.Exit.Value.Should().Be(new Aboard());
        felt.Length.Should().BeApproximately(0.494, 0.005);
        (Math.Acos(Double3.Dot(felt, -Double3.UnitY) / felt.Length) * 180 / Math.PI).Should().BeApproximately(1.7, 0.3);
    }

    /// <summary>
    /// In the tick that handles the order, the second after its post, the passenger opens its belt and the exit starts its
    /// walk. Before it, the person is strapped and aboard.
    /// </summary>
    [Fact]
    public void TheOrderOpensTheBeltAndStartsTheWalk()
    {
        ExitSample first = run.Samples[1];
        run.Samples[0].Belt.Value.Should().BeOfType<Strapped>();
        run.Samples[0].Exit.Value.Should().Be(new Aboard());
        first.Belt.Value.Should().BeOfType<Unstrapped>("the order opens the belt");
        first.Exit.Value.Should().BeOfType<Walking>("the order starts the walk");
        first.CopyHeld.Should().BeTrue();
    }

    /// <summary>
    /// The person walks aft through the cabin on a floor whose felt gravity leans 1 to 2.5 degrees from the floor's down
    /// toward port, -X in the cabin's axes. The scene starts the person with its up against that gravity, and its capsule
    /// settles on the floor in the first quarter second, by a few millimeters. From then on it stands in every tick of the
    /// leg, its center keeps within 0.25 m of the leg's line across the floor and within 3 cm of the height at which its
    /// capsule, tipped with its up, stands on the floor, and it reaches the leg's end.
    /// </summary>
    [Fact]
    public void ThePersonWalksAftOnTheCabinsLeaningFloor()
    {
        Double3 from = Shuttle.SeatStand,
            to = Shuttle.InsideHatch;
        Double3 line = new Double3(to.X - from.X, 0, to.Z - from.Z).Normalized();
        int start = run.Samples.FindIndex(s => s.Exit.Value is Walking);
        ExitSample[] walk =
        [
            .. run.Samples.Skip(start).TakeWhile(s => Double3.Distance(s.InCabinAxes, to) > Walker.ArrivalRadius),
        ];
        ExitSample[] leg = [.. walk.Where(s => s.Tick > 15)];
        walk.Take(15)
            .Should()
            .OnlyContain(
                s => Math.Abs(s.InCabinAxes.Y - DemoScenes.DemoExit.StandAtSeat.Position.Y) < 0.01,
                "the capsule settles by a few millimeters"
            );
        leg.Length.Should().BeGreaterThan(200, "the walk aft takes about 6 s");
        foreach (ExitSample s in leg)
        {
            Double3 felt = run.World.Frames.FeltGravity(run.Cabin, s.InCabinAxes);
            double lean = Math.Acos(-felt.Y / felt.Length) * 180 / Math.PI;
            lean.Should().BeInRange(1, 2.5, $"the floor feels tilted at tick {s.Tick}");
            felt.X.Should().BeNegative($"the floor feels tilted toward port at tick {s.Tick}");
            s.Standing.Should().BeTrue($"the person stands on the floor at tick {s.Tick}");
            Double3 across = new(s.InCabinAxes.X - from.X, 0, s.InCabinAxes.Z - from.Z);
            (across - line * Double3.Dot(across, line))
                .Length.Should()
                .BeLessThan(0.25, $"the person keeps to the leg's line at tick {s.Tick}");
            double floor = from.Y - 0.9,
                stand =
                    floor + StationExit.PersonRadius + DemoScenes.DemoExit.CapsuleHalfHeight * -felt.Y / felt.Length;
            s.InCabinAxes.Y.Should()
                .BeApproximately(
                    stand,
                    0.03,
                    $"the person's capsule, tipped with its up, stands on the floor at tick {s.Tick}"
                );
        }

        run.Samples[start + walk.Length]
            .InCabinAxes.Z.Should()
            .BeApproximately(to.Z, Walker.ArrivalRadius + 0.05, "the walk aft reaches the doorway's line");
    }

    /// <summary>
    /// The person leaves the hull through the hatch and the porch: its center crosses the hull's starboard face, the
    /// porch's open end, x = 6.92 m in the cabin's axes, in the doorway's lane, with its whole capsule inside the
    /// doorway's width.
    /// </summary>
    [Fact]
    public void ThePersonLeavesThroughTheHatch()
    {
        BoundingBox hatch = Shuttle.Hatch;
        ExitSample outside = First(s => s.InCabinAxes.X > Shuttle.Hull.Max.X);
        ExitSample inside = run.Samples[run.Samples.IndexOf(outside) - 1];
        inside.InCabinAxes.X.Should().BeLessThanOrEqualTo(Shuttle.Hull.Max.X);
        outside
            .InCabinAxes.Z.Should()
            .BeInRange(hatch.Min.Z + 0.3, hatch.Max.Z - 0.3, "the capsule fits the doorway's width");
        outside.Exit.Value.Should().BeOfType<Walking>();
    }

    /// <summary>
    /// The person walks down the deck's ramp and along the deck onto the lift, through its open +Y side, all in the lift's
    /// lane along the axis. From 1 s past the ramp's top it stands in every tick, but over the gap of 5 cm round the lift's
    /// floor in its hole, where the capsule's foot, 0.3 m in radius, dips 0.9 mm and touches neither floor for 4 ticks. On
    /// the ramp its center stays 0.9 m above the ramp's slope, within 4 cm, and on the deck and the lift 0.9 m above the
    /// deck, within 3 cm. Its center keeps within 0.25 m of the line from the landing to the lift's middle across the axis,
    /// and it stops within the walker's stop radius of that middle.
    /// </summary>
    [Fact]
    public void ThePersonWalksDownTheRampAndAlongTheDeckOntoTheLift()
    {
        Waypoint[] way = StationExit.WayOut(run.Cabin, run.Station);
        Double3 landing = way[2].Position,
            middle = way[^1].Position,
            top = StationExit.StairPlace + StationExit.Stair.OnLanding(above: 0);
        double rampTop = top.Y - BoardingStair.Width / 2,
            rampFoot = rampTop - StationExit.Stair.Run,
            slope = Math.Tan(BoardingStair.RampSlope);
        ExitSample[] down =
        [
            .. run
                .Samples.Where(s => s.Exit.Value is Walking)
                .SkipWhile(s => s.Position.Z < landing.Z - 0.5)
                .SkipWhile(s => s.Position.Y > rampTop - 1),
        ];
        down.Should().HaveCountGreaterThan(18 * 60, "the walk down the ramp and along the deck takes about 20 s");
        double gapEdge = Layout.LiftFootprint.Max.Y;
        foreach (ExitSample s in down)
        {
            bool overGap = s.Position.Y < gapEdge + Layout.LiftHoleGap + 0.3 && s.Position.Y > gapEdge - 0.3;
            if (!overGap)
                s.Standing.Should().BeTrue($"the person stands on the ramp or the deck at tick {s.Tick}");
            bool onRamp = s.Position.Y > rampFoot;
            double under = Math.Max(Layout.Deck, top.X - (rampTop - s.Position.Y) * slope);
            s.Position.X.Should()
                .BeApproximately(
                    under + StationExit.Standing / (onRamp ? Math.Cos(BoardingStair.RampSlope) : 1),
                    onRamp ? 0.04 : 0.03,
                    $"on the ramp or the deck at tick {s.Tick}"
                );
            double lane = landing.Z + (middle.Z - landing.Z) * (landing.Y - s.Position.Y) / (landing.Y - middle.Y);
            Math.Abs(s.Position.Z - lane)
                .Should()
                .BeLessThan(0.25, $"the person keeps to the lift's lane at tick {s.Tick}, at {s.Position}");
        }

        ExitSample boarded = run.Samples.First(s => s.Exit.Value is Riding);
        Double3 off = boarded.Position - middle;
        new Double3(0, off.Y, off.Z)
            .Length.Should()
            .BeLessThan(Walker.StopRadius + 0.02, "it stops in the lift's middle");
        boarded.Standing.Should().BeTrue();
    }

    /// <summary>
    /// No step of the way out throws the person up. The way has three places where the person leaves its ground for a
    /// moment: the threshold's ramp onto the hatch's sill, the porch's lip onto the landing, 3.5 cm down, and the ramp's
    /// top. Each time it leaves the ground for at most 0.8 s and rises at most 2 cm above the higher of its stances before
    /// and after. Without the threshold, the climb of the sill's 0.1 m step threw the person 0.67 m up for 3.8 s in the
    /// deck's gravity, because the engine's climb of a stair turns the walk into the plane of the edge it touches. A gap
    /// of 0.3 m before a landing flush with the porch threw it 0.15 m up.
    /// </summary>
    [Fact]
    public void TheWayOutThrowsThePersonUpNowhere()
    {
        ExitSample[] walk = [.. run.Samples.Where(s => s.Exit.Value is Walking).SkipWhile(s => s.Tick <= 30)];
        static double Height(ExitSample s) => -Math.Sqrt(s.Position.X * s.Position.X + s.Position.Z * s.Position.Z);
        int flights = 0;
        for (int leave = Array.FindIndex(walk, s => !s.Standing); leave > 0; flights++)
        {
            int land = Array.FindIndex(walk, leave, s => s.Standing);
            land.Should().BePositive("the person lands");
            ExitSample[] flight = walk[leave..land];
            double stance = Math.Max(Height(walk[leave - 1]), Height(walk[land]));
            (flight.Length / 60.0).Should().BeLessThanOrEqualTo(0.8, $"the flight from tick {flight[0].Tick} is short");
            flight
                .Max(s => Height(s) - stance)
                .Should()
                .BeLessThan(0.02, $"the flight from tick {flight[0].Tick} rises no more than 2 cm");
            leave = Array.FindIndex(walk, land, s => !s.Standing);
        }

        flights.Should().BePositive("the person leaves its ground at the sill, the lip and the ramp's top");
    }

    /// <summary>
    /// The person orders the lift down once it stops in the lift's middle: the lift leaves the top in the tick after the
    /// exit starts its ride, and not before.
    /// </summary>
    [Fact]
    public void ThePersonOrdersTheLiftDownOnceItStandsOnIt()
    {
        int riding = run.Samples.FindIndex(s => s.Exit.Value is Riding);
        run.Samples.Take(riding + 1).Should().OnlyContain(s => s.Lift.Value is AtTop, "the lift waits at the top");
        run.Samples[riding + 1].Lift.Value.Should().BeOfType<Descending>("the order reaches the lift in the next tick");
    }

    /// <summary>
    /// The lift follows its profile in the station's axes, in every tick of its ride, to 1 mm: its place along the spoke
    /// from the top, and its speed. It starts in the parked shuttle's frame, whose bubble covers its place on the deck, and
    /// ends in the station's.
    /// </summary>
    [Fact]
    public void TheLiftFollowsItsProfileInTheStationsAxes()
    {
        LiftProfile ride = StationExit.Ride;
        ExitSample[] descent = [.. run.Samples.Where(s => s.Lift.Value is Descending)];
        double start = ((Descending)descent[0].Lift.Value).Start;

        descent.Should().NotBeEmpty();
        run.Samples[0].LiftInStation.Should().BeFalse("the parked shuttle's bubble holds the lift at the top");
        foreach (ExitSample s in descent)
        {
            double elapsed = s.Time - start;
            Double3 expected =
                StationExit.LiftTop.Position + StationExit.LiftOutward * (ride.Distance(elapsed) - ride.Top);
            (s.LiftPosition - expected)
                .Length.Should()
                .BeLessThan(1e-3, $"the lift follows its profile at {elapsed} s");
            (s.LiftVelocity - StationExit.LiftOutward * ride.Speed(elapsed))
                .Length.Should()
                .BeLessThan(1e-3, $"the lift's speed follows its profile at {elapsed} s");
        }

        run.Samples[^1].LiftInStation.Should().BeTrue("the lift leaves the shuttle's bubble in the shaft");
        run.Samples[^1].Lift.Value.Should().Be(new AtFoot());
        (run.Samples[^1].LiftPosition.X + ride.Foot).Should().BeApproximately(0, 1e-3, "the lift rests at the foot");
    }

    /// <summary>
    /// The person stands on the lift's floor in every tick of the ride but the two of its hand-off from the cabin's frame
    /// into the station's, where the engine reports a character as not standing (a known fault). It never takes the rigid
    /// form: the felt gravity at its place stays above twice the zero-gravity threshold. Its velocity is the lift's, so
    /// across the floor it keeps within 2 cm of the place where it boarded. Along the spoke its center stays 0.9 m above
    /// the current floor, including the step after the lift changes frames.
    /// </summary>
    [Fact]
    public void ThePersonStandsOnTheLiftForTheWholeRide()
    {
        ExitSample[] ride = [.. run.Ride];
        Double3 boarded = ride[0].Position - ride[0].LiftPosition;
        int handOff = Array.FindIndex(ride, s => s.InStation);

        ride.Should().HaveCountGreaterThan(125 * 60, "the ride takes over two minutes");
        handOff.Should().BePositive();
        for (int i = 0; i < ride.Length; i++)
        {
            ExitSample s = ride[i];
            if (i != handOff && i != handOff + 1)
                s.Standing.Should()
                    .BeTrue($"the person stands on the lift at {s.Time - ride[0].Time:F2} s into the ride");
            s.FeltGravity.Should()
                .BeGreaterThan(2 * FrameTree.ZeroGravityThreshold, "the person keeps the walking form");
            Double3 onFloor = s.Position - s.LiftPosition;
            new Double3(0, onFloor.Y - boarded.Y, onFloor.Z - boarded.Z)
                .Length.Should()
                .BeLessThan(0.02, $"the person keeps its place on the floor at {s.Time - ride[0].Time:F2} s");
            onFloor
                .X.Should()
                .BeApproximately(
                    StationExit.Standing,
                    0.02,
                    "the floor supports the rider at the same simulation time"
                );
        }
    }

    /// <summary>The person moves from the cabin's frame into the station's once, on the ride, and stays there.</summary>
    [Fact]
    public void ThePersonEntersTheStationsFrameOnTheRide()
    {
        int enter = run.Samples.FindIndex(s => s.InStation);
        enter.Should().BePositive();
        run.Samples.Take(enter).Should().OnlyContain(s => s.InCabin);
        run.Samples.Skip(enter).Should().OnlyContain(s => s.InStation);
        run.Samples[enter].Exit.Value.Should().BeOfType<Riding>("the person leaves the shuttle's bubble in the shaft");
    }

    /// <summary>
    /// The person arrives at the stroll's end about three minutes after the order: 36 s of walk to the lift, down the porch
    /// and the deck's ramp, 131.3 s of ride and 3.9 s of stroll.
    /// </summary>
    [Fact]
    public void ThePersonArrivesAboutThreeMinutesAfterTheOrder() =>
        First(s => s.Exit.Value is Arrived).Time.Should().BeApproximately(ArrivalAfterOrder, 0.5);

    /// <summary>
    /// The ride takes the profile's time, 131 s, from the deck at 102.2 m from the axis to the foot, at 20 m/s at most,
    /// below the 50 m/s at which a numeric frame takes a group with a character.
    /// </summary>
    [Fact]
    public void TheRideTakesAboutTwoMinutesAtTwentyMetersASecond()
    {
        ExitSample[] ride = [.. run.Ride];
        double time = ride[^1].Time - ride[0].Time;
        time.Should().BeInRange(125, 140);
        time.Should().BeApproximately(StationExit.Ride.Duration, 0.1);
        ride.Max(s => s.Velocity.Length).Should().BeInRange(19.9, 20.01);
    }

    /// <summary>
    /// The rider keeps at least half its weight: in the boost the floor accelerates outward at half the spin gravity, so the
    /// weight that the floor holds along the spoke, the spin gravity at the person less the floor's acceleration, stays
    /// above 0.45 of the spin gravity in every tick of the ride. Across the spoke the floor also holds the Coriolis term of
    /// the ride, 2 ω v, up to 2.8 m/s² toward antispinward at 20 m/s, where a railing stands.
    /// </summary>
    [Fact]
    public void TheRiderKeepsAtLeastHalfItsWeight()
    {
        ExitSample[] ride = [.. run.Ride];
        for (int i = 1; i < ride.Length - 1; i++)
        {
            double acceleration =
                    (ride[i + 1].LiftVelocity.X - ride[i - 1].LiftVelocity.X) / (ride[i + 1].Time - ride[i - 1].Time),
                spin = Layout.Rate * Layout.Rate * -ride[i].Position.X,
                weight = spin + acceleration;
            weight
                .Should()
                .BeGreaterThan(0.45 * spin, $"the rider keeps its weight at {ride[i].Time - ride[0].Time:F2} s");
        }

        double coriolis = 2 * Layout.Rate * ride.Max(s => s.Velocity.Length);
        coriolis.Should().BeApproximately(2.8, 0.01);
    }

    /// <summary>
    /// The film <c>ride</c> sees the person on the lift: in every tick of the ride, the person's center lies in the ride
    /// camera's picture, 2 to 6 m in front of it.
    /// </summary>
    [Fact]
    public void TheRideFilmSeesThePersonOnTheLift()
    {
        BlueDanube.ExitSections.Single(section => section.Name == "ride").Camera.Should().Be("ride-camera");
        foreach (ExitSample s in run.Ride)
        {
            Double3 seen = Picture.Project(s.RideView, s.Position, run.World.VerticalFov);
            Picture.Contains(seen).Should().BeTrue($"the ride camera sees the person at {s.Time:F2} s");
            seen.Z.Should().BeInRange(2, 6);
        }
    }

    /// <summary>
    /// The lift stops at the foot with its floor a step above the wheel's floor, and the person steps off it: the stroll
    /// starts in the tick that the lift's stop reaches the person, and the person ends standing on the wheel's floor.
    /// </summary>
    [Fact]
    public void ThePersonStepsOffTheLiftOntoTheWheelsFloor()
    {
        int stroll = run.Samples.FindIndex(s => s.Exit.Value is Strolling);
        stroll.Should().BePositive();
        run.Samples[stroll - 1].Lift.Value.Should().Be(new AtFoot(), "the lift stopped before the stroll");
        ExitSample end = run.Samples[^1];
        end.Standing.Should().BeTrue();
        Layout
            .WheelFloor.Height(end.Position)
            .Should()
            .BeApproximately(StationExit.Standing, 0.03, "on the wheel's floor");
    }

    [Fact]
    public void ArrivalLeavesTheDeckGateShutAndTheRimGateClearOfThePassenger()
    {
        double lane = DemoScenes.DemoStation.Layout.LiftAcross;
        run.World.PoseIn(run.Scene["deck-gate"], run.Station).Position.Z.Should().BeApproximately(lane, 0.005);
        run.World.PoseIn(run.Scene["rim-gate"], run.Station).Position.Z.Should().BeGreaterThan(lane + 1.5);
    }

    /// <summary>
    /// At the end the person stands on the wheel's floor and feels a full gravity at its center: ω² r, 0.9 m inside the
    /// floor's 9.80665 m/s², 9.8022 m/s², within 0.1 percent.
    /// </summary>
    [Fact]
    public void ThePersonStandsOnTheWheelsFloorInAFullGravity()
    {
        ExitSample end = run.Samples[^1];
        end.Exit.Value.Should().Be(new Arrived());
        end.InStation.Should().BeTrue();
        double r = Math.Sqrt(end.Position.X * end.Position.X + end.Position.Z * end.Position.Z);
        end.FeltGravity.Should().BeApproximately(StationLayout.StandardGravity * r / Layout.FloorRadius, 1e-6);
        end.FeltGravity.Should().BeApproximately(9.8022, 0.0098);
    }

    /// <summary>On the floor the person stops at the stroll's end: within 0.1 m of it, still for the last half second.</summary>
    [Fact]
    public void ThePersonWalksOffTheLiftAndStops()
    {
        Double3 end = StationExit.Stroll(run.Station)[^1].Position;
        (run.Samples[^1].Position - end).Length.Should().BeLessThan(0.1);
        run.Samples.TakeLast(30).Should().OnlyContain(s => s.Velocity.Length < 0.05, "the person stands still");
    }

    /// <summary>The pen's copy stays in the person's hand from the seat to the floor.</summary>
    [Fact]
    public void ThePenStaysInThePersonsHand() =>
        run.Samples.Should().OnlyContain(s => s.CopyHeld, "the pen stays in the person's hand");

    /// <summary>The exit logs nothing, and the world records no error.</summary>
    [Fact]
    public void TheExitLogsNothingAndTheWorldHasNoErrors()
    {
        run.Log.Should().BeEmpty();
        run.World.Errors.Should().BeEmpty();
    }
}

/// <summary>
/// Short runs of the scene <c>demo-exit</c> that change one thing about the exit: no order, or a station whose frame is
/// gone. Each builds its own World on the real Jolt backend.
/// </summary>
public sealed class DisembarkShortRunTests : IDisposable
{
    private readonly World world = new(
        new()
        {
            MaxDegreeOfParallelism = 1,
            DebugChecks = false,
            DisposePhysics = true,
        },
        new JoltPhysicsBackend(JoltBackendOptions.Unpaced)
    );

    private readonly List<string> log = [];
    private readonly SceneInstance scene;
    private readonly FrameHandle cabin;

    public DisembarkShortRunTests()
    {
        world.Log += log.Add;
        scene = DemoScenes.DemoExit.Build(world, RenderResources.None);
        cabin = world.GetOwnedFrame(scene["shuttle"]);
    }

    private Entity Person => scene["person"];

    public void Dispose() => world.Dispose();

    /// <summary>
    /// Without the order the person stays strapped at its seat, aboard, for 2 s, and the lift waits at the top. The
    /// person's capsule, which starts with its up against the cabin's leaning gravity, settles by a few millimeters in the
    /// first second, and then holds still.
    /// </summary>
    [Fact]
    public void WithoutTheOrderThePersonStaysAtItsSeat()
    {
        for (int i = 0; i < 60; i++)
            world.Tick();
        Double3 settled = world.PoseIn(Person, cabin).Position;
        for (int i = 0; i < 60; i++)
            world.Tick();
        world.GetComponent<Disembark>(Person)!.State.Value.Should().Be(new Aboard());
        world.GetComponent<Passenger>(Person)!.State.Value.Should().BeOfType<Strapped>();
        world.GetComponent<Lift>(scene["lift"])!.State.Value.Should().Be(new AtTop());
        Double3 end = world.PoseIn(Person, cabin).Position;
        (end - DemoScenes.DemoExit.StandAtSeat.Position)
            .Length.Should()
            .BeLessThan(0.02, "the person stays at its seat");
        (end - settled).Length.Should().BeLessThan(0.01, "the person holds still");
        log.Should().BeEmpty();
    }

    /// <summary>
    /// When the station's frame dies during the walk, the ride or the stroll, the exit halts with one log line, and adds
    /// nothing after that.
    /// </summary>
    [Theory]
    [InlineData(nameof(Walking))]
    [InlineData(nameof(Riding))]
    [InlineData(nameof(Strolling))]
    public void TheExitHaltsWhenTheStationIsGone(string during)
    {
        world.Post(Event.Create(target: Person, sender: Entity.Null, payload: new LeaveShip()));
        for (
            int i = 0;
            i < 300 * 60 && world.GetComponent<Disembark>(Person)!.State.Value.GetType().Name != during;
            i++
        )
            world.Tick();
        world.GetComponent<Disembark>(Person)!.State.Value.GetType().Name.Should().Be(during);
        world.Destroy(scene["station"]).Should().BeTrue();
        for (int i = 0; i < 60; i++)
            world.Tick();
        world.GetComponent<Disembark>(Person)!.State.Value.Should().Be(new Halted());
        log.Should().ContainSingle(line => line.Contains("Disembark halts"));
    }
}
