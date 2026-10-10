using System.Collections.Concurrent;

namespace Tertius.Simulation;

/// <summary>
/// Turns the messages of one tick into the inboxes of the next. At the sync point, <see cref="Route"/> joins every
/// source into one list in a fixed order, expands each target into recipients, drops the recipients whose mask lacks
/// the event type, and sorts the deliveries by recipient with a stable counting sort. During the next tick, each
/// worker copies its entities' inboxes with <see cref="CopyInbox"/>.
/// </summary>
/// <remarks>
/// Invariants:
/// <list type="bullet">
/// <item>Each routed event is stored once, in routing order: the engine events, the chunk outboxes in chunk order, the
/// delayed sends that are due in the order they were sent, then the posted events. A delivery names an event by its
/// index in that list.</item>
/// <item>An inbox holds its events in routing order, so it is the same in every run with the same inputs, whatever
/// the threads did.</item>
/// <item>Every live entity accepts <see cref="Destroy"/>, because every entity mask contains it.</item>
/// <item>An inbox is a range of the sorted deliveries, every delivery in it names its entity's slot, and its events
/// are in routing order, each once. Inboxes do not overlap. A non-empty inbox belongs to a live entity: the inboxes
/// are a column of the entity table, which empties an entity's inbox when it releases the entity, so a new entity in a
/// reused slot never sees the old entity's messages.</item>
/// <item>The routed events and the inboxes stay valid and unchanged from one <see cref="Route"/> to the next, apart
/// from released entities, so the workers can read them without locks during the tick.</item>
/// </list>
/// The interface takes entity handles and checks them. Slots appear only inside: a delivery names its recipient's
/// slot, because the counting sort runs over the whole inbox column by slot.
/// </remarks>
internal sealed class Router
{
    /// <summary>One recipient of one routed event: the recipient's slot and the event's index in <see cref="routed"/>.</summary>
    private readonly record struct Delivery(uint Recipient, int Event);

    /// <summary>An entity's inbox: <see cref="Count"/> deliveries from <see cref="Start"/> in <see cref="sortedDeliveries"/>.</summary>
    private readonly record struct InboxRange(int Start, int Count);

    private readonly EntityTable entities;
    private readonly TopicSets topics;

    // Message sources, in routing order. Engine events are the ones that the sync point itself sends: FrameChanged and
    // Collision. Timers hold delayed sends by due tick, each bucket in send order.
    private readonly RefList<Event> engineEvents = new(16);
    private readonly Dictionary<long, RefList<Event>> timers = new();
    private readonly Stack<RefList<Event>> spareTimerLists = new();
    private readonly ConcurrentQueue<Event> posted = new();

    private readonly RefList<Event> routed = new(256);
    private readonly RefList<Delivery> deliveries = new(256);
    private Delivery[] sortedDeliveries = new Delivery[256];
    private readonly EntityTable.Column<InboxRange> inbox;

    public Router(EntityTable entities, TopicSets topics)
    {
        this.entities = entities;
        this.topics = topics;
        inbox = entities.AddColumn<InboxRange>();
    }

    /// <summary>Events routed at the last <see cref="Route"/>, each counted once.</summary>
    public int RoutedCount => routed.Count;

    /// <summary>Inbox entries written at the last <see cref="Route"/>.</summary>
    public int DeliveryCount => deliveries.Count;

    /// <summary>Queues an event that the sync point sends. It joins the next routing, ahead of every outbox.</summary>
    public void AddEngineEvent(in Event e) => engineEvents.Add(e);

    /// <summary>Queues an event from outside the simulation. Thread-safe. It joins the next routing, after every other source.</summary>
    public void Post(in Event e) => posted.Enqueue(e);

    /// <summary>
    /// Step 8 of the sync point. Moves the chunks' delayed sends into the timer queue, joins the sources in routing
    /// order, expands the targets and builds the inboxes. The chunk outboxes and delayed lists are left empty.
    /// </summary>
    /// <param name="tick">The tick that is ending. Delayed sends due at this tick join this routing.</param>
    public void Route(long tick)
    {
        QueueDelayedSends();
        Join(tick);
        Expand();
        SortByRecipient();
    }

    /// <summary>The number of events in an entity's inbox. Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe.</summary>
    public int InboxCount(Entity entity) => inbox[entity].Count;

    /// <summary>
    /// Appends an entity's inbox to <paramref name="events"/>, in routing order. The worker that runs the entity calls
    /// it, so the per-recipient copies happen in parallel on the workers, never at the serial sync point. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public void CopyInbox(Entity entity, EventList events)
    {
        InboxRange range = inbox[entity];
        foreach (Delivery d in sortedDeliveries.AsSpan(range.Start, range.Count))
            events.Add(in routed[d.Event]);
    }

    /// <summary>
    /// Checks every invariant in the class comment. O(capacity + deliveries). That the inbox of every slot that is not
    /// live is clear is the entity table's invariant over its columns, so this checks the live entities' inboxes, in
    /// slot order.
    /// </summary>
    internal void AssertInvariants()
    {
        int total = deliveries.Count;
        int previousEnd = 0;
        foreach (Entity entity in entities.LiveEntities)
        {
            InboxRange range = inbox[entity];
            if (range.Count == 0)
            {
                Contract.Invariant(range == default);
                continue;
            }
            Contract.Invariant(range.Count > 0 && range.Start >= previousEnd && range.Start + range.Count <= total);
            previousEnd = range.Start + range.Count;
            int previousEvent = -1;
            foreach (Delivery d in sortedDeliveries.AsSpan(range.Start, range.Count))
            {
                Contract.Invariant(d.Recipient == entity.Index && d.Event > previousEvent && d.Event < routed.Count);
                previousEvent = d.Event;
            }
        }
    }

    private void QueueDelayedSends()
    {
        foreach (ChunkState chunk in entities.Chunks)
        {
            RefList<DelayedEvent> delayed = chunk.Delayed;
            RefList<Event>? bucket = null;
            long bucketDue = 0;
            foreach (ref DelayedEvent d in delayed.AsSpan())
            {
                if (bucket is null || d.Due != bucketDue)
                {
                    bucketDue = d.Due;
                    bucket = TimerBucket(bucketDue);
                }
                bucket.Add(d.Event);
            }
            delayed.Clear();
        }
    }

    /// <summary>The messages due at <paramref name="tick"/>, in the order they were sent.</summary>
    private RefList<Event> TimerBucket(long tick)
    {
        if (!timers.TryGetValue(tick, out RefList<Event>? bucket))
        {
            bucket = spareTimerLists.Count > 0 ? spareTimerLists.Pop() : new RefList<Event>(8);
            timers.Add(tick, bucket);
        }
        return bucket;
    }

    /// <summary>Stores every event of this routing once, in routing order.</summary>
    private void Join(long tick)
    {
        routed.Clear();
        routed.AddRange(engineEvents.AsSpan());
        engineEvents.Clear();
        foreach (ChunkState chunk in entities.Chunks)
        {
            RefList<Event> outbox = chunk.Outbox;
            routed.AddRange(outbox.AsSpan());
            outbox.Clear();
        }
        if (timers.Remove(tick, out RefList<Event>? due))
        {
            routed.AddRange(due.AsSpan());
            due.Clear();
            spareTimerLists.Push(due);
        }
        while (posted.TryDequeue(out Event e))
            routed.Add(e);
    }

    /// <summary>Expands each event's target into deliveries, in routing order. Recipients that do not accept the type are dropped.</summary>
    private void Expand()
    {
        deliveries.Clear();
        for (int i = 0; i < routed.Count; i++)
        {
            ref Event e = ref routed[i];
            int type = e.Type;
            switch (e.Target)
            {
                case Entity target:
                    // A stale handle reaches nobody, even if a new entity reuses the slot.
                    if (entities.IsAlive(target))
                        AddDelivery(target, i, type);
                    break;
                case Topic topic:
                    foreach (Entity recipient in topics.RecipientsOf(topic))
                        AddDelivery(recipient, i, type);
                    break;
                case Broadcast:
                    foreach (Entity recipient in entities.LiveEntities)
                        AddDelivery(recipient, i, type);
                    break;
            }
        }
    }

    private void AddDelivery(Entity recipient, int eventIndex, int type)
    {
        // Every mask contains Destroy, so a Destroy always gets through.
        if (entities.Accepts(recipient, type))
            deliveries.Add(new Delivery(recipient.Index, eventIndex));
    }

    /// <summary>
    /// Stable counting sort of the deliveries by recipient slot: each inbox keeps routing order. The inbox ranges double
    /// as the fill cursors: the counts become start offsets, and the fill pass counts each range up again. An empty
    /// inbox stays clear. A whole-column pass, so it indexes the inbox column by slot.
    /// </summary>
    private void SortByRecipient()
    {
        Span<InboxRange> ranges = inbox.AsSpan()[..entities.SlotCount];
        ranges.Clear();
        ReadOnlySpan<Delivery> unsorted = deliveries.AsSpan();
        foreach (Delivery d in unsorted)
        {
            ref InboxRange range = ref ranges[(int)d.Recipient];
            range = range with { Count = range.Count + 1 };
        }

        int start = 0;
        foreach (ref InboxRange range in ranges)
        {
            int count = range.Count;
            if (count == 0)
                continue;
            range = new InboxRange(start, 0);
            start += count;
        }

        if (sortedDeliveries.Length < unsorted.Length)
            sortedDeliveries = new Delivery[Math.Max(unsorted.Length, sortedDeliveries.Length * 2)];
        foreach (Delivery d in unsorted)
        {
            ref InboxRange range = ref ranges[(int)d.Recipient];
            sortedDeliveries[range.Start + range.Count] = d;
            range = range with { Count = range.Count + 1 };
        }
    }
}
