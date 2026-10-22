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
/// <param name="InCabin">True while the person is in the cabin's frame.</param>
/// <param name="InStation">True while the person is in the station's frame.</param>
/// <param name="Position">The person's center in the station's axes.</param>
/// <param name="InCabinAxes">The person's center in the cabin's axes.</param>
/// <param name="Velocity">The person's velocity in the station's frame.</param>
/// <param name="Standing">Whether physics stood the person on walkable ground in the step.</param>
/// <param name="Exit">The case of the person's <see cref="Disembark"/>.</param>
/// <param name="Belt">The case of the person's <see cref="Passenger"/>.</param>
/// <param name="FeltGravity">The gravity that the person feels at its center, in m/s².</param>
/// <param name="CopyHeld">True while the pen's drawn copy lives and is attached to the person.</param>
/// <param name="Rotation">The person's rotation in the station's axes.</param>
/// <param name="ChuteView">The pose in the station's frame of the camera that films the section <c>chute</c>.</param>
/// <param name="ChuteViewClear">True when a ray from that camera to the person's center meets no body but the person's.</param>
/// <param name="Facing">The way that the person faces, the -Z of its view, in the station's axes.</param>
public sealed record ExitSample(
    long Tick,
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
    DPose ChuteView,
    bool ChuteViewClear,
    Double3 Facing);

/// <summary>
/// One run of the scene <c>demo-exit</c> from the docking's end state: the order <see cref="LeaveShip"/> before the first
/// tick, and then ticks until the exit is over and 1 s more, or 120 s at most, on the real Jolt backend without the world's
/// invariant checks. It records every tick.
/// </summary>
public sealed class ExitRun : IDisposable
{
    private readonly JoltPhysicsBackend backend = new();

    public ExitRun()
    {
        World = new(new() { MaxDegreeOfParallelism = 1, DebugChecks = false, DisposePhysics = true }, backend);
        World.Log += Log.Add;
        Scene = DemoScenes.DemoExit.Build(World, RenderResources.None);
        Station = World.GetOwnedFrame(Scene["station"]);
        Cabin = World.GetOwnedFrame(Scene["shuttle"]);
        Start = Record(0);
        Entity person = Scene["person"];
        World.Post(Event.Create(person, Entity.Null, new LeaveShip()));
        int after = -1;
        for (long tick = 1; tick <= 120 * 60 && after != 0; tick++)
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

    private ExitSample Record(long tick)
    {
        Entity person = Scene["person"];
        FrameHandle frame = World.GetFrame(person);
        DPose pose = World.PoseIn(person, Station);
        Double3 position = pose.Position;
        DPose view = World.PoseIn(Scene[BlueDanube.ExitSections.Single(section => section.Name == "chute").Camera],
            Station);
        Double3 sight = position - view.Position;
        var hits = new List<RayCastHit>();
        backend.CastRay(Station, view.Position, sight.Normalized(), 0, sight.Length, new AllBut(person),
            RayCastKind.Nearest, hits);
        Entity copy = World.GetComponent<Passenger>(person)!.State.Value switch
        {
            Strapped strapped => strapped.Copy,
            Unstrapped unstrapped => unstrapped.Copy,
            _ => Entity.Null
        };
        return new(tick, frame == Cabin, frame == Station, position, World.PoseIn(person, Cabin).Position,
            World.VelocityIn(person, Station),
            World.IsStanding(person), World.GetComponent<Disembark>(person)!.State,
            World.GetComponent<Passenger>(person)!.State,
            World.Frames.FeltGravity(Station, position).Length,
            !copy.IsNull && World.IsAlive(copy) && World.Attachments.ParentOf(copy) == person,
            pose.Rotation, view, hits.Count == 0,
            pose.Rotation.Rotate(
                (Double3)Vector3.Transform(-Vector3.UnitZ, World.GetRender(person).ViewOffset.Rotation)));
    }

    private sealed class AllBut(Entity left) : IPhysicsQueryFilter
    {
        public bool Accepts(Entity entity) => entity != left;
    }

    public void Dispose() => World.Dispose();
}

/// <summary>
/// The exit on the real World (docs/plans/blue-danube-demo.md, "The exit"): from the docking's end state, the person
/// opens its belt, walks out of the hatch and along the deck, steps into the chute's top, slides down the spoke and stands
/// on the wheel's floor. Every place is in the station's axes unless it names the cabin's.
/// </summary>
public sealed class DisembarkTests(ExitRun run) : IClassFixture<ExitRun>
{
    private static Chute Chute => StationExit.Chute;

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
        foreach (ExitSample sample in run.Samples.Where(sample =>
                     sample.Standing && sample.Exit.Value is Walking or Strolling))
        {
            Double3 up = sample.Rotation.Rotate(Double3.UnitY);
            Double3 across = sample.Velocity - up * Double3.Dot(sample.Velocity, up);
            if (across.Length < 0.9)
                continue;
            Double3 facing = sample.Facing - up * Double3.Dot(sample.Facing, up);
            angles.Add(Math.Acos(Math.Clamp(Double3.Dot(across.Normalized(), facing.Normalized()), -1, 1)) * 180 /
                       Math.PI);
        }

        angles.Should().HaveCountGreaterThan(600, "the walk out and the stroll take more than 10 s at 1 m/s");
        angles.Count(angle => angle < 10).Should().BeGreaterThanOrEqualTo(angles.Count * 9 / 10);
    }

    /// <summary>
    /// The scene starts at the docking's end state: the hull is static at <see cref="DemoStation.ParkedPose"/>, its cabin's
    /// frame is a child of the station's, and the person is <see cref="Strapped"/> at its seat with the pen in its hand,
    /// waiting <see cref="Aboard"/>, with its up against the felt gravity. The felt gravity at the seat stand is 0.783 m/s²,
    /// leaning 30 degrees toward port.
    /// </summary>
    [Fact]
    public void TheSceneStartsAtTheDockingsEndState()
    {
        World world = run.World;
        Entity shuttle = run.Scene["shuttle"];
        world.GetFrame(shuttle).Should().Be(run.Station);
        world.Frames.Parent(run.Cabin).Should().Be(run.Station, "the cabin's frame is a child of the station's");
        world.Frames.HullMotion(run.Cabin).Should().Be(BodyMotion.Static, "the docked hull is static");
        (world.PoseIn(shuttle, run.Station).Position - DemoStation.ParkedPose.Position).Length.Should()
            .BeLessThan(1e-6);
        ExitSample start = run.Start;
        start.InCabin.Should().BeTrue();
        Double3 felt = world.Frames.FeltGravity(run.Cabin, Shuttle.SeatStand);
        (start.InCabinAxes - DemoScenes.DemoExit.StandAtSeat.Position).Length.Should()
            .BeLessThan(0.01, "the person stands at its seat");
        (DemoScenes.DemoExit.FeltAtSeat - felt).Length.Should()
            .BeLessThan(1e-3, "the scene's felt gravity at the seat is the frame tree's");
        Double3 up = start.Rotation.Rotate(Double3.UnitY),
            inCabin = world.Frames.Relative(run.Station, run.Cabin).Rotation.Rotate(up);
        (inCabin + felt.Normalized()).Length.Should()
            .BeLessThan(0.01, "the person's up stands against the felt gravity");
        start.Belt.Value.Should().BeOfType<Strapped>();
        start.CopyHeld.Should().BeTrue("the person holds the pen");
        start.Exit.Value.Should().Be(new Aboard());
        felt.Length.Should().BeApproximately(0.783, 0.03);
        (Math.Acos(Double3.Dot(felt, -Double3.UnitY) / felt.Length) * 180 / Math.PI).Should().BeApproximately(30, 2);
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
    /// The person walks aft through the cabin on a floor whose felt gravity leans 22 to 33 degrees from the floor's down
    /// toward port, -X in the cabin's axes. The scene starts the person with its up against that gravity, and its capsule
    /// settles on the floor in the first quarter second, by a few millimeters. From then on it stands in every tick of the
    /// leg, its center keeps within 0.25 m of the leg's line across the floor and within 3 cm of the height at which its
    /// capsule, tipped with its up, stands on the floor, and it reaches the leg's end.
    /// </summary>
    [Fact]
    public void ThePersonWalksAftOnTheCabinsLeaningFloor()
    {
        Double3 from = Shuttle.SeatStand, to = Shuttle.InsideHatch;
        Double3 line = new Double3(to.X - from.X, 0, to.Z - from.Z).Normalized();
        int start = run.Samples.FindIndex(s => s.Exit.Value is Walking);
        ExitSample[] walk =
            [.. run.Samples.Skip(start).TakeWhile(s => Double3.Distance(s.InCabinAxes, to) > Walker.ArrivalRadius)];
        ExitSample[] leg = [.. walk.Where(s => s.Tick > 15)];
        walk.Take(15).Should()
            .OnlyContain(s => Math.Abs(s.InCabinAxes.Y - DemoScenes.DemoExit.StandAtSeat.Position.Y) < 0.01,
                "the capsule settles by a few millimeters");
        leg.Length.Should().BeGreaterThan(200, "the walk aft takes about 6 s");
        foreach (ExitSample s in leg)
        {
            Double3 felt = run.World.Frames.FeltGravity(run.Cabin, s.InCabinAxes);
            double lean = Math.Acos(-felt.Y / felt.Length) * 180 / Math.PI;
            lean.Should().BeInRange(22, 33, $"the floor feels tilted at tick {s.Tick}");
            felt.X.Should().BeNegative($"the floor feels tilted toward port at tick {s.Tick}");
            s.Standing.Should().BeTrue($"the person stands on the floor at tick {s.Tick}");
            Double3 across = new(s.InCabinAxes.X - from.X, 0, s.InCabinAxes.Z - from.Z);
            (across - line * Double3.Dot(across, line)).Length.Should()
                .BeLessThan(0.25, $"the person keeps to the leg's line at tick {s.Tick}");
            double floor = from.Y - 0.9,
                stand = floor + StationExit.PersonRadius +
                        DemoScenes.DemoExit.CapsuleHalfHeight * -felt.Y / felt.Length;
            s.InCabinAxes.Y.Should().BeApproximately(stand, 0.03,
                $"the person's capsule, tipped with its up, stands on the floor at tick {s.Tick}");
        }

        run.Samples[start + walk.Length].InCabinAxes.Z.Should().BeApproximately(to.Z, Walker.ArrivalRadius + 0.05,
            "the walk aft reaches the doorway's line");
    }

    /// <summary>
    /// The person leaves the cabin through the hatch: its center crosses the hull's starboard face, x = 3 m in the cabin's
    /// axes, within the doorway, with its whole capsule inside the doorway's width.
    /// </summary>
    [Fact]
    public void ThePersonLeavesThroughTheHatch()
    {
        BoundingBox hatch = Shuttle.Hatch;
        ExitSample outside = First(s => s.InCabinAxes.X > Shuttle.Hull.X);
        ExitSample inside = run.Samples[run.Samples.IndexOf(outside) - 1];
        inside.InCabinAxes.X.Should().BeLessThanOrEqualTo(Shuttle.Hull.X);
        outside.InCabinAxes.Z.Should()
            .BeInRange(hatch.Min.Z + 0.3, hatch.Max.Z - 0.3, "the capsule fits the doorway's width");
        outside.Exit.Value.Should().BeOfType<Walking>();
    }

    /// <summary>
    /// On the deck the person walks along the axis to the point beside the chute's top and then antispinward toward it. It
    /// stands on both legs, and its center keeps within 0.25 m of each leg's line.
    /// </summary>
    [Fact]
    public void ThePersonWalksAlongTheDeckToTheChutesTop()
    {
        ExitSample[] deck =
        [
            .. run.Samples.Where(s =>
                s.Exit.Value is Walking && s.Position.Z > -1.5 && s.Position.Y < 4.5 && s.Position.X > DemoStation.Deck)
        ];
        deck.Should().NotBeEmpty();
        ExitSample[] along =
                [.. deck.Where(s => s.Position.Y > 0.3)],
            toward = [.. deck.Where(s => s.Position.Y <= 0.3 && s.Position.Z < 0.4 && s.Standing)];
        along.Should().NotBeEmpty();
        toward.Should().NotBeEmpty();
        foreach (ExitSample s in along)
        {
            s.Standing.Should().BeTrue($"the person stands on the deck at tick {s.Tick}");
            Math.Abs(s.Position.Z - 0.6).Should()
                .BeLessThan(0.25, $"the walk along the axis keeps to z = 0.6 m at tick {s.Tick}");
        }

        foreach (ExitSample s in toward)
            Math.Abs(s.Position.Y).Should()
                .BeLessThan(0.25, $"the walk into the chute's top keeps to y = 0 at tick {s.Tick}");
    }

    /// <summary>
    /// The step into the chute's top, antispinward, goes at <see cref="StationExit.StepInSpeed"/> from its first tick, so
    /// the Coriolis force of the step leaves the person enough weight to sink through the hole: while the person stands on
    /// the deck, its speed toward -Z stays within 0.05 m/s of the step-in speed.
    /// </summary>
    [Fact]
    public void TheStepIntoTheChutesTopGoesAtTheStepInSpeedFromItsFirstTick()
    {
        ExitSample[] deck =
        [
            .. run.Samples.Where(s => s.Exit.Value is Walking && s.Standing && s.InCabinAxes.X > Shuttle.Hull.X + 0.5)
        ];
        deck.Should().NotBeEmpty();
        ExitSample fastest = deck.MaxBy(s => -s.Velocity.Z)!;
        (-fastest.Velocity.Z).Should().BeLessThan(StationExit.StepInSpeed + 0.05,
            $"the person steps toward the chute's top at tick {fastest.Tick}, at {fastest.Position}");
    }

    /// <summary>The person goes off its feet over the chute's top, and the exit starts its slide.</summary>
    [Fact]
    public void ThePersonDropsIntoTheChute()
    {
        ExitSample sliding = First(s => s.Exit.Value is Sliding);
        Math.Abs(sliding.Position.Y).Should().BeLessThan(Chute.HalfWidth, "the person is between the rails");
        sliding.Position.Z.Should().BeInRange(Chute.BackFace, Chute.BackFace + Chute.Depth,
            "the person is over the chute's top");
        sliding.Standing.Should().BeFalse();
    }

    /// <summary>
    /// The person moves from the cabin's frame into the station's once, in the spoke, below the chute's top and above
    /// the ceiling, while it slides, and stays in the station's frame from then on.
    /// </summary>
    [Fact]
    public void ThePersonEntersTheStationsFrameInTheSpoke()
    {
        ExitSample entry = First(s => s.InStation);
        entry.Exit.Value.Should().BeOfType<Sliding>();
        entry.Position.X.Should().BeInRange(-DemoStation.CeilingRadius, DemoStation.Deck - 0.5,
            "the person enters the station's frame in the spoke");
        run.Samples.Skip(run.Samples.IndexOf(entry)).Should()
            .OnlyContain(s => s.InStation, "the person stays in the station's frame");
        run.Samples.Should().OnlyContain(s => s.InCabin || s.InStation, "no numeric frame takes the person");
    }

    /// <summary>
    /// From 3 m below the deck to the ceiling, the Coriolis force holds the person on the back plate: its center lies
    /// from its radius to its radius plus <see cref="Chute.ContactMargin"/> off the plate's face, where the chute's law
    /// counts it as touching, and its capsule lies between the rails.
    /// </summary>
    [Fact]
    public void TheCoriolisForceHoldsThePersonOnTheBackPlate()
    {
        ExitSample[] spoke =
        [
            .. run.Samples.Where(s =>
                s.Exit.Value is Sliding && s.Position.X < DemoStation.Deck - 3 &&
                s.Position.X > -DemoStation.CeilingRadius)
        ];
        spoke.Length.Should().BeGreaterThan(60, "the slide down the spoke takes many ticks");
        foreach (ExitSample s in spoke)
        {
            (s.Position.Z - Chute.BackFace).Should().BeInRange(StationExit.PersonRadius,
                StationExit.PersonRadius + Chute.ContactMargin,
                $"the person lies on the back plate at tick {s.Tick}, x = {s.Position.X:F2} m");
            Math.Abs(s.Position.Y).Should().BeLessThanOrEqualTo(Chute.HalfWidth - StationExit.PersonRadius + 0.01,
                $"the person lies between the rails at tick {s.Tick}");
        }
    }

    /// <summary>
    /// The chute's friction holds the slide's top speed between 8 and 10 m/s, far below the 50 m/s at which a numeric
    /// frame takes a thrusting group.
    /// </summary>
    [Fact]
    public void TheSlideStaysBelowTheSpeedOfANumericFrame()
    {
        double top = run.Samples.Where(s => s.Exit.Value is Sliding).Max(s => s.Velocity.Length);
        top.Should().BeInRange(8, 10, "the friction holds the slide near 8.9 m/s");
    }

    /// <summary>
    /// The film <c>chute</c> sees the person slide down the open trough. From 10 m below the deck to 10 m above the
    /// ceiling, as the section runs, its camera stays in the spoke's inside, 0.2 m from its walls, and above the trough's
    /// open side. It keeps 1.5 to 4 m from the person's center, the person's whole box lies in its picture and covers 1 to
    /// 30 percent of it, and no body lies between the camera and the person's center.
    /// </summary>
    [Fact]
    public void TheChuteFilmSeesThePersonSlideDownTheOpenTrough()
    {
        ExitSample[] film =
        [
            .. run.Samples.Where(s =>
                s.Exit.Value is Sliding && s.Position.X < DemoStation.Deck - 10 &&
                s.Position.X > -(DemoStation.CeilingRadius - 10))
        ];
        film.Length.Should().BeGreaterThan(600, "the slide down the spoke takes many ticks");
        double inside = DemoStation.SpokeHalfWidth - DemoStation.SpokeWall - 0.2;
        foreach (ExitSample s in film)
        {
            Double3 camera = s.ChuteView.Position;
            Math.Abs(camera.Y).Should().BeLessThan(inside, $"the camera is in the spoke at tick {s.Tick}");
            Math.Abs(camera.Z).Should().BeLessThan(inside, $"the camera is in the spoke at tick {s.Tick}");
            camera.Z.Should().BeGreaterThan(Chute.BackFace + StationExit.RailDepth,
                $"the camera is above the trough's open side at tick {s.Tick}");
            Double3.Distance(camera, s.Position).Should()
                .BeInRange(1.5, 4, $"the camera keeps its distance at tick {s.Tick}");
            double cover = Picture.Cover(s.ChuteView, new(s.Position, s.Rotation), DemoScenes.DemoSurface.PersonHalf,
                run.World.VerticalFov);
            cover.Should().BeInRange(0.01, 0.3, $"the person fills part of the picture at tick {s.Tick}");
            for (int corner = 0; corner < 8; corner++)
            {
                var half = (Double3)DemoScenes.DemoSurface.PersonHalf;
                var local = new Double3((corner & 1) == 0 ? -half.X : half.X, (corner & 2) == 0 ? -half.Y : half.Y,
                    (corner & 4) == 0 ? -half.Z : half.Z);
                Picture.Contains(Picture.Project(s.ChuteView, new DPose(s.Position, s.Rotation).TransformPoint(local),
                        run.World.VerticalFov))
                    .Should().BeTrue($"the person's whole box is in the picture at tick {s.Tick}");
            }

            s.ChuteViewClear.Should().BeTrue($"nothing hides the person at tick {s.Tick}");
        }
    }

    /// <summary>The brake run slows the person below 3.5 m/s before its feet reach the wheel's floor.</summary>
    [Fact]
    public void TheBrakeRunSlowsThePersonBeforeTheFloor()
    {
        ExitSample[] last =
            [.. run.Samples.Where(s => s.Exit.Value is Sliding && s.Position.X < -(DemoStation.FloorRadius - 1.5))];
        last.Should().NotBeEmpty("the person slides down to the floor");
        last.Max(s => s.Velocity.Length).Should().BeLessThan(3.5);
    }

    /// <summary>
    /// The person lands on its feet on the wheel's floor: the stroll starts in the tick after a step in which the person
    /// stood on the floor, between the rails.
    /// </summary>
    [Fact]
    public void ThePersonLandsOnItsFeetOnTheWheelsFloor()
    {
        int start = run.Samples.FindIndex(s => s.Exit.Value is Strolling);
        start.Should().BePositive();
        ExitSample landed = run.Samples[start - 1];
        landed.Exit.Value.Should().BeOfType<Sliding>();
        landed.Standing.Should().BeTrue();
        double radius = Math.Sqrt(landed.Position.X * landed.Position.X + landed.Position.Z * landed.Position.Z);
        radius.Should().BeInRange(DemoStation.FloorRadius - 1, DemoStation.FloorRadius,
            "the person stands on the floor");
        Math.Abs(landed.Position.Y).Should().BeLessThan(Chute.HalfWidth, "the person lands between the rails");
    }

    /// <summary>
    /// At the end the person stands on the wheel's floor, 99.1 m from the axis, and feels a full gravity there:
    /// ω² × 99.1 m = 9.72 m/s², within 1 percent.
    /// </summary>
    [Fact]
    public void ThePersonStandsOnTheWheelsFloorInAFullGravity()
    {
        ExitSample end = run.Samples[^1];
        end.Exit.Value.Should().Be(new Arrived());
        end.Standing.Should().BeTrue();
        end.InStation.Should().BeTrue();
        end.FeltGravity.Should().BeApproximately(9.72, 0.0972);
    }

    /// <summary>
    /// On the floor the person walks spinward, out of the trough's open side, and stops at the stroll's end: within
    /// 0.1 m of it, still for the last half second.
    /// </summary>
    [Fact]
    public void ThePersonWalksAFewStepsSpinwardAndStops()
    {
        Double3 end = StationExit.Stroll(run.Station)[0].Position;
        ExitSample[] stroll = [.. run.Samples.Where(s => s.Exit.Value is Strolling)];
        stroll.Should().NotBeEmpty();
        stroll[^1].Position.Z.Should().BeGreaterThan(stroll[0].Position.Z + 4, "the person walks spinward, toward +Z");
        ExitSample last = run.Samples[^1];
        (last.Position - end).Length.Should().BeLessThan(0.1, "the person stops at the stroll's end");
        run.Samples.TakeLast(30).Should()
            .OnlyContain(s => s.Velocity.Length < 0.05, "the person stands still at the end");
    }

    /// <summary>The pen's copy stays in the person's hand from the seat to the floor.</summary>
    [Fact]
    public void ThePenStaysInThePersonsHand() =>
        run.Samples.Should().OnlyContain(s => s.CopyHeld, "the person never lets go of the pen");

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
    private readonly World world = new(new() { MaxDegreeOfParallelism = 1, DebugChecks = false, DisposePhysics = true },
        new JoltPhysicsBackend());

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
    /// Without the order the person stays strapped at its seat, aboard, for 2 s. Its capsule, which starts with its up
    /// against the cabin's leaning gravity, settles by a few millimeters in the first second, and then holds still.
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
        Double3 end = world.PoseIn(Person, cabin).Position;
        (end - DemoScenes.DemoExit.StandAtSeat.Position).Length.Should()
            .BeLessThan(0.02, "the person stays at its seat");
        (end - settled).Length.Should().BeLessThan(0.01, "the person holds still");
        log.Should().BeEmpty();
    }

    /// <summary>
    /// A person who stands again above the ceiling has not landed, and the slide asks it for no walk. A test component
    /// puts the sliding person back on the deck, 3 m along the axis from the chute's top. There it stands, and the exit
    /// stays in <see cref="Sliding"/>. The person also holds still, because the slide replaced the step into the chute's
    /// top, which physics keeps, with no walk.
    /// </summary>
    [Fact]
    public void StandingAboveTheCeilingIsNoLandingAndTheSlideAsksForNoWalk()
    {
        FrameHandle station = world.GetOwnedFrame(scene["station"]);
        world.Post(Event.Create(Person, Entity.Null, new LeaveShip()));
        for (int i = 0; i < 120 * 60 && world.GetComponent<Disembark>(Person)!.State.Value is not Sliding; i++)
            world.Tick();
        world.GetComponent<Disembark>(Person)!.State.Value.Should().BeOfType<Sliding>();
        world.GetFrame(Person).Should().Be(cabin, "the person drops in the cabin's frame");
        Double3 onTheDeck = world.Frames.Relative(station, cabin).TransformPoint(new(DemoStation.Deck + 0.9, 3, 0.6));
        world.AddComponent(Person, new PutOnce(new(onTheDeck, world.PoseIn(Person, cabin).Rotation)));
        for (int i = 0; i < 60; i++)
            world.Tick();
        Double3 settled = world.PoseIn(Person, station).Position;
        for (int i = 0; i < 60; i++)
            world.Tick();
        world.IsStanding(Person).Should().BeTrue("the person stands on the deck");
        world.GetComponent<Disembark>(Person)!.State.Value.Should()
            .BeOfType<Sliding>("a person on the deck has not landed");
        (world.PoseIn(Person, station).Position - settled).Length.Should()
            .BeLessThan(0.02, "the slide asks for no walk");
    }

    /// <summary>A test component that teleports its entity to a pose in its frame once, in its first tick.</summary>
    private sealed class PutOnce(DPose pose) : IComponent
    {
        private bool done;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (done)
                return;
            done = true;
            self.Body.Teleport(pose);
        }
    }

    /// <summary>
    /// When the station's frame dies during the walk, the slide or the stroll, the exit halts with one log line, and adds
    /// nothing after that.
    /// </summary>
    [Theory]
    [InlineData(nameof(Walking))]
    [InlineData(nameof(Sliding))]
    [InlineData(nameof(Strolling))]
    public void TheExitHaltsWhenTheStationIsGone(string during)
    {
        world.Post(Event.Create(Person, Entity.Null, new LeaveShip()));
        for (int i = 0;
             i < 120 * 60 && world.GetComponent<Disembark>(Person)!.State.Value.GetType().Name != during;
             i++)
            world.Tick();
        world.GetComponent<Disembark>(Person)!.State.Value.GetType().Name.Should().Be(during);
        world.Destroy(scene["station"]).Should().BeTrue();
        for (int i = 0; i < 60; i++)
            world.Tick();
        world.GetComponent<Disembark>(Person)!.State.Value.Should().Be(new Halted());
        log.Should().ContainSingle(line => line.Contains("Disembark halts"));
    }
}
