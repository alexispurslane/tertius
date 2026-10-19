using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A triangle mesh as the interior of a frame owner (ter-imi5, item 3 of docs/plans/complete-ghosts.md), on the World and
/// the real backend. The interior is the body in the frame's own system that people stand on. It is static while the hull
/// is static, and kinematic and at rest in the frame's own system while the hull moves (see
/// <c>JoltPhysicsBackend.InteriorMotion</c>). Jolt simulates a kinematic mesh against dynamic bodies only when the mesh has
/// a mass, so the backend gives the mesh interior 1 kg. These tests put a ball on a mesh floor in the ship's frame and check
/// that the ball rests there, whatever the hull does.
/// </summary>
public class MeshInteriorTests
{
    /// <summary>The pull in the ship's frame along its -Y, in m/s².</summary>
    private const float Pull = 9.81f;

    /// <summary>The half side of the mesh floor along X and Z, in meters.</summary>
    private const float FloorHalf = 4;

    /// <summary>The radius of the ball, in meters. Its center rests this high above the floor.</summary>
    private const float BallRadius = 0.5f;

    /// <summary>How far the ball may move in the ship's frame while it rests, in meters: 1 mm.</summary>
    private const double Tolerance = 0.001;

    /// <summary>The ticks the ball needs to fall the 10 cm onto the floor and settle, at 60 ticks a second: 2 s.</summary>
    private const int SettleTicks = 120;

    /// <summary>The ticks over which the ball must keep resting, at 60 ticks a second: 5 s.</summary>
    private const int RestTicks = 300;

    /// <summary>
    /// A flat floor of two triangles at y = 0 in the frame's own system, with its normal toward +Y: a square of
    /// <see cref="FloorHalf"/> m from its center to each side.
    /// </summary>
    private static readonly MeshShapeDesc MeshFloor = new(
        [new Vector3(-FloorHalf, 0, -FloorHalf), new Vector3(-FloorHalf, 0, FloorHalf), new Vector3(FloorHalf, 0, FloorHalf), new Vector3(FloorHalf, 0, -FloorHalf)],
        [0, 1, 2, 0, 2, 3]);

    /// <summary>A slow circular orbit of 400 km in an <see cref="Scenes.AsteroidSpace"/>, at 224 m/s. The frame turns at 0.2 rad/s.</summary>
    private static readonly KeplerOrbit Orbit = KeplerOrbit.Circular(4e5, phase: 0.3, spinRate: 0.2);

    /// <summary>A turn in place about the frame's own +Y at 0.2 rad/s.</summary>
    private static readonly FrameSpin Spin = new(DQuaternion.Identity, 0.2);

    /// <summary>What a test needs of a ship with a ball on its mesh floor.</summary>
    private sealed record Scene(World World, FaultInjectingBackend Faults, FrameHandle Space, Entity Owner, FrameHandle Ship, Entity Ball) : IDisposable
    {
        public void Dispose() => World.Dispose();
    }

    /// <summary>
    /// The spawn of a ship in <paramref name="space"/>: a hull that is a box of 5 m by 3 m by 5 m of 1000 kg with
    /// <paramref name="motion"/>, the mesh floor <see cref="MeshFloor"/> as its interior, and a frame with a bubble of 30 m
    /// and a pull of <see cref="Pull"/> along -Y.
    /// </summary>
    private static SpawnDesc Ship(FrameHandle space, BodyMotion motion) => new()
    {
        Frame = space,
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 5)), motion, Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: MeshFloor),
        AsFrame = new FrameSpec(FrameBounds.Sphere(30), GravityField.Uniform(new Vector3(0, -Pull, 0))),
    };

    /// <summary>
    /// A ball of <see cref="BallRadius"/> and 1 kg, 10 cm above the floor at the origin of the frame of <paramref name="owner"/>,
    /// which falls onto the floor.
    /// </summary>
    private static Entity DropBall(World world, Entity owner) => world.Spawn(new SpawnDesc
    {
        Frame = world.GetOwnedFrame(owner),
        Pose = At(0, BallRadius + 0.1),
        Body = Ball(BallRadius),
    });

    /// <summary>
    /// A ship that <paramref name="ship"/> spawns in the root space that <paramref name="makeSpace"/> makes, and a ball that
    /// falls onto its floor and settles there, before the test checks anything.
    /// </summary>
    private static Scene ShipWithBall(Func<World, FrameHandle> makeSpace, Func<World, FrameHandle, SpawnDesc> ship)
    {
        World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = makeSpace(world);
        Entity owner = world.Spawn(ship(world, space));
        // A frame on rails stands on its rails from the second sync point after its spawn, so the ball comes after that.
        Tick(world, 3);
        Entity ball = DropBall(world, owner);
        Tick(world, SettleTicks);
        return new Scene(world, faults, space, owner, world.GetOwnedFrame(owner), ball);
    }

    private static FrameHandle PlainSpace(World world) => world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e8f)));

    /// <summary>The place of the ball in the ship's own system, in meters.</summary>
    private static Double3 BallPlace(Scene scene) => scene.World.PoseIn(scene.Ball, scene.Ship).Position;

    /// <summary>
    /// Ticks <paramref name="ticks"/> times, and returns the largest distance in meters that the ball was from
    /// <paramref name="rest"/>, in the ship's own system, after any of those ticks.
    /// </summary>
    private static double LargestDriftFrom(Scene scene, Double3 rest, int ticks)
    {
        double drift = 0;
        for (int i = 0; i < ticks; i++)
        {
            scene.World.Tick();
            drift = Math.Max(drift, (BallPlace(scene) - rest).Length);
        }
        return drift;
    }

    /// <summary>
    /// Checks that the ball lies on the floor: it is in the ship's frame, its center is one radius above the floor to within
    /// 2 cm, and it is near the floor's center.
    /// </summary>
    private static void AssertBallLiesOnTheFloor(Scene scene, string when)
    {
        scene.World.GetFrame(scene.Ball).Should().Be(scene.Ship, "the ball stays in the ship's frame {0}", when);
        Double3 place = BallPlace(scene);
        place.Y.Should().BeInRange(BallRadius - 0.02, BallRadius + 0.001, "the ball rests on the mesh floor {0}", when);
        Math.Abs(place.X).Should().BeLessThan(0.01);
        Math.Abs(place.Z).Should().BeLessThan(0.01);
    }

    /// <summary>Checks that the backend keeps its invariants, the contact ledger among them, and that the world recorded no error.</summary>
    private static void AllIsWell(Scene scene)
    {
        scene.Faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        scene.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABallRestsOnTheMeshInteriorOfAHullThatHoldsStill()
    {
        using Scene scene = ShipWithBall(PlainSpace, (_, space) => Ship(space, BodyMotion.Static));
        AssertBallLiesOnTheFloor(scene, "of a static hull");
        Double3 rest = BallPlace(scene);

        double drift = LargestDriftFrom(scene, rest, RestTicks);

        drift.Should().BeLessThan(Tolerance, "a static floor holds the ball still");
        AssertBallLiesOnTheFloor(scene, "of a static hull after 5 s");
        AllIsWell(scene);
    }

    /// <summary>How a hull on rails moves: a Kepler orbit carries it through its space at 224 m/s, and a spin turns it in place.</summary>
    public enum Movement
    {
        Kepler,
        Spin,
    }

    /// <summary>The spawn of a kinematic ship on <paramref name="movement"/> in <paramref name="space"/>.</summary>
    private static SpawnDesc ShipOnRails(World world, FrameHandle space, Movement movement) => movement == Movement.Kepler
        ? Ship(space, BodyMotion.Kinematic).OnKeplerOrbit(world, Orbit)
        : Ship(space, BodyMotion.Kinematic).Spinning(world, Spin);

    /// <summary>
    /// A kinematic hull follows its rails: along a Kepler orbit of 224 m/s through a space with the pull of an asteroid, or
    /// turning at 0.2 rad/s in place. The interior is kinematic and at rest in the frame's own system, and the ball on its
    /// mesh floor stays at rest there to within 1 mm over 300 ticks, while the hull and the frame move through their space.
    /// </summary>
    [Theory]
    [InlineData(Movement.Kepler)]
    [InlineData(Movement.Spin)]
    public void ABallStaysAtRestOnTheMeshInteriorOfAKinematicHullThatMoves(Movement movement)
    {
        using Scene scene = ShipWithBall(movement == Movement.Kepler ? Scenes.AsteroidSpace : PlainSpace, (world, space) => ShipOnRails(world, space, movement));
        AssertBallLiesOnTheFloor(scene, "of a hull that moves");
        Double3 rest = BallPlace(scene);
        DPose hullBefore = scene.World.PoseIn(scene.Owner, scene.Space);

        double drift = LargestDriftFrom(scene, rest, RestTicks);

        scene.World.PoseIn(scene.Owner, scene.Space).Should().NotBe(hullBefore, "the hull moved on its rails");
        (scene.World.PoseIn(scene.Ball, scene.Space).Position - scene.World.PoseIn(scene.Owner, scene.Space).Position).Length
            .Should().BeLessThan(1, "the ball goes with the ship");
        drift.Should().BeLessThan(Tolerance, "the interior is at rest in the frame's own system, and so is the ball on it");
        AssertBallLiesOnTheFloor(scene, "of a hull that moved for 5 s");
        AllIsWell(scene);
    }

    /// <summary>
    /// The hull's motion changes from static to kinematic, and back to static, each time with a component on the same
    /// entity (see <see cref="MotionChanges.SetMotionNextTick"/>): the interior turns kinematic when the hull follows a spin,
    /// and static when the hull holds still again. Jolt gives it each motion without a new body, and the ball on the mesh
    /// floor keeps resting to within 1 mm through both changes and the 120 ticks after each one. The world records no error.
    /// </summary>
    [Fact]
    public void ABallKeepsRestingOnTheMeshInteriorWhenItsHullChangesFromStaticToKinematicAndBack()
    {
        using Scene scene = ShipWithBall(PlainSpace, (_, space) => Ship(space, BodyMotion.Static));
        AssertBallLiesOnTheFloor(scene, "of a static hull");
        Double3 rest = BallPlace(scene);
        DPose parked = scene.World.PoseIn(scene.Owner, scene.Space);
        var spin = new SpinMotion { Spin = Spin };

        scene.World.SetMotionNextTick(scene.Owner, BodyMotion.Kinematic, add: spin);
        double driftWhileTurning = LargestDriftFrom(scene, rest, SettleTicks);
        DPose turning = scene.World.PoseIn(scene.Owner, scene.Space);
        scene.World.SetMotionNextTick(scene.Owner, BodyMotion.Static, remove: spin);
        double driftWhileStopping = LargestDriftFrom(scene, rest, SettleTicks);
        DPose stopped = scene.World.PoseIn(scene.Owner, scene.Space);
        scene.World.Tick();
        DPose stillStopped = scene.World.PoseIn(scene.Owner, scene.Space);

        turning.Should().NotBe(parked, "the kinematic hull turned on its spin");
        stillStopped.Should().Be(stopped, "the static hull holds still again");
        driftWhileTurning.Should().BeLessThan(Tolerance, "the ball rests while the interior turns kinematic and the hull turns");
        driftWhileStopping.Should().BeLessThan(Tolerance, "the ball rests while the interior turns static again and the hull stops");
        AssertBallLiesOnTheFloor(scene, "after the hull was static, kinematic and static again");
        AllIsWell(scene);
    }
}
