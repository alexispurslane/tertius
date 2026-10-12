using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A router over a hand-built entity table and topic sets, for the router tests. The table has one slot per chunk, so
/// every entity has an outbox of its own, and a test fills the outbox of any chunk directly, as the entities of that
/// chunk would. The tests read an inbox the way the worker that runs the entity does.
/// </summary>
internal sealed class RouterHarness
{
    public readonly EntityTable Entities;
    public readonly TopicSets Topics;
    public readonly Router Router;

    public RouterHarness()
    {
        EngineEvents.Register();
        Entities = new EntityTable(chunkSize: 1, initialCapacity: 16);
        Topics = new TopicSets(Entities);
        Router = new Router(Entities, Topics);
    }

    /// <summary>A handle for a frame, with a made-up owner. The topic sets only need the index and the generation.</summary>
    public static FrameHandle Frame(int index, uint generation) => new(new Entity(15, generation), index, generation);

    /// <summary>A live entity whose one component wants <paramref name="wants"/>, optionally a member of a frame.</summary>
    public Entity Spawn(EventMask wants, FrameHandle frame = default)
    {
        Entity entity = Entities.Reserve();
        Entities.Activate(entity, new TransformRow { Frame = frame, PhysicsHome = frame }, new RenderData(), [new Recorder(wants)], [wants], null, FrameHandle.Null);
        Topics.MoveMember(entity, frame, out _);
        return entity;
    }

    public void Subscribe(Entity entity, Topic topic) => Topics.Subscribe(entity, topic).Should().BeTrue();

    /// <summary>
    /// Writes a message into the outbox of a chunk, as an entity of that chunk would. The table only has chunks for the
    /// slots it has handed out, so a chunk that has no entity yet gets filler entities that want nothing.
    /// </summary>
    public void Send<T>(int chunk, Target target, T payload) where T : unmanaged, IEventPayload
    {
        EnsureChunk(chunk);
        Entities.Chunks[chunk].Outbox.Add(Event.Create(target, Entity.Null, payload));
    }

    /// <summary>Writes a delayed message into a chunk, as an entity of that chunk would. It is due at routing number <paramref name="due"/>.</summary>
    public void SendAt<T>(int chunk, long due, Target target, T payload) where T : unmanaged, IEventPayload
    {
        EnsureChunk(chunk);
        Entities.Chunks[chunk].Delayed.Add(new DelayedEvent { Due = due, Event = Event.Create(target, Entity.Null, payload) });
    }

    // With one slot per chunk, chunk n exists once slot n has been handed out.
    private void EnsureChunk(int chunk)
    {
        while (Entities.Chunks.Length <= chunk)
            Spawn(EventMask.None);
    }

    /// <summary>Routes, and checks the invariants of the router and the topic sets after it.</summary>
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
        Router.InboxCount(entity).Should().Be(events.Count);
        var copy = new List<Event>();
        for (int i = 0; i < events.Count; i++)
            copy.Add(events[i]);
        return copy;
    }

    /// <summary>The value of each <see cref="Ping"/> in the inbox of an entity, in inbox order.</summary>
    public List<int> Pings(Entity entity) => Inbox(entity).Where(e => e.Is<Ping>()).Select(e => e.As<Ping>().Value).ToList();

    /// <summary>The amount of each <see cref="Heal"/> in the inbox of an entity, in inbox order.</summary>
    public List<float> Heals(Entity entity) => Inbox(entity).Where(e => e.Is<Heal>()).Select(e => e.As<Heal>().Amount).ToList();

    /// <summary>How many <see cref="Destroy"/> events the inbox of an entity holds.</summary>
    public int Destroys(Entity entity) => Inbox(entity).Count(e => e.Is<Destroy>());

    /// <summary>Every recipient of a topic, in the order the topic sets give them.</summary>
    public List<Entity> RecipientsOf(Topic topic)
    {
        var recipients = new List<Entity>();
        foreach (Entity recipient in Topics.RecipientsOf(topic))
            recipients.Add(recipient);
        return recipients;
    }
}
