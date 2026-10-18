using System.Collections.Concurrent;

namespace Tertius.Simulation;

/// <summary>
/// Turns the messages of one tick into the inboxes of the next. At the sync point, <see cref="Route"/> joins every
/// source into one list in a fixed order, expands each target into recipients, drops the recipients whose mask lacks
/// the event type, and sorts the deliveries by recipient with a stable counting sort. During the next tick, each
/// worker copies its entities' inboxes with <see cref="CopyInbox"/>.
/// </summary>
/// <remarks>
/// <para>
/// A routing has two phases of independent work items. Each block of chunks copies its outboxes into the routed list
/// and expands its routed events into <see cref="DeliveryCells"/>, and then each slot range sorts its cells into the
/// inboxes of its slots. A routing of at least <see cref="ParallelThreshold"/> messages runs the work items on the
/// workers; a smaller one runs one block and one slot range on the sync thread. Both give the same routed events,
/// deliveries and inboxes, bit for bit, because no order depends on the threads: each chunk's outbox goes to an offset
/// that a serial pass computed in chunk order, each block's events come before the next block's, the sort of a slot
/// range reads its cells in block order, so that every inbox lists its deliveries in routing order as one stable sort
/// of the whole list does, and each slot range writes its deliveries from an offset that a serial pass computed in
/// slot order.
/// </para>
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
    /// <summary>An entity's inbox: <see cref="Count"/> deliveries from <see cref="Start"/> in <see cref="sortedDeliveries"/>.</summary>
    private readonly record struct InboxRange(int Start, int Count);

    /// <summary>
    /// The default of <see cref="ParallelThreshold"/>. Measured on ter-sn28 with all workers (Apple M1 Max, 10 cores,
    /// busy with other builds), in the tick benchmark with one message per entity: the workers lose at 3,000 to 4,000
    /// messages and win by about a fifth at 6,000 to 7,000.
    /// </summary>
    internal const int DefaultParallelThreshold = 6000;

    /// <summary>The default of <see cref="WorkItems"/>: on 10 cores, 8, 16 and 32 measured the same within the noise.</summary>
    internal const int DefaultWorkItems = 16;

    private readonly EntityTable entities;
    private readonly TopicSets topics;
    private readonly ParallelOptions? workers;

    // Message sources, in routing order. Engine events are the ones that the sync point itself sends: FrameChanged and
    // Collision. Timers hold delayed sends by due tick, each bucket in send order.
    private readonly RefList<Event> engineEvents = new(16);
    private readonly Dictionary<long, RefList<Event>> timers = new();
    private readonly Stack<RefList<Event>> spareTimerLists = new();
    private readonly ConcurrentQueue<Event> posted = new();

    private readonly RefList<Event> routed = new(256);
    private readonly DeliveryCells cells = new();
    private Delivery[] sortedDeliveries = new Delivery[256];
    private readonly EntityTable.Column<InboxRange> inbox;

    // This routing: whether it runs on the workers, its chunk count, and per chunk the index in the routed list of its
    // outbox's first event, with the end of the last outbox after them. Per block, its first chunk, with the chunk count
    // after the last block.
    private bool onWorkers;
    private int chunkCount;
    private int[] outboxStarts = new int[16];
    private int[] blockChunks = new int[2];
    private readonly Action<int> fillBlock, sortRange;

    /// <param name="entities">The entity table whose chunks hold the outboxes, and whose slots the inboxes follow.</param>
    /// <param name="topics">The topic sets that a topic message expands into.</param>
    /// <param name="workers">The workers on which a large routing runs, or null to run every routing on the calling thread.</param>
    public Router(EntityTable entities, TopicSets topics, ParallelOptions? workers = null)
    {
        this.entities = entities;
        this.topics = topics;
        this.workers = workers;
        inbox = entities.AddColumn<InboxRange>();
        fillBlock = FillBlock;
        sortRange = SortRange;
    }

    /// <summary>
    /// The fewest messages for which a routing runs on the workers: the engine events, the outbox messages and the due
    /// delayed sends, not the posted events. 1 or more. Below it the work items cost more than they save. Both paths
    /// fill the same inboxes, so only tests change it: at 1, every routing with a message takes the workers.
    /// </summary>
    internal int ParallelThreshold
    {
        get => parallelThreshold;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            parallelThreshold = value;
        }
    }

    private int parallelThreshold = DefaultParallelThreshold;

    /// <summary>
    /// How many blocks a routing on the workers cuts the chunks into, and the most slot ranges it cuts the slots into.
    /// More work items than workers lets a fast worker take the work of a slow one. Any count gives the same result.
    /// Tests change it to move the edges of the blocks and the slot ranges.
    /// </summary>
    internal int WorkItems { get; set; } = DefaultWorkItems;

    /// <summary>Events routed at the last <see cref="Route"/>, each counted once.</summary>
    public int RoutedCount => routed.Count;

    /// <summary>The events routed at the last <see cref="Route"/>, each once, in routing order.</summary>
    public ReadOnlySpan<Event> Routed => routed.AsSpan();

    /// <summary>Inbox entries written at the last <see cref="Route"/>.</summary>
    public int DeliveryCount => cells.Count;

    /// <summary>Queues an event that the sync point sends. It joins the next routing, ahead of every outbox.</summary>
    public void AddEngineEvent(in Event e) => engineEvents.Add(e);

    /// <summary>Queues an event from outside the simulation. Thread-safe. It joins the next routing, after every other source.</summary>
    public void Post(in Event e) => posted.Enqueue(e);

    /// <summary>
    /// Step 10 of the sync point. Moves the chunks' delayed sends into the timer queue, joins the sources in routing
    /// order, expands the targets and builds the inboxes. The chunk outboxes and delayed lists are left empty.
    /// </summary>
    /// <param name="tick">The tick that is ending. Delayed sends due at this tick join this routing.</param>
    public void Route(long tick)
    {
        int outboxEnd = PlaceOutboxes();
        timers.Remove(tick, out RefList<Event>? due);
        onWorkers = workers is not null && outboxEnd + (due?.Count ?? 0) >= ParallelThreshold;
        if (onWorkers)
        {
            cells.Shape(WorkItems, entities.SlotCount);
            // A topic set applies its queued changes when it is next read: the sync thread reads first.
            topics.Settle();
        }
        else
        {
            cells.ShapeOne(entities.SlotCount);
        }
        PlaceBlocks();
        JoinSyncSources(outboxEnd, due);
        Run(cells.BlockCount, fillBlock);
        cells.PlaceRanges();
        if (sortedDeliveries.Length < cells.Count)
            sortedDeliveries = new Delivery[Math.Max(cells.Count, sortedDeliveries.Length * 2)];
        Run(cells.RangeCount, sortRange);
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
        int total = cells.Count;
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

    /// <summary>
    /// Moves every chunk's delayed sends into the timer queue, and gives each chunk's outbox its place in the routed
    /// list: after the engine events and the outboxes of the chunks before it. Returns the end of the last outbox.
    /// </summary>
    private int PlaceOutboxes()
    {
        ReadOnlySpan<ChunkState> chunks = entities.Chunks;
        chunkCount = chunks.Length;
        if (outboxStarts.Length < chunkCount + 1)
            outboxStarts = new int[Math.Max(chunkCount + 1, outboxStarts.Length * 2)];
        int end = engineEvents.Count;
        for (int c = 0; c < chunkCount; c++)
        {
            ChunkState chunk = chunks[c];
            QueueDelayedSends(chunk.Delayed);
            outboxStarts[c] = end;
            end += chunk.Outbox.Count;
        }
        outboxStarts[chunkCount] = end;
        return end;
    }

    private void QueueDelayedSends(RefList<DelayedEvent> delayed)
    {
        if (delayed.Count == 0)
            return;
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

    /// <summary>
    /// Cuts the chunks into as many blocks as the cells have, of about as many outbox messages each: block b starts at the
    /// first chunk whose outbox starts at or after b blocks' share of the messages.
    /// </summary>
    private void PlaceBlocks()
    {
        int blockCount = cells.BlockCount;
        if (blockChunks.Length < blockCount + 1)
            blockChunks = new int[blockCount + 1];
        int engineCount = outboxStarts[0];
        long messages = outboxStarts[chunkCount] - engineCount;
        blockChunks[0] = 0;
        for (int b = 1; b < blockCount; b++)
        {
            int target = engineCount + (int)(messages * b / blockCount);
            // The first chunk whose outbox starts at or after the target, by bisection: the starts never decrease.
            int low = blockChunks[b - 1], high = chunkCount;
            while (low < high)
            {
                int mid = (low + high) >>> 1;
                if (outboxStarts[mid] < target)
                    low = mid + 1;
                else
                    high = mid;
            }
            blockChunks[b] = low;
        }
        blockChunks[blockCount] = chunkCount;
    }

    /// <summary>
    /// Runs <paramref name="body"/> for each of <paramref name="count"/> work items: on the workers, or in order on this
    /// thread. On the workers, <c>Parallel.For</c> allocates about 2 KB for its bookkeeping, the one allocation that
    /// ARCHITECTURE.md accepts on the hot path ("Threads and the tick"). On this thread, a routing allocates nothing.
    /// </summary>
    private void Run(int count, Action<int> body)
    {
        if (onWorkers)
        {
            Parallel.For(0, count, workers!, body);
            return;
        }
        for (int i = 0; i < count; i++)
            body(i);
    }

    /// <summary>
    /// Stores the events that the sync thread holds at their places in the routed list: the engine events first, the due
    /// delayed sends after the outboxes, then the posted events. <see cref="FillBlock"/> copies the outboxes.
    /// </summary>
    private void JoinSyncSources(int outboxEnd, RefList<Event>? due)
    {
        int dueCount = due?.Count ?? 0;
        // Every item below the new count is written here or by FillBlock, so none needs clearing first.
        routed.SetCount(outboxEnd + dueCount);
        Span<Event> all = routed.AsSpan();
        engineEvents.AsSpan().CopyTo(all);
        engineEvents.Clear();
        if (due is not null)
        {
            due.AsSpan().CopyTo(all[outboxEnd..]);
            due.Clear();
            spareTimerLists.Push(due);
        }
        while (posted.TryDequeue(out Event e))
            routed.Add(e);
    }

    /// <summary>
    /// One block: copies the outboxes of its chunks to their places in the routed list and empties them, then expands the
    /// targets of its routed events into its cells, in routing order. The block's events are its outboxes' messages, with
    /// the engine events ahead of the first block and the due and posted events after the last. Recipients that do not
    /// accept the type are dropped.
    /// </summary>
    private void FillBlock(int block)
    {
        int firstChunk = blockChunks[block], endChunk = blockChunks[block + 1];
        ReadOnlySpan<ChunkState> chunks = entities.Chunks;
        Event[] items = routed.Items;
        for (int c = firstChunk; c < endChunk; c++)
        {
            RefList<Event> outbox = chunks[c].Outbox;
            if (outbox.Count == 0)
                continue;
            outbox.AsSpan().CopyTo(items.AsSpan(outboxStarts[c]));
            outbox.Clear();
        }

        DeliveryCells.BlockCells mine = cells.Block(block);
        int first = block == 0 ? 0 : outboxStarts[firstChunk];
        int end = block == cells.BlockCount - 1 ? routed.Count : outboxStarts[endChunk];
        Span<Event> all = routed.AsSpan();
        for (int i = first; i < end; i++)
        {
            ref Event e = ref all[i];
            int type = e.Type;
            switch (e.Target)
            {
                case Entity target:
                    // A stale handle reaches nobody, even if a new entity reuses the slot.
                    if (entities.IsAlive(target))
                        AddDelivery(mine, target, i, type);
                    break;
                case Topic topic:
                    foreach (Entity recipient in topics.RecipientsOf(topic))
                        AddDelivery(mine, recipient, i, type);
                    break;
                case Broadcast:
                    foreach (Entity recipient in entities.LiveEntities)
                        AddDelivery(mine, recipient, i, type);
                    break;
            }
        }
    }

    private void AddDelivery(DeliveryCells.BlockCells mine, Entity recipient, int eventIndex, int type)
    {
        // Every mask contains Destroy, so a Destroy always gets through.
        if (entities.Accepts(recipient, type))
            mine.Add(new Delivery(recipient.Index, eventIndex));
    }

    /// <summary>
    /// Stable counting sort of the deliveries of one slot range by recipient slot: each inbox keeps routing order, since
    /// the cells are read in block order. The inbox ranges double as the fill cursors: the counts become start offsets,
    /// and the fill pass counts each range up again. An empty inbox stays clear. A whole-column pass, so it indexes the
    /// inbox column by slot.
    /// </summary>
    private void SortRange(int range)
    {
        (int firstSlot, int endSlot) = cells.Slots(range);
        Span<InboxRange> ranges = inbox.AsSpan()[firstSlot..endSlot];
        ranges.Clear();
        int blockCount = cells.BlockCount;
        for (int b = 0; b < blockCount; b++)
        {
            foreach (Delivery d in cells.Cell(b, range))
            {
                ref InboxRange slot = ref ranges[(int)d.Recipient - firstSlot];
                slot = slot with { Count = slot.Count + 1 };
            }
        }

        int start = cells.RangeStart(range);
        foreach (ref InboxRange slot in ranges)
        {
            int count = slot.Count;
            if (count == 0)
                continue;
            slot = new InboxRange(start, 0);
            start += count;
        }

        Delivery[] sorted = sortedDeliveries;
        for (int b = 0; b < blockCount; b++)
        {
            foreach (Delivery d in cells.Cell(b, range))
            {
                ref InboxRange slot = ref ranges[(int)d.Recipient - firstSlot];
                sorted[slot.Start + slot.Count] = d;
                slot = slot with { Count = slot.Count + 1 };
            }
        }
    }
}
