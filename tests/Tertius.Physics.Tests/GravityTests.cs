using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The gravity fields of frames, on the World and the real backend: a point field of a planet, a uniform field in a
/// ship's own axes, the accelerometer of a ship, and what a sleeping body feels. The tests read the World's state, and the
/// outputs that Jolt really sent when a test needs to know what Jolt reported.
/// </summary>
public class GravityTests
{
    private const double EarthRadius = 6_371_000;
    private const double EarthMu = Scenes.EarthMu;

    /// <summary>A dynamic box with sides of 2 · <paramref name="half"/> m that nothing slows down on its own and that does not bounce.</summary>
    private static BodyDesc BoxBody(float half, float mass) =>
        new(new BoxShapeDesc(new Vector3(half)), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A dynamic ball that nothing slows down on its own and that does not bounce.</summary>
    private static BodyDesc BallBody(float radius, float mass = 1f) =>
        new(new SphereShapeDesc(radius), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A static slab of 2 · <paramref name="halfSize"/> m by 1 m by 2 · <paramref name="halfSize"/> m.</summary>
    private static BodyDesc FloorBody(float halfSize) => new(new BoxShapeDesc(new Vector3(halfSize, 0.5f, halfSize)), BodyMotion.Static);

    /// <summary>The planet: a root frame with the point gravity of the Earth.</summary>
    private static FrameHandle Planet(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu)));

    /// <summary>Every body state of <paramref name="entity"/> that Jolt sent, from the output at index <paramref name="from"/> on.</summary>
    private static List<BodyState> ReportsOf(FaultInjectingBackend faults, Entity entity, int from = 0) =>
        faults.Outputs.Skip(from).SelectMany(output => output.Bodies).Where(state => state.Entity == entity).ToList();

    [Fact]
    public void BoxComesToRestOnPlanetSurfaceFarFromOrigin()
    {
        using World world = NewWorld(out _);
        FrameHandle planet = Planet(world);
        // The ground is a slab whose top face lies on the surface.
        world.Spawn(new SpawnDesc { Frame = planet, Pose = At(0, EarthRadius - 0.5), Body = FloorBody(50) });
        Entity box = world.Spawn(new SpawnDesc { Frame = planet, Pose = At(0, EarthRadius + 2), Body = BoxBody(0.5f, 10) });

        Tick(world, 150);

        world.GetFrame(box).Should().Be(planet);
        DPose pose = world.PoseIn(box, planet);
        double height = pose.Position.Y - (EarthRadius + 0.5);
        height.Should().BeApproximately(0, 0.02, "the box rests on the surface");
        pose.Position.X.Should().BeApproximately(0, 1e-3);
        pose.Position.Z.Should().BeApproximately(0, 1e-3);
        world.VelocityInOwnFrame(box).Length().Should().BeLessThan(0.05f);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A ground frame with uniform gravity, a floor whose top is at y = 0, and a box of 1 m that rests on it.</summary>
    private static (FrameHandle Ground, Entity Box) BoxOnTheGround(World world)
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5), Body = BoxBody(0.5f, 10) });
        world.Tick();
        return (ground, box);
    }

    /// <summary>
    /// Gravity is a force that the backend adds to each awake body before each step. The force leaves the body's sleep
    /// timer alone, so a body that rests under gravity falls asleep, the same as a body that rests without gravity.
    /// </summary>
    [Fact]
    public void ABoxThatRestsOnAFloorUnderGravityFallsAsleepWithinTwoSeconds()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var (ground, box) = BoxOnTheGround(world);

        int ticks = TickUntil(world, () => !IsAwake(faults.Jolt, ground, box), 120);

        ticks.Should().BeLessThan(120, "the box falls asleep within 2 s");
        world.PoseInOwnFrame(box).Position.Y.Should().BeApproximately(0.5, 0.01, "the box sleeps on the floor");
        ReportsOf(faults, box)[^1].LinearVelocity.Should().Be(Vector3.Zero, "the last report of a body that fell asleep has no velocity");
        world.VelocityInOwnFrame(box).Should().Be(Vector3.Zero, "the World holds the state of that last report");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABoxThatSleepsUnderGravityWakesWhenABallHitsIt()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var (ground, box) = BoxOnTheGround(world);
        TickUntil(world, () => !IsAwake(faults.Jolt, ground, box), 120);
        IsAwake(faults.Jolt, ground, box).Should().BeFalse("the box fell asleep");
        // A sleeping body gets no gravity force, so nothing reports it, and gravity does not wake it.
        int asleepSince = faults.Outputs.Count;
        Tick(world, 30);
        ReportsOf(faults, box, asleepSince).Should().BeEmpty();

        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 2), Body = BallBody(0.25f) });
        int ticks = TickUntil(world, () => IsAwake(faults.Jolt, ground, box), 120);

        ticks.Should().BeLessThan(120, "the ball hits the box within 2 s");
        ReportsOf(faults, box, asleepSince).Should().NotBeEmpty("the box is awake, so physics reports it again");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void FreeFallNearSurfaceMatchesPointGravity()
    {
        using World world = NewWorld(out _);
        FrameHandle planet = Planet(world);
        Entity ball = world.Spawn(new SpawnDesc { Frame = planet, Pose = At(0, EarthRadius + 100), Body = BallBody(0.2f) });

        Tick(world, 30);

        double g = EarthMu / ((EarthRadius + 100) * (EarthRadius + 100));
        double t = 30 * (double)world.DeltaTime;
        // Jolt's velocity is the chord of the last step, the instant velocity half a step before its end.
        ((double)world.VelocityInOwnFrame(ball).Y).Should().BeApproximately(-g * (t - world.DeltaTime / 2.0), 5e-3);
        // Jolt gets the chord of the spawn's instant velocity of zero, so the ball falls the exact g t² / 2.
        double fall = EarthRadius + 100 - world.PoseIn(ball, planet).Position.Y;
        fall.Should().BeApproximately(0.5 * g * t * t, 1e-4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void UniformArtificialGravityActsInShipAxes()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        // The ship lies on its side in space. Its gravity points along its own -Y, which is space +X.
        var shipPose = new DPose(new Double3(1000, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2));
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(10, 0.25f, 10)), new Pose(new Vector3(0, -2.25f, 0), Quaternion.Identity))]);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(10, 3, 10)), BodyMotion.Static, InteriorShape: deck);
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = shipPose,
            Body = hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(40), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))),
        }));
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(0, 1), Body = BoxBody(0.5f, 5) });

        Tick(world, 10);
        world.GetFrame(crate).Should().Be(ship);
        Vector3 falling = world.VelocityInOwnFrame(crate);
        // Jolt's velocity is the chord of the last step, the instant velocity half a step before its end.
        ((double)falling.Y).Should().BeApproximately(-9.81 * 9.5 * world.DeltaTime, 5e-4);
        MathF.Abs(falling.X).Should().BeLessThan(1e-4f);
        MathF.Abs(falling.Z).Should().BeLessThan(1e-4f);

        Tick(world, 110);
        world.GetFrame(crate).Should().Be(ship);
        world.PoseInOwnFrame(crate).Position.Y.Should().BeApproximately(-2 + 0.5, 0.02, "the crate rests on the deck");
        faults.Outputs.SelectMany(output => output.HandOffs).Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ThrustingShipPushesLooseCrateTowardStern()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        // The ship's bow (+Z in ship axes) points along space +X.
        var shipPose = new DPose(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2));
        var sternWall = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(5, 3, 0.25f)), new Pose(new Vector3(0, 0, -9.25f), Quaternion.Identity))]);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 10)), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: sternWall);
        var pilot = new Pilot();
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = shipPose,
            Body = hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Physics),
            Components = [pilot],
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(0, 0, 0), Body = BoxBody(0.5f, 1) });

        // Coasting: the crate floats in place.
        Tick(world, 60);
        world.PoseInOwnFrame(crate).Position.Length.Should().BeLessThan(1e-4);
        world.Frames.ProperAcceleration(ship).Length().Should().BeLessThan(1e-4f);

        // Thrust of 2000 N on 1000 kg, along the bow. Gameplay forces are in the hull's frame axes, which are space axes.
        // A force that gameplay adds in one tick acts in the physics step of the next tick. So the first tick only asks
        // for the push, and each of the next 60 steps has it.
        const int ticks = 60;
        pilot.EveryTick = body => body.AddForce(new Vector3(2000, 0, 0));
        Tick(world, 1 + ticks);

        Vector3 accelerometer = world.Frames.ProperAcceleration(ship);
        Vector3.Distance(accelerometer, new Vector3(0, 0, 2)).Should().BeLessThan(1e-3f, "the accelerometer reads the thrust, {0}", accelerometer);
        world.Frames.LinearVelocity(ship).X.Should().BeGreaterThan(1.9);

        world.GetFrame(crate).Should().Be(ship);
        // The crate feels -2 m/s² along the ship axis, one tick after the thrust starts.
        double t = (ticks - 1) * (double)world.DeltaTime;
        DPose pose = world.PoseInOwnFrame(crate);
        ((double)world.VelocityInOwnFrame(crate).Z).Should().BeApproximately(-2 * t, 5e-3);
        pose.Position.Z.Should().BeInRange(-0.5 * 2 * t * t - 0.05, -0.5 * 2 * t * t + 0.05);
        pose.Position.X.Should().BeApproximately(0, 1e-3);
        pose.Position.Y.Should().BeApproximately(0, 1e-3);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void LandedShipPassesPlanetGravityToItsInterior()
    {
        using World world = NewWorld(out _);
        FrameHandle planet = Planet(world);
        world.Spawn(new SpawnDesc { Frame = planet, Pose = At(0, EarthRadius - 0.5), Body = FloorBody(100) });
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(4, 0.25f, 4)), new Pose(new Vector3(0, -1.75f, 0), Quaternion.Identity))]);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(4, 2, 4)), Mass: 5000, InteriorShape: deck);
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Pose = At(0, EarthRadius + 2.05),
            Body = hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Physics),
        }));
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(0, 0, 0), Body = BoxBody(0.25f, 1) });

        Tick(world, 120);

        // The ship rests on the ground, so its accelerometer reads 1 g upward, and the crate falls to the deck.
        Vector3 accelerometer = world.Frames.ProperAcceleration(ship);
        accelerometer.Y.Should().BeInRange(9.7f, 9.95f);
        world.GetFrame(crate).Should().Be(ship);
        world.PoseInOwnFrame(crate).Position.Y.Should().BeApproximately(-1.5 + 0.25, 0.03, "the crate rests on the deck");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ShipInOrbitIsInFreeFallAndItsCargoFloats()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle planet = Planet(world);
        const double radius = 7_000_000;
        float speed = (float)Math.Sqrt(EarthMu / radius);
        var hull = new BodyDesc(new SphereShapeDesc(3), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(0.5f));
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Pose = At(radius),
            LinearVelocity = new Double3(0, 0, -speed),
            Body = hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Physics),
        }));
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(5), Body = BoxBody(0.25f, 1) });

        // At 7.5 km/s a float velocity step is about 0.03 m/s² over one tick. That noise must not reach the cargo.
        for (int i = 0; i < 180; i++)
        {
            world.Tick();
            world.Frames.ProperAcceleration(ship).Should().Be(Vector3.Zero);
        }
        List<FrameStepRecord> sent = faults.Outputs.SelectMany(output => output.Frames).ToList();
        sent.Should().NotBeEmpty("Jolt reports the state of the ship's frame");
        sent.Should().OnlyContain(state => state.Motion.ProperAcceleration == Vector3.Zero, "Jolt reported no proper acceleration for the ship in any step");
        (world.PoseInOwnFrame(crate).Position - new Double3(5, 0, 0)).Length.Should().BeLessThan(1e-9);
        // The ship really orbits: in 3 s it flew 22.6 km and curved about 36 m toward the planet, at constant radius.
        Double3 position = world.Frames.LocalPose(ship).Position;
        position.Length.Should().BeApproximately(radius, 1, "the orbit is circular");
        position.Z.Should().BeInRange(-speed * 3.0 - 1, -speed * 3.0 + 1);
        (radius - position.X).Should().BeInRange(30, 42);
        world.Errors.Should().BeEmpty();
    }
}
