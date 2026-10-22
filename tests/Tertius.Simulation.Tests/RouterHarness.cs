using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A router over a hand-built entity table and topic sets, for the router tests. The table has one slot per chunk, so
/// every entity has an outbox of its own, and a test fills the outbox of any chunk directly, as the entities of that
/// chunk would. The tests read an inbox the way the worker that runs the entity does. <see cref="OnWorkers"/> makes a
/// harness whose router routes every message on the workers.
/// </summary>
internal sealed class RouterHarness
{
    public readonly EntityTable Entities;
    public readonly TopicSets Topics;
    public readonly Router Router;
    private readonly CountingScheduler? workers;

    /// <summary>A harness whose router has no workers, so every routing runs on the calling thread.</summary>
    public RouterHarness() : this(null)
    {
    }

    private RouterHarness(CountingScheduler? workers)
    {
        EngineEvents.Register();
        Entities = new(chunkSize: 1, initialCapacity: 16);
        Topics = new(Entities);
        this.workers = workers;
        Router = new(Entities, Topics,
            workers is null ? null : new ParallelOptions { MaxDegreeOfParallelism = 4, TaskScheduler = workers });
    }

    /// <summary>How many tasks the router queued on its workers so far. Always 0 for a harness without workers.</summary>
    public int TasksQueued => workers?.Queued ?? 0;

    /// <summary>
    /// A harness whose router routes on four workers from <paramref name="threshold"/> messages on, in
    /// <paramref name="workItems"/> work items per phase. A threshold of 1, the default here, sends every routing with a
    /// message to the workers; null keeps the router's own threshold.
    /// </summary>
    public static RouterHarness OnWorkers(int workItems = 3, int? threshold = 1)
    {
        var h = new RouterHarness(new());
        if (threshold is { } t)
            h.Router.ParallelThreshold = t;
        h.Router.WorkItems = workItems;
        return h;
    }

    /// <summary>A handle for a frame, with a made-up owner. The topic sets only need the index and the generation.</summary>
    public static FrameHandle Frame(int index, uint generation) => new(new(15, generation), index, generation);

    /// <summary>A live entity whose one component wants <paramref name="wants"/>, optionally a member of a frame.</summary>
    public Entity Spawn(EventMask wants, FrameHandle frame = default)
    {
        Entity entity = Entities.Reserve();
        Entities.Activate(entity, new() { Frame = frame, PhysicsHome = frame }, new(), [new Recorder(wants)], [wants],
            null, Vector3.Zero, FrameHandle.Null);
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

    /// <summary>Writes a delayed message into a chunk, as an entity of that chunk would. It is due at routing number
    /// <paramref name="due"/>.</summary>
    public void SendAt<T>(int chunk, long due, Target target, T payload) where T : unmanaged, IEventPayload
    {
        EnsureChunk(chunk);
        Entities.Chunks[chunk].Delayed.Add(new() { Due = due, Event = Event.Create(target, Entity.Null, payload) });
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
    public List<int> Pings(Entity entity) =>
        [.. Inbox(entity).Where(e => e.Is<Ping>()).Select(e => e.As<Ping>().Value)];

    /// <summary>The amount of each <see cref="Heal"/> in the inbox of an entity, in inbox order.</summary>
    public List<float> Heals(Entity entity) =>
        [.. Inbox(entity).Where(e => e.Is<Heal>()).Select(e => e.As<Heal>().Amount)];

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

/// <summary>
/// A task scheduler that runs each task on the thread pool and counts the tasks queued on it, so that a test can tell
/// whether a routing ran on the workers that its router was given.
/// </summary>
internal sealed class CountingScheduler : TaskScheduler
{
    private int queued;

    public int Queued => Volatile.Read(ref queued);

    protected override void QueueTask(Task task)
    {
        Interlocked.Increment(ref queued);
        ThreadPool.UnsafeQueueUserWorkItem(_ => TryExecuteTask(task), null);
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);

    protected override IEnumerable<Task> GetScheduledTasks() => [];
}
