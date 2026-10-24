using Tertius.Demo;
using Tertius.Physics;
using Tertius.Simulation;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using SpinStation = Tertius.Demo.DemoScenes.SpinStation;
using WheelCoda = Tertius.Demo.DemoScenes.WheelCoda;

namespace Tertius.Host.Tests;

/// <summary>What the coda's run recorded at the end of one tick. Places and velocities are in the station's frame.</summary>
/// <param name="Tick">The tick's number, from 1 after the order to play.</param>
/// <param name="Person">The person's center.</param>
/// <param name="Standing">Whether physics stood the person on walkable ground in the step.</param>
/// <param name="Play">The case of the person's <see cref="BoxPlay"/>.</param>
/// <param name="Pen">The case of the person's <see cref="Passenger"/>.</param>
/// <param name="Stack">The center and the speed of each box of the stack.</param>
/// <param name="ThrowBoxes">Whether each box to throw still lives, and its center while it lives.</param>
/// <param name="Thrown">The center of each thrown box so far.</param>
public sealed record CodaSample(
    long Tick,
    Double3 Person,
    bool Standing,
    BoxPlayState Play,
    PassengerState Pen,
    (Double3 Place, double Speed)[] Stack,
    (bool Alive, Double3 Place)[] ThrowBoxes,
    Double3[] Thrown);

/// <summary>
/// One run of the scene <c>demo-exit</c> through the exit and the coda: the order <see cref="LeaveShip"/>, ticks until the
/// person arrives on the wheel's floor and 2 s more, as the film <c>floor</c> holds, then the order
/// <see cref="PlayWithBoxes"/>, and ticks until the play is over and both thrown boxes have landed, and 3 s more, or 90 s
/// at most, on the real Jolt backend without the world's invariant checks. It records every tick of the coda.
/// </summary>
public sealed class CodaRun : IDisposable
{
    public CodaRun()
    {
        World = new(new() { MaxDegreeOfParallelism = 1, DebugChecks = false, DisposePhysics = true },
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced));
        World.Log += Log.Add;
        Scene = DemoScenes.DemoExit.Build(World, RenderResources.None);
        Station = World.GetOwnedFrame(Scene["station"]);
        Stack = [.. WheelCoda.Stack().Select(box => Scene[box.Id])];
        Entity person = Scene["person"];
        World.Post(Event.Create(target: person, sender: Entity.Null, payload: new LeaveShip()));
        int after = -1;
        for (int tick = 0; tick < 120 * 60 && after != 0; tick++)
        {
            World.Tick();
            if (after < 0 && World.GetComponent<Disembark>(person)!.State.Value is Arrived)
                after = 120;
            else if (after > 0)
                after--;
        }

        PenCopy = World.GetComponent<Passenger>(person)!.State.Value is Unstrapped { Copy: var copy }
            ? copy
            : Entity.Null;
        Start = Record(0);
        World.Post(Event.Create(target: person, sender: Entity.Null, payload: new PlayWithBoxes()));
        after = -1;
        for (long tick = 1; tick <= 90 * 60 && after != 0; tick++)
        {
            World.Tick();
            CodaSample sample = Record(tick);
            Samples.Add(sample);
            var play = World.GetComponent<BoxPlay>(person)!;
            for (int i = ThrownTurns.Count; i < play.Thrown.Count; i++)
                ThrownTurns.Add(World.PoseIn(play.Thrown[i], Station).Rotation);
            bool landed = play.Thrown.Count == BoxPlayState.Throws &&
                          play.Thrown.All(box => World.GetComponent<FlightRecorder>(box)!.Landing.HasValue);
            if (after < 0 && (sample.Play.Value is Halted || (sample.Play.Value is Played && landed)))
                after = 180;
            else if (after > 0)
                after--;
        }
    }

    public World World { get; }
    public SceneInstance Scene { get; }
    public FrameHandle Station { get; }
    public List<CodaSample> Samples { get; } = [];
    public List<string> Log { get; } = [];

    /// <summary>The turn of each thrown box in the station's axes, at the end of its first tick, in the order of the throws.</summary>
    public List<DQuaternion> ThrownTurns { get; } = [];

    /// <summary>The stack's boxes, in the order of <see cref="WheelCoda.Stack"/>.</summary>
    public Entity[] Stack { get; }

    /// <summary>The state at the order to play, before its tick.</summary>
    public CodaSample Start { get; }

    /// <summary>The pen's drawn copy in the person's hand before the order to play.</summary>
    public Entity PenCopy { get; }

    /// <summary>The play of the person.</summary>
    public BoxPlay Play => World.GetComponent<BoxPlay>(Scene["person"])!;

    /// <summary>The flight recorder of the box of throw <paramref name="index"/>.</summary>
    public FlightRecorder Flight(int index) => World.GetComponent<FlightRecorder>(Play.Thrown[index])!;

    private CodaSample Record(long tick)
    {
        Entity person = Scene["person"];
        var play = World.GetComponent<BoxPlay>(person)!;
        return new(Tick: tick, Person: World.PoseIn(person, Station).Position, Standing: World.IsStanding(person),
            Play: play.State,
            Pen: World.GetComponent<Passenger>(person)!.State,
            Stack:
            [
                .. Stack.Select(box => (World.PoseIn(box, Station).Position, World.VelocityIn(box, Station).Length))
            ],
            ThrowBoxes:
            [
                .. Enumerable.Range(0, BoxPlayState.Throws).Select(i => Scene[WheelCoda.ThrowBoxId(i)]).Select(box =>
                    World.IsAlive(box) ? (true, World.PoseIn(box, Station).Position) : (false, default(Double3)))
            ],
            Thrown: [.. play.Thrown.Where(World.IsAlive).Select(box => World.PoseIn(box, Station).Position)]);
    }

    public void Dispose() => World.Dispose();
}

/// <summary>
/// The coda on the real World (docs/plans/blue-danube-demo.md, "The coda"): after the exit, the person puts the pen away,
/// walks into a stack of loose boxes and knocks it over, and throws two loose boxes the same way, one spinward and one
/// antispinward. Each lands where the straight line of its release in inertial space says, short of a flat field's point
/// spinward and long of it antispinward. Every place is in the station's axes.
/// </summary>
public sealed class WheelCodaTests(CodaRun run) : IClassFixture<CodaRun>
{
    private static WheelFloor Floor => DemoStation.WheelFloor;

    private static double Gravity => SpinStation.Rate * SpinStation.Rate * Floor.Radius;

    /// <summary>The first sample in which <paramref name="holds"/> holds.</summary>
    private CodaSample First(Func<CodaSample, bool> holds) => run.Samples.First(holds);

    /// <summary>
    /// The loose boxes spawn at rest on the floor with the scene, and when the coda starts, after the whole exit, each rests
    /// within 1 cm of its spawn place, still. A box that a step had left floating, or that the exit had woken and moved,
    /// would lie elsewhere.
    /// </summary>
    [Fact]
    public void TheLooseBoxesRestWhereTheySpawnedWhenTheCodaStarts()
    {
        (string Id, DPose Pose)[] stack = [.. WheelCoda.Stack()];
        for (int i = 0; i < stack.Length; i++)
        {
            Double3.Distance(run.Start.Stack[i].Place, stack[i].Pose.Position).Should()
                .BeLessThan(0.01, $"{stack[i].Id} rests where it spawned");
            run.Start.Stack[i].Speed.Should().BeLessThan(1e-3, $"{stack[i].Id} is still");
        }

        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            run.Start.ThrowBoxes[i].Alive.Should().BeTrue();
            Double3.Distance(run.Start.ThrowBoxes[i].Place, WheelCoda.ThrowBoxPose(i).Position).Should()
                .BeLessThan(0.01, $"the box of throw {i} rests where it spawned");
        }

        run.Start.Play.Value.Should().Be(new Idle());
    }

    [Fact]
    public void TheOrderPutsThePenAwayAndStartsTheWalkIntoTheStack()
    {
        run.PenCopy.IsNull.Should().BeFalse("the person holds the pen's copy on the floor");
        CodaSample order = First(s => s.Play.Value is not Idle);
        order.Tick.Should().BeLessThanOrEqualTo(2, "the order arrives with the next tick's messages");
        order.Pen.Value.Should().Be(new PenAway(), "the pen goes in the tick of the order");
        order.Play.TryGetValue(out Shoving shoving).Should().BeTrue();
        shoving.Walk.Next.Should().Be(0);
        shoving.Walk.Pace.Should().BeApproximately(Walker.StartAcceleration / 60, 1e-6,
            "the walk starts from rest in the tick of the order, one tick at the start limit");
        run.World.IsAlive(run.PenCopy).Should().BeFalse("the pen's copy goes");
    }

    /// <summary>
    /// The person walks into the stack and through its middle. The top box falls to the floor, and the middle box of the
    /// floor's row and both boxes on it are pushed or knocked away. The side boxes of the floor's row stand clear of the
    /// person's way. By the time the person takes the first box to throw, every box of the stack has settled.
    /// </summary>
    [Fact]
    public void ThePersonKnocksTheStackOverAndItsBoxesSettle()
    {
        CodaSample taking = First(s => s.Play.Value is Taking);
        Double3 top = taking.Stack[5].Place;
        Floor.Height(top).Should().BeLessThan(WheelCoda.BoxHalf + 0.15, "the top box lies on the floor");
        foreach (int moved in new[] { 1, 3, 4, 5 })
            Double3.Distance(taking.Stack[moved].Place, run.Start.Stack[moved].Place).Should()
                .BeGreaterThan(0.3, $"stack-{moved} is knocked away");
        taking.Stack.Max(box => box.Speed).Should().BeLessThan(0.05, "the boxes have settled");
        taking.Stack.Should().OnlyContain(box => Math.Abs(box.Place.Y) < 9.4, "the boxes stay inside the side walls");
    }

    [Fact]
    public void ThePersonWalksToTheStandAndTakesEachBoxToThrowByUseAndOffer()
    {
        CodaSample first = First(s => s.Play.Value is Taking);
        Double3.Distance(first.Person, WheelCoda.Stand).Should()
            .BeLessThan(Walker.StopRadius + 0.02, "the person stands at the stand");
        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            CodaSample aiming = First(s => s.Play.Value is Aiming { Throw: var index } && index == i);
            CodaSample taking = First(s => s.Play.Value is Taking { Throw: var index } && index == i);
            (aiming.Tick - taking.Tick).Should().BeLessThanOrEqualTo(Crate.AnswerTicks + 1,
                "the use reaches the box, the box offers itself, and the person takes the offer, a tick for each message");
            (aiming.Tick - taking.Tick).Should().BeGreaterThan(0);
            run.Samples[(int)aiming.Tick].ThrowBoxes[i].Alive.Should()
                .BeFalse($"the box of throw {i} goes once it is taken");
        }
    }

    /// <summary>
    /// Each throw leaves at the throw speed relative to the floor, 30 degrees above it, spinward for the first and
    /// antispinward for the second, along the lane, from about 2 m above the floor: where a box clears the capsule along
    /// the aim from the eye.
    /// </summary>
    [Fact]
    public void EachThrowLeavesTheSameWayAt8MetersPerSecond30DegreesUp()
    {
        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            ThrowRelease release = run.Flight(i).Release!.Value;
            release.Velocity.Length.Should()
                .BeApproximately(WheelCoda.ThrowSpeed, 0.02, $"throw {i} leaves at the throw speed");
            Double3 up = WheelFloor.UpAt(release.Place);
            Math.Asin(Double3.Dot(release.Velocity.Normalized(), up)).Should()
                .BeApproximately(WheelCoda.ThrowAngle, 0.01, $"throw {i} leaves 30 degrees up");
            Math.Sign(Double3.Dot(release.Velocity, WheelFloor.SpinwardAt(release.Place))).Should().Be(i == 0 ? 1 : -1);
            Math.Abs(release.Velocity.Y).Should().BeLessThan(0.02, "the throw runs along the lane");
            Floor.Height(release.Place).Should()
                .BeInRange(1.8, 2.2, "the box starts above the eye, clear of the capsule");
            Math.Abs(release.Place.Y - WheelCoda.LaneY).Should().BeLessThan(0.1);
        }
    }

    /// <summary>
    /// Each thrown box flies free and lands where the straight line of its release in inertial space brings it down to the
    /// floor, to 1 mm: the spinward throw short of the flat field's point by more than 1.5 m, and the antispinward throw long
    /// of it by more than 3 m. The flight recorder measures the landing from the box's own free places.
    /// </summary>
    [Fact]
    public void EachThrownBoxLandsWhereTheStraightLineOfItsReleaseSays()
    {
        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            FlightRecorder flight = run.Flight(i);
            ThrowRelease release = flight.Release!.Value;
            ThrowLanding measured = flight.Landing!.Value;
            ThrowLanding line = release.Landing(SpinStation.Rate, Floor, WheelCoda.BoxHalf);
            double flat = release.FlatLanding(Gravity, Floor, WheelCoda.BoxHalf).Spinward;
            double run1 = Floor.Spinward(release.Place, measured.Place),
                predicted = Floor.Spinward(release.Place, line.Place);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"throw {i}: from {release.Place} at {release.Velocity}; lands {run1:F4} m spinward after {measured.Time:F4} s; the line says {predicted:F4} m after {line.Time:F4} s; a flat field {flat:F4} m");

            Double3.Distance(measured.Place, line.Place).Should()
                .BeLessThan(1e-3, $"throw {i} lands where its line meets the floor");
            measured.Time.Should().BeApproximately(line.Time, 1e-3);
            if (i == 0)
                run1.Should().BeLessThan(flat - 1.5, "spinward, the box lands short of the flat field's point");
            else
                run1.Should().BeLessThan(-Math.Abs(flat) - 3,
                    "antispinward, the box lands long of the flat field's point");
        }
    }

    /// <summary>
    /// Each throw stays in its lane, clear of the chute's trough, below the ceiling 5 m up, and inside the lit stretch of the
    /// floor round the chute's end, z from -12 to 16 m.
    /// </summary>
    [Fact]
    public void TheThrowsStayInTheirLaneUnderTheCeilingAndInTheLight()
    {
        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            FlightRecorder flight = run.Flight(i);
            ThrowRelease release = flight.Release!.Value;
            double apex = Enumerable.Range(0, (int)(flight.Landing!.Value.Time * 1000))
                .Max(ms => Floor.Height(release.PlaceAt(ms / 1000.0, SpinStation.Rate)));
            apex.Should().BeLessThan(5 - 0.5 - WheelCoda.BoxHalf * Math.Sqrt(3), $"throw {i} passes below the ceiling");
            flight.Landing.Value.Place.Z.Should().BeInRange(-12, 16, $"throw {i} lands in the light");
            Math.Abs(flight.Landing.Value.Place.Y - WheelCoda.LaneY).Should()
                .BeLessThan(0.1, $"throw {i} lands in its lane, 2 m from the trough's line");
        }
    }

    /// <summary>Each floor mark lies within 6 cm of where a flat field lands the run's own release: the stand lets the person stop up to 5 cm off it.</summary>
    [Fact]
    public void TheFloorMarksLieWhereAFlatFieldLandsEachThrow()
    {
        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            ThrowRelease release = run.Flight(i).Release!.Value;
            double flat = release.FlatLanding(Gravity, Floor, WheelCoda.BoxHalf).Spinward;
            Double3 mark = run.World.PoseIn(run.Scene[$"flat-mark-{i}"], run.Station).Position;
            Floor.Spinward(release.Place, mark).Should()
                .BeApproximately(flat, 0.06, $"the mark of throw {i} lies at the flat field's point");
            Floor.Height(mark).Should().BeApproximately(0.004, 1e-4, "the mark, 8 mm thick, lies flat on the floor");
        }
    }

    [Fact]
    public void ThePersonStandsThroughoutThePlay() =>
        run.Samples.Where(s => s.Play.Value is Taking or Aiming).Should().OnlyContain(s => s.Standing,
            "the floor holds the person through the takes, the throws and their recoil");

    /// <summary>
    /// Each thrown box starts turned so that its faces lie flat on the facet where it lands: its +X along that facet's
    /// outward normal, and its +Y along the axis. So it lands on a face, not on an edge.
    /// </summary>
    [Fact]
    public void EachThrownBoxStartsTurnedFlatToTheFacetWhereItLands()
    {
        run.ThrownTurns.Should().HaveCount(BoxPlayState.Throws);
        for (int i = 0; i < BoxPlayState.Throws; i++)
        {
            Double3 landing = run.Flight(i).Landing!.Value.Place;
            Double3.Distance(run.ThrownTurns[i].Rotate(Double3.UnitX), Floor.Normal(landing)).Should()
                .BeLessThan(1e-6, $"throw {i} lies flat to its landing's facet");
            Double3.Distance(run.ThrownTurns[i].Rotate(Double3.UnitY), Double3.UnitY).Should().BeLessThan(1e-6);
        }
    }

    /// <summary>
    /// Every loose box, the thrown ones too, and every mark is drawn with no impostor, as every part of the station is, so
    /// a small box far from a camera keeps its real geometry. The boxes also cast shadows, as every crate does.
    /// </summary>
    [Fact]
    public void EveryBoxAndMarkOfTheCodaIsDrawnWithNoImpostor()
    {
        Entity[] boxes = [.. run.Stack, .. run.Play.Thrown];
        boxes.Should().HaveCount(6 + BoxPlayState.Throws);
        foreach (Entity box in boxes)
            run.World.GetRender(box).Flags.Should()
                .Be(RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor);
        for (int i = 0; i < BoxPlayState.Throws; i++)
            run.World.GetRender(run.Scene[$"flat-mark-{i}"]).Flags.Should()
                .Be(RenderFlags.Visible | RenderFlags.NoImpostor);
    }

    [Fact]
    public void TheCodaLogsNothingAndTheWorldHasNoErrors()
    {
        run.Samples[^1].Play.Value.Should().Be(new Played());
        run.Log.Should().BeEmpty();
        run.World.Errors.Should().BeEmpty();
    }
}
