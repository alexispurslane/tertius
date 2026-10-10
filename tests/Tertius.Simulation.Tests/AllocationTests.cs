using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The steady-state tick must not allocate on the ticking thread: the garbage collector must never pause a frame because
/// of the simulation. Each test warms the world up first, so that every buffer has grown to its working size, and then
/// counts the bytes that the ticking thread allocates over many ticks. The worlds run with one worker (so that all the
/// work is on the counted thread) and with <see cref="WorldOptions.DebugChecks"/> off (its checks allocate).
/// </summary>
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

    /// <summary>Subscribes to a topic on even ticks and unsubscribes on odd ticks.</summary>
    private sealed class Churn(Topic topic) : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>().And<Ping>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if ((self.Tick & 1) == 0)
                self.Subscribe(topic);
            else
                self.Unsubscribe(topic);
            outbox.Send(topic, new Ping { Value = 1 });
        }
    }

    private const int Churners = 200;

    /// <summary>A world with 200 entities that each subscribe on even ticks and unsubscribe on odd ticks.</summary>
    private static TestWorld ChurningWorld(Topic topic)
    {
        var t = new TestWorld(debugChecks: false);
        for (int i = 0; i < Churners; i++)
            t.SpawnAt(new Double3(i, 0, 0), new Churn(topic));
        return t;
    }

    [Fact]
    public void SubscriptionChurnAllocatesNothingPerTickOnceTheWorldIsWarm()
    {
        using TestWorld t = ChurningWorld(Topics.Get("tests.allocation.churn"));
        t.Run(60);

        long before = GC.GetAllocatedBytesForCurrentThread();
        t.Run(200);
        long perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / 200;

        perTick.Should().Be(0, "a steady-state tick must not allocate");
        t.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheChurnScenarioReallyChangesWhoHearsTheTopicEveryTick()
    {
        // Guards the test above against passing for the wrong reason: if the entities did not churn, it would measure nothing.
        // Tick 0 is even, so everyone subscribes at its sync point and the 200 sends of that tick reach all 200 of them.
        using TestWorld t = ChurningWorld(Topics.Get("tests.allocation.churn.deliveries"));

        t.World.Tick();
        t.World.Stats.Deliveries.Should().Be(Churners * Churners);
        // Tick 1 is odd: everyone unsubscribes at its sync point, before routing, so nobody is left to hear.
        t.World.Tick();
        t.World.Stats.Deliveries.Should().Be(0);
    }

    /// <summary>
    /// 2000 visible entities that each hold a <see cref="Busy"/> component aimed at the entity spawned before them;
    /// one in ten is subscribed to <paramref name="topic"/>. The last entity is the camera.
    /// </summary>
    private static TestWorld BusyWorld(Topic topic)
    {
        var t = new TestWorld(debugChecks: false);
        Entity previous = default;
        for (int i = 0; i < 2000; i++)
        {
            previous = t.World.Spawn(new SpawnDesc
            {
                Frame = t.Root,
                Pose = TestWorld.At(i % 50, i / 50, 0),
                Render = new RenderData { Mesh = new MeshId(1), Flags = RenderFlags.Visible },
                Components = [new Busy(previous, topic)],
                Subscriptions = i % 10 == 0 ? [topic] : [],
            });
        }
        t.World.Camera = previous;
        return t;
    }

    [Fact]
    public void ASteadyStateTickAllocatesNothingOnTheTickingThread()
    {
        using TestWorld t = BusyWorld(Topics.Get("tests.allocation.topic"));
        t.Run(60);

        long before = GC.GetAllocatedBytesForCurrentThread();
        t.Run(100);
        long perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;

        perTick.Should().Be(0, "a steady-state tick must not allocate");
        t.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheBusyScenarioReallyDeliversMessagesEveryTick()
    {
        // Guards the test above against passing for the wrong reason: 2000 entities each send one direct message, so
        // far more than 2000 deliveries show that the busy components ran and their messages were routed.
        using TestWorld t = BusyWorld(Topics.Get("tests.allocation.topic.deliveries"));

        t.Run(4);

        t.World.Stats.Deliveries.Should().BeGreaterThan(2000);
    }
}
