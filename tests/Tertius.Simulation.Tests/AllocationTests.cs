namespace Tertius.Simulation.Tests;

public class AllocationTests
{
    /// <summary>Moves, sends a direct message and a topic message every tick, and reads its inbox. It allocates nothing.</summary>
    private sealed class Busy(Entity neighbor, Topic topic) : IComponent
    {
        public Entity Neighbor = neighbor;
        public float Total;
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>().And<Ping>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref Ping ping in events.Of<Ping>())
                Total += ping.Value;
            events.RemoveAll(static (ref Ping p) => p.Value < 0);
            ref DPose pose = ref self.Transform;
            pose = pose with { Position = self.Pose.Position + new Double3(0.001, 0, 0) };
            self.Render.Scale.X = 1;
            if (self.Snapshot.IsAlive(Neighbor))
                outbox.Send(Neighbor, new Ping { Value = 1 });
            if ((self.Tick & 7) == (self.Entity.Index & 7))
                outbox.Send(topic, new Ping { Value = 2 });
            if ((self.Tick & 15) == 0)
                outbox.SendAfter(3, self.Entity, new Ping { Value = 3 });
        }
    }

    [Fact]
    public void ASteadyStateTickDoesNotAllocateOnTheTickingThread()
    {
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1 });
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static));
        Topic topic = Topics.Get("tests.allocation.topic");
        Entity previous = default;
        for (int i = 0; i < 2000; i++)
        {
            previous = world.Spawn(new SpawnDesc
            {
                Frame = root,
                Pose = TestWorld.At(i % 50, i / 50, 0),
                Render = new RenderData { Mesh = new MeshId(1), Flags = RenderFlags.Visible },
                Components = [new Busy(previous, topic)],
                Subscriptions = i % 10 == 0 ? [topic] : [],
            });
        }
        world.Camera = previous;

        world.Run(60);
        long before = GC.GetAllocatedBytesForCurrentThread();
        world.Run(100);
        long perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;

        Assert.True(world.Stats.Deliveries > 2000);
        Assert.True(perTick == 0, $"{perTick} bytes allocated per tick");
        Assert.Empty(world.Errors);
    }
}
