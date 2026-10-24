using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using static Tertius.Testing.WorldTesting;
using DemoSurface = Tertius.Demo.DemoScenes.DemoSurface;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// Passenger: a person rides out a thrust at its seat in the demo's shuttle, and once the cabin is weightless it floats to
/// a loose pen and takes it, on the World and the real Jolt backend. The person is the capsule of the demo scenes, 1.8 m
/// tall, of 70 kg, with its eye 0.75 m above its center, and it starts at <see cref="Shuttle.SeatStand"/>, turned to face
/// aft, toward the pen. The shuttle is parked unturned in a root frame without gravity, so its cabin is weightless, unless
/// a test unparks it and runs its main engine at full throttle, three gravities along its nose, -Z. The pen of the demo,
/// 14 cm long and of 20 g, floats loose in the cabin, aft and to port of the seat, 1.8 m above the floor, about 4.3 m from
/// the person's center.
/// </summary>
public sealed class PassengerTests : IDisposable
{
    /// <summary>The pen of the demo. The world has no renderer, so the mesh and the material are only numbers.</summary>
    private static readonly CrateSpec Pen = new(HalfExtents: DemoSurface.PenHalf, Mass: DemoSurface.PenMass,
        Mesh: new(11), Material: new(5));

    private static readonly Double3 PenPlace = new(-1, 0, 0);

    /// <summary>The person's turn: half a turn about the cabin's up, so that it faces aft, +Z, toward the pen.</summary>
    private static readonly DQuaternion FacingAft = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI);

    private readonly World world = NewJoltWorld();
    private FrameHandle cabin;
    private Entity person;
    private Entity pen;

    public void Dispose() => world.Dispose();

    /// <summary>
    /// The shuttle parked in <paramref name="frame"/> with its belly at <paramref name="belly"/>, the pen at
    /// <see cref="PenPlace"/>, moving at <paramref name="penVelocity"/>, and the passenger at its seat, moving at
    /// <paramref name="personVelocity"/>. With <paramref name="thrust"/>, the shuttle unparks on its first tick and its
    /// main engine pushes at full throttle from then on. With <paramref name="burn"/>, the shuttle unparks on its first tick
    /// and its main engine pushes with the throttle of the burn.
    /// </summary>
    private void Board(FrameHandle frame, double belly = 0, bool thrust = false, Double3 penVelocity = default,
        Double3 personVelocity = default,
        Burn? burn = null)
    {
        IComponent[] engine = [];
        if (thrust)
            engine = [new Unpark(), new FullThrottle(), new MainEngine(Shuttle.Mass * Shuttle.ThrustAcceleration)];
        else if (burn != null)
            engine = [new Unpark(), burn, new MainEngine(Shuttle.Mass * Shuttle.ThrustAcceleration)];
        Entity shuttle = Shuttle.Park(world: world, frame: frame, pose: WorldTesting.At(0, belly + Shuttle.Hull.Y),
            components: engine);
        cabin = world.GetOwnedFrame(shuttle);
        pen = world.Spawn(Pen.Spawn(cabin, new(PenPlace, DQuaternion.Identity), penVelocity));
        person = world.Spawn(new()
        {
            Frame = cabin,
            Pose = new(Shuttle.SeatStand, FacingAft),
            LinearVelocity = personVelocity,
            Body = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70),
            Render = new RenderData { ViewOffset = new(DemoSurface.Eye, Quaternion.Identity) },
            Components = [new Passenger(new(cabin, Shuttle.SeatStand), pen)]
        });
    }

    private void BoardInSpace(bool thrust = false, Double3 penVelocity = default, Double3 personVelocity = default,
        Burn? burn = null) =>
        Board(frame: world.CreateRootFrame(new(FrameBounds.Sphere(1e6f))), thrust: thrust, penVelocity: penVelocity,
            personVelocity: personVelocity,
            burn: burn);

    /// <summary>Tells the passenger to fasten its belt, as the script does.</summary>
    private void FastenBelt() =>
        world.Post(Event.Create(target: person, sender: Entity.Null, payload: new FastenBelt()));

    /// <summary>The passenger floats to the pen and takes it, is told to fasten its belt, and floats back to its seat and straps in.</summary>
    private void TakeThePenAndStrapIn()
    {
        TickUntil<Holding>(limit: 1200);
        FastenBelt();
        TickUntil<Strapped>(limit: 1200);
    }

    private Passenger PassengerOf() => world.GetComponent<Passenger>(person)!;

    /// <summary>Every live entity that has a component of type <typeparamref name="T"/>, in slot order.</summary>
    private List<Entity> With<T>() where T : class, IComponent
    {
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        return [.. live.Where(entity => world.GetComponent<T>(entity) != null)];
    }

    /// <summary>How far the person's center is from its seat, in meters.</summary>
    private double FromSeat() => Double3.Distance(world.PoseIn(person, cabin).Position, Shuttle.SeatStand);

    /// <summary>Ticks until the passenger is in the case <typeparamref name="T"/>, at most <paramref name="limit"/> ticks, and fails if it never gets there.</summary>
    private void TickUntil<T>(int limit)
    {
        WorldTesting.TickUntil(world, () => PassengerOf().State.Value is T, limit);
        PassengerOf().State.Value.Should().BeOfType<T>();
    }

    /// <summary>Where the person's hand takes the pen, in the shuttle's frame: its eye plus <see cref="Passenger.PenHold"/>, in its axes.</summary>
    private Double3 Hand()
    {
        DPose pose = world.PoseIn(person, cabin);
        return pose.TransformPoint(DemoSurface.Eye + Passenger.PenHold);
    }

    /// <summary>
    /// The passenger floats with one back jet, a part of the person (docs/plans/parts.md, "The demo's use"): in its first
    /// tick it spawns an attached entity with a <see cref="BackJet"/> at <see cref="BackJet.Place"/> on its back. In a
    /// weightless cabin it then floats away from its seat to the pen with that jet.
    /// </summary>
    [Fact]
    public void ThePassengerFloatsWithABackJetOnItsBack()
    {
        BoardInSpace();
        Tick(world, 1);

        Entity jet = PassengerOf().BackJetPart;
        world.IsAlive(jet).Should().BeTrue();
        world.GetComponent<BackJet>(jet).Should().NotBeNull();
        DPose body = world.PoseIn(person, cabin), pose = world.PoseIn(jet, cabin);
        (pose.Position - body.TransformPoint(BackJet.Place)).Length.Should()
            .BeLessThan(1e-5, "the jet sits on the person's back");
        TickUntil<Fetching>(limit: 600);
        Tick(world, 60);
        FromSeat().Should().BeGreaterThan(0.05, "the back jet floats the passenger from its seat");
    }

    /// <summary>
    /// Three gravities along the nose for 4 s would throw a loose person 11.5 m aft onto the cabin's aft wall. The belt
    /// catches the person in the first ticks of the thrust and then holds it at its seat.
    /// </summary>
    [Fact]
    public void TheBeltHoldsThePassengerAtItsSeatWhileTheShuttleThrustsAtThreeGravities()
    {
        BoardInSpace(thrust: true);
        double farthest = 0;

        for (int t = 0; t < 240; t++)
        {
            world.Tick();
            farthest = Math.Max(farthest, FromSeat());
        }

        world.Frames.ProperAcceleration(cabin).Z.Should().BeApproximately(-Shuttle.ThrustAcceleration, 0.5f,
            "the shuttle thrusts along its nose");
        world.GetFrame(person).Should().Be(cabin);
        farthest.Should().BeLessThan(0.15);
        FromSeat().Should().BeLessThan(0.02);
    }

    [Fact]
    public void ThePassengerStaysBuckledWhileTheCabinAccelerates()
    {
        BoardInSpace(thrust: true);

        Tick(world, 600);

        PassengerOf().State.Value.Should().BeOfType<Seated>();
        FromSeat().Should().BeLessThan(0.02);
    }

    /// <summary>
    /// The shuttle stands parked on a floor in 9.81 m/s² of gravity, so the person stands at its seat. The belt pushes
    /// nothing while the gravity would keep the person on its feet, so it does not lift the person off the floor.
    /// </summary>
    [Fact]
    public void APassengerStandsAtItsSeatOnTheGround()
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Board(frame: ground);

        Tick(world, 120);

        PassengerOf().State.Value.Should().BeOfType<Seated>();
        world.IsStanding(person).Should().BeTrue();
        FromSeat().Should().BeLessThan(0.01);
    }

    /// <summary>
    /// The cabin is weightless from the start, and the person starts with a drift of 0.1 m/s along the cabin, which would
    /// carry it 0.2 m from its seat before it opens its belt. The belt holds it within a few centimeters of its seat, and
    /// the passenger opens its belt after <see cref="Passenger.UnbuckleSeconds"/>.
    /// </summary>
    [Fact]
    public void InAWeightlessCabinThePassengerStaysAtItsSeatForAWhileAndThenLeavesIt()
    {
        BoardInSpace(personVelocity: new(0, 0, 0.1));
        double farthest = 0;

        for (int t = 0; t < (int)(Passenger.UnbuckleSeconds * 60) - 2; t++)
        {
            world.Tick();
            farthest = Math.Max(farthest, FromSeat());
        }

        PassengerOf().State.Value.Should().BeOfType<Seated>();
        farthest.Should().BeLessThan(0.05, "the belt holds the passenger");
        Tick(world, 6);
        PassengerOf().State.Value.Should().BeOfType<Fetching>();
    }

    /// <summary>
    /// The passenger floats to the pen and takes it: the loose pen goes, and the passenger holds a drawn copy of it, away
    /// from its seat.
    /// </summary>
    [Fact]
    public void ThePassengerFloatsToThePenAndTakesIt()
    {
        BoardInSpace();

        TickUntil<Holding>(limit: 1200);
        Tick(world, 4);

        world.IsAlive(pen).Should().BeFalse("the passenger took the loose pen");
        Entity copy = With<HeldInView>().Should().ContainSingle().Which;
        PassengerOf().State.Should().Be(new PassengerState(new Holding(copy)), "the passenger holds the copy");
        world.GetRender(copy).Mesh.Should().Be(Pen.Mesh);
        world.GetRender(copy).Material.Should().Be(Pen.Material);
        world.GetBody(copy).Should().BeNull("the pen in hand is only drawn");
        world.GetFrame(copy).Should().Be(cabin);
        FromSeat().Should().BeGreaterThan(2, "the passenger left its seat");
    }

    /// <summary>
    /// The pen in hand nearly touches the person: its near end is less than 6 cm in front of the front of the capsule, 0.3 m
    /// ahead of its axis, in front of the chest, between the person's center and its eye. A pen held farther out reads as
    /// a pen that floats free. The pen floats unturned, along the cabin, so it points ahead in the hand.
    /// </summary>
    [Fact]
    public void ThePassengerHoldsThePenInFrontOfItsChest()
    {
        BoardInSpace();
        TickUntil<Holding>(limit: 1200);
        Tick(world, 4);

        Entity copy = With<HeldInView>().Should().ContainSingle().Which;
        DPose personPose = world.PoseInOwnFrame(person);
        DPose copyPose = world.PoseInOwnFrame(copy);
        // The pen's center and its long axis, in the person's axes: +Y up and -Z ahead.
        Double3 center = personPose.Rotation.Conjugate().Rotate(copyPose.Position - personPose.Position);
        Double3 axis = (personPose.Rotation.Conjugate() * copyPose.Rotation).Rotate(Double3.UnitZ);
        double nearEnd = -center.Z - Math.Abs(axis.Z) * DemoSurface.PenHalf.Z;

        nearEnd.Should().BeInRange(0.3, 0.36, "the pen's near end lies just in front of the body");
        center.Y.Should().BeInRange(0, DemoSurface.Eye.Y, "the pen is in front of the chest");
        Math.Abs(center.X).Should().BeLessThan(0.3, "the pen is in front of the body, not beside it");
    }

    /// <summary>The hand comes within <see cref="Passenger.GripReach"/> of the pen before the passenger takes it.</summary>
    [Fact]
    public void ThePassengersHandComesToThePenBeforeItTakesIt()
    {
        BoardInSpace();
        double nearest = double.PositiveInfinity;
        int beforeReach = 0;

        for (int t = 0; t < 1200 && world.IsAlive(pen); t++)
        {
            nearest = Math.Min(nearest, Double3.Distance(world.PoseIn(pen, cabin).Position, Hand()));
            if (nearest > Passenger.GripReach)
                beforeReach++;
            world.Tick();
        }

        world.IsAlive(pen).Should().BeFalse();
        nearest.Should().BeLessThanOrEqualTo(Passenger.GripReach);
        beforeReach.Should().BeGreaterThan(60, "the passenger floats for a while before its hand reaches the pen");
    }

    /// <summary>The pen drifts aft and up at 0.3 m/s, away from the passenger, and the passenger still catches it.</summary>
    [Fact]
    public void ThePassengerCatchesAPenThatDrifts()
    {
        BoardInSpace(penVelocity: new(0, 0.05, 0.3));

        TickUntil<Holding>(limit: 1500);
        Tick(world, 4);

        world.IsAlive(pen).Should().BeFalse();
        With<HeldInView>().Should().ContainSingle();
    }

    [Fact]
    public void ThePassengerStopsOnceItHoldsThePen()
    {
        BoardInSpace();
        TickUntil<Holding>(limit: 1200);

        Tick(world, 180);

        world.VelocityIn(person, cabin).Length.Should().BeLessThan(0.02);
    }

    /// <summary>
    /// The pen goes while the passenger floats to it, but no one took it, so the passenger holds nothing: it stops, and it
    /// is not holding a pen.
    /// </summary>
    [Fact]
    public void APassengerWhosePenGoesBeforeItsTakeStopsWithEmptyHands()
    {
        BoardInSpace();
        TickUntil<Fetching>(limit: 200);
        Tick(world, 30);

        world.Destroy(pen);
        Tick(world, 180);

        PassengerOf().State.Value.Should().BeOfType<Stopped>("the passenger holds no pen");
        With<HeldInView>().Should().BeEmpty();
        world.VelocityIn(person, cabin).Length.Should().BeLessThan(0.02, "the passenger stops");
    }

    /// <summary>
    /// The passenger holds the pen, away from its seat, and is told to fasten its belt. It floats back to its seat with the
    /// copy of the pen still in its hand, never faster than <see cref="Passenger.FloatSpeed"/>, and straps in there, within
    /// <see cref="Passenger.BuckleRadius"/> of the seat.
    /// </summary>
    [Fact]
    public void AFastenedBeltBringsTheHoldingPassengerBackToItsSeatWithThePen()
    {
        BoardInSpace();
        TickUntil<Holding>(limit: 1200);
        Tick(world, 60);
        Entity copy = With<HeldInView>().Should().ContainSingle().Which;
        FromSeat().Should().BeGreaterThan(2, "the passenger took the pen away from its seat");

        FastenBelt();
        TickUntil<Returning>(limit: 2);
        PassengerOf().State.Should().Be(new PassengerState(new Returning(copy)));
        double fastest = 0;
        for (int t = 0; t < 1200 && PassengerOf().State.Value is Returning; t++)
        {
            world.Tick();
            fastest = Math.Max(fastest, world.VelocityIn(person, cabin).Length);
        }

        PassengerOf().State.Value.Should().BeOfType<Strapped>();
        fastest.Should().BeInRange(0.9 * Passenger.FloatSpeed, 1.01 * Passenger.FloatSpeed,
            "the passenger floats at its float speed, and no faster");

        PassengerOf().State.Should().Be(new PassengerState(new Strapped(copy)), "the passenger still holds the copy");
        world.IsAlive(copy).Should().BeTrue();
        With<HeldInView>().Should().Equal(copy);
        FromSeat().Should().BeLessThanOrEqualTo(Passenger.BuckleRadius);
    }

    /// <summary>
    /// The strapped passenger rides out a burn of 3 m/s² along the nose for 20 s, a throttle of 3 / 29.43, which would
    /// throw a loose person onto the aft wall. The belt holds it within 0.1 m of its seat.
    /// </summary>
    [Fact]
    public void AStrappedPassengerStaysAtItsSeatThroughABurn()
    {
        var burn = new Burn();
        BoardInSpace(burn: burn);
        TakeThePenAndStrapIn();
        burn.Value = 3 / Shuttle.ThrustAcceleration;
        double farthest = 0;

        for (int t = 0; t < 1200; t++)
        {
            world.Tick();
            farthest = Math.Max(farthest, FromSeat());
        }

        world.Frames.ProperAcceleration(cabin).Z.Should().BeApproximately(-3, 0.1f, "the shuttle burns along its nose");
        farthest.Should().BeLessThan(0.1);
        PassengerOf().State.Value.Should().BeOfType<Strapped>();
    }

    /// <summary>
    /// A seated passenger opens its belt after <see cref="Passenger.UnbuckleSeconds"/> in a weightless cabin. A strapped
    /// passenger never does: after 60 s weightless, it is still strapped at its seat.
    /// </summary>
    [Fact]
    public void AStrappedPassengerNeverOpensItsBelt()
    {
        BoardInSpace();
        TakeThePenAndStrapIn();

        Tick(world, 3600);

        PassengerOf().State.Value.Should().BeOfType<Strapped>();
        FromSeat().Should().BeLessThan(Passenger.BuckleRadius);
    }

    /// <summary>
    /// The passenger is told to fasten its belt while it sits at its seat, and again while it floats to the pen. Neither
    /// changes what it does: it takes the pen, and it holds it.
    /// </summary>
    [Fact]
    public void AFastenBeltBeforeTheTakeChangesNothing()
    {
        BoardInSpace();
        Tick(world, 30);
        PassengerOf().State.Value.Should().BeOfType<Seated>();
        FastenBelt();
        TickUntil<Fetching>(limit: 200);
        FastenBelt();

        TickUntil<Holding>(limit: 1200);
        Tick(world, 120);

        PassengerOf().State.Value.Should().BeOfType<Holding>();
    }

    /// <summary>
    /// A test component that adds a <see cref="Throttle"/> of <see cref="Value"/> to its entity's events on every tick, for
    /// the entity's <see cref="MainEngine"/>, which runs after it. A test sets the value between ticks.
    /// </summary>
    private sealed class Burn : IComponent
    {
        public float Value { get; set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            events.Add(new Throttle { Value = Value });
    }
}
