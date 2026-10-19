using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The proper acceleration of a frame, on the World and the real backend (docs/plans/rails-components.md, "The proper
/// acceleration of a frame"). Step 2 of every sync point derives it from the frame's step record by one rule: the
/// frame's acceleration minus the gravity that acts at its place, in the frame's own axes. Physics measures a dynamic
/// hull's value by the same rule. No gameplay writes it. The ground of these tests is a root frame with a uniform gravity
/// of 9.81 m/s² along -Y (see <see cref="Ground"/>), and the planet a root frame with the point gravity of the Earth.
/// </summary>
public class FrameProperAccelerationTests
{
    private const float G = 9.81f;
    private static readonly Vector3 Weight = new(0, G, 0);

    /// <summary>A box that can be the hull of a frame, with <paramref name="motion"/>.</summary>
    private static BodyDesc HullBody(BodyMotion motion, float mass = 1000) =>
        new(new BoxShapeDesc(new Vector3(2, 0.5f, 2)), motion, Mass: mass, LinearDamping: 0, AngularDamping: 0);

    private static Entity SpawnFrame(World world, FrameHandle parent, DPose pose, BodyDesc? body, params IComponent[] components) =>
        world.Spawn(new SpawnDesc
        {
            Frame = parent,
            Pose = pose,
            Body = body,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Components = components,
        });

    private static FrameHandle Planet(World world) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), GravityField.Point(Scenes.EarthMu)));

    /// <summary>The gravity of <paramref name="field"/> at <paramref name="place"/>, reversed, in the axes of <paramref name="rotation"/>.</summary>
    private static Vector3 Reversed(GravityField field, Double3 place, DQuaternion rotation) =>
        rotation.Conjugate().Rotate(-field.AtPrecise(place)).ToVector3();

    private static void ShouldBeNear(Vector3 actual, Vector3 expected, float tolerance, string because) =>
        Vector3.Distance(actual, expected).Should().BeLessThan(tolerance, "{0}: {1} is not {2}", because, actual, expected);

    [Theory]
    [InlineData("no body")]
    [InlineData("kinematic hull in its semantic parent")]
    [InlineData("kinematic hull in a following frame")]
    public void AFrameOnAKeplerOrbitInAFreelyFallingParentHasExactlyZeroProperAcceleration(string kind)
    {
        using World world = NewWorld(out _);
        FrameHandle space = Scenes.AsteroidSpace(world);
        BodyDesc? body = kind == "no body" ? null : HullBody(BodyMotion.Kinematic);
        // A circular orbit of 50 km moves at 632 m/s, too slow and too near for a numeric frame. One of 5 km moves at 2 km/s.
        double radius = kind == "kinematic hull in a following frame" ? 5_000 : 50_000;
        FrameHandle moon = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = body,
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f)),
        }.OnKeplerOrbit(world, KeplerOrbit.Circular(radius))));
        Tick(world, 3);
        world.Frames.InFollowingFrame(moon).Should().Be(kind == "kinematic hull in a following frame");

        for (int tick = 0; tick < 30; tick++)
        {
            world.Tick();

            world.Frames.ProperAcceleration(moon).Should().Be(Vector3.Zero, "a frame on its orbit falls freely");
        }
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("static hull")]
    [InlineData("no body")]
    [InlineData("kinematic hull without a motion intent")]
    public void AFrameThatHoldsStillInAGravityFieldFeelsThatGravityReversed(string kind)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, G);
        BodyDesc? body = kind switch
        {
            "static hull" => HullBody(BodyMotion.Static),
            "kinematic hull without a motion intent" => HullBody(BodyMotion.Kinematic),
            _ => null,
        };
        FrameHandle cabin = world.GetOwnedFrame(SpawnFrame(world, ground, At(0, 100), body));

        Tick(world, 3);

        ShouldBeNear(world.Frames.ProperAcceleration(cabin), Weight, 1e-6f, "the cabin's contents have weight");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AStaticHullThatTeleportsFeelsTheGravityAtItsNewPlaceReversed()
    {
        using World world = NewWorld(out _);
        FrameHandle planet = Planet(world);
        GravityField field = world.Frames.Gravity(planet);
        var pilot = new Pilot();
        var start = new Double3(6.4e6, 0, 0);
        FrameHandle platform = world.GetOwnedFrame(SpawnFrame(world, planet, new DPose(start, DQuaternion.Identity),
            HullBody(BodyMotion.Static), pilot));
        Tick(world, 3);
        ShouldBeNear(world.Frames.ProperAcceleration(platform), Reversed(field, start, DQuaternion.Identity), 1e-5f, "before the teleport");

        var target = new DPose(new Double3(0, 4.6e6, 4.6e6), DQuaternion.FromAxisAngle(Double3.UnitX, 0.5));
        pilot.Once = hull => hull.Teleport(target);
        Tick(world, 2);

        DPose moved = world.Frames.LocalPose(platform);
        (moved.Position - target.Position).Length.Should().BeLessThan(1e-6, "the platform stands where it teleported");
        ShouldBeNear(world.Frames.ProperAcceleration(platform), Reversed(field, target.Position, target.Rotation), 1e-5f,
            "the platform feels the gravity at its new place, in its own axes");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ATurntableOnTheGroundGivesItsCargoWeightWithNoValueToCopy()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, G);
        FrameHandle turntable = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 100),
            AsFrame = new FrameSpec(FrameBounds.Sphere(20)),
        }.Spinning(world, new FrameSpin(DQuaternion.Identity, 0.5))));
        Entity crate = world.Spawn(new SpawnDesc { Frame = turntable, Pose = At(3, 1), Body = Ball() });
        const int ticks = 30;

        Tick(world, ticks);

        ShouldBeNear(world.Frames.ProperAcceleration(turntable), Weight, 1e-5f, "the turntable holds its place against the ground's gravity");
        world.GetFrame(crate).Should().Be(turntable);
        ((double)world.VelocityInOwnFrame(crate).Y).Should().BeLessThan(-G * (ticks - 2) * world.DeltaTime, "the crate falls in the turntable");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShipThatParksFeelsTheGravityAtItsPlaceReversedAndKeepsNoThrust()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, G);
        const float mass = 1000;
        // The engine holds the ship against gravity and lifts it at 1 m/s² more.
        var pilot = new Pilot { EveryTick = hull => hull.AddForce(new Vector3(0, mass * (G + 1), 0)) };
        Entity owner = SpawnFrame(world, ground, At(0, 100), HullBody(BodyMotion.Dynamic, mass), pilot);
        FrameHandle ship = world.GetOwnedFrame(owner);
        Tick(world, 30);
        ShouldBeNear(world.Frames.ProperAcceleration(ship), new Vector3(0, G + 1, 0), 1e-2f, "the engine pushes the ship");

        pilot.EveryTick = null;
        world.SetMotionNextTick(owner, BodyMotion.Static);
        Tick(world, 3);

        ShouldBeNear(world.Frames.ProperAcceleration(ship), Weight, 1e-6f, "a parked ship's cargo has the weight of the ground and no thrust");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ATransformWriteGivesTheFrameTheGravityAtItsPlaceReversed()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, G);
        var turned = new DPose(new Double3(5, 100, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2));
        var flight = new ClimbThenWrite(turned);
        FrameHandle cabin = world.GetOwnedFrame(SpawnFrame(world, ground, At(0, 100), body: null, flight));
        Tick(world, 3);
        ShouldBeNear(world.Frames.ProperAcceleration(cabin), new Vector3(0, G + 2, 0), 1e-5f, "the samples lift the cabin");

        flight.Write = true;
        world.Tick();

        world.Frames.LocalPose(cabin).Should().Be(turned, "the write wins for one tick");
        ShouldBeNear(world.Frames.ProperAcceleration(cabin), new Vector3(G, 0, 0), 1e-5f,
            "a write leaves the cabin at rest, so it feels the ground's gravity reversed, in its turned axes");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFrameInAnAcceleratingFrameFeelsItsParentsProperAcceleration()
    {
        using World world = NewWorld(out _);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var engine = new Thrust();
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
            Components = [engine],
        }));
        FrameHandle turntable = world.GetOwnedFrame(SmallFrame(world, ship, At(10), body: null));
        FrameHandle cart = world.GetOwnedFrame(SmallFrame(world, turntable, At(1), body: null));
        // The fighter's hull, 0.5 m half high, stands on the hangar's floor, whose top is at y = 0 in the ship.
        world.Spawn(new SpawnDesc { Frame = ship, Pose = At(-10, -0.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 0.5f, 5)), BodyMotion.Static) });
        FrameHandle fighter = world.GetOwnedFrame(SmallFrame(world, ship, At(-10, 0.5), HullBody(BodyMotion.Dynamic, 100)));
        Tick(world, 3);
        var thrust = new Vector3(0, 1.5f, 0);

        // The engine starts in tick T: its sample acts in the step of the next tick, step N.
        engine.Acceleration = thrust;
        world.Tick();
        world.Frames.ProperAcceleration(ship).Should().Be(Vector3.Zero, "the step of tick T applied the sample of the tick before");

        world.Tick();
        world.Frames.ProperAcceleration(ship).Should().Be(thrust, "step N carried the ship at 1.5 m/s² in free space");
        world.Frames.ProperAcceleration(turntable).Should().Be(Vector3.Zero,
            "during step N the ship's stored value was zero, so no gravity acted at the turntable's place");
        ShouldBeNear(world.Frames.ProperAcceleration(fighter), Vector3.Zero, 1e-3f, "physics gave the fighter no gravity in step N");

        world.Tick();
        world.Frames.ProperAcceleration(turntable).Should().Be(thrust, "in step N + 1 the ship's value acted on everything in it");
        ShouldBeNear(world.Frames.ProperAcceleration(fighter), thrust, 1e-2f, "the floor held the fighter against the gravity of step N + 1");
        world.Frames.ProperAcceleration(cart).Should().Be(Vector3.Zero, "during step N + 1 the turntable's stored value was still zero");

        world.Tick();
        world.Frames.ProperAcceleration(cart).Should().Be(thrust, "a change moves one level down the chain in each step");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ANewFrameTakesItsProperAccelerationByTheRuleAtItsFirstSyncPoint()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, G);
        FrameHandle cabin = world.GetOwnedFrame(SpawnFrame(world, ground, At(0, 100), HullBody(BodyMotion.Static)));
        // The ball's bottom hangs 1 m above the cabin's floor, the top of the static hull's shape inside the cabin, at y = 0.5.
        Entity crate = world.Spawn(new SpawnDesc { Frame = cabin, Pose = At(0, 2), Body = Ball() });
        world.Frames.ProperAcceleration(cabin).Should().Be(Vector3.Zero, "a new frame has no step record yet");

        world.Tick();
        world.VelocityInOwnFrame(crate).Should().Be(Vector3.Zero, "the cabin had zero proper acceleration during its first step");
        ShouldBeNear(world.Frames.ProperAcceleration(cabin), Weight, 1e-6f, "the first step 2 gives the cabin the gravity at its place, reversed");

        world.Tick();
        ((double)world.VelocityInOwnFrame(crate).Y).Should().BeApproximately(-G * world.DeltaTime, 1e-4, "the crate falls from the second step on");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>A frame with a bubble of 1 m, so that nothing near it hands into it.</summary>
    private static Entity SmallFrame(World world, FrameHandle parent, DPose pose, BodyDesc? body) =>
        world.Spawn(new SpawnDesc { Frame = parent, Pose = pose, Body = body, AsFrame = new FrameSpec(FrameBounds.Sphere(1)) });

    /// <summary>Writes in every tick a sample at the origin with no velocity and the acceleration <see cref="Acceleration"/>.</summary>
    private sealed class Thrust : IComponent
    {
        public Vector3 Acceleration { get; set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.MoveOnRails(new MotionSample(At(0), Double3.Zero, Double3.Zero, (Double3)Acceleration, Double3.Zero, Double3.Zero));
    }

    /// <summary>
    /// Writes in every tick a sample at (0, 100, 0) with an acceleration of 2 m/s² along +Y, and after it, once
    /// <see cref="Write"/> is set, a write of the transform to the pose it was given, which wins for one tick.
    /// </summary>
    private sealed class ClimbThenWrite(DPose written) : IComponent
    {
        public bool Write { get; set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            self.MoveOnRails(new MotionSample(At(0, 100), Double3.Zero, Double3.Zero, new Double3(0, 2, 0), Double3.Zero, Double3.Zero));
            if (Write)
                self.SetTransform(written);
        }
    }

    [Fact]
    public void ADynamicHullTakesTheProperAccelerationThatPhysicsMeasured()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var pilot = new Pilot();
        FrameHandle ship = world.GetOwnedFrame(SpawnFrame(world, space, At(0), HullBody(BodyMotion.Dynamic, 500), pilot));
        Tick(world, 2);

        pilot.Once = hull => hull.AddImpulse(new Vector3(0, 0, 1000));
        Tick(world, 2);

        FrameStepRecord sent = faults.Outputs[^1].Frames.Single(state => state.Frame == ship);
        sent.ProperAcceleration.Should().NotBeNull("physics measures a dynamic hull's proper acceleration");
        world.Frames.ProperAcceleration(ship).Should().Be(sent.ProperAcceleration!.Value);
        // The impulse set the velocity before the step, so the step record's acceleration leaves it out. The accelerometer
        // feels it: Δv / dt for that one step.
        sent.Motion.Acceleration.Length.Should().BeLessThan(1e-3);
        world.Frames.ProperAcceleration(ship).Z.Should().BeApproximately(2 / world.DeltaTime, 0.05f);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheProperAccelerationIsInTheFramesOwnAxes()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, G);
        var turned = new DPose(new Double3(0, 100, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2));
        FrameHandle cabin = world.GetOwnedFrame(SpawnFrame(world, ground, turned, HullBody(BodyMotion.Static)));

        Tick(world, 3);

        ShouldBeNear(world.Frames.ProperAcceleration(cabin), new Vector3(G, 0, 0), 1e-5f,
            "the cabin is turned a quarter turn about +Z, so the ground's up is the cabin's +X");
    }

    [Fact]
    public void ARootFrameAndAFrameAtTheCentreOfAPointFieldFeelNoGravity()
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, G);
        FrameHandle planet = Planet(world);
        FrameHandle surface = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = planet,
            AsFrame = new FrameSpec(FrameBounds.Sphere(7e6f), GravityField.Point(Scenes.EarthMu)),
        }.Spinning(world, new FrameSpin(DQuaternion.Identity, 7.29e-5))));

        Tick(world, 3);

        world.Frames.ProperAcceleration(ground).Should().Be(Vector3.Zero, "a root's own field acts on its contents, not on the root");
        world.Frames.ProperAcceleration(planet).Should().Be(Vector3.Zero);
        world.Frames.ProperAcceleration(surface).Should().Be(Vector3.Zero, "no gravity acts at the centre of a point field");
        world.Errors.Should().BeEmpty();
    }
}
