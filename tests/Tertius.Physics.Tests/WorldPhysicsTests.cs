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

        world.Frames.SetDriver(ship, MotionDriver.Static);
        for (int i = 0; i < 3; i++)
            world.Tick();

        world.Errors.Should().BeEmpty();
        warnings.Should().BeEmpty();
        world.Frames.LocalPose(ship).Should().Be(parked);
        world.GetPose(owner).Should().Be(parked);
        world.GetVelocity(owner).Should().Be(Vector3.Zero);
    }
}
