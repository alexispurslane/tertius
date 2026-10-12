using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Sleep on the World and the real backend. A body falls asleep when it rests. A body that a net acceleration moves
/// from rest stays awake, also when that acceleration is a small part of a weak gravity: on a ramp, over a ledge, on
/// the deck of a ship that thrusts, or in free fall with damping. Jolt alone would put such a body to sleep when it
/// moves less than about 3 cm in half a second.
/// </summary>
/// <remarks>
/// These tests run for up to 60 s of simulated time, 33,000 ticks in all. So they leave the world's checks of its
/// invariants at the end of every tick off, which would take 15 s in a Debug build. The other World tests keep them on.
/// </remarks>
public class WorldSleepTests
{
    /// <summary>
    /// The reviewer's probe S10. A ball (or a box without friction) is let go at rest on a long ramp. A ball rolls with
    /// a = 5/7 · g · sin θ, and a box without friction slides with a = g · sin θ. The test runs for 20 s, or until the
    /// body has moved about 50 m, whichever comes first, and compares the distance along the ramp with a · t² / 2.
    /// </summary>
    [Theory]
    [InlineData(9.81f, 30f, true)]
    [InlineData(0.1f, 30f, true)]
    [InlineData(0.2f, 30f, true)]
    [InlineData(0.1f, 45f, true)]
    [InlineData(1.62f, 5f, true)]
    [InlineData(9.81f, 20f, false)]
    [InlineData(0.1f, 20f, false)]
    [InlineData(0.05f, 20f, false)]
    [InlineData(0.1f, 40f, false)]
    public void ABodyLetGoAtRestOnARampMovesDownIt(float g, float degrees, bool ball)
    {
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle ground = Ground(world, g);
        double theta = degrees * Math.PI / 180;
        DQuaternion tilt = DQuaternion.FromAxisAngle(Double3.UnitZ, -theta);
        Double3 normal = tilt.Rotate(Double3.UnitY);
        Double3 downhill = tilt.Rotate(Double3.UnitX);
        world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(Double3.Zero, tilt),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(80, 0.5f, 5)), BodyMotion.Static, Friction: ball ? 0.5f : 0f),
        });
        Double3 start = normal * 1.0 - downhill * 60.0;
        Entity body = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(start, ball ? DQuaternion.Identity : tilt),
            Body = ball
                ? new BodyDesc(new SphereShapeDesc(0.5f), Mass: 1, LinearDamping: 0, AngularDamping: 0)
                : new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 1, Friction: 0, LinearDamping: 0, AngularDamping: 0),
        });
        double a = (ball ? 5.0 / 7.0 : 1.0) * g * Math.Sin(theta);
        double seconds = Math.Min(20, Math.Sqrt(2 * 50 / a));
        int ticks = (int)Math.Round(seconds * 60);

        Tick(world, ticks);

        double along = Double3.Dot(world.PoseInOwnFrame(body).Position - start, downhill);
        double expected = 0.5 * a * (ticks / 60.0) * (ticks / 60.0);
        along.Should().BeGreaterThan(0.8 * expected, "gravity pulls the body down the ramp the whole time");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The reviewer's probe S11. A box of 1 m stands on a ledge with its center 0.1 m past the edge, so gravity tips it
    /// over the edge. It falls 10 m to the ground. At 0.1 m/s² the fall alone takes 14 s, and the test runs for 30 s.
    /// </summary>
    [Theory]
    [InlineData(9.81f)]
    [InlineData(0.3f)]
    [InlineData(0.1f)]
    public void ABoxThatHangsOverALedgeTipsOffIt(float g)
    {
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle ground = Ground(world, g);
        Floor(world, ground, top: -10);
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, -0.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(2, 0.5f, 2)), BodyMotion.Static) });
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(2.1, 0.5), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 1) });

        Tick(world, 1800);

        world.PoseInOwnFrame(box).Position.Y.Should().BeLessThan(-5, "the box tips off the ledge and falls to the ground");
    }

    /// <summary>
    /// The reviewer's probe S16. A box with the default damping is let go at rest 1 m above the floor in very weak
    /// gravity. Damping holds it near g / 0.05 per second, so at 0.002 m/s² it falls at about 4 cm/s and lands after about
    /// 43 s. The test runs for 60 s.
    /// </summary>
    [Theory]
    [InlineData(0.002f)]
    [InlineData(0.004f)]
    [InlineData(0.006f)]
    [InlineData(0.01f)]
    public void ABoxLetGoInVeryWeakGravityFallsToTheFloor(float g)
    {
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle ground = Ground(world, g);
        Floor(world, ground);
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 1.5), Body = Cube() });

        Tick(world, 3600);

        world.PoseInOwnFrame(box).Position.Y.Should().BeApproximately(0.5, 0.01, "the box fell 1 m onto the floor");
    }

    /// <summary>
    /// The reviewer's probe S17. A ship has artificial gravity <paramref name="g"/> along its -Y, and a crate falls asleep
    /// on its deck. Then the ship thrusts along +Z at <paramref name="thrust"/> m/s² for 10 s. Friction (0.5) holds 0.5 · g,
    /// so the crate slides toward the stern at a = thrust − 0.5 · g. At 1 m/s² and 0.6 m/s² that is 0.1 m/s², less than a
    /// tenth of the effective gravity.
    /// </summary>
    [Theory]
    [InlineData(9.81f, 6.0f)]
    [InlineData(9.81f, 5.2f)]
    [InlineData(1f, 0.6f)]
    [InlineData(1f, 0.8f)]
    public void CargoThatFrictionCannotQuiteHoldSlidesTowardTheStern(float g, float thrust)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        var deck = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(10, 0.25f, 30)), new Pose(new Vector3(0, -2.25f, 0), Quaternion.Identity))]);
        var pilot = new Pilot();
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(10, 3, 30)), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: deck),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Physics, GravityField.Uniform(new Vector3(0, -g, 0))),
            Components = [pilot],
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        Entity crate = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(0, -1.5), Body = Cube(mass: 5) });
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, ship, crate), 300);
        IsAwake(backend, ship, crate).Should().BeFalse("the crate fell asleep on the deck");
        double z0 = world.PoseInOwnFrame(crate).Position.Z;

        pilot.EveryTick = body => body.AddForce(new Vector3(0, 0, thrust * 1000));
        Tick(world, 600);

        double a = thrust - 0.5 * g;
        double expected = 0.5 * a * 10 * 10;
        (world.PoseInOwnFrame(crate).Position.Z - z0).Should().BeLessThan(-0.8 * expected, "the crate slides toward the stern for 10 s");
    }

    /// <summary>
    /// Without gravity, a box with no damping drifts at 2 cm/s. Jolt alone would stop it after half a second, because it
    /// moves less than 3 cm in that time. It keeps drifting: after 10 s it has moved 20 cm.
    /// </summary>
    [Fact]
    public void ABoxThatDriftsSlowlyWithoutGravityKeepsDrifting()
    {
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        Entity box = world.Spawn(new SpawnDesc
        {
            Frame = space,
            LinearVelocity = new Vector3(0.02f, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0),
        });

        Tick(world, 600);

        world.PoseInOwnFrame(box).Position.X.Should().BeApproximately(0.2, 0.002, "nothing slows the box down");
    }

    /// <summary>
    /// A stack of five boxes, let go at rest on the floor, falls asleep within 2 s, in strong gravity as in weak gravity.
    /// So the rule that keeps an accelerated body awake still lets a resting stack sleep.
    /// </summary>
    [Theory]
    [InlineData(9.81f)]
    [InlineData(1.62f)]
    [InlineData(0.1f)]
    [InlineData(0.01f)]
    public void AStackOfFiveBoxesOnTheFloorFallsAsleepWithinTwoSeconds(float g)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        FrameHandle ground = Ground(world, g);
        Floor(world, ground);
        var boxes = new List<Entity>();
        for (int i = 0; i < 5; i++)
            boxes.Add(world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 0.5 + i), Body = Cube() }));

        world.Tick();
        int ticks = 1 + TickUntil(world, () => boxes.All(box => !IsAwake(backend, ground, box)), 119);

        ticks.Should().BeLessThan(120, "the stack rests on the floor");
        world.PoseInOwnFrame(boxes[^1]).Position.Y.Should().BeApproximately(4.5, 0.02, "the stack stands where it was let go");
    }
}
