using AwesomeAssertions;
using Tertius.Testing;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The steady-state tick must not allocate on the ticking thread: the garbage collector must never pause a frame because
/// of the simulation. Each test warms the world up first, so that every buffer has grown to its working size, and then
/// counts the bytes that the ticking thread allocates over many ticks. The worlds run with one worker (so that all the
/// work is on the counted thread) and with <see cref="WorldOptions.DebugChecks"/> off (its checks allocate).
/// </summary>
public class AllocationTests
{
    private const int Churners = 200;

    /// <summary>
    /// Moves, sends a direct message and a topic message every tick, and reads its inbox with every query word and
    /// ending. It allocates nothing.
    /// </summary>
    private sealed class Busy(Entity neighbor, Topic topic) : IComponent
    {
        private static readonly EventQuery<Ping> Delayed = new EventQuery<Ping>().Where(static (ref p) => p.Value == 3);

        public readonly Entity Neighbor = neighbor;
        public float Total;
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>().And<Ping>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (ref Ping ping in events.Of<Ping>())
                Total += ping.Value;
            Total += events.Of<Ping>().From(Neighbor).Count() + events.From(Entity.Null).Count();
            if (events.Of<Ping>().Where(2, static (ref p, value) => p.Value == value).TryFirst(out Ping fromTopic))
                Total += fromTopic.Value;
            if (events.Find(Delayed).Any())
                Total += 3;
            events.Of<Ping>().Where(static (ref p) => p.Value < 0).Remove();
            events.Where(static (ref e) => e.Is<Ping>() && e.As<Ping>().Value < 0).Remove();
            self.SetTransform(self.PoseInOwnFrame() with
            {
                Position = self.PoseInOwnFrame().Position + new Double3(0.001, 0, 0)
            });
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

    /// <summary>A world with 200 entities that each subscribe on even ticks and unsubscribe on odd ticks.</summary>
    private static TestWorld ChurningWorld(Topic topic)
    {
        var t = new TestWorld(debugChecks: false);
        for (int i = 0; i < Churners; i++)
            t.SpawnAt(new(i, 0, 0), new Churn(topic));
        return t;
    }

    [Fact]
    public void SubscriptionChurnAllocatesNothingPerTickOnceTheWorldIsWarm()
    {
        using TestWorld t = ChurningWorld(Topics.Get("tests.allocation.churn"));
        t.Run(60);

        long allocated = Allocations.Fewest(() => t.Run(200));

        allocated.Should().Be(0, "a steady-state tick must not allocate");
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
            previous = t.World.Spawn(new()
            {
                Frame = t.Root,
                Pose = TestWorld.At(i % 50, i / 50, 0),
                Render = new RenderData { Mesh = new(1), Flags = RenderFlags.Visible },
                Components = [new Busy(previous, topic)],
                Subscriptions = i % 10 == 0 ? [topic] : []
            });
        t.World.Camera = previous;
        return t;
    }

    [Fact]
    public void ASteadyStateTickAllocatesNothingOnTheTickingThread()
    {
        using TestWorld t = BusyWorld(Topics.Get("tests.allocation.topic"));
        t.Run(60);

        long allocated = Allocations.Fewest(() => t.Run(100));

        allocated.Should().Be(0, "a steady-state tick must not allocate");
        t.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Runs entity queries on every tick: a type-only stored query, a stored query whose filters all sit inline, and
    /// shorthand chains with filters on the stack, one of which starts from the spatial grid and sorts its candidates.
    /// </summary>
    private sealed class Searcher : IComponent
    {
        private static readonly EntityQuery TypeOnly = EntityQuery.With<Flammable>().Without<Wet>();

        private static readonly EntityQuery Inline =
            EntityQuery.With<Flammable>().Near(6).Where(static (s, e) => (e.Index & 1) == 0);

        public long Total;
        public int GridRuns;
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            foreach (Entity entity in self.Snapshot.Find(TypeOnly))
                Total += entity.Index;
            Total += self.Snapshot.Find(Inline).Count();
            FoundEntities chain = self.Snapshot.Entities.With<Flammable>().Near(3).Near(4)
                .Where(static (s, e) => (e.Index & 3) != 0).Where(static (s, e) => true).In(self.Frame).In(self.Frame);
            FoundEntities.Enumerator run = chain.GetEnumerator();
            if (run.Index == QueryIndex.Grid)
                GridRuns++;
            while (run.MoveNext())
                Total += run.Current.Index;
            run.Dispose();
            if (self.Snapshot.Entities.With<Metal>().TryFirst(out Entity metal))
                Total += metal.Index;
            if (self.Snapshot.HasComponent<Flammable>(self.Entity))
                Total++;
        }
    }

    /// <summary>600 entities with flags on a line, 2 m apart, so that a small Near touches few grid cells; one in six of them searches.</summary>
    private static (TestWorld World, List<Searcher> Searchers) SearchingWorld()
    {
        var t = new TestWorld(debugChecks: false);
        var searchers = new List<Searcher>();
        for (int i = 0; i < 600; i++)
        {
            var components = new List<IComponent> { i % 3 == 0 ? new Wet() : new Flammable() };
            if (i % 7 == 0)
                components.Add(new Metal());
            if (i % 6 == 0)
            {
                var searcher = new Searcher();
                searchers.Add(searcher);
                components.Add(searcher);
            }

            t.SpawnAt(new(i * 2, 0, 0), [.. components]);
        }

        return (t, searchers);
    }

    [Fact]
    public void EntityQueryRunsAllocateNothingOnceTheWorldIsWarm()
    {
        (TestWorld t, _) = SearchingWorld();
        using TestWorld world = t;
        world.Run(30);

        long allocated = Allocations.Fewest(() => world.Run(100));

        allocated.Should().Be(0, "a type-only or inline query run must not allocate");
        world.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheSearchScenarioReallyFindsEntitiesAndStartsRunsFromTheGrid()
    {
        // Guards the test above against passing for the wrong reason: the searchers find entities, and the chain with
        // a small Near starts from the grid, the path that takes a buffer.
        (TestWorld t, List<Searcher> searchers) = SearchingWorld();
        using TestWorld world = t;

        world.Run(2);

        searchers.Should().OnlyContain(s => s.Total > 0 && s.GridRuns == 2);
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
