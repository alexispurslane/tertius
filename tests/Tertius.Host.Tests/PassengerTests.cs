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
/// tall, of 70 kg, with its eye 0.75 m above its center, and it starts at <see cref="Shuttle.SeatStand"/>. The shuttle is
/// parked unturned in a root frame without gravity, so its cabin is weightless, unless a test unparks it and runs its main
/// engine at full throttle, three gravities along its nose, -Z. The pen of the demo, 14 cm long and of 20 g, floats loose
/// in the cabin, aft and to port of the seat, 1.8 m above the floor, about 4.3 m from the person's center.
/// </summary>
public sealed class PassengerTests : IDisposable
{
    /// <summary>The pen of the demo. The world has no renderer, so the mesh and the material are only numbers.</summary>
    private static readonly CrateSpec Pen = new(DemoSurface.PenHalf, DemoSurface.PenMass, new MeshId(11), new MaterialId(5));

    private static readonly Double3 PenPlace = new(-1, 0, 0);

    private readonly World world = NewJoltWorld();
    private FrameHandle cabin;
    private Entity person;
    private Entity pen;

    public void Dispose() => world.Dispose();

    /// <summary>Unparks its hull on its first tick, to dynamic.</summary>
    private sealed class Unpark : IComponent
    {
        private bool done;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (!done)
                self.Body.SetMotion(BodyMotion.Dynamic);
            done = true;
        }
    }

    /// <summary>
    /// The shuttle parked in <paramref name="frame"/> with its belly at <paramref name="belly"/>, the pen at
    /// <see cref="PenPlace"/>, moving at <paramref name="penVelocity"/>, and the passenger at its seat. With
    /// <paramref name="thrust"/>, the shuttle unparks on its first tick and its main engine pushes at full throttle from then on.
    /// </summary>
    private void Board(FrameHandle frame, double belly = 0, bool thrust = false, Double3 penVelocity = default)
    {
        IComponent[] engine = thrust ? [new Unpark(), new MainEngine(Shuttle.Mass * Shuttle.ThrustAcceleration) { Throttle = 1 }] : [];
        Entity shuttle = Shuttle.Park(world, frame, WorldTesting.At(0, belly + Shuttle.Hull.Y), components: engine);
        cabin = world.GetOwnedFrame(shuttle);
        pen = world.Spawn(Pen.Spawn(cabin, new DPose(PenPlace, DQuaternion.Identity), penVelocity));
        person = world.Spawn(new SpawnDesc
        {
            Frame = cabin,
            Pose = new DPose(Shuttle.SeatStand, DQuaternion.Identity),
            Body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70),
            Render = new RenderData { ViewOffset = new Pose(DemoSurface.Eye, Quaternion.Identity) },
            Components = [new Passenger(new Waypoint(cabin, Shuttle.SeatStand), pen)],
        });
    }

    private void BoardInSpace(bool thrust = false, Double3 penVelocity = default) =>
        Board(world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f))), thrust: thrust, penVelocity: penVelocity);

    private Passenger PassengerOf() => world.GetComponent<Passenger>(person)!;

    /// <summary>Every live entity that has a component of type <typeparamref name="T"/>, in slot order.</summary>
    private List<Entity> With<T>() where T : class, IComponent
    {
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        return live.Where(entity => world.GetComponent<T>(entity) != null).ToList();
    }

    /// <summary>How far the person's center is from its seat, in meters.</summary>
    private double FromSeat() => Double3.Distance(world.PoseIn(person, cabin).Position, Shuttle.SeatStand);

    /// <summary>Ticks until the passenger is in <paramref name="phase"/>, at most <paramref name="limit"/> ticks, and fails if it never gets there.</summary>
    private void TickUntil(PassengerPhase phase, int limit)
    {
        WorldTesting.TickUntil(world, () => PassengerOf().Phase == phase, limit);
        PassengerOf().Phase.Should().Be(phase);
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

        world.Frames.ProperAcceleration(cabin).Z.Should().BeApproximately(-Shuttle.ThrustAcceleration, 0.5f, "the shuttle thrusts along its nose");
        world.GetFrame(person).Should().Be(cabin);
        farthest.Should().BeLessThan(0.15);
        FromSeat().Should().BeLessThan(0.02);
    }

    [Fact]
    public void ThePassengerStaysBuckledWhileTheCabinAccelerates()
    {
        BoardInSpace(thrust: true);

        Tick(world, 600);

        PassengerOf().Phase.Should().Be(PassengerPhase.Seated);
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
        Board(ground);

        Tick(world, 120);

        PassengerOf().Phase.Should().Be(PassengerPhase.Seated);
        world.IsStanding(person).Should().BeTrue();
        FromSeat().Should().BeLessThan(0.01);
    }

    /// <summary>The cabin is weightless from the start, so the passenger opens its belt after <see cref="Passenger.UnbuckleSeconds"/>.</summary>
    [Fact]
    public void InAWeightlessCabinThePassengerStaysAtItsSeatForAWhileAndThenLeavesIt()
    {
        BoardInSpace();
        double farthest = 0;

        for (int t = 0; t < (int)(Passenger.UnbuckleSeconds * 60) - 2; t++)
        {
            world.Tick();
            farthest = Math.Max(farthest, FromSeat());
        }

        PassengerOf().Phase.Should().Be(PassengerPhase.Seated);
        farthest.Should().BeLessThan(0.05, "the belt holds the passenger");
        Tick(world, 6);
        PassengerOf().Phase.Should().Be(PassengerPhase.Fetching);
    }

    /// <summary>
    /// The passenger floats to the pen and takes it: the loose pen goes, and the passenger holds a drawn copy of it, away
    /// from its seat.
    /// </summary>
    [Fact]
    public void ThePassengerFloatsToThePenAndTakesIt()
    {
        BoardInSpace();

        TickUntil(PassengerPhase.Holding, limit: 1200);
        Tick(world, 4);

        world.IsAlive(pen).Should().BeFalse("the passenger took the loose pen");
        Entity copy = With<HeldInView>().Should().ContainSingle().Which;
        world.GetRender(copy).Mesh.Should().Be(Pen.Mesh);
        world.GetRender(copy).Material.Should().Be(Pen.Material);
        world.GetBody(copy).Should().BeNull("the pen in hand is only drawn");
        world.GetFrame(copy).Should().Be(cabin);
        FromSeat().Should().BeGreaterThan(2, "the passenger left its seat");
    }

    [Fact]
    public void ThePassengerComesWithinReachOfThePenBeforeItTakesIt()
    {
        BoardInSpace();
        double nearest = double.PositiveInfinity;

        for (int t = 0; t < 1200 && world.IsAlive(pen); t++)
        {
            nearest = Math.Min(nearest, Double3.Distance(world.PoseIn(pen, cabin).Position, world.PoseIn(person, cabin).Position));
            world.Tick();
        }

        world.IsAlive(pen).Should().BeFalse();
        nearest.Should().BeLessThanOrEqualTo(Passenger.GrabReach);
    }

    /// <summary>The pen drifts aft and up at 0.3 m/s, away from the passenger, and the passenger still catches it.</summary>
    [Fact]
    public void ThePassengerCatchesAPenThatDrifts()
    {
        BoardInSpace(penVelocity: new Double3(0, 0.05, 0.3));

        TickUntil(PassengerPhase.Holding, limit: 1500);
        Tick(world, 4);

        world.IsAlive(pen).Should().BeFalse();
        With<HeldInView>().Should().ContainSingle();
    }

    [Fact]
    public void ThePassengerStopsOnceItHoldsThePen()
    {
        BoardInSpace();
        TickUntil(PassengerPhase.Holding, limit: 1200);

        Tick(world, 180);

        world.VelocityIn(person, cabin).Length.Should().BeLessThan(0.02);
    }
}
