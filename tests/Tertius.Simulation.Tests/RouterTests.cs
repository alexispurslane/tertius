namespace Tertius.Simulation.Tests;

/// <summary>
/// The router on its own, over a hand-built entity table and topic sets: routing order, mask filtering, frame-topic
/// generations and the Destroy rule.
/// </summary>
public class RouterTests
{
    private static readonly Topic Squad = Topics.Get("tests.router.squad");

    /// <summary>
    /// An entity table with one slot per chunk, so that every entity has its own outbox, its topic sets, and a router
    /// over both.
    /// </summary>
    private sealed class Harness
    {
        public readonly EntityTable Entities;
        public readonly TopicSets Topics;
        public readonly Router Router;

        public Harness()
        {
            EngineEvents.Register();
            Entities = new EntityTable(chunkSize: 1, initialCapacity: 16);
            Topics = new TopicSets(Entities);
            Router = new Router(Entities, Topics);
        }

        /// <summary>A live entity whose one component wants <paramref name="wants"/>, optionally a member of a frame.</summary>
        public Entity Spawn(EventMask wants, FrameHandle frame = default)
        {
            Entity entity = Entities.Reserve();
            Entities.Activate(entity, new TransformRow { Frame = frame }, new RenderData(), [new Recorder(wants)], [wants], null, FrameHandle.Null);
            Topics.MoveMember(entity, frame, out _);
            return entity;
        }

        public void Subscribe(Entity entity, Topic topic) => Assert.True(Topics.Subscribe(entity, topic));

        /// <summary>Writes a message into the outbox of a chunk, as an entity of that chunk would.</summary>
        public void Send<T>(int chunk, Target target, T payload) where T : unmanaged, IEventPayload =>
            Entities.Chunks[chunk].Outbox.Add(Event.Create(target, Entity.Null, payload));

        public void SendAt<T>(int chunk, long due, Target target, T payload) where T : unmanaged, IEventPayload =>
            Entities.Chunks[chunk].Delayed.Add(new DelayedEvent { Due = due, Event = Event.Create(target, Entity.Null, payload) });

        public void Route(long tick)
        {
            Router.Route(tick);
            Router.AssertInvariants();
            Topics.AssertInvariants();
        }

        /// <summary>The inbox of an entity, as the worker that runs it would copy it.</summary>
        public List<Event> Inbox(Entity entity)
        {
            var buffer = new EventBuffer();
            EventList events = buffer.Begin(entity);
            Router.CopyInbox(entity, events);
            Assert.Equal(events.Count, Router.InboxCount(entity));
            var copy = new List<Event>();
            for (int i = 0; i < events.Count; i++)
                copy.Add(events[i]);
            return copy;
        }

        public List<int> Pings(Entity entity) => Inbox(entity).Where(e => e.Is<Ping>()).Select(e => e.As<Ping>().Value).ToList();
    }

    private static FrameHandle Frame(int index, uint generation) => new(new Entity(15, generation), index, generation);

    [Fact]
    public void InboxesKeepRoutingOrderAcrossEverySource()
    {
        var h = new Harness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        Entity b = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(a, Squad);
        h.Subscribe(b, Squad);

        // Added out of routing order on purpose: posted first, chunk 1 before chunk 0, engine events last.
        h.Router.Post(Event.Create(a, Entity.Null, new Ping { Value = 40 }));
        h.SendAt(1, 5, a, new Ping { Value = 31 });
        h.SendAt(0, 6, a, new Ping { Value = 32 });
        h.SendAt(0, 5, a, new Ping { Value = 30 });
        h.Send(1, a, new Ping { Value = 20 });
        h.Send(1, new Broadcast(), new Ping { Value = 21 });
        h.Send(0, Squad, new Ping { Value = 10 });
        h.Send(0, b, new Ping { Value = 11 });
        h.Send(0, a, new Ping { Value = 12 });
        h.Router.AddEngineEvent(Event.Create(a, Entity.Null, new Ping { Value = 1 }));

        h.Route(tick: 5);

        // Engine events, then outboxes in chunk order, then delayed sends due now in chunk and send order, then posts.
        Assert.Equal([1, 10, 12, 20, 21, 30, 31, 40], h.Pings(a));
        Assert.Equal([10, 11, 21], h.Pings(b));
        // Every event is stored once, however many recipients it has.
        Assert.Equal(9, h.Router.RoutedCount);
        Assert.Equal(11, h.Router.DeliveryCount);
        foreach (ChunkState chunk in h.Entities.Chunks)
        {
            Assert.Equal(0, chunk.Outbox.Count);
            Assert.Equal(0, chunk.Delayed.Count);
        }

        // The send due later waits for its own routing, and the inboxes of the last routing are replaced.
        h.Route(tick: 6);
        Assert.Equal([32], h.Pings(a));
        Assert.Empty(h.Pings(b));
        Assert.Equal(1, h.Router.RoutedCount);
    }

    [Fact]
    public void TheCountingSortIsStableWhenRecipientsInterleave()
    {
        var h = new Harness();
        var everyone = new List<Entity>();
        for (int i = 0; i < 10; i++)
            everyone.Add(h.Spawn(EventMask.Of<Ping>()));
        Entity last = everyone[^1], first = everyone[0];
        Entity quiet = everyone[4];
        h.Router.Post(Event.Create(last, Entity.Null, new Ping { Value = 1 }));

        // Deliveries arrive interleaved: each broadcast touches every slot, and the direct sends jump between the ends.
        h.Send(0, last, new Ping { Value = 100 });
        h.Send(0, new Broadcast(), new Ping { Value = 200 });
        h.Send(0, first, new Ping { Value = 300 });
        h.Send(0, last, new Ping { Value = 400 });
        h.Send(0, new Broadcast(), new Ping { Value = 500 });

        h.Route(tick: 0);

        Assert.Equal([200, 300, 500], h.Pings(first));
        Assert.Equal([100, 200, 400, 500, 1], h.Pings(last));
        Assert.Equal([200, 500], h.Pings(quiet));
        Assert.Equal(6, h.Router.RoutedCount);
        Assert.Equal(24, h.Router.DeliveryCount);
    }

    [Fact]
    public void RecipientsWhoseMaskLacksTheTypeAreDroppedInEveryMode()
    {
        var h = new Harness();
        Entity pings = h.Spawn(EventMask.Of<Ping>());
        Entity heals = h.Spawn(EventMask.Of<Heal>());
        Entity both = h.Spawn(EventMask.Of<Ping>().And<Heal>());
        Entity none = h.Spawn(EventMask.None);
        foreach (Entity e in new[] { pings, heals, both, none })
            h.Subscribe(e, Squad);

        h.Send(0, heals, new Ping { Value = 1 });
        h.Send(0, Squad, new Ping { Value = 2 });
        h.Send(0, new Broadcast(), new Heal { Amount = 3 });
        h.Send(0, none, new Heal { Amount = 4 });

        h.Route(tick: 0);

        Assert.Equal([2], h.Pings(pings));
        Assert.Equal(3f, Assert.Single(h.Inbox(heals)).As<Heal>().Amount);
        Assert.Equal(2, h.Inbox(both).Count);
        Assert.Empty(h.Inbox(none));
        Assert.Equal(4, h.Router.RoutedCount);
        Assert.Equal(4, h.Router.DeliveryCount);
    }

    [Fact]
    public void DestroyIsAlwaysDeliveredToLiveEntitiesAndNeverToStaleHandles()
    {
        var h = new Harness();
        Entity deaf = h.Spawn(EventMask.None);
        Entity heals = h.Spawn(EventMask.Of<Heal>());
        h.Subscribe(heals, Squad);
        Entity doomed = h.Spawn(EventMask.None);
        h.Entities.Release(doomed);
        Entity reused = h.Spawn(EventMask.None);
        Assert.Equal(doomed.Index, reused.Index);

        h.Send(0, deaf, new Destroy());
        h.Send(0, Squad, new Destroy());
        h.Send(1, new Broadcast(), new Destroy());
        h.Send(1, doomed, new Destroy());

        h.Route(tick: 0);

        Assert.Equal(2, h.Inbox(deaf).Count(e => e.Is<Destroy>()));
        Assert.Equal(2, h.Inbox(heals).Count(e => e.Is<Destroy>()));
        // The stale handle's Destroy does not reach the entity that reused its slot; the broadcast does.
        Assert.Single(h.Inbox(reused), e => e.Is<Destroy>());
    }

    [Fact]
    public void AFrameTopicChecksTheFrameGeneration()
    {
        var h = new Harness();
        FrameHandle first = Frame(index: 3, generation: 1);
        h.Topics.AddFrame(first);
        Entity inside = h.Spawn(EventMask.Of<Ping>(), first);
        Entity watcher = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(watcher, first.Topic);

        h.Send(0, first.Topic, new Ping { Value = 1 });
        h.Route(tick: 0);
        Assert.Equal([1], h.Pings(inside));
        Assert.Equal([1], h.Pings(watcher));

        // The frame dies and a new frame takes its index. Its member moves into the new frame.
        h.Topics.MoveMember(inside, FrameHandle.Null, out _);
        h.Topics.RemoveFrame(first);
        FrameHandle second = Frame(index: 3, generation: 2);
        h.Topics.AddFrame(second);
        h.Topics.MoveMember(inside, second, out _);
        Assert.Equal(first.Topic.Id, second.Topic.Id);

        h.Send(0, first.Topic, new Ping { Value = 2 });
        h.Send(0, new Topic(first.Topic.Id), new Ping { Value = 3 });
        h.Send(0, second.Topic, new Ping { Value = 4 });
        h.Route(tick: 1);

        // Only the message for the live generation arrives, and the old frame's subscriber is not a subscriber of the new one.
        Assert.Equal([4], h.Pings(inside));
        Assert.Empty(h.Pings(watcher));
        Assert.False(h.Topics.Subscribe(watcher, first.Topic));

        // A dead frame whose index nobody reuses resolves to nobody too.
        h.Topics.MoveMember(inside, FrameHandle.Null, out _);
        h.Topics.RemoveFrame(second);
        h.Send(0, second.Topic, new Ping { Value = 5 });
        h.Route(tick: 2);
        Assert.Empty(h.Pings(inside));
        Assert.Equal(0, h.Router.DeliveryCount);
    }

    [Fact]
    public void AFrameTopicReachesMembersAndSubscribersOnceInSlotOrder()
    {
        var h = new Harness();
        FrameHandle bay = Frame(index: 0, generation: 1);
        h.Topics.AddFrame(bay);
        Entity outsider = h.Spawn(EventMask.Of<Ping>());
        Entity member = h.Spawn(EventMask.Of<Ping>(), bay);
        Entity memberAndSubscriber = h.Spawn(EventMask.Of<Ping>(), bay);
        h.Subscribe(memberAndSubscriber, bay.Topic);
        h.Subscribe(outsider, bay.Topic);

        var recipients = new List<Entity>();
        foreach (Entity recipient in h.Topics.RecipientsOf(bay.Topic))
            recipients.Add(recipient);
        Assert.Equal([outsider, member, memberAndSubscriber], recipients);

        h.Send(0, bay.Topic, new Ping { Value = 7 });
        h.Route(tick: 0);
        Assert.Equal([7], h.Pings(outsider));
        Assert.Equal([7], h.Pings(member));
        Assert.Equal([7], h.Pings(memberAndSubscriber));
        Assert.Equal(3, h.Router.DeliveryCount);
    }

    [Fact]
    public void ANewFrameAtAReusedIndexStartsWithNoMembersOrSubscribers()
    {
        var h = new Harness();
        FrameHandle first = Frame(index: 2, generation: 1);
        h.Topics.AddFrame(first);
        Entity inside = h.Spawn(EventMask.Of<Ping>(), first);
        Entity watcher = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(watcher, first.Topic);
        Assert.Equal([inside], h.Topics.MembersOf(first).ToArray());

        // The frame is forgotten while its sets still hold slots: the sets empty at once.
        h.Topics.RemoveFrame(first);
        FrameHandle second = Frame(index: 2, generation: 2);
        h.Topics.AddFrame(second);

        Assert.Empty(h.Topics.MembersOf(second).ToArray());
        Assert.Empty(h.Topics.MembersOf(first).ToArray());
        var recipients = new List<Entity>();
        foreach (Entity recipient in h.Topics.RecipientsOf(second.Topic))
            recipients.Add(recipient);
        Assert.Empty(recipients);
        // The old frame's subscriber lost the subscription, so it can subscribe to the new frame.
        Assert.True(h.Topics.Subscribe(watcher, second.Topic));
    }

    [Fact]
    public void TopicsThatNoSetBacksReachNobody()
    {
        var h = new Harness();
        Entity listener = h.Spawn(EventMask.Of<Ping>());
        h.Subscribe(listener, Squad);

        // Named topics past every subscribed id, and frame topics at and far past the known frame indices.
        h.Send(0, new Topic(Squad.Id + 1), new Ping { Value = 1 });
        h.Send(0, new Topic(Squad.Id + 1000), new Ping { Value = 2 });
        h.Send(0, new Topic(-1 - 16, 1), new Ping { Value = 3 });
        h.Send(0, new Topic(-1 - 1000, 1), new Ping { Value = 4 });
        h.Route(tick: 0);

        Assert.Empty(h.Pings(listener));
        Assert.Equal(4, h.Router.RoutedCount);
        Assert.Equal(0, h.Router.DeliveryCount);
        Assert.Empty(h.Topics.MembersOf(Frame(index: 16, generation: 1)).ToArray());
        Assert.Empty(h.Topics.MembersOf(Frame(index: 1000, generation: 1)).ToArray());
        Assert.False(h.Topics.IsLive(Frame(index: 16, generation: 1)));
        Assert.False(h.Topics.IsLive(FrameHandle.Null));
    }

    [Fact]
    public void NamedTopicsCanBeSubscribedInAnyIdOrder()
    {
        var h = new Harness();
        Entity a = h.Spawn(EventMask.Of<Ping>());
        var ids = new[] { Squad.Id + 3, Squad.Id + 4, Squad.Id + 1, Squad.Id };
        foreach (int id in ids)
            h.Subscribe(a, new Topic(id));
        foreach (int id in ids)
            h.Send(0, new Topic(id), new Ping { Value = id });
        h.Route(tick: 0);

        Assert.Equal(ids, h.Pings(a));
    }

    [Fact]
    public void AReleasedSlotLosesItsInboxSoTheNextEntityThereStartsEmpty()
    {
        var h = new Harness();
        Entity old = h.Spawn(EventMask.Of<Ping>());
        Entity other = h.Spawn(EventMask.Of<Ping>());
        h.Send(0, old, new Ping { Value = 1 });
        h.Send(0, other, new Ping { Value = 2 });
        h.Route(tick: 0);
        Assert.Equal(1, h.Router.InboxCount(old));

        // Released between two routings, as an outside-tick Destroy does, and taken again at once. The released
        // entity's inbox entry is clear: the table checks every column entry of a slot that is not live.
        h.Entities.Release(old);
        h.Entities.AssertInvariants();
        Entity reused = h.Spawn(EventMask.Of<Ping>());
        Assert.Equal(old.Index, reused.Index);

        Assert.Empty(h.Inbox(reused));
        h.Router.AssertInvariants();
        Assert.Equal([2], h.Pings(other));
        // A slot past the last routing has an empty inbox without being told.
        Entity fresh = h.Spawn(EventMask.Of<Ping>());
        Assert.Equal(0, h.Router.InboxCount(fresh));
    }
}
