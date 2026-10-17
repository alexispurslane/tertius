using System.Numerics;
using BenchmarkDotNet.Attributes;
using JoltPhysicsSharp;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of the ghost of a mesh (ter-dq92). One tick of a world in which a parked station's hull, a floor of triangles
/// of 40 m by 40 m, has a ghost in a ship beside it, and 100 boxes of 1 m in the ship stand on that ghost, in 10 rows of
/// 10, 1.5 m apart. The ship has its own gravity of 1 g.
/// <list type="bullet">
/// <item><see cref="ShipShuttles"/> false: the ship hangs at rest, so the ghost holds still. It is a static body, as the
/// hull is, and the boxes fall asleep on it.</item>
/// <item><see cref="ShipShuttles"/> true: the ship shuttles along X at 0.5 m/s and turns every 2 s, so the ghost slides
/// under the boxes, and they stay awake. The ghost is a kinematic body that Jolt moves with that velocity.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class MeshGhostBenchmarks
{
    /// <summary>Sets the velocity of its entity's body to ±0.5 m/s along X, and turns it every 120 ticks.</summary>
    private sealed class Shuttle : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tertius.Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick % 120 == 0)
                self.Body.SetVelocity(new Double3(self.Tick / 120 % 2 == 0 ? 0.5 : -0.5, 0, 0));
        }
    }

    [Params(false, true)]
    public bool ShipShuttles;

    private World world = null!;

    [GlobalSetup]
    public void Setup()
    {
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 });
        world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true }, backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        var hull = new MeshShapeDesc([new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20), new(-20, 0, 20)], [0, 2, 1, 0, 3, 2]);
        Entity station = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(hull, BodyMotion.Static, InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(3), MotionDriver.Static),
        });
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(12, 20, 0),
            Body = new BodyDesc(new SphereShapeDesc(2), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(1)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30), MotionDriver.Physics, GravityField.Uniform(new Vector3(0, -9.81f, 0))),
            Components = ShipShuttles ? [new Shuttle()] : [],
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        for (int i = 0; i < 100; i++)
            world.Spawn(new SpawnDesc { Frame = ship, Pose = At(i % 10 * 1.5 - 7, -19.5, i / 10 * 1.5 - 7), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10) });
        // Three seconds: the boxes settle, and on the still ghost they fall asleep.
        for (int i = 0; i < 180; i++)
            world.Tick();

        if (!backend.HasGhostIn(station, ship))
            throw new InvalidOperationException("The ship holds no ghost of the station's hull.");
        List<JoltPhysicsBackend.BodyFacts> facts = backend.BodiesIn(ship);
        MotionType ghost = facts.Single(body => BodyUserData.Kind(body.UserData) == BodyKind.Ghost).Motion;
        int awake = facts.Count(body => body.Active && body.Motion == MotionType.Dynamic);
        Console.WriteLine($"// The ghost of the hull is {ghost}, and {awake} of 100 boxes are awake.");
    }

    private static DPose At(double x, double y, double z) => new(new Double3(x, y, z), DQuaternion.Identity);

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();
}
