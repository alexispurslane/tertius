using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A game can change a frame's motion driver at any time between ticks, for example to park a ship. Physics follows the
/// frame tree, so at the start of its next step it gives the frame's hull the motion that the new driver needs: dynamic
/// for the physics driver, kinematic for Kepler, and static for the static driver. These tests run the World on the real
/// backend, so the game changes the driver through <see cref="World.SetFrameDriver"/>.
/// </summary>
public class DriverChangeTests
{
    private const double EarthMu = Scenes.EarthMu;

    /// <summary>
    /// The G·M of the planet in the scenes with a Kepler driver. At 5 km, a circular orbit round it has an orbital speed of
    /// 10 m/s: √(5e5 / 5000). That is slow and close to the planet's origin, so the engine puts no numeric frame round the
    /// ship, and the hull stays in the planet's own system.
    /// </summary>
    private const double PlanetMu = 5e5;

    private static readonly KeplerOrbit ShipOrbit = KeplerOrbit.Circular(5000, phase: 1.0);

    /// <summary>A dynamic box with sides of 2 · <paramref name="half"/> m that nothing slows down on its own and that does not bounce.</summary>
    private static BodyDesc BoxBody(float half, float mass) =>
        new(new BoxShapeDesc(new Vector3(half)), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// What Jolt holds for the hull of the frame that <paramref name="owner"/> owns, in the system of the owner's physics
    /// home: the frame's parent, or a numeric frame that the engine made below it.
    /// </summary>
    private static JoltPhysicsBackend.BodyFacts Hull(World world, JoltPhysicsBackend backend, Entity owner) =>
        backend.BodiesIn(world.PhysicsHomeOf(owner))
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));

    /// <summary>Every frame state that Jolt sent for <paramref name="frame"/>, from the output at index <paramref name="from"/> on.</summary>
    private static List<FrameState> FrameStatesSince(FaultInjectingBackend faults, FrameHandle frame, int from) =>
        faults.Outputs.Skip(from).SelectMany(output => output.Frames).Where(state => state.Frame == frame).ToList();

    /// <summary>A ship in space, with the physics driver, that flies along +X at 10 m/s with no damping, after five ticks.</summary>
    private static (FrameHandle Space, Entity Owner, FrameHandle Ship) FlyingShip(World world)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            LinearVelocity = new Double3(10, 0, 0),
            Body = BoxBody(2, 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Physics),
        });
        Tick(world, 5);
        return (space, owner, world.GetOwnedFrame(owner));
    }

    /// <summary>
    /// A ship above a planet with a point gravity, 5 km from its origin on <see cref="ShipOrbit"/>, with <paramref name="driver"/>
    /// as its motion driver. A ship that physics or the static driver moves also has the orbit, so that the game can give it the
    /// Kepler driver later, as the World demands. A ship with the physics driver starts at the orbital speed, and a ship with
    /// the static driver at rest.
    /// </summary>
    private static (FrameHandle Planet, Entity Owner, FrameHandle Ship) ShipAbovePlanet(World world, MotionDriver driver)
    {
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, GravityField.Point(PlanetMu)));
        (Double3 position, Double3 velocity) = KeplerRails.StateAt(ShipOrbit, world.Time, PlanetMu);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Pose = new DPose(position, DQuaternion.Identity),
            LinearVelocity = driver == MotionDriver.Static ? Double3.Zero : velocity,
            Body = BoxBody(2, 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), driver, Orbit: ShipOrbit),
        });
        return (planet, owner, world.GetOwnedFrame(owner));
    }

    [Fact]
    public void AShipThatTheGameParksGetsAStaticHullThatStaysWhereTheShipStopped()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var (_, owner, ship) = FlyingShip(world);
        int reported = faults.Outputs.Count;
        DPose parked = world.Frames.LocalPose(ship);

        world.SetFrameDriver(ship, MotionDriver.Static);
        Tick(world, 3);

        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Static);
        world.Frames.LocalPose(ship).Should().Be(parked);
        (Hull(world, faults.Jolt, owner).Position - parked.Position).Length.Should().BeLessThan(1e-9);
        FrameStatesSince(faults, ship, reported).Should().BeEmpty("physics no longer drives the ship's frame");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The reviewer's probe D1. The game parks a ship that flies at 10 m/s, which sets the frame's velocities in the tree to
    /// zero, and then gives it the physics driver again, before or after one step. Physics puts the hull at the frame's pose
    /// and velocity in the tree after every driver call, also when the driver ends where it started. So the ship stays.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AShipThatIsParkedAndUnparkedStaysWhereItWasParked(bool stepBetween)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        var (_, owner, ship) = FlyingShip(world);
        DPose parked = world.Frames.LocalPose(ship);

        world.SetFrameDriver(ship, MotionDriver.Static);
        if (stepBetween)
            world.Tick();
        world.SetFrameDriver(ship, MotionDriver.Physics);
        Tick(world, 60);

        (world.Frames.LocalPose(ship).Position - parked.Position).Length.Should().BeLessThan(1e-6, "the parked ship had no velocity left");
        world.Frames.LinearVelocity(ship).Length.Should().BeLessThan(1e-6);
        Hull(world, backend, owner).Motion.Should().Be(MotionType.Dynamic);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AStaticFrameThatGetsThePhysicsDriverFallsWithItsHull()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 100),
            Body = BoxBody(2, 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static),
        });
        FrameHandle lander = world.GetOwnedFrame(owner);
        Tick(world, 3);
        world.Frames.LocalPose(lander).Position.Y.Should().Be(100, "a static frame does not fall");
        int reported = faults.Outputs.Count;

        world.SetFrameDriver(lander, MotionDriver.Physics);
        Tick(world, 30);

        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Dynamic);
        FrameStatesSince(faults, lander, reported).Should().HaveCount(30);
        // Half a second of free fall: semi-implicit Euler falls a little further than g t² / 2 = 1.226 m.
        double dt = world.DeltaTime;
        double t = 30 * dt;
        (100 - world.Frames.LocalPose(lander).Position.Y).Should().BeInRange(0.5 * 9.81 * t * t, 0.5 * 9.81 * t * (t + dt) + 1e-6);
        world.Frames.LinearVelocity(lander).Y.Should().BeApproximately(-9.81 * t, 1e-3);
        world.Frames.ProperAcceleration(lander).Length().Should().BeLessThan(1e-3f, "the lander is in free fall");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AKeplerFrameThatGetsThePhysicsDriverCoastsOnItsOrbitAndFollowsTheOrbitAgainWithTheKeplerDriver()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu)));
        var orbit = KeplerOrbit.Circular(7_000_000, phase: 1.0);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = BoxBody(10, 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Kepler, Orbit: orbit),
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        Tick(world, 10);
        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Kinematic);
        int reported = faults.Outputs.Count;

        world.SetFrameDriver(station, MotionDriver.Physics);
        Tick(world, 60);

        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Dynamic);
        FrameStatesSince(faults, station, reported).Should().HaveCount(60);
        // In free fall the hull stays on the orbit that the Kepler driver gave it, up to the float rounding of its velocity.
        (world.Frames.LocalPose(station).Position - KeplerRails.StateAt(orbit, world.Time, EarthMu).Position).Length.Should().BeLessThan(0.5);

        reported = faults.Outputs.Count;
        world.SetFrameDriver(station, MotionDriver.Kepler);
        Tick(world, 10);

        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Kinematic);
        FrameStatesSince(faults, station, reported).Should().BeEmpty("the Kepler driver moves the frame again");
        (world.Frames.LocalPose(station).Position - KeplerRails.StateAt(orbit, world.Time, EarthMu).Position).Length.Should().BeLessThan(1e-6);
        // At 7.5 km/s the hull rests at the origin of an orbit frame of its own (ter-dhkf rule 3), which follows the orbit.
        BodyState hull = faults.Outputs[^1].Bodies.Single(state => state.Entity == owner);
        world.Frames.Numeric(hull.Frame).Should().Be(NumericMode.Orbiting);
        world.Frames.TreeParent(station).Should().Be(hull.Frame);
        (hull.Pose.Position - world.Frames.TreeLocalPose(station).Position).Length.Should().BeLessThan(1e-3,
            "the kinematic hull is where the orbit puts the frame, at the orbit frame's origin");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The World demands an orbit and a planet with a point gravity for the Kepler driver. So the scene of the Kepler case has
    /// both, and the ship has the orbit in its spec before the game gives it the driver.
    /// </summary>
    [Theory]
    [InlineData(MotionDriver.Static)]
    [InlineData(MotionDriver.Kepler)]
    public void AHullKeepsItsBodyIdWhenItsFramesDriverChanges(MotionDriver driver)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        var (_, owner, ship) = driver == MotionDriver.Kepler ? ShipAbovePlanet(world, MotionDriver.Physics) : FlyingShip(world);
        Tick(world, 5);
        BodyID before = Hull(world, backend, owner).Id;

        world.SetFrameDriver(ship, driver);
        world.Tick();

        Hull(world, backend, owner).Id.Should().Be(before);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Gives Jolt the frame tree as it is now, with an empty change batch, as the start of a step does before it moves any
    /// body. Jolt then holds each hull in the motion of its frame's current driver, and no step has moved the hull yet. The
    /// World calls the backend only with a non-empty batch or in a step, so a test that reads the hull between the two makes
    /// this call itself.
    /// </summary>
    private static void Reconcile(World world, FaultInjectingBackend faults) => faults.Jolt.ApplyChanges(new PhysicsChanges(), world.Frames);

    /// <summary>
    /// Runs a tick whose step throws before Jolt moves any body, so that Jolt holds exactly what the tick's change batch gave
    /// it: the World sends its batch first, and the step is the only call after it. The World records the failed step in its
    /// errors, and the test clears them.
    /// </summary>
    private static void ApplyThePendingChangesWithoutAStep(World world, FaultInjectingBackend faults)
    {
        faults.ThrowFromStep = new InvalidOperationException("The test skips this step on purpose.");
        world.Tick();
        world.Errors.Should().ContainSingle().Which.Should().Contain("the physics step failed");
        world.ClearErrors();
    }

    /// <summary>The motion that the hull of a frame has under <paramref name="driver"/>.</summary>
    private static MotionType MotionOf(MotionDriver driver) => driver switch
    {
        MotionDriver.Physics => MotionType.Dynamic,
        MotionDriver.Kepler => MotionType.Kinematic,
        _ => MotionType.Static,
    };

    /// <summary>
    /// A growth rebuilds every body from its spec. The spec of a hull has the motion of its frame's current driver, and a
    /// hull whose motion changed has the motion quality and the mass of a hull that the backend creates with that motion.
    /// So the growth rebuilds the hull as it is. A step moves the hull, and its velocity with it, so the test compares the
    /// hull before and after the growth with no step between them (see <see cref="Reconcile"/> and
    /// <see cref="ApplyThePendingChangesWithoutAStep"/>).
    /// </summary>
    [Theory]
    [InlineData(MotionDriver.Static, MotionDriver.Physics)]
    [InlineData(MotionDriver.Physics, MotionDriver.Static)]
    [InlineData(MotionDriver.Kepler, MotionDriver.Physics)]
    [InlineData(MotionDriver.Physics, MotionDriver.Kepler)]
    public void AHullWhoseFramesDriverChangedComesThroughAGrowthOfItsSystemUnchanged(MotionDriver from, MotionDriver to)
    {
        var faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { MaxBodiesPerFrame = 8, WorkerThreads = 2 }));
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, faults);
        var (planet, owner, ship) = ShipAbovePlanet(world, from);
        world.Tick();
        world.SetFrameDriver(ship, to);
        Reconcile(world, faults);
        JoltPhysicsBackend.BodyFacts before = Hull(world, faults.Jolt, owner);
        before.Motion.Should().Be(MotionOf(to), "the reconcile gave the hull the motion of the new driver");

        int room = (int)faults.Jolt.CapacityOf(planet) - faults.Jolt.BodiesIn(planet).Count;
        for (int i = 0; i <= room; i++)
            world.Spawn(new SpawnDesc { Frame = planet, Pose = At(3 * i, -100), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        ApplyThePendingChangesWithoutAStep(world, faults);

        faults.Jolt.CapacityOf(planet).Should().Be(16);
        JoltPhysicsBackend.BodyFacts after = Hull(world, faults.Jolt, owner);
        (after.Position - before.Position).Length.Should().BeLessThan(1e-12);
        after.Should().Be(before with { Position = after.Position });
    }

    [Fact]
    public void TheInvariantsFindAHullWhoseMotionDoesNotMatchItsFramesDriverUntilTheNextReconcile()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        var (_, _, ship) = FlyingShip(world);
        world.SetFrameDriver(ship, MotionDriver.Static);

        backend.Invoking(b => b.AssertInvariants()).Should().Throw<ContractException>("the hull is still dynamic");

        // The next step reconciles the backend with the frame tree, so one tick does.
        world.Tick();

        backend.Invoking(b => b.AssertInvariants()).Should().NotThrow();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A hull that Jolt cannot move in the way that a driver needs.</summary>
    public enum UnmovableHull
    {
        /// <summary>A mesh hull and the physics driver. Jolt moves no mesh.</summary>
        MeshUnderPhysics,
        /// <summary>A mesh hull and the Kepler driver.</summary>
        MeshUnderKepler,
        /// <summary>A hull without mass and the physics driver. Jolt needs mass for a dynamic body.</summary>
        MasslessUnderPhysics,
    }

    /// <summary>
    /// The simulation never gives a frame a driver that its hull cannot follow: World.SetFrameDriver and World.Spawn refuse
    /// it. So a frame tree that does comes only from a bug. A test stands in for that bug: it gives the driver to the frame
    /// tree, past the World. Physics then breaks a contract in a Debug build. A Release build skips that driver for the hull:
    /// the hull keeps the motion that it has, physics reports the bug once in the errors of the step output, not as a
    /// warning, and it reports no state for the frame. A driver that the hull can follow is followed again.
    /// </summary>
    [Theory]
    [InlineData(UnmovableHull.MeshUnderPhysics)]
    [InlineData(UnmovableHull.MeshUnderKepler)]
    [InlineData(UnmovableHull.MasslessUnderPhysics)]
    public void ADriverThatAHullCannotFollowComesOnlyFromABug(UnmovableHull hull)
    {
        var warnings = new List<string>();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, warnings);
        // The Kepler driver needs a planet with a point gravity and an orbit, even for the bug.
        GravityField gravity = hull == UnmovableHull.MeshUnderKepler ? GravityField.Point(PlanetMu) : default;
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, gravity));
        var ramp = new MeshShapeDesc([new(-5, 0, -5), new(5, 0, -5), new(5, 2, 5), new(-5, 2, 5)], [0, 2, 1, 0, 3, 2]);
        BodyDesc desc = hull == UnmovableHull.MasslessUnderPhysics
            ? new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static, Mass: 0)
            : new BodyDesc(ramp, BodyMotion.Static);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(50),
            Body = desc,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), MotionDriver.Static, Orbit: ShipOrbit),
        });
        FrameHandle dock = world.GetOwnedFrame(owner);
        world.Tick();
        int reported = faults.Outputs.Count;

        world.Frames.SetDriver(dock, hull == UnmovableHull.MeshUnderKepler ? MotionDriver.Kepler : MotionDriver.Physics);
        Action ticks = () => Tick(world, 3);

#if TERTIUS_CONTRACTS
        ticks.Should().Throw<ContractException>().WithMessage($"*{owner}*");
#else
        ticks.Should().NotThrow();
        world.Errors.Should().ContainSingle().Which.Should().Contain(owner.ToString()).And.Contain("Only a bug");
        warnings.Should().BeEmpty();
        FrameStatesSince(faults, dock, reported).Should().BeEmpty();
#endif
        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Static);

#if !TERTIUS_CONTRACTS
        // A contract that failed is a bug in the engine, and the World does not go on after it. A Release build does.
        world.SetFrameDriver(dock, MotionDriver.Static);
        Tick(world, 3);

        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Static);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
#endif
    }
}
