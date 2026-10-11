using System.Numerics;

namespace Tertius.Physics.Tests;

public class GravityTests
{
    private const double EarthRadius = 6_371_000;
    private const double EarthMu = 3.986004418e14;

    [Fact]
    public void BoxComesToRestOnPlanetSurfaceFarFromOrigin()
    {
        using var h = new PhysicsHarness();
        FrameHandle planet = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu));
        // The ground is a slab whose top face lies on the surface.
        h.AddBody(planet, Shapes.Floor(50), Shapes.At(0, EarthRadius - 0.5, 0));
        Entity box = h.AddBody(planet, Shapes.Box(0.5f, mass: 10), Shapes.At(0, EarthRadius + 2, 0));

        h.Step(150);

        BodyState state = h[box];
        state.Frame.Should().Be(planet);
        double height = state.Pose.Position.Y - (EarthRadius + 0.5);
        height.Should().BeApproximately(0, 0.02, "the box rests on the surface");
        state.Pose.Position.X.Should().BeApproximately(0, 1e-3);
        state.Pose.Position.Z.Should().BeApproximately(0, 1e-3);
        state.LinearVelocity.Length().Should().BeLessThan(0.05f);
    }

    /// <summary>What Jolt holds for the body of <paramref name="entity"/> in the system of <paramref name="frame"/>.</summary>
    private static JoltPhysicsBackend.BodyFacts BodyOf(PhysicsHarness h, FrameHandle frame, Entity entity) =>
        h.Backend.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, entity));

    /// <summary>A ground frame with uniform gravity, a floor whose top is at y = 0, and a box of 1 m that rests on it.</summary>
    private static (FrameHandle Ground, Entity Box) BoxOnTheGround(PhysicsHarness h)
    {
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        h.AddBody(ground, Shapes.Floor(50), Shapes.At(0, -0.5, 0));
        Entity box = h.AddBody(ground, Shapes.Box(0.5f, mass: 10), Shapes.At(0, 0.5, 0));
        h.Step();
        return (ground, box);
    }

    /// <summary>
    /// Gravity is a force that the backend adds to each awake body before each step. The force leaves the body's sleep
    /// timer alone, so a body that rests under gravity falls asleep, the same as a body that rests without gravity.
    /// </summary>
    [Fact]
    public void ABoxThatRestsOnAFloorUnderGravityFallsAsleepWithinTwoSeconds()
    {
        using var h = new PhysicsHarness();
        var (ground, box) = BoxOnTheGround(h);

        int ticks = h.StepUntil(() => !BodyOf(h, ground, box).Active, 120);

        ticks.Should().BeLessThan(120, "the box falls asleep within 2 s");
        h[box].Pose.Position.Y.Should().BeApproximately(0.5, 0.01, "the box sleeps on the floor");
        h[box].LinearVelocity.Should().Be(Vector3.Zero, "the last report of a body that fell asleep has no velocity");
    }

    [Fact]
    public void ABoxThatSleepsUnderGravityWakesWhenABallHitsIt()
    {
        using var h = new PhysicsHarness();
        var (ground, box) = BoxOnTheGround(h);
        h.StepUntil(() => !BodyOf(h, ground, box).Active, 120);
        BodyOf(h, ground, box).Active.Should().BeFalse("the box fell asleep");
        // A sleeping body gets no gravity force, so nothing reports it, and gravity does not wake it.
        var reported = new List<long>();
        h.AfterStep = output =>
        {
            if (output.Bodies.Any(state => state.Entity == box))
                reported.Add(h.Tick);
        };
        h.Step(30);
        reported.Should().BeEmpty();

        h.AddBody(ground, Shapes.Ball(0.25f, mass: 1), Shapes.At(0, 2, 0));
        int ticks = h.StepUntil(() => BodyOf(h, ground, box).Active, 120);

        ticks.Should().BeLessThan(120, "the ball hits the box within 2 s");
        reported.Should().NotBeEmpty("the box is awake, so physics reports it again");
    }

    [Fact]
    public void FreeFallNearSurfaceMatchesPointGravity()
    {
        using var h = new PhysicsHarness();
        FrameHandle planet = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu));
        Entity ball = h.AddBody(planet, Shapes.Ball(0.2f), Shapes.At(0, EarthRadius + 100, 0));

        h.Step(30);

        double g = EarthMu / ((EarthRadius + 100) * (EarthRadius + 100));
        double t = 30 * PhysicsHarness.Dt;
        ((double)h[ball].LinearVelocity.Y).Should().BeApproximately(-g * t, 5e-3);
        // Semi-implicit Euler falls a little further than the exact g t² / 2.
        double fall = EarthRadius + 100 - h[ball].Pose.Position.Y;
        fall.Should().BeInRange(0.5 * g * t * t, 0.5 * g * t * (t + PhysicsHarness.Dt) + 1e-6);
    }

    [Fact]
    public void UniformArtificialGravityActsInShipAxes()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        // The ship lies on its side in space. Its gravity points along its own -Y, which is space +X.
        var shipPose = new DPose(new Double3(1000, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2));
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(10, 0.25f, 10)), new Pose(new Vector3(0, -2.25f, 0), Quaternion.Identity))]);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(10, 3, 10)), InteriorShape: deck);
        FrameHandle ship = h.AddFrameWithBody(space, shipPose, FrameBounds.Sphere(40), MotionDriver.Static, hull,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Entity crate = h.AddBody(ship, Shapes.Box(0.5f, mass: 5), Shapes.At(0, 1, 0));

        h.Step(10);
        BodyState falling = h[crate];
        falling.Frame.Should().Be(ship);
        ((double)falling.LinearVelocity.Y).Should().BeApproximately(-9.81 * 10 * PhysicsHarness.Dt, 5e-4);
        MathF.Abs(falling.LinearVelocity.X).Should().BeLessThan(1e-4f);
        MathF.Abs(falling.LinearVelocity.Z).Should().BeLessThan(1e-4f);

        h.Step(110);
        BodyState resting = h[crate];
        resting.Frame.Should().Be(ship);
        resting.Pose.Position.Y.Should().BeApproximately(-2 + 0.5, 0.02, "the crate rests on the deck");
        h.HandOffs.Should().BeEmpty();
    }

    [Fact]
    public void ThrustingShipPushesLooseCrateTowardStern()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        // The ship's bow (+Z in ship axes) points along space +X.
        var shipPose = new DPose(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2));
        var sternWall = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(5, 3, 0.25f)), new Pose(new Vector3(0, 0, -9.25f), Quaternion.Identity))]);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 10)), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: sternWall);
        FrameHandle ship = h.AddFrameWithBody(space, shipPose, FrameBounds.Sphere(50), MotionDriver.Physics, hull);
        Entity crate = h.AddBody(ship, Shapes.Box(0.5f, mass: 1), Shapes.At(0, 0, 0));

        // Coasting: the crate floats in place.
        h.Step(60);
        h[crate].Pose.Position.Length.Should().BeLessThan(1e-4);
        h.Frames.ProperAcceleration(ship).Length().Should().BeLessThan(1e-4f);

        // Thrust of 2000 N on 1000 kg, along the bow. Gameplay forces are in the hull's frame axes, which are space axes.
        const int ticks = 60;
        for (int i = 0; i < ticks; i++)
        {
            h.Intent(ship.Entity).Force = new Vector3(2000, 0, 0);
            h.Step();
        }

        Vector3 accelerometer = h.Frames.ProperAcceleration(ship);
        Vector3.Distance(accelerometer, new Vector3(0, 0, 2)).Should().BeLessThan(1e-3f, "the accelerometer reads the thrust, {0}", accelerometer);
        h.Frames.LinearVelocity(ship).X.Should().BeGreaterThan(1.9);

        BodyState state = h[crate];
        state.Frame.Should().Be(ship);
        // The crate feels -2 m/s² along the ship axis, one tick after the thrust starts.
        double t = (ticks - 1) * PhysicsHarness.Dt;
        ((double)state.LinearVelocity.Z).Should().BeApproximately(-2 * t, 5e-3);
        state.Pose.Position.Z.Should().BeInRange(-0.5 * 2 * t * t - 0.05, -0.5 * 2 * t * t + 0.05);
        state.Pose.Position.X.Should().BeApproximately(0, 1e-3);
        state.Pose.Position.Y.Should().BeApproximately(0, 1e-3);
    }

    [Fact]
    public void LandedShipPassesPlanetGravityToItsInterior()
    {
        using var h = new PhysicsHarness();
        FrameHandle planet = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu));
        h.AddBody(planet, Shapes.Floor(100), Shapes.At(0, EarthRadius - 0.5, 0));
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(4, 0.25f, 4)), new Pose(new Vector3(0, -1.75f, 0), Quaternion.Identity))]);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(4, 2, 4)), Mass: 5000, InteriorShape: deck);
        FrameHandle ship = h.AddFrameWithBody(planet, Shapes.At(0, EarthRadius + 2.05, 0), FrameBounds.Sphere(20), MotionDriver.Physics, hull);
        Entity crate = h.AddBody(ship, Shapes.Box(0.25f), Shapes.At(0, 0, 0));

        h.Step(120);

        // The ship rests on the ground, so its accelerometer reads 1 g upward, and the crate falls to the deck.
        Vector3 accelerometer = h.Frames.ProperAcceleration(ship);
        accelerometer.Y.Should().BeInRange(9.7f, 9.95f);
        h[crate].Frame.Should().Be(ship);
        h[crate].Pose.Position.Y.Should().BeApproximately(-1.5 + 0.25, 0.03, "the crate rests on the deck");
    }

    [Fact]
    public void ShipInOrbitIsInFreeFallAndItsCargoFloats()
    {
        using var h = new PhysicsHarness();
        FrameHandle planet = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu));
        const double radius = 7_000_000;
        float speed = (float)Math.Sqrt(EarthMu / radius);
        var hull = new BodyDesc(new SphereShapeDesc(3), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(0.5f));
        FrameHandle ship = h.AddFrameWithBody(planet, Shapes.At(radius, 0, 0), FrameBounds.Sphere(30), MotionDriver.Physics, hull,
            velocity: new Vector3(0, 0, -speed));
        Entity crate = h.AddBody(ship, Shapes.Box(0.25f), Shapes.At(5, 0, 0));

        // At 7.5 km/s a float velocity step is about 0.03 m/s² over one tick. That noise must not reach the cargo.
        for (int i = 0; i < 180; i++)
        {
            h.Step();
            h.Frames.ProperAcceleration(ship).Should().Be(Vector3.Zero);
        }
        (h[crate].Pose.Position - new Double3(5, 0, 0)).Length.Should().BeLessThan(1e-9);
        // The ship really orbits: in 3 s it flew 22.6 km and curved about 36 m toward the planet, at constant radius.
        Double3 position = h.Frames.LocalPose(ship).Position;
        position.Length.Should().BeApproximately(radius, 1, "the orbit is circular");
        position.Z.Should().BeInRange(-speed * 3.0 - 1, -speed * 3.0 + 1);
        (radius - position.X).Should().BeInRange(30, 42);
    }
}
