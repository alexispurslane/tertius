using BenchmarkDotNet.Attributes;
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
    private sealed class Busy(Entity neighbor, Topic topic) : IComponent
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
            ref DPose pose = ref self.Transform;
            pose = pose with { Position = self.Pose.Position + new Double3(0.01, 0, 0) };
            outbox.Send(neighbor, new Damage { Amount = 1 });
            if ((self.Entity.Index & 1023) == (self.Tick & 1023))
                outbox.Send(topic, new Damage { Amount = 2 });
        }
    }

    private World world = null!;

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
    }

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();
}
