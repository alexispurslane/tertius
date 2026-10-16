using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The membership rules of numeric frames (docs/plans/numeric-frames.md, "Membership", step 4): at each sync point the
/// engine puts fast or far bodies into numeric frames, merges frames before their members can touch, splits groups that
/// stay apart, drains common velocity into the frame and collects empty frames.
/// </summary>
public class WorldNumericClusteringTests
{
    /// <summary>Earth's G·M, in m³/s², as in the demo scenes.</summary>
    private const double EarthMu = 3.986004418e14;

    /// <summary>The radius of the ship's orbit in the demo scenes, 400 km above Earth's mean radius, in meters.</summary>
    private const double OrbitRadius = 6.371e6 + 400e3;

    private static readonly double OrbitSpeed = Math.Sqrt(EarthMu / OrbitRadius);

    /// <summary>The velocity of a circular orbit through (<see cref="OrbitRadius"/>, 0, 0), along Z.</summary>
    private static readonly Double3 Orbit = new(0, 0, OrbitSpeed);

    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);

    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    private static readonly BodyDesc Hull = new(new BoxShapeDesc(new Vector3(5)), Mass: 1e4f, LinearDamping: 0, AngularDamping: 0,
        InteriorShape: new SphereShapeDesc(0.05f));

    private static FrameHandle Earth(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu)));

    private static Entity CrateInEarth(World world, FrameHandle earth, Double3 offset, Double3 relativeVelocity, params IComponent[] components) =>
        world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = At(OrbitRadius + offset.X, offset.Y, offset.Z),
            LinearVelocity = Orbit + relativeVelocity,
            Body = Crate,
            Components = components,
        });

    [Fact]
    public void TwoRunsWithTheSameInputMakeTheSameClustersInTheSameOrder()
    {
        List<string> first = RunScatteringCrates(), second = RunScatteringCrates();

        second.Should().Equal(first);
        first.SelectMany(line => line.Split(' ')).Distinct().Count().Should().BeGreaterThan(4,
            "the crates went through several numeric frames, so the run says something about their order");
    }

    /// <summary>
    /// Four crates that start in contact in earth and fly apart at a few m/s, and a fifth that comes back to one of them.
    /// Returns, for each tick, the physics home of every crate.
    /// </summary>
    private static List<string> RunScatteringCrates()
    {
        using World world = NewWorld(out _);
        FrameHandle earth = Earth(world);
        Entity[] crates =
        [
            CrateInEarth(world, earth, new Double3(0, 0, 0), new Double3(-2, 0, 0)),
            CrateInEarth(world, earth, new Double3(0, 1.1, 0), new Double3(0, 3, 0)),
            CrateInEarth(world, earth, new Double3(0, 0, 1.1), new Double3(0, 0, 4)),
            CrateInEarth(world, earth, new Double3(0, 1.1, 1.1), new Double3(0, 2, 2)),
            CrateInEarth(world, earth, new Double3(0, -40, 0), new Double3(0, 4, 0)),
        ];
        var lines = new List<string>();
        for (int tick = 0; tick < 900; tick++)
        {
            world.Tick();
            lines.Add(string.Join(' ', crates.Select(crate => world.PhysicsHomeOf(crate).ToString())));
        }
        world.Errors.Should().BeEmpty();
        return lines;
    }

    [Fact]
    public void APersonWhoThrustsForAMinuteNearTheShipsOrbitGainsLessThanAMillimeterPerSecondOfErrorRelativeToTheShip()
    {
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle earth = Earth(world);
        Entity hull = world.Spawn(new SpawnDesc { Frame = earth, Pose = At(OrbitRadius), LinearVelocity = Orbit, Body = Hull with { InteriorShape = null } });
        var pilot = new Pilot();
        Entity person = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = At(OrbitRadius, 0, 30),
            LinearVelocity = Orbit,
            Body = Person,
            Components = [pilot],
        });
        // The thrusters of the demo's person: 105 N along the orbit, sideways to the person's up, which points away from
        // Earth. A force from the gameplay of one tick acts in the physics step of the next.
        var thrust = new Vector3(0, 0, 105);
        pilot.EveryTick = body => body.AddForce(thrust);
        const int thrustTicks = 60 * 60;
        Tick(world, thrustTicks);
        pilot.EveryTick = null;
        Tick(world, 1);

        // The same motion in doubles, with the step scheme of Jolt and of the falling frames: the velocity gains the
        // field at the start of the step, and the force, then the position gains the new velocity.
        GravityField field = GravityField.Point(EarthMu);
        double dt = world.DeltaTime;
        Double3 hullPosition = new(OrbitRadius, 0, 0), hullVelocity = Orbit;
        Double3 personPosition = new(OrbitRadius, 0, 30), personVelocity = Orbit;
        for (int step = 0; step <= thrustTicks; step++)
        {
            hullVelocity += field.AtPrecise(hullPosition) * dt;
            hullPosition += hullVelocity * dt;
            personVelocity += field.AtPrecise(personPosition) * dt;
            if (step >= 1)
                personVelocity += (Double3)(thrust / 70) * dt;
            personPosition += personVelocity * dt;
        }

        world.Errors.Should().BeEmpty();
        Double3 relative = world.VelocityIn(person, earth) - world.VelocityIn(hull, earth);
        Double3 expected = personVelocity - hullVelocity;
        relative.Z.Should().BeGreaterThan(80, "the person thrust along the orbit for a minute");
        double sideways = Math.Sqrt(Math.Pow(relative.X - expected.X, 2) + Math.Pow(relative.Y - expected.Y, 2));
        sideways.Should().BeLessThan(1e-3, $"the person moved at {relative} relative to the ship, and the doubles say {expected}");
        Math.Abs(relative.Z - expected.Z).Should().BeLessThan(1e-3);
    }

    [Fact]
    public void APersonBehindACrateAlongTheOrbitClosesOnItAsIfItMovedWithThePersonAndTouchesItOnlyWhenTheyMeet()
    {
        using World world = NewWorld(out _);
        FrameHandle earth = Earth(world);
        Entity crate = CrateInEarth(world, earth, Double3.Zero, Double3.Zero);
        // The person starts 20 m behind the crate along the orbit and closes at 1 m/s. Its capsule of radius 0.3 m meets
        // the crate's back face, 0.5 m from its center, after about 19.2 m.
        Entity person = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = At(OrbitRadius, 0, -20),
            LinearVelocity = Orbit + new Double3(0, 0, 1),
            Body = Person,
        });
        Tick(world, 15 * 60);

        world.Errors.Should().BeEmpty();
        Double3 offset = world.PoseIn(person, earth).Position - world.PoseIn(crate, earth).Position;
        offset.Z.Should().BeApproximately(-5, 0.05, "the person closed on the crate at 1 m/s, as if the crate moved with it");
        (world.VelocityIn(person, earth) - world.VelocityIn(crate, earth)).Z.Should().BeApproximately(1, 0.01, "they have not met yet");

        Tick(world, 10 * 60);
        offset = world.PoseIn(person, earth).Position - world.PoseIn(crate, earth).Position;
        offset.Z.Should().BeInRange(-0.9, -0.75, "they met, and the person pushes the crate along without passing into it");
        (world.VelocityIn(person, earth) - world.VelocityIn(crate, earth)).Z.Should().BeApproximately(0, 0.05);
    }

    [Fact]
    public void ACrateAtRestOnAFloorFarFromItsFramesOriginGetsARestingFrameAndStaysAtRest()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        const double top = 6.371e6;
        Entity floor = Floor(world, ground, top);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, top + 0.5), Body = Crate });
        Tick(world, 120);

        world.Errors.Should().BeEmpty();
        FrameHandle home = world.PhysicsHomeOf(crate);
        home.Should().NotBe(ground, "the crate is 6,371 km from the origin of the frame ground");
        world.Frames.Numeric(home).Should().Be(NumericMode.Resting);
        world.PhysicsHomeOf(floor).Should().Be(home, "the floor that holds the crate went with it");
        world.GetFrame(crate).Should().Be(ground);
        (world.PoseIn(crate, ground).Position - new Double3(0, top + 0.5, 0)).Length.Should().BeLessThan(0.01);
        world.VelocityIn(crate, ground).Length.Should().BeLessThan(1e-3);
    }

    [Fact]
    public void ACrateThrownOutOfTheShipJoinsTheShipsNumericFrameThenLeavesItOnceWithNoThrashing()
    {
        using World world = NewWorld(out _);
        FrameHandle earth = Earth(world);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = earth,
            Pose = At(OrbitRadius),
            LinearVelocity = Orbit,
            Body = Hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Physics),
        });
        world.Frames.TryGetFrame(owner, out FrameHandle ship).Should().BeTrue();
        // Thrown out of the ship along its Z axis at 3 m/s, from beside the hull.
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(0, 0, 10), LinearVelocity = new Double3(0, 0, 3), Body = Crate });

        var crateHomes = new List<FrameHandle> { world.PhysicsHomeOf(crate) };
        var hullHomes = new List<FrameHandle> { world.PhysicsHomeOf(owner) };
        for (int tick = 0; tick < 30 * 60; tick++)
        {
            world.Tick();
            if (world.PhysicsHomeOf(crate) != crateHomes[^1])
                crateHomes.Add(world.PhysicsHomeOf(crate));
            if (world.PhysicsHomeOf(owner) != hullHomes[^1])
                hullHomes.Add(world.PhysicsHomeOf(owner));
        }

        world.Errors.Should().BeEmpty();
        hullHomes.Should().HaveCount(2, "the hull got its falling frame once and kept it");
        FrameHandle shipNumeric = hullHomes[1];
        world.Frames.Numeric(shipNumeric).Should().Be(NumericMode.Falling);
        crateHomes.Should().HaveCount(3, "the crate left the ship into the ship's numeric frame, then split off once");
        crateHomes[0].Should().Be(ship);
        crateHomes[1].Should().Be(shipNumeric);
        world.Frames.Numeric(crateHomes[2]).Should().Be(NumericMode.Falling);
        world.GetFrame(crate).Should().Be(earth);
        // The crate flies on at 3 m/s from the ship, about 90 m away after 30 s.
        Double3 apart = world.PoseIn(crate, earth).Position - world.PoseIn(owner, earth).Position;
        apart.Length.Should().BeInRange(80, 100);
    }
}
