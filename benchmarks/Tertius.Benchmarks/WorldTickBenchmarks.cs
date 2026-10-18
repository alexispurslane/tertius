using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Tertius.Frames.Rails;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of one full tick: every entity runs on Tick, reads its inbox, moves itself and sends one direct message.
/// About one entity in a thousand also sends to a topic with 100 subscribers, so the work grows linearly with the
/// entity count. Includes routing, the snapshot swap, the spatial grid and the render snapshot of every entity.
/// </summary>
[MemoryDiagnoser]
public class WorldTickBenchmarks
{
    internal sealed class Busy(Entity neighbor, Topic topic) : IComponent
    {
        private readonly Entity neighbor = neighbor;
        private readonly Topic topic = topic;
        public float Total;

        public int Priority => 0;
        // Qualified: inside this class, Tick names the benchmark method.
        public EventMask Wants => EventMask.Of<Tertius.Tick>().And<Damage>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref Damage d in events.Of<Damage>())
                Total += d.Amount;
            DPose pose = self.PoseIn(self.Frame);
            self.SetTransform(pose with { Position = pose.Position + new Double3(0.01, 0, 0) });
            outbox.Send(neighbor, new Damage { Amount = 1 });
            if ((self.Entity.Index & 1023) == (self.Tick & 1023))
                outbox.Send(topic, new Damage { Amount = 2 });
        }
    }

    private World world = null!;
    private readonly SyncProfileReport profile = new();

    [Params(10_000, 100_000)]
    public int Entities;

    [Params(1, -1)]
    public int MaxDegreeOfParallelism;

    [GlobalSetup]
    public void Setup()
    {
        world = new World(new WorldOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, InitialCapacity = Entities + 1024 });
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static));
        Topic topic = Topics.Get("benchmarks.squad");
        var mesh = new RenderData { Mesh = new MeshId(0), Flags = RenderFlags.Visible, BoundingRadius = 0.5f };
        Entity previous = default;
        for (int i = 0; i < Entities; i++)
        {
            previous = world.Spawn(new SpawnDesc
            {
                Frame = root,
                Pose = new DPose(new Double3(i % 300 * 2.0, i / 300 * 2.0, 0), DQuaternion.Identity),
                Render = mesh,
                Components = [new Busy(previous.IsNull ? root.Entity : previous, topic)],
                Subscriptions = i % (Entities / 100) == 0 ? [topic] : [],
            });
        }
        world.Camera = previous;
        for (int i = 0; i < 20; i++)
            world.Tick();
        profile.Mark(world);
        SyncSampler.RemarkProfile = () => profile.Mark(world);
        SyncSampler.StateHash = () => WorldStateHash.Of(world);
    }

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup()
    {
        profile.Print(world, $"WorldTick-{Entities}-dop{MaxDegreeOfParallelism}");
        world.Dispose();
    }
}

/// <summary>
/// The cost of one tick of 1,000 frames without bodies on Kepler orbits around one root, with no physics backend and no
/// other gameplay. The <see cref="KeplerMotion"/> of each frame solves its orbit in its tick, in the parallel chunks, and
/// the sync point only puts each frame on the sample that its component wrote a tick before. The cleanup also prints the
/// mean time of the sync point per tick, in microseconds, so that a run tells the two parts apart. The results before and
/// after the rails components are in a note on the ticket ter-6r8c.
/// </summary>
[MemoryDiagnoser]
public class KeplerFrameTickBenchmarks
{
    private const int FrameCount = 1_000;
    private const double EarthMu = 3.986004418e14;

    private World world = null!;
    internal World World => world;
    private long syncTimestampsAtStart;
    private long syncCountAtStart;
    private readonly SyncProfileReport profile = new();

    [Params(1, -1)]
    public int MaxDegreeOfParallelism;

    [GlobalSetup]
    public void Setup()
    {
        world = new World(new WorldOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, InitialCapacity = FrameCount + 1024 });
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu)));
        for (int i = 0; i < FrameCount; i++)
        {
            double radius = 7e6 + i * 35e3;
            double phase = i * 2.399963229728653;
            var orbit = KeplerOrbit.Circular(radius, phase: phase);
            (Double3 place, Double3 velocity) = orbit.StateAt(world.Time, EarthMu);
            world.Spawn(new SpawnDesc
            {
                Frame = root,
                Pose = new DPose(place, orbit.RotationAt(world.Time, EarthMu)),
                LinearVelocity = velocity,
                AsFrame = new FrameSpec(FrameBounds.Sphere(1), MotionDriver.Kepler),
                Components = [new KeplerMotion { Orbit = orbit }],
            });
        }
        for (int i = 0; i < 20; i++)
            world.Tick();
        syncTimestampsAtStart = world.SyncTimestampTotal;
        syncCountAtStart = world.SyncCount;
        profile.Mark(world);
        SyncSampler.RemarkProfile = () => profile.Mark(world);
        SyncSampler.StateHash = () => WorldStateHash.Of(world);
    }

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup()
    {
        long syncs = world.SyncCount - syncCountAtStart;
        double meanMicroseconds = syncs == 0 ? 0 : (world.SyncTimestampTotal - syncTimestampsAtStart) * 1e6 / Stopwatch.Frequency / syncs;
        Console.WriteLine($"KeplerFrameSync MaxDegreeOfParallelism={MaxDegreeOfParallelism}: mean sync point {meanMicroseconds:F2} us per tick over {syncs} ticks");
        profile.Print(world, $"Kepler1000-dop{MaxDegreeOfParallelism}");
        world.Dispose();
    }
}

/// <summary>
/// ter-sn28 profiling scene: one tick of a mixed world with physics. A planet root with point gravity holds
/// <see cref="KeplerFrames"/> frames without bodies on Kepler orbits, as in <see cref="KeplerFrameTickBenchmarks"/>, and
/// one static station frame without gravity. The station holds 2,000 dynamic cubes 3 m apart that spin without damping,
/// so Jolt keeps them awake and reports a body state for each in every step, and 8,000 entities without bodies that run
/// the gameplay of <see cref="WorldTickBenchmarks"/>: each writes its transform and sends one message in every tick.
/// </summary>
[MemoryDiagnoser]
public class MixedBodiesTickBenchmarks
{
    private const int Bodies = 2_000;
    private const int Plain = 8_000;
    private const int KeplerFrames = 100;
    private const double EarthMu = 3.986004418e14;

    private World world = null!;
    private readonly SyncProfileReport profile = new();

    [Params(1, -1)]
    public int MaxDegreeOfParallelism;

    [GlobalSetup]
    public void Setup()
    {
        var backend = new Tertius.Physics.JoltPhysicsBackend(new Tertius.Physics.JoltBackendOptions { WorkerThreads = 4 });
        world = new World(new WorldOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, InitialCapacity = Bodies + Plain + KeplerFrames + 1024, DisposePhysics = true }, backend);
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu)));
        for (int i = 0; i < KeplerFrames; i++)
        {
            var orbit = KeplerOrbit.Circular(7e6 + i * 35e3, phase: i * 2.399963229728653);
            (Double3 place, Double3 velocity) = orbit.StateAt(world.Time, EarthMu);
            world.Spawn(new SpawnDesc
            {
                Frame = planet,
                Pose = new DPose(place, orbit.RotationAt(world.Time, EarthMu)),
                LinearVelocity = velocity,
                AsFrame = new FrameSpec(FrameBounds.Sphere(1), MotionDriver.Kepler),
                Components = [new KeplerMotion { Orbit = orbit }],
            });
        }
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Pose = new DPose(new Double3(0, 0, -2e6), DQuaternion.Identity),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1000), MotionDriver.Static),
        }));
        var mesh = new RenderData { Mesh = new MeshId(0), Flags = RenderFlags.Visible, BoundingRadius = 0.5f };
        var cube = new BodyDesc(new BoxShapeDesc(new System.Numerics.Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);
        for (int i = 0; i < Bodies; i++)
        {
            world.Spawn(new SpawnDesc
            {
                Frame = station,
                Pose = new DPose(new Double3(i % 20 * 3.0, i / 20 % 10 * 3.0, i / 200 * 3.0), DQuaternion.Identity),
                AngularVelocity = new System.Numerics.Vector3(0.3f + i % 7 * 0.1f, 1f, 0.2f),
                Body = cube,
                Render = mesh,
            });
        }
        Topic topic = Topics.Get("benchmarks.mixed");
        Entity previous = default;
        for (int i = 0; i < Plain; i++)
        {
            previous = world.Spawn(new SpawnDesc
            {
                Frame = station,
                Pose = new DPose(new Double3(i % 100 * 2.0, 200 + i / 100 * 2.0, 0), DQuaternion.Identity),
                Render = mesh,
                Components = [new WorldTickBenchmarks.Busy(previous.IsNull ? station.Entity : previous, topic)],
                Subscriptions = i % (Plain / 100) == 0 ? [topic] : [],
            });
        }
        world.Camera = previous;
        for (int i = 0; i < 20; i++)
            world.Tick();
        profile.Mark(world);
        SyncSampler.RemarkProfile = () => profile.Mark(world);
        SyncSampler.StateHash = () => WorldStateHash.Of(world);
    }

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup()
    {
        profile.Print(world, $"Mixed-dop{MaxDegreeOfParallelism}");
        world.Dispose();
    }
}
