using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Physics.Tests;

/// <summary>
/// The simulation's World on the real Jolt backend. The other physics tests stand in for the sync point with the
/// <see cref="PhysicsHarness"/>. These tests run the real sync point, so they show what a game sees: its events, its
/// frames and <see cref="World.Errors"/>.
/// </summary>
public class WorldPhysicsTests
{
    private static DPose At(double x, double y = 0, double z = 0) => new(new Double3(x, y, z), DQuaternion.Identity);

    /// <summary>A world with one gameplay thread and its debug checks on, which owns <paramref name="backend"/>.</summary>
    private static World NewWorld(JoltPhysicsBackend backend) =>
        new(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, backend);

    /// <summary>Records the tick and the other entity of every Collision that its entity hears.</summary>
    private sealed class CollisionLog : IComponent
    {
        public List<(long Tick, Entity Other)> Seen { get; } = new();

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Collision>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i].Is<Collision>())
                    Seen.Add((self.Tick, events[i].As<Collision>().Other));
        }
    }

    [Fact]
    public void ABallThatRollsOnTheFloorHearsOneCollisionAcrossTwoGrowthsOfItsSystem()
    {
        var warnings = new List<string>();
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { MaxBodiesPerFrame = 8, WorkerThreads = 2, Log = warnings.Add });
        using World world = NewWorld(backend);
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        Entity floor = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(100, 0.5f, 100)), BodyMotion.Static),
        });
        var log = new CollisionLog();
        world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 0.5),
            LinearVelocity = new Vector3(1, 0, 0),
            Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 2, LinearDamping: 0, AngularDamping: 0),
            Components = [log],
        });
        for (int i = 0; i < 30; i++)
            world.Tick();
        log.Seen.Should().ContainSingle().Which.Other.Should().Be(floor);

        // A level load: one batch of 15 static crates, far below the floor, grows the ground's system twice.
        for (int i = 0; i < 15; i++)
            world.Spawn(new SpawnDesc { Frame = ground, Pose = At(3 * i, -100), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        for (int i = 0; i < 10; i++)
            world.Tick();

        warnings.Count(warning => warning.Contains("so it grew")).Should().Be(2);
        log.Seen.Should().ContainSingle("the ball never left the floor, so its collision did not start again");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The reviewer's probe W4: a ship flies through space at 10 m/s, and between ticks the game parks it by giving its
    /// frame the static driver. Physics follows the change, so it reports no state for the parked frame, and the sync
    /// point finds nothing wrong, in a Debug build and in a Release build.
    /// </summary>
    [Fact]
    public void AShipThatTheGameParksStopsWhereItIsWithNoError()
    {
        var warnings = new List<string>();
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2, Log = warnings.Add });
        using World world = NewWorld(backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            LinearVelocity = new Vector3(10, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        for (int i = 0; i < 5; i++)
            world.Tick();
        DPose parked = world.Frames.LocalPose(ship);
        parked.Position.X.Should().BeGreaterThan(0.5, "the ship flew for 5 ticks");

        world.SetFrameDriver(ship, MotionDriver.Static);
        world.VelocityInOwnFrame(owner).Should().Be(Vector3.Zero, "the owner mirrors its frame at once");
        for (int i = 0; i < 3; i++)
            world.Tick();

        world.Errors.Should().BeEmpty();
        warnings.Should().BeEmpty();
        world.Frames.LocalPose(ship).Should().Be(parked);
        world.PoseInOwnFrame(owner).Should().Be(parked);
        world.VelocityInOwnFrame(owner).Should().Be(Vector3.Zero);
    }

    /// <summary>
    /// A parked station rests 10 km from the point of its orbit where it belongs now. The game gives it the Kepler driver.
    /// The station moves onto its orbit at once, as it does for a new orbit, and its owner with it. Physics then puts the
    /// hull there at the start of the next step, so it never drives the hull across the gap: after one tick the hull is on
    /// the orbit and moves at the orbital speed of 10 m/s.
    /// </summary>
    [Fact]
    public void AFrameThatGetsTheKeplerDriverMovesOntoItsOrbitAtOnceSoItsHullNeverCrossesTheGap()
    {
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 });
        using World world = NewWorld(backend);
        // At 5 km, a parent with this gravitational parameter gives an orbital speed of 10 m/s: √(5e5 / 5000).
        const double planetMu = 5000.0 * 100;
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, GravityField.Point(planetMu)));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Pose = At(-5000),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5)), Mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static),
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        world.Tick();
        KeplerOrbit orbit = KeplerOrbit.Circular(5000);
        world.SetFrameOrbit(station, orbit);

        world.SetFrameDriver(station, MotionDriver.Kepler);

        (Double3 onOrbit, Double3 orbitalVelocity) = orbit.StateAt(world.Time, planetMu);
        (world.Frames.LocalPose(station).Position - onOrbit).Length.Should().BeLessThan(1e-6, "the station moved onto its orbit at once");
        (world.Frames.LinearVelocity(station) - orbitalVelocity).Length.Should().BeLessThan(1e-6);
        world.PoseInOwnFrame(owner).Should().Be(world.Frames.LocalPose(station), "the owner mirrors its frame at once");

        world.Tick();

        JoltPhysicsBackend.BodyFacts hull = backend.BodiesIn(planet).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));
        hull.LinearVelocity.Length().Should().BeApproximately(10f, 0.01f, "the hull moves along the orbit, not across the gap");
        (hull.Position - orbit.StateAt(world.Time, planetMu).Position).Length.Should().BeLessThan(0.01, "the hull is on the orbit");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The reviewer's probe W7. Between two ticks, or with one tick between, the game parks a ship that flies at 10 m/s and
    /// gives it the physics driver again. The ship stays where it was parked.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AShipThatTheGameParksAndUnparksStaysWhereItWasParked(bool tickBetween)
    {
        var warnings = new List<string>();
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2, Log = warnings.Add });
        using World world = NewWorld(backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            LinearVelocity = new Vector3(10, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        for (int i = 0; i < 5; i++)
            world.Tick();
        DPose parked = world.Frames.LocalPose(ship);

        world.SetFrameDriver(ship, MotionDriver.Static);
        if (tickBetween)
            world.Tick();
        world.SetFrameDriver(ship, MotionDriver.Physics);
        for (int i = 0; i < 60; i++)
            world.Tick();

        (world.Frames.LocalPose(ship).Position - parked.Position).Length.Should().BeLessThan(1e-6, "the parked ship had no velocity left");
        world.VelocityInOwnFrame(owner).Length().Should().BeLessThan(1e-6f);
        world.Errors.Should().BeEmpty();
        warnings.Should().BeEmpty();
    }

    /// <summary>
    /// The reviewer's probe W8. A crate rests on the floor of a frame with 1 g and falls asleep, while a ball rolls far
    /// away and keeps the frame's system stepping. After 5 s a second ball drops onto the crate and wakes it. The crate
    /// never leaves the floor, so it hears the floor once, when it arrives, and the dropped ball once.
    /// </summary>
    [Fact]
    public void ACrateThatSleepsOnTheFloorAndWakesWhenABallHitsItHearsTheFloorOnlyOnce()
    {
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 });
        using World world = NewWorld(backend);
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        Entity floor = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(500, 0.5f, 500)), BodyMotion.Static),
        });
        var log = new CollisionLog();
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10), Components = [log] });
        world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(-100, 0.5),
            LinearVelocity = new Vector3(2, 0, 0),
            Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 2, LinearDamping: 0, AngularDamping: 0),
        });
        for (int i = 0; i < 300; i++)
            world.Tick();

        Entity dropped = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 2.5), Body = new BodyDesc(new SphereShapeDesc(0.25f), Mass: 0.5f) });
        for (int i = 0; i < 120; i++)
            world.Tick();

        log.Seen.Select(seen => seen.Other).Should().Equal([floor, dropped], "the crate never left the floor");
        world.Errors.Should().BeEmpty();
    }
}
