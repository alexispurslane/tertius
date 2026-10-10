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
        Assert.Equal(planet, state.Frame);
        double height = state.Pose.Position.Y - (EarthRadius + 0.5);
        Assert.True(Math.Abs(height) < 0.02, $"rest height error {height} m");
        Assert.True(Math.Abs(state.Pose.Position.X) < 1e-3 && Math.Abs(state.Pose.Position.Z) < 1e-3, state.Pose.Position.ToString());
        Assert.True(state.LinearVelocity.Length() < 0.05f, state.LinearVelocity.ToString());
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
        Assert.Equal(-g * t, h[ball].LinearVelocity.Y, 2);
        // Semi-implicit Euler falls a little further than the exact g t² / 2.
        double fall = EarthRadius + 100 - h[ball].Pose.Position.Y;
        Assert.InRange(fall, 0.5 * g * t * t, 0.5 * g * t * (t + PhysicsHarness.Dt) + 1e-6);
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
        Assert.Equal(ship, falling.Frame);
        Assert.Equal(-9.81 * 10 * PhysicsHarness.Dt, falling.LinearVelocity.Y, 3);
        Assert.True(MathF.Abs(falling.LinearVelocity.X) < 1e-4f && MathF.Abs(falling.LinearVelocity.Z) < 1e-4f);

        h.Step(110);
        BodyState resting = h[crate];
        Assert.Equal(ship, resting.Frame);
        Assert.True(Math.Abs(resting.Pose.Position.Y - (-2 + 0.5)) < 0.02, resting.Pose.Position.ToString());
        Assert.Empty(h.HandOffs);
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
        Assert.True(h[crate].Pose.Position.Length < 1e-4, h[crate].Pose.Position.ToString());
        Assert.True(h.Frames.ProperAcceleration(ship).Length() < 1e-4f);

        // Thrust of 2000 N on 1000 kg, along the bow. Gameplay forces are in the hull's frame axes, which are space axes.
        const int ticks = 60;
        for (int i = 0; i < ticks; i++)
        {
            h.Intent(ship.Entity).Force = new Vector3(2000, 0, 0);
            h.Step();
        }

        Vector3 accelerometer = h.Frames.ProperAcceleration(ship);
        Assert.True(Vector3.Distance(accelerometer, new Vector3(0, 0, 2)) < 1e-3f, accelerometer.ToString());
        Assert.True(h.Frames.LinearVelocity(ship).X > 1.9);

        BodyState state = h[crate];
        Assert.Equal(ship, state.Frame);
        // The crate feels -2 m/s² along the ship axis, one tick after the thrust starts.
        double t = (ticks - 1) * PhysicsHarness.Dt;
        Assert.Equal(-2 * t, state.LinearVelocity.Z, 2);
        Assert.InRange(state.Pose.Position.Z, -0.5 * 2 * t * t - 0.05, -0.5 * 2 * t * t + 0.05);
        Assert.True(Math.Abs(state.Pose.Position.X) < 1e-3 && Math.Abs(state.Pose.Position.Y) < 1e-3);
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
        Assert.InRange(accelerometer.Y, 9.7f, 9.95f);
        Assert.Equal(ship, h[crate].Frame);
        Assert.True(Math.Abs(h[crate].Pose.Position.Y - (-1.5 + 0.25)) < 0.03, h[crate].Pose.Position.ToString());
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
            Assert.Equal(Vector3.Zero, h.Frames.ProperAcceleration(ship));
        }
        Assert.True((h[crate].Pose.Position - new Double3(5, 0, 0)).Length < 1e-9, h[crate].Pose.Position.ToString());
        // The ship really orbits: in 3 s it flew 22.6 km and curved about 36 m toward the planet, at constant radius.
        Double3 position = h.Frames.LocalPose(ship).Position;
        Assert.True(Math.Abs(position.Length - radius) < 1, $"radius changed by {position.Length - radius} m");
        Assert.InRange(position.Z, -speed * 3.0 - 1, -speed * 3.0 + 1);
        Assert.InRange(radius - position.X, 30, 42);
    }
}
