using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The simulation's World on the real Jolt backend: what a game sees, which is its events, its frames and
/// <see cref="World.Errors"/>. Each test that reads what the backend reported does so through the
/// <see cref="FaultInjectingBackend"/>, which records every step output that the World received.
/// </summary>
public class WorldPhysicsTests
{
    /// <summary>The contacts, in the order of the steps, that Jolt reported between <paramref name="a"/> and <paramref name="b"/>, with the index of the step that reported each.</summary>
    private static List<int> StepsThatReportedContactBetween(FaultInjectingBackend faults, Entity a, Entity b)
    {
        var steps = new List<int>();
        for (int step = 0; step < faults.Outputs.Count; step++)
            foreach (ContactStarted contact in faults.Outputs[step].Contacts)
                if ((contact.A == a && contact.B == b) || (contact.A == b && contact.B == a))
                    steps.Add(step);
        return steps;
    }

    [Fact]
    public void ABallThatRollsOnTheFloorHearsOneCollisionAcrossTwoGrowthsOfItsSystem()
    {
        var warnings = new List<string>();
        // The backend holds 8 bodies in a frame's system at first, so the level load below makes it grow.
        var faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { MaxBodiesPerFrame = 8, WorkerThreads = 2, Log = warnings.Add }));
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, faults);
        FrameHandle ground = Ground(world, 9.81f);
        Entity floor = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(100, 0.5f, 100)), BodyMotion.Static),
        });
        var log = new CollisionLog();
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 0.5),
            LinearVelocity = new Double3(1, 0, 0),
            Body = Ball(0.5f, mass: 2),
            Components = [log],
        });
        Tick(world, 30);
        log.Seen.Should().ContainSingle().Which.Other.Should().Be(floor);

        // A level load: one batch of 15 static crates, far below the floor, grows the ground's system twice.
        for (int i = 0; i < 15; i++)
            world.Spawn(new SpawnDesc { Frame = ground, Pose = At(3 * i, -100), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        Tick(world, 10);

        warnings.Count(warning => warning.Contains("so it grew")).Should().Be(2);
        log.Seen.Should().ContainSingle("the ball never left the floor, so its collision did not start again");
        // The World made the one event from the one contact that Jolt reported, and delivered it in the tick after the step.
        List<int> reported = StepsThatReportedContactBetween(faults, ball, floor);
        reported.Should().ContainSingle("Jolt reported one contact between the ball and the floor, and none after the growths");
        log.Seen.Single().Tick.Should().Be(reported.Single() + 1);
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
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, warnings);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            LinearVelocity = new Double3(10, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        Tick(world, 5);
        DPose parked = world.Frames.LocalPose(ship);
        parked.Position.X.Should().BeGreaterThan(0.5, "the ship flew for 5 ticks");
        faults.Outputs.Should().OnlyContain(output => output.Frames.Any(state => state.Frame == ship), "while the ship flies, Jolt reports the state of its frame in every step");

        world.SetFrameDriver(ship, MotionDriver.Static);
        world.VelocityInOwnFrame(owner).Should().Be(Vector3.Zero, "the owner mirrors its frame at once");
        Tick(world, 3);

        world.Errors.Should().BeEmpty();
        warnings.Should().BeEmpty();
        faults.Outputs.Skip(5).SelectMany(output => output.Frames).Should().BeEmpty("Jolt reports no state for a parked frame");
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
        using World world = NewWorld(out JoltPhysicsBackend backend);
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

        (Double3 onOrbit, Double3 orbitalVelocity) = KeplerRails.StateAt(orbit, world.Time, planetMu);
        (world.Frames.LocalPose(station).Position - onOrbit).Length.Should().BeLessThan(1e-6, "the station moved onto its orbit at once");
        (world.Frames.LinearVelocity(station) - orbitalVelocity).Length.Should().BeLessThan(1e-6);
        world.PoseInOwnFrame(owner).Should().Be(world.Frames.LocalPose(station), "the owner mirrors its frame at once");

        world.Tick();

        // The state of Jolt's hull after the tick: no tick has changed it since the step that read it.
        JoltPhysicsBackend.BodyFacts hull = backend.BodiesIn(planet).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));
        hull.LinearVelocity.Length().Should().BeApproximately(10f, 0.01f, "the hull moves along the orbit, not across the gap");
        (hull.Position - KeplerRails.StateAt(orbit, world.Time, planetMu).Position).Length.Should().BeLessThan(0.01, "the hull is on the orbit");
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
        using World world = NewWorld(out _, warnings);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            LinearVelocity = new Double3(10, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        Tick(world, 5);
        DPose parked = world.Frames.LocalPose(ship);

        world.SetFrameDriver(ship, MotionDriver.Static);
        if (tickBetween)
            world.Tick();
        world.SetFrameDriver(ship, MotionDriver.Physics);
        Tick(world, 60);

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
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        Entity floor = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, -0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(500, 0.5f, 500)), BodyMotion.Static),
        });
        var log = new CollisionLog();
        Entity crate = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10), Components = [log] });
        world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(-100, 0.5),
            LinearVelocity = new Double3(2, 0, 0),
            Body = Ball(0.5f, mass: 2),
        });
        Tick(world, 300);

        Entity dropped = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 2.5), Body = new BodyDesc(new SphereShapeDesc(0.25f), Mass: 0.5f) });
        Tick(world, 120);

        log.Seen.Select(seen => seen.Other).Should().Equal([floor, dropped], "the crate never left the floor");
        // Each event came from one contact that Jolt reported, and arrived in the tick after its step.
        List<int> withFloor = StepsThatReportedContactBetween(faults, crate, floor);
        List<int> withDropped = StepsThatReportedContactBetween(faults, crate, dropped);
        withFloor.Should().ContainSingle("Jolt reported the floor once, when the crate arrived");
        withDropped.Should().ContainSingle();
        log.Seen.Select(seen => seen.Tick).Should().Equal([withFloor.Single() + 1, withDropped.Single() + 1]);
        world.Errors.Should().BeEmpty();
    }
}
