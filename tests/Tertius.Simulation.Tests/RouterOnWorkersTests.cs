using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>The tests of <see cref="RouterTests"/>, with every routing on the workers.</summary>
public sealed class RouterTestsOnWorkers : RouterTests
{
    private protected override RouterHarness NewHarness() => RouterHarness.OnWorkers();
}

/// <summary>The tests of <see cref="RouterDeliveryTests"/>, with every routing on the workers.</summary>
public sealed class RouterDeliveryTestsOnWorkers : RouterDeliveryTests
{
    private protected override RouterHarness NewHarness() => RouterHarness.OnWorkers();
}

/// <summary>The tests of <see cref="RouterTopicTests"/>, with every routing on the workers.</summary>
public sealed class RouterTopicTestsOnWorkers : RouterTopicTests
{
    private protected override RouterHarness NewHarness() => RouterHarness.OnWorkers();
}

/// <summary>
/// A routing on the workers: from how many messages on the router uses them, and that it then builds the same routed
/// events and the same inboxes as a routing on the sync thread, whatever the number of work items.
/// </summary>
public sealed class RouterOnWorkersTests
{
    private static readonly Topic Squad = Topics.Get("tests.routeronworkers.squad");

    // ---- Same result as on the sync thread -----------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(64)]
    public void ARoutingOnTheWorkersBuildsTheSameEventsAndInboxesAsARoutingOnTheSyncThread(int workItems)
    {
        var serial = new RouterHarness();
        var wide = RouterHarness.OnWorkers(workItems);
        RouterHarness[] both = [serial, wide];
        var random = new Random(1000 + workItems);
        FrameHandle bay = RouterHarness.Frame(index: 0, generation: 1);
        foreach (RouterHarness h in both)
            h.Topics.AddFrame(bay);
        EventMask[] masks =
        [
            EventMask.None,
            EventMask.Of<Ping>(),
            EventMask.Of<Heal>(),
            EventMask.Of<Ping>().And<Heal>(),
        ];
        var everyone = new List<Entity>();
        int value = 0;

        // The same draws act on both harnesses, so they hold the same entities and get the same messages.
        void Spawn()
        {
            EventMask wants = masks[random.Next(masks.Length)];
            bool inBay = random.Next(3) == 0,
                squad = random.Next(4) == 0,
                watchesBay = random.Next(5) == 0;
            Entity spawned = default;
            foreach (RouterHarness h in both)
            {
                spawned = h.Spawn(wants, inBay ? bay : default);
                if (squad)
                    h.Subscribe(spawned, Squad);
                if (watchesBay)
                    h.Subscribe(spawned, bay.Topic);
            }

            everyone.Add(spawned);
        }

        for (int i = 0; i < 24; i++)
            Spawn();

        for (int tick = 0; tick < 16; tick++)
        {
            int sends = random.Next(0, 300);
            for (int s = 0; s < sends; s++)
            {
                int chunk = random.Next(serial.Entities.Chunks.Length);
                // Live entities, released ones and the null handle; the named topic, the frame topic and everyone.
                Target target = random.Next(12) switch
                {
                    < 7 => everyone[random.Next(everyone.Count)],
                    7 => Entity.Null,
                    8 or 9 => Squad,
                    10 => bay.Topic,
                    _ => new Broadcast(),
                };
                int source = random.Next(10),
                    kind = random.Next(8),
                    delay = random.Next(0, 3);
                int v = ++value;
                foreach (RouterHarness h in both)
                {
                    Event e = kind switch
                    {
                        < 4 => Event.Create(target: target, sender: Entity.Null, payload: new Ping { Value = v }),
                        < 7 => Event.Create(target: target, sender: Entity.Null, payload: new Heal { Amount = v }),
                        _ => Event.Create(target: target, sender: Entity.Null, payload: new Destroy()),
                    };
                    switch (source)
                    {
                        case < 6:
                            h.Entities.Chunks[chunk].Outbox.Add(e);
                            break;
                        case < 8:
                            h.Entities.Chunks[chunk].Delayed.Add(new() { Due = tick + delay, Event = e });
                            break;
                        case 8:
                            h.Router.AddEngineEvent(e);
                            break;
                        default:
                            h.Router.Post(e);
                            break;
                    }
                }
            }

            // Some entities go, and new ones take their slots or new slots, so the slot count grows between routings.
            int releases = random.Next(0, 4);
            for (int r = 0; r < releases; r++)
            {
                Entity doomed = everyone[random.Next(everyone.Count)];
                foreach (RouterHarness h in both)
                    if (h.Entities.IsAlive(doomed))
                        h.Entities.Release(doomed);
            }

            int spawns = random.Next(0, 6);
            for (int n = 0; n < spawns; n++)
                Spawn();

            foreach (RouterHarness h in both)
                h.Route(tick);

            Describe(wide.Router.Routed)
                .Should()
                .Equal(Describe(serial.Router.Routed), $"routing {tick} joins the same events in the same order");
            wide.Router.DeliveryCount.Should().Be(serial.Router.DeliveryCount);
            foreach (Entity entity in everyone)
            {
                if (!serial.Entities.IsAlive(entity))
                    continue;
                Describe(wide.Inbox(entity))
                    .Should()
                    .Equal(
                        Describe(serial.Inbox(entity)),
                        $"routing {tick} fills the inbox of {entity} in the same order"
                    );
            }
        }

        serial
            .Router.DeliveryCount.Should()
            .BeGreaterThan(0, "the script must deliver something, or the equality proves nothing");
        wide.TasksQueued.Should().BeGreaterThan(0, "the routings must really run on the workers");
    }

    [Fact]
    public void ATopicWhoseSubscribersChangedJustBeforeARoutingOnTheWorkersReachesEveryNewSubscriber()
    {
        // A topic set applies its queued changes when it is next read, so a routing on the workers must not let its work
        // items be the first readers: they would all apply the same changes at once. Many changes and many readers make
        // that race show.
        var h = RouterHarness.OnWorkers(workItems: 16);
        var subscribers = new List<Entity>();
        for (int i = 0; i < 2000; i++)
            subscribers.Add(h.Spawn(EventMask.Of<Ping>()));
        // Everyone subscribes and unsubscribes in turn, each time just before a routing.
        for (int round = 0; round < 8; round++)
        {
            bool subscribed = round % 2 == 0;
            foreach (Entity s in subscribers)
                if (subscribed)
                    h.Subscribe(s, Squad);
                else
                    h.Topics.Unsubscribe(s, Squad).Should().BeTrue();
            for (int m = 0; m < 64; m++)
                h.Send(m, Squad, new Ping { Value = m });

            h.Route(tick: round);

            int expected = subscribed ? 64 : 0;
            h.Pings(subscribers[0]).Should().HaveCount(expected);
            h.Pings(subscribers[^1]).Should().HaveCount(expected);
            h.Router.DeliveryCount.Should().Be(expected * subscribers.Count);
        }

        h.TasksQueued.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Sends a ping to a neighbor every tick, joins and leaves a topic in turn and talks to it, and now and then destroys
    /// its neighbor, so that the sync point changes the topic sets and the live entities right before it routes.
    /// </summary>
    private sealed class Talker(Entity neighbor) : IComponent
    {
        public int Priority => 0;
        public EventMask Wants => EventMask.Of<Tick>().And<Ping>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            int index = (int)self.Entity.Index;
            if (((self.Tick + index) & 1) == 0)
                self.Subscribe(Squad);
            else
                self.Unsubscribe(Squad);
            outbox.Send(neighbor, new Ping { Value = (int)self.Tick * 1000 + index });
            if ((self.Tick + index) % 5 == 0)
                outbox.Send(Squad, new Ping { Value = -index });
            if ((self.Tick * 7 + index) % 97 == 0)
                outbox.Send(neighbor, new Destroy());
        }
    }

    [Fact]
    public void AWorldThatRoutesOnItsWorkersFillsTheSameInboxesAsAWorldOnOneThread()
    {
        using var serial = new TestWorld(chunkSize: 16);
        using var wide = new TestWorld(maxDegreeOfParallelism: 4, chunkSize: 16);
        Router wideRouter = Inside.RouterOf(wide.World);
        wideRouter.ParallelThreshold = 1;
        wideRouter.WorkItems = 3;
        var everyone = new List<Entity>();
        foreach (TestWorld t in new[] { serial, wide })
        {
            Entity previous = t.Root.Entity;
            for (int i = 0; i < 300; i++)
                previous = t.SpawnWith(new Talker(previous));
        }

        foreach (Entity entity in serial.World.Entities.LiveEntities)
            everyone.Add(entity);

        for (int tick = 0; tick < 30; tick++)
        {
            serial.World.Tick();
            wide.World.Tick();

            Describe(wide.World.RoutedEvents)
                .Should()
                .Equal(Describe(serial.World.RoutedEvents), $"tick {tick} routes the same events");
            wide.World.Stats.Should().Be(serial.World.Stats);
            foreach (Entity entity in everyone)
                if (serial.World.Entities.IsAlive(entity))
                    Describe(InboxOf(wide.World, entity))
                        .Should()
                        .Equal(
                            Describe(InboxOf(serial.World, entity)),
                            $"tick {tick} fills the inbox of {entity} alike"
                        );
        }

        serial.World.Stats.LiveEntities.Should().BeLessThan(300, "the talkers must destroy some of their neighbors");
    }

    private static List<Event> InboxOf(World world, Entity entity)
    {
        var buffer = new EventBuffer();
        EventList events = buffer.Begin(entity);
        Inside.RouterOf(world).CopyInbox(entity, events);
        var copy = new List<Event>();
        for (int i = 0; i < events.Count; i++)
            copy.Add(events[i]);
        return copy;
    }

    // ---- From how many messages on ---------------------------------------------------------------------------------------

    [Fact]
    public void ARoutingWithFewerMessagesThanTheThresholdStaysOnTheSyncThread()
    {
        var h = RouterHarness.OnWorkers(threshold: 10);
        Entity a = h.Spawn(EventMask.Of<Ping>());
        for (int i = 0; i < 9; i++)
            h.Send(0, a, new Ping { Value = i });

        h.Route(tick: 0);

        h.TasksQueued.Should().Be(0);
        h.Pings(a).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void ARoutingWithAsManyMessagesAsTheThresholdRunsOnTheWorkers()
    {
        var h = RouterHarness.OnWorkers(threshold: 10);
        Entity a = h.Spawn(EventMask.Of<Ping>());
        for (int i = 0; i < 10; i++)
            h.Send(0, a, new Ping { Value = i });

        h.Route(tick: 0);

        h.TasksQueued.Should().BeGreaterThan(0);
        h.Pings(a).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public void TheThresholdCountsEngineEventsOutboxMessagesAndDueDelayedSends()
    {
        var h = RouterHarness.OnWorkers(threshold: 3);
        Entity a = h.Spawn(EventMask.Of<Ping>());
        h.Router.AddEngineEvent(Event.Create(target: a, sender: Entity.Null, payload: new Ping { Value = 1 }));
        h.Send(0, a, new Ping { Value = 2 });
        h.SendAt(chunk: 0, due: 0, target: a, payload: new Ping { Value = 3 });

        h.Route(tick: 0);

        h.TasksQueued.Should().BeGreaterThan(0);
        h.Pings(a).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void ARouterWithWorkersRoutesTheMessagesOfASmallWorldOnTheSyncThread()
    {
        // The default threshold: a routing must be large before the workers pay for themselves.
        var h = RouterHarness.OnWorkers(threshold: null);
        var entities = new List<Entity>();
        for (int i = 0; i < 100; i++)
            entities.Add(h.Spawn(EventMask.Of<Ping>()));
        for (int i = 0; i < 100; i++)
            h.Send(i, entities[(i + 1) % 100], new Ping { Value = i });

        h.Route(tick: 0);

        h.TasksQueued.Should().Be(0);
        h.Pings(entities[1]).Should().Equal(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AThresholdOfARoutingOnTheWorkersBelowOneMessageIsRefused(int threshold)
    {
        var h = RouterHarness.OnWorkers(threshold: null);

        Action set = () => h.Router.ParallelThreshold = threshold;

        set.Should().Throw<ArgumentOutOfRangeException>();
        h.Router.ParallelThreshold.Should().Be(Router.DefaultParallelThreshold, "a refused threshold changes nothing");
    }

    /// <summary>What a test compares of an event: its target, its type and its payload, which every test makes unique.</summary>
    private static List<string> Describe(ReadOnlySpan<Event> events)
    {
        var described = new List<string>(events.Length);
        foreach (Event e in events)
            described.Add(Describe(e));
        return described;
    }

    private static List<string> Describe(List<Event> events) => [.. events.Select(Describe)];

    private static string Describe(Event e)
    {
        if (e.Is<Ping>())
            return $"{e.Target} ping {e.As<Ping>().Value}";
        if (e.Is<Heal>())
            return $"{e.Target} heal {e.As<Heal>().Amount}";
        return $"{e.Target} {EventTypeRegistry.NameOf(e.Type)}";
    }
}
