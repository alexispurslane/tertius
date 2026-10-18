using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DestroyEvent = Tertius.Destroy;

namespace Tertius.Simulation;

/// <summary>
/// The entity slots and everything stored per entity: the slot lifecycle and generations, the deterministic spawn
/// pools, the double-buffered transform, render, component flag, collision and intent tables, the component lists with
/// their cached mask, the index of the entities by component type, and the dirty tracking that copies changed back rows
/// forward. Other subsystems keep their per-entity state in columns that
/// they add with <see cref="AddColumn{T}"/>: the table grows every column together with its own tables, and clears an
/// entity's entry in every column when it releases the entity.
/// </summary>
/// <remarks>
/// <para>
/// The interface speaks in <see cref="Entity"/> handles only. Every member that takes one checks the handle's
/// generation itself, on every call, in every build, so that a handle whose slot was released, or released and
/// reused, never reaches another entity's data. Each member's comment says which of these it does with a stale or null
/// handle: it throws <see cref="StaleEntityException"/>, because it reads or writes the entity's data; it does nothing
/// and returns false, because its goal already holds for a dead entity; or, for a member that hands out entities, it
/// never hands out a dead one. Slots appear only inside this class, and as the index of a column's whole-column view.
/// </para>
/// <para>
/// A slot goes Free, then Reserved (<see cref="Reserve"/> outside a tick, or <see cref="ReserveForSpawn"/> from the
/// spawner's chunk pool during one), then Live (<see cref="Activate"/>), then Free again (<see cref="Release"/>, which
/// also ends a reservation that never became live). Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>A Free slot below <see cref="SlotCount"/> is in the free list once. A Reserved slot is in one chunk's spawn
/// pool or handed out for a spawn that is not live yet. Every slot from <see cref="SlotCount"/> up is fresh, with
/// generation 0; every other slot has a generation of 1 or more.</item>
/// <item>Releasing a slot increments its generation, so every handle to the old entity is stale.</item>
/// <item>Every column, the table's own and the added ones, has <see cref="Capacity"/> entries, and every entry of a
/// slot that is not live is clear. A released slot carries no stale data.</item>
/// <item>A live slot's mask is <see cref="DestroyEvent"/> plus the OR of its components' cached wants.</item>
/// <item>A live slot whose back row differs from its front row is dirty and in a dirty list, so
/// <see cref="CopyDirtyRows"/> never misses a change. Gameplay never writes the transform of a slot with a body.</item>
/// <item>A live slot's back component flags are the types of its components. Its collision rows are the default for an
/// entity without a body. Its kept bounding radii are the <see cref="ShapeDesc.BoundingRadius"/> of its body's shape and
/// of the shape inside it (<see cref="BodyDesc.InteriorShape"/>, or the shape when it has none), or 0 without a body.</item>
/// <item>A slot is in the set of <see cref="BodyEntities"/> exactly when it is live and has a body.</item>
/// <item>The component index lists exactly the live entities whose front flags hold each type. Nothing changes it
/// during the gameplay part of a tick, which starts with it settled, so the workers read it without a race.</item>
/// <item>A back intent is set only for a live entity in its chunk's list of intent writers, and a front intent only
/// for an entity in <see cref="IntentEntities"/>. A writer that died since it wrote stays in the list until the swap,
/// which skips it: releasing it cleared its slot.</item>
/// <item>Every live entity whose front transform changed since the last <see cref="TakeMoved"/> is in the moved
/// list.</item>
/// <item>Outside <see cref="CopyDirtyRows"/>, the lists in which a copy on the workers keeps each chunk's moved entities
/// and component flag copies, and the list of component flag copies, are empty.</item>
/// </list>
/// The gameplay workers call only the members documented as worker-safe, and only for entities of the chunk they run.
/// </para>
/// </remarks>
internal sealed partial class EntityTable
{
    private const int MaxSpawnPool = 1024;

    private enum SlotState : byte
    {
        Free,
        /// <summary>Held in a chunk's spawn pool, or handed out for a spawn that is not live yet.</summary>
        Reserved,
        Live,
    }

    /// <summary>The table's own per-slot bits. Releasing a slot clears them.</summary>
    [Flags]
    private enum SlotFlags : byte
    {
        None = 0,
        /// <summary>The back transform row may differ from the front row, and the slot is in a dirty list.</summary>
        TransformDirty = 1 << 0,
        /// <summary>The back render row may differ from the front row, and the slot is in a dirty list.</summary>
        RenderDirty = 1 << 1,
        /// <summary>The slot's entity is in its chunk's list of intent writers for this tick.</summary>
        IntentRegistered = 1 << 2,
        /// <summary>The entity became live at the last sync and has not had its first tick yet.</summary>
        FirstTick = 1 << 3,
        /// <summary>Gameplay wrote the transform this tick.</summary>
        GameplayWroteTransform = 1 << 4,
        /// <summary>The entity is in the moved list.</summary>
        FrontMoved = 1 << 5,
        /// <summary>The back component flags may differ from the front flags, and the slot is in a dirty list.</summary>
        ComponentsDirty = 1 << 6,
        /// <summary>The back collision row may differ from the front row, and the slot is in a dirty list.</summary>
        CollisionDirty = 1 << 7,
    }

    /// <summary>The bits that put a slot in a dirty list: one for each double-buffered row that <see cref="CopyRow"/> copies.</summary>
    private const SlotFlags RowsDirty = SlotFlags.TransformDirty | SlotFlags.RenderDirty | SlotFlags.ComponentsDirty | SlotFlags.CollisionDirty;

    /// <summary>The per-entity data that is cold, or that changes only through a component change or a spawn.</summary>
    private struct Record
    {
        /// <summary>Sorted by priority, stable for ties.</summary>
        public IComponent[]? Components;
        /// <summary>The cached <see cref="IComponent.Wants"/> of each component.</summary>
        public EventMask[]? Wants;
        public BodyDesc? Body;
        /// <summary>The <see cref="ShapeDesc.BoundingRadius"/> of the body's shape, or 0 without a body. Kept from the spawn, because it costs a pass over the shape.</summary>
        public float BodyBoundingRadius;
        /// <summary>
        /// The <see cref="ShapeDesc.BoundingRadius"/> of the shape inside the body, which a frame owner's interior body
        /// has: <see cref="BodyDesc.InteriorShape"/>, or <see cref="BodyDesc.Shape"/> when it has none. 0 without a body.
        /// Kept from the spawn, as <see cref="BodyBoundingRadius"/> is.
        /// </summary>
        public float InteriorBoundingRadius;
        /// <summary>The frame this entity is, if any.</summary>
        public FrameHandle OwnedFrame;
    }

    /// <summary>The table's bookkeeping for one chunk. During a tick, only the worker that runs the chunk writes it.</summary>
    private sealed class ChunkBook
    {
        public readonly List<uint> DirtySlots = new();
        /// <summary>
        /// The entities that wrote an intent this tick, in the order they first wrote it. Handles, not slots: a writer
        /// that dies before the swap must not hand its place to the entity that takes its slot.
        /// </summary>
        public readonly List<Entity> IntentWriters = new();
        /// <summary>Free slots reserved for this chunk's spawns, in a deterministic order.</summary>
        public readonly Queue<uint> SpawnPool = new();
        /// <summary>
        /// During a <see cref="CopyDirtyRows"/> on the workers: the entities of this chunk that the copy lists as moved,
        /// in the chunk's dirty order. The copy joins them to the moved list in chunk order, and empties this list.
        /// </summary>
        public readonly List<Entity> Moved = new();
        /// <summary>
        /// During a <see cref="CopyDirtyRows"/> on the workers: the slots of this chunk whose component flags the copy
        /// must still bring forward, in the chunk's dirty order. Joined and emptied like <see cref="Moved"/>.
        /// </summary>
        public readonly List<uint> ComponentCopies = new();
        public int LiveCount;
        public int SpawnsThisTick;
        public int LastSpawnCount;
    }

    /// <summary>What the table needs from a column. Private, so that only the table can grow or clear one.</summary>
    private interface IColumn
    {
        void Grow(int capacity);
        void Clear(uint slot);
        bool IsClear(int slot);
    }

    /// <summary>
    /// The table's own per-slot storage. The type is private, so only the table indexes it by slot, after it has checked
    /// the handle.
    /// </summary>
    private sealed class SlotColumn<T> : IColumn
    {
        private T[] items;

        public SlotColumn(int capacity) => items = new T[capacity];

        public ref T this[uint slot] => ref items[slot];

        public ref T this[int slot] => ref items[slot];

        /// <summary>A read-only view of every entry, by slot. It does not follow a later growth.</summary>
        public ReadOnlyMemory<T> AsReadOnlyMemory() => items;

        public void Grow(int capacity) => Array.Resize(ref items, capacity);

        public void Clear(uint slot) => items[slot] = default!;

        public bool IsClear(int slot) => IsDefault(in items[slot]);
    }

    /// <summary>
    /// One entry per entity, stored in a subsystem's own type. The entity table grows it with every other per-slot table
    /// and clears an entity's entry when it releases the entity. A ref to an entry stays valid until the table grows,
    /// which happens only outside a tick or at the sync point.
    /// </summary>
    public sealed class Column<T> : IColumn
    {
        private readonly EntityTable table;
        private T[] items;

        internal Column(EntityTable table, int capacity)
        {
            this.table = table;
            items = new T[capacity];
        }

        /// <summary>The entity's entry. Throws <see cref="StaleEntityException"/> for a stale or null handle. Worker-safe.</summary>
        public ref T this[Entity entity] => ref items[table.LiveSlot(entity)];

        /// <summary>
        /// Every entry, indexed by slot (<see cref="Entity.Index"/>), with no generation check. Only for passes that the
        /// column's owner makes over the whole column, such as the router's counting sort; to reach one entity's entry,
        /// use the indexer, which checks the handle. Entries of slots that are not live are clear. The span does not
        /// follow a later growth.
        /// </summary>
        public Span<T> AsSpan() => items;

        void IColumn.Grow(int capacity) => Array.Resize(ref items, capacity);

        void IColumn.Clear(uint slot) => items[slot] = default!;

        bool IColumn.IsClear(int slot) => IsDefault(in items[slot]);
    }

    /// <summary>
    /// The live entities in a range of slots, in slot order, as handles. Use it with <c>foreach</c>; it allocates
    /// nothing. It reads each slot's state when it gets there, so it never yields a dead entity.
    /// </summary>
    public struct LiveEntityEnumerator
    {
        private readonly EntityTable? table;
        private readonly int end;
        private int next;
        private Entity current;

        /// <summary>The live entities among <paramref name="count"/> slots from <paramref name="start"/>.</summary>
        internal LiveEntityEnumerator(EntityTable table, int start, int count)
        {
            this.table = table;
            next = start;
            end = start + count;
            current = default;
        }

        public readonly LiveEntityEnumerator GetEnumerator() => this;

        public readonly Entity Current => current;

        public bool MoveNext()
        {
            while (next < end)
            {
                int slot = next++;
                if (table!.state[slot] == SlotState.Live)
                {
                    current = new Entity((uint)slot, table.generation[slot]);
                    return true;
                }
            }
            return false;
        }
    }

    private readonly int chunkSize;
    private readonly List<IColumn> columns = new();
    private readonly List<Action<Entity>> releaseHooks = new();

    private int capacity;
    // Slots ever handed out. During gameplay it grows only through the spawn fallback, atomically and past the tables;
    // EndGameplay grows the tables over those slots.
    private int slotCount;
    private int slotCountAtTickStart;
    private int liveCount;
    // Reserved slots that were handed out and are not live yet: not in any spawn pool.
    private int reservationsOut;
    private bool gameplayRunning;
    private uint[] generation = [];
    private SlotState[] state = [];
    private readonly Queue<uint> freeSlots = new();
    private ChunkState[] chunks = [];
    private ChunkBook[] books = [];
    // The slots that are live with a body.
    private readonly BodySlots bodySlots = new();
    // The workers that copy the rows that gameplay wrote, or null to copy them on the sync thread, and the fewest such rows
    // for which they do.
    private readonly ParallelOptions? copyParallelism;
    private readonly int parallelCopyThreshold;

    private readonly SlotColumn<SlotFlags> flags;
    private readonly SlotColumn<Record> records;
    private readonly SlotColumn<EventMask> masks;
    private readonly SlotColumn<TransformRow> frontTransform;
    private readonly SlotColumn<TransformRow> backTransform;
    private readonly SlotColumn<RenderData> frontRender;
    private readonly SlotColumn<RenderData> backRender;
    private readonly SlotColumn<PhysicsIntent> frontIntent;
    private readonly SlotColumn<PhysicsIntent> backIntent;
    private readonly SlotColumn<ComponentFlags> frontComponents;
    private readonly SlotColumn<ComponentFlags> backComponents;
    private readonly SlotColumn<BodyCollision> frontCollision;
    private readonly SlotColumn<BodyCollision> backCollision;
    // The live entities by the types in their front component flags.
    private readonly ComponentIndex componentIndex = new();

    private readonly RefList<Entity> frontIntentEntities = new(64);
    // Set when a live slot is released after the intent swap: the intent list may name it until the list is next read.
    private bool intentEntitiesMayHoldDead;
    // Back rows that the sync point changed. The chunk books hold the rows that gameplay changed.
    private readonly List<uint> syncDirty = new();
    // Entities whose front transform changed, or that became live, since the last TakeMoved; and the list that the last
    // TakeMoved handed out, kept until the next call. An entity that died since it was listed stays listed until then.
    private List<Entity> moved = new();
    private List<Entity> takenMoved = new();

    /// <param name="chunkSize">Entity slots per gameplay chunk.</param>
    /// <param name="initialCapacity">Slots to allocate up front.</param>
    /// <param name="copyParallelism">
    /// The workers on which <see cref="CopyDirtyRows"/> copies the rows that gameplay wrote, or null to copy every row on
    /// the calling thread.
    /// </param>
    /// <param name="parallelCopyThreshold">
    /// The fewest rows that gameplay wrote in a tick for which <see cref="CopyDirtyRows"/> copies them on the workers. 1 or
    /// more.
    /// </param>
    public EntityTable(int chunkSize, int initialCapacity, ParallelOptions? copyParallelism = null,
        int parallelCopyThreshold = DefaultParallelCopyThreshold)
    {
        Contract.Requires(chunkSize > 0 && initialCapacity >= 0 && parallelCopyThreshold >= 1);
        this.chunkSize = chunkSize;
        this.copyParallelism = copyParallelism;
        this.parallelCopyThreshold = parallelCopyThreshold;
        flags = AddSlotColumn<SlotFlags>();
        records = AddSlotColumn<Record>();
        masks = AddSlotColumn<EventMask>();
        frontTransform = AddSlotColumn<TransformRow>();
        backTransform = AddSlotColumn<TransformRow>();
        frontRender = AddSlotColumn<RenderData>();
        backRender = AddSlotColumn<RenderData>();
        frontIntent = AddSlotColumn<PhysicsIntent>();
        backIntent = AddSlotColumn<PhysicsIntent>();
        frontComponents = AddSlotColumn<ComponentFlags>();
        backComponents = AddSlotColumn<ComponentFlags>();
        frontCollision = AddSlotColumn<BodyCollision>();
        backCollision = AddSlotColumn<BodyCollision>();
        Grow(initialCapacity);
    }

    /// <summary>Entity slots allocated in every column.</summary>
    public int Capacity => capacity;

    /// <summary>Slots ever handed out that the tables cover. Every slot at or past it is fresh. Worker-safe.</summary>
    public int SlotCount => Math.Min(Volatile.Read(ref slotCount), capacity);

    /// <summary>Entities that are live now.</summary>
    public int LiveCount => liveCount;

    /// <summary>True during the gameplay part of a tick: from <see cref="BeginTick"/> to <see cref="EndGameplay"/>. Worker-safe.</summary>
    public bool GameplayRunning => gameplayRunning;

    /// <summary>
    /// The chunks of the slots handed out so far, in chunk order: what each chunk's entities produced during the tick.
    /// Worker-safe.
    /// </summary>
    public ReadOnlySpan<ChunkState> Chunks => chunks.AsSpan(0, ChunkCountFor(SlotCount));

    /// <summary>
    /// Adds a column with one entry per entity. <paramref name="release"/>, if given, lets the owner let go of an entity
    /// when it is released: it gets the entity's handle while the entity is still live, before any column entry of the
    /// entity is cleared. Release hooks run in the order their columns were added. Outside a tick only.
    /// </summary>
    public Column<T> AddColumn<T>(Action<Entity>? release = null)
    {
        Contract.Requires(!gameplayRunning);
        var column = new Column<T>(this, capacity);
        columns.Add(column);
        if (release != null)
            releaseHooks.Add(release);
        return column;
    }

    // ---- Live entities ----------------------------------------------------------------------------------------------

    /// <summary>Every live entity, in slot order. Worker-safe during gameplay, when no entity is released or made live.</summary>
    public LiveEntityEnumerator LiveEntities => new(this, 0, SlotCount);

    /// <summary>
    /// The live entities whose slots are in the range from <paramref name="start"/>, <paramref name="count"/> slots long,
    /// in slot order. For passes that split the table into parts, such as the render builder's parallel slices.
    /// </summary>
    public LiveEntityEnumerator LiveEntitiesIn(int start, int count)
    {
        Contract.Requires(start >= 0 && count >= 0);
        return new LiveEntityEnumerator(this, start, Math.Max(0, Math.Min(count, SlotCount - start)));
    }

    /// <summary>
    /// The live entities with a body, in slot order. It costs a step per 64 slots and one per body, so a pass over the
    /// bodies does not pay for the entities without one. Worker-safe during gameplay, when no entity is released or made
    /// live.
    /// </summary>
    public BodyEntityEnumerator BodyEntities => new(this);

    /// <summary>The live entities of one gameplay chunk, in slot order. Worker-safe during gameplay.</summary>
    public LiveEntityEnumerator LiveEntitiesOfChunk(int chunk)
    {
        if (books[chunk].LiveCount == 0)
            return default;
        int start = chunk * chunkSize;
        return new LiveEntityEnumerator(this, start, Math.Min(chunkSize, SlotCount - start));
    }

    // ---- The tick -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts the gameplay part of a tick. Reserves free slots for each chunk with live entities, in chunk order, so
    /// that spawns during the tick get the same handles in every run. A chunk with no live entities returns its pool.
    /// The component index applies its queued changes, so that the workers only read it.
    /// </summary>
    /// <param name="spawnPoolSize">The smallest pool; a chunk that spawned more last tick gets twice that many.</param>
    public void BeginTick(int spawnPoolSize)
    {
        Contract.Requires(!gameplayRunning);
        componentIndex.Settle();
        int count = ChunkCountFor(slotCount);
        for (int c = 0; c < count; c++)
        {
            ChunkBook book = books[c];
            if (book.LiveCount == 0 || spawnPoolSize == 0)
            {
                while (book.SpawnPool.TryDequeue(out uint slot))
                {
                    state[slot] = SlotState.Free;
                    freeSlots.Enqueue(slot);
                }
                continue;
            }
            int target = Math.Min(Math.Max(spawnPoolSize, book.LastSpawnCount * 2), MaxSpawnPool);
            while (book.SpawnPool.Count < target)
                book.SpawnPool.Enqueue(AllocateSlot());
        }
        slotCountAtTickStart = slotCount;
        gameplayRunning = true;
    }

    /// <summary>
    /// Ends the gameplay part of the tick: the tables grow over the slots that spawns took past their chunk's pool.
    /// Returns how many spawns did that. Until this runs, the sync point cannot use the tables.
    /// </summary>
    public int EndGameplay()
    {
        Contract.Requires(gameplayRunning);
        gameplayRunning = false;
        int end = slotCount;
        int fallback = end - slotCountAtTickStart;
        if (fallback > 0)
        {
            Grow(end);
            for (int slot = slotCountAtTickStart; slot < end; slot++)
            {
                generation[slot] = 1;
                state[slot] = SlotState.Reserved;
            }
        }
        for (int c = 0; c < ChunkCountFor(end); c++)
            reservationsOut += books[c].SpawnsThisTick;
        return fallback;
    }

    /// <summary>
    /// Ends the tick: every chunk's lists of this tick start empty for the next one. <see cref="SwapIntents"/> must have
    /// run since the intents of this tick were written: it is what empties the lists of intent writers and clears the
    /// writers' flags, which must change together.
    /// </summary>
    public void EndTick()
    {
        Contract.Requires(!gameplayRunning);
        int count = ChunkCountFor(slotCount);
        for (int c = 0; c < count; c++)
        {
            chunks[c].EndTick();
            ChunkBook book = books[c];
            Contract.Requires(book.IntentWriters.Count == 0);
            book.DirtySlots.Clear();
            book.LastSpawnCount = book.SpawnsThisTick;
            book.SpawnsThisTick = 0;
        }
    }

    // ---- The lifecycle ----------------------------------------------------------------------------------------------

    /// <summary>
    /// True if the handle names a live entity: its slot is live and has the handle's generation. False for a stale or
    /// null handle. Worker-safe.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Entity entity)
    {
        uint slot = entity.Index;
        uint[] generations = generation;
        // A live slot's generation is never 0, so the null handle fails one of the two tests.
        return slot < (uint)generations.Length && generations[slot] == entity.Generation && state[slot] == SlotState.Live;
    }

    /// <summary>Reserves a slot for a spawn outside a tick: from the free list, or a fresh one.</summary>
    public Entity Reserve()
    {
        Contract.Requires(!gameplayRunning);
        uint slot = AllocateSlot();
        reservationsOut++;
        return new Entity(slot, generation[slot]);
    }

    /// <summary>
    /// Reserves a slot for a spawn by the running entity <paramref name="spawner"/>, from its chunk's pool, so that the
    /// handle is final and the same in every run. Worker-safe for the running entity. If the pool ran dry, a fresh slot
    /// past the high-water mark is taken atomically; which chunk gets which slot then depends on thread timing, and the
    /// tables cover the slot from <see cref="EndGameplay"/> on. Throws <see cref="StaleEntityException"/> for a stale
    /// spawner.
    /// </summary>
    public Entity ReserveForSpawn(Entity spawner)
    {
        Contract.Requires(gameplayRunning);
        ChunkBook book = books[LiveSlot(spawner) / chunkSize];
        uint slot, gen;
        if (book.SpawnPool.TryDequeue(out slot))
        {
            gen = generation[slot];
        }
        else
        {
            slot = (uint)(Interlocked.Increment(ref slotCount) - 1);
            gen = 1;
        }
        book.SpawnsThisTick++;
        return new Entity(slot, gen);
    }

    /// <summary>
    /// Makes a reserved entity live: writes both copies of every row and the cached mask, and lists the entity in the
    /// component index. The entity runs its first tick after the next sync point. Throws
    /// <see cref="InvalidOperationException"/> for a handle to a live entity, which is current but names no reservation,
    /// and <see cref="StaleEntityException"/> for any other handle that does not name its slot's current reservation. A
    /// component type that has no id throws as <see cref="ComponentTypes.IdOf"/> does, before anything changes.
    /// </summary>
    public void Activate(Entity entity, in TransformRow row, in RenderData render, IComponent[] components, EventMask[] wants, BodyDesc? body, FrameHandle ownedFrame)
    {
        Contract.Requires(!gameplayRunning);
        if (!IsCurrentReservation(entity))
        {
            if (IsAlive(entity))
                throw new InvalidOperationException($"{entity} is live, so the handle does not name a reservation to activate.");
            StaleEntityException.Throw(entity);
        }
        uint slot = entity.Index;
        Contract.Requires(components.Length == wants.Length);
        ComponentFlags types = ComponentFlags.Of(components);
        BodyCollision collision = body is null ? default : new BodyCollision(body.CollisionGroups, body.CollisionMask);
        float radius = body?.Shape.BoundingRadius() ?? 0;
        records[slot] = new Record
        {
            Components = components, Wants = wants, Body = body, BodyBoundingRadius = radius,
            InteriorBoundingRadius = body?.InteriorShape is { } interior ? interior.BoundingRadius() : radius, OwnedFrame = ownedFrame,
        };
        if (body != null)
            bodySlots.Add(slot);
        frontTransform[slot] = row;
        backTransform[slot] = row;
        frontRender[slot] = render;
        backRender[slot] = render;
        frontComponents[slot] = types;
        backComponents[slot] = types;
        frontCollision[slot] = collision;
        backCollision[slot] = collision;
        componentIndex.Add(entity, types);
        masks[slot] = MaskOf(wants);
        flags[slot] = SlotFlags.FirstTick;
        state[slot] = SlotState.Live;
        reservationsOut--;
        liveCount++;
        books[slot / chunkSize].LiveCount++;
        MarkMoved(slot, moved);
    }

    /// <summary>
    /// Ends a live entity or a reservation: the slot returns to the free list with a new generation, so every handle to
    /// it is stale. For a live entity, each column's owner first lets go of it, then every column entry of its slot is
    /// cleared. Returns false, and does nothing, if the handle names neither: the entity or reservation already ended,
    /// or the handle is null.
    /// </summary>
    public bool Release(Entity entity)
    {
        Contract.Requires(!gameplayRunning);
        if (!IsCurrent(entity))
            return false;
        uint slot = entity.Index;
        if (state[slot] == SlotState.Live)
        {
            foreach (Action<Entity> release in releaseHooks)
                release(entity);
            componentIndex.Remove(entity, frontComponents[slot]);
            bodySlots.Remove(slot);
            foreach (IColumn column in columns)
                column.Clear(slot);
            liveCount--;
            books[slot / chunkSize].LiveCount--;
            intentEntitiesMayHoldDead = true;
        }
        else
        {
            reservationsOut--;
        }
        uint next = generation[slot] + 1;
        generation[slot] = next == 0 ? 1 : next;
        state[slot] = SlotState.Free;
        freeSlots.Enqueue(slot);
        return true;
    }

    /// <summary>True on the entity's first run, once. Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe for the running entity.</summary>
    public bool TakeFirstTick(Entity entity)
    {
        ref SlotFlags f = ref flags[LiveSlot(entity)];
        if ((f & SlotFlags.FirstTick) == 0)
            return false;
        f &= ~SlotFlags.FirstTick;
        return true;
    }

    // ---- Components, body and owned frame -------------------------------------------------------------------------

    /// <summary>
    /// True if the entity accepts an event type: the OR of its components' wants contains it. Every mask contains
    /// <see cref="DestroyEvent"/>. Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Accepts(Entity entity, int type) => masks[LiveSlot(entity)].Contains(type);

    /// <summary>The entity's components in pipeline order. Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe.</summary>
    public ReadOnlySpan<IComponent> Components(Entity entity) => records[LiveSlot(entity)].Components;

    /// <summary>
    /// The cached wants of each component, in the order of <see cref="Components"/>. Throws
    /// <see cref="StaleEntityException"/> for a stale handle. Worker-safe.
    /// </summary>
    public ReadOnlySpan<EventMask> ComponentWants(Entity entity) => records[LiveSlot(entity)].Wants;

    /// <summary>
    /// The physics body, or null for an entity without one. Throws <see cref="StaleEntityException"/> for a stale handle,
    /// so null always means "no body". Worker-safe.
    /// </summary>
    public BodyDesc? Body(Entity entity) => records[LiveSlot(entity)].Body;

    /// <summary>
    /// The description that physics creates the entity's body again from, or null for an entity without a body: the
    /// description of its spawn with the collision groups and mask of the back collision row, which a
    /// <see cref="IntentFlags.SetCollision"/> may have changed. After <see cref="ApplyCollisionIntents"/>, the back row holds
    /// the groups and mask that the body has in physics. It allocates only when they differ from the spawn's. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public BodyDesc? BodyToRecreate(Entity entity)
    {
        uint slot = LiveSlot(entity);
        if (records[slot].Body is not { } body)
            return null;
        ref readonly BodyCollision collision = ref backCollision[slot];
        return collision.Groups == body.CollisionGroups && collision.Mask == body.CollisionMask
            ? body
            : body with { CollisionGroups = collision.Groups, CollisionMask = collision.Mask };
    }

    /// <summary>
    /// The <see cref="ShapeDesc.BoundingRadius"/> of the body's shape, kept from the spawn, or 0 for an entity without a
    /// body. Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe.
    /// </summary>
    public float BodyBoundingRadius(Entity entity) => records[LiveSlot(entity)].BodyBoundingRadius;

    /// <summary>
    /// The <see cref="ShapeDesc.BoundingRadius"/> of the shape inside the body, kept from the spawn:
    /// <see cref="BodyDesc.InteriorShape"/>, or <see cref="BodyDesc.Shape"/> when it has none. 0 for an entity without a
    /// body. Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe.
    /// </summary>
    public float BodyInteriorBoundingRadius(Entity entity) => records[LiveSlot(entity)].InteriorBoundingRadius;

    /// <summary>The frame this entity is, or null. Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe.</summary>
    public FrameHandle OwnedFrame(Entity entity) => records[LiveSlot(entity)].OwnedFrame;

    /// <summary>
    /// Inserts a component after every component with the same or lower priority, and updates the mask and the back
    /// component flags, which the next <see cref="CopyDirtyRows"/> copies forward. Throws
    /// <see cref="StaleEntityException"/> for a stale handle, <see cref="InvalidOperationException"/> if the entity
    /// already has this instance, and as <see cref="ComponentTypes.IdOf"/> does for a type that has no id, before
    /// anything changes. Worker-safe for the running entity, right after its run.
    /// </summary>
    public void AddComponent(Entity entity, IComponent component)
    {
        uint slot = LiveSlot(entity);
        ref Record rec = ref records[slot];
        IComponent[] old = rec.Components!;
        foreach (IComponent existing in old)
            if (ReferenceEquals(existing, component))
                throw new InvalidOperationException($"{entity} already has this {component.GetType().Name} instance.");
        int priority = component.Priority;
        int at = old.Length;
        while (at > 0 && old[at - 1].Priority > priority)
            at--;
        EventMask[] oldWants = rec.Wants!;
        IComponent[] components = [.. old.AsSpan(0, at), component, .. old.AsSpan(at)];
        EventMask[] wants = [.. oldWants.AsSpan(0, at), component.Wants, .. oldWants.AsSpan(at)];
        int typeId = ComponentTypes.IdOf(component.GetType());
        rec.Components = components;
        rec.Wants = wants;
        masks[slot] = MaskOf(wants);
        backComponents[slot].Add(typeId);
        MarkDirty(slot, SlotFlags.ComponentsDirty);
    }

    /// <summary>
    /// Removes a component instance and updates the mask and the back component flags, which the next
    /// <see cref="CopyDirtyRows"/> copies forward. Returns false, and does nothing, if the entity does not have it,
    /// which includes a stale handle: a dead entity has no components. Worker-safe for the running entity, right after
    /// its run.
    /// </summary>
    public bool RemoveComponent(Entity entity, IComponent component)
    {
        if (!IsAlive(entity))
            return false;
        uint slot = entity.Index;
        ref Record rec = ref records[slot];
        IComponent[] old = rec.Components!;
        int at = Array.FindIndex(old, c => ReferenceEquals(c, component));
        if (at < 0)
            return false;
        EventMask[] oldWants = rec.Wants!;
        rec.Components = [.. old.AsSpan(0, at), .. old.AsSpan(at + 1)];
        rec.Wants = [.. oldWants.AsSpan(0, at), .. oldWants.AsSpan(at + 1)];
        masks[slot] = MaskOf(rec.Wants);
        // Another instance of the same type keeps the type in the flags.
        backComponents[slot] = ComponentFlags.Of(rec.Components);
        MarkDirty(slot, SlotFlags.ComponentsDirty);
        return true;
    }

    /// <summary>
    /// The front component flags: the types of the entity's components at the end of the previous tick. Throws
    /// <see cref="StaleEntityException"/> for a stale handle. Worker-safe.
    /// </summary>
    public ref readonly ComponentFlags FrontComponents(Entity entity) => ref frontComponents[LiveSlot(entity)];

    /// <summary>
    /// The live entities whose front component flags hold the type <paramref name="typeId"/>, in slot order. Worker-safe
    /// during gameplay, when the index is settled.
    /// </summary>
    public ReadOnlySpan<Entity> WithComponent(int typeId)
    {
        // A read of a set with queued changes merges them, which races with a read on another worker.
        Contract.Requires(!gameplayRunning || componentIndex.IsSettled);
        return componentIndex.With(typeId);
    }

    /// <summary>
    /// The front collision row: the collision groups and mask that physics used in the step of the previous tick, or the
    /// default for an entity without a body. Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe.
    /// </summary>
    public ref readonly BodyCollision FrontCollision(Entity entity) => ref frontCollision[LiveSlot(entity)];

    // ---- Rows -----------------------------------------------------------------------------------------------------
    // A ref cannot say "no entity", so every row accessor throws for a stale handle.

    /// <summary>
    /// The front transform row: the state at the end of the previous tick. Throws <see cref="StaleEntityException"/>
    /// for a stale handle. Worker-safe.
    /// </summary>
    public ref readonly TransformRow FrontRow(Entity entity) => ref frontTransform[LiveSlot(entity)];

    /// <summary>
    /// The back transform row, which the tick and the sync point are building. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public ref readonly TransformRow BackRow(Entity entity) => ref backTransform[LiveSlot(entity)];

    /// <summary>The front render row. Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe.</summary>
    public ref readonly RenderData FrontRender(Entity entity) => ref frontRender[LiveSlot(entity)];

    /// <summary>
    /// The running entity's back pose, for writing. The entity must have no body: physics owns those transforms.
    /// Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe for the running entity.
    /// </summary>
    public ref DPose WritableTransform(Entity entity)
    {
        uint slot = LiveSlot(entity);
        Contract.Requires(gameplayRunning && records[slot].Body == null);
        MarkDirty(slot, SlotFlags.TransformDirty | SlotFlags.GameplayWroteTransform);
        return ref backTransform[slot].Pose;
    }

    /// <summary>
    /// The running entity's back render row, for writing. Throws <see cref="StaleEntityException"/> for a stale handle.
    /// Worker-safe for the running entity.
    /// </summary>
    public ref RenderData WritableRender(Entity entity)
    {
        uint slot = LiveSlot(entity);
        Contract.Requires(gameplayRunning);
        MarkDirty(slot, SlotFlags.RenderDirty);
        return ref backRender[slot];
    }

    /// <summary>
    /// The running entity's intent for the next physics step, for writing. The entity must have a body. The intent names
    /// the frame that the entity is in during the tick, which its poses, velocities and directions are relative to. Throws
    /// <see cref="StaleEntityException"/> for a stale handle. Worker-safe for the running entity.
    /// </summary>
    public ref PhysicsIntent WritableIntent(Entity entity)
    {
        uint slot = LiveSlot(entity);
        Contract.Requires(gameplayRunning && records[slot].Body != null);
        ref SlotFlags f = ref flags[slot];
        if ((f & SlotFlags.IntentRegistered) == 0)
        {
            f |= SlotFlags.IntentRegistered;
            books[slot / chunkSize].IntentWriters.Add(entity);
            backIntent[slot].Frame = frontTransform[slot].Frame;
        }
        return ref backIntent[slot];
    }

    /// <summary>
    /// The intent of <paramref name="entity"/> that the next physics step applies, for the sync point after
    /// <see cref="SwapIntents"/> and for a change outside a tick: its front intent, which is empty when the entity wrote
    /// none. Throws <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public ref PhysicsIntent NextStepIntent(Entity entity)
    {
        uint slot = LiveSlot(entity);
        Contract.Requires(!gameplayRunning && (flags[slot] & SlotFlags.IntentRegistered) == 0);
        return ref frontIntent[slot];
    }

    /// <summary>
    /// The intent written so far during this tick. Throws <see cref="StaleEntityException"/> for a stale handle.
    /// Worker-safe for the running entity.
    /// </summary>
    public PhysicsIntent PendingIntent(Entity entity) => backIntent[LiveSlot(entity)];

    /// <summary>True if gameplay wrote the entity's transform during this tick. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public bool GameplayWroteTransform(Entity entity) => (flags[LiveSlot(entity)] & SlotFlags.GameplayWroteTransform) != 0;

    /// <summary>
    /// Writes a live entity's back transform row, outside the gameplay part of a tick. The row is copied forward at the
    /// next <see cref="CopyDirtyRows"/>, unless it is equal bit for bit to the row it replaces. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public void SetBackTransform(Entity entity, in TransformRow row)
    {
        uint slot = LiveSlot(entity);
        Contract.Requires(!gameplayRunning);
        ref TransformRow back = ref backTransform[slot];
        if (BitEquals(back, row))
            return;
        back = row;
        MarkDirty(slot, SlotFlags.TransformDirty);
    }

    /// <summary>
    /// Publishes the collision groups and masks that the front intents set, which the physics step that just ended
    /// applied: each entity whose front intent has <see cref="IntentFlags.SetCollision"/> gets them as its back collision
    /// row, which the next <see cref="CopyDirtyRows"/> copies forward. Outside the gameplay part of a tick, before
    /// <see cref="SwapIntents"/> replaces the front intents.
    /// </summary>
    public void ApplyCollisionIntents()
    {
        Contract.Requires(!gameplayRunning);
        foreach (Entity entity in IntentEntities.Span)
        {
            uint slot = LiveSlot(entity);
            ref readonly PhysicsIntent intent = ref frontIntent[slot];
            if ((intent.Flags & IntentFlags.SetCollision) == 0)
                continue;
            Contract.Assert(records[slot].Body != null);
            backCollision[slot] = new BodyCollision(intent.CollisionGroups, intent.CollisionMask);
            MarkDirty(slot, SlotFlags.CollisionDirty);
        }
    }

    // ---- The swap -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The intents that the next physics step reads, indexed by slot (<see cref="Entity.Index"/>), the layout that the
    /// physics seam in Core defines. Only the entries of the entities in <see cref="IntentEntities"/> matter.
    /// </summary>
    public ReadOnlyMemory<PhysicsIntent> FrontIntents => frontIntent.AsReadOnlyMemory();

    /// <summary>
    /// The entities whose intent the next physics step applies, in the order they wrote it. Never a dead entity: one that
    /// died since the swap leaves the list when the list is read.
    /// </summary>
    public ReadOnlyMemory<Entity> IntentEntities
    {
        get
        {
            if (intentEntitiesMayHoldDead)
            {
                Span<Entity> entities = frontIntentEntities.AsSpan();
                int write = 0;
                foreach (Entity entity in entities)
                    if (IsAlive(entity))
                        entities[write++] = entity;
                frontIntentEntities.Truncate(write);
                intentEntitiesMayHoldDead = false;
            }
            return frontIntentEntities.AsMemory();
        }
    }

    /// <summary>
    /// Makes this tick's intents the input of the next physics step and clears the back intents. With
    /// <paramref name="keep"/> false, as with no physics backend, the intents are dropped.
    /// </summary>
    public void SwapIntents(bool keep)
    {
        Contract.Requires(!gameplayRunning);
        foreach (Entity entity in frontIntentEntities.AsSpan())
            frontIntent[entity.Index] = default;
        frontIntentEntities.Clear();
        intentEntitiesMayHoldDead = false;

        int count = ChunkCountFor(slotCount);
        for (int c = 0; c < count; c++)
        {
            foreach (Entity writer in books[c].IntentWriters)
            {
                // Releasing a writer cleared its slot, intent and flag included. An entity that took the slot since
                // then wrote nothing in this tick, so the slot is left as it is.
                if (!IsAlive(writer))
                    continue;
                uint slot = writer.Index;
                flags[slot] &= ~SlotFlags.IntentRegistered;
                if (keep)
                {
                    frontIntent[slot] = backIntent[slot];
                    frontIntentEntities.Add(writer);
                }
                backIntent[slot] = default;
            }
            books[c].IntentWriters.Clear();
        }
    }

    /// <summary>
    /// Copies every dirty back row forward: the rows that gameplay wrote, in chunk order, then the rows that the sync
    /// point wrote. Rows that did not change cost nothing. The entities whose front transform changed join the moved list
    /// in that order, and the component index takes the changed component flags in that order.
    /// </summary>
    /// <remarks>
    /// With workers, and at least the threshold of rows that gameplay wrote (see the constructor), the workers copy those
    /// rows, a chunk per work item. A chunk's dirty list holds only the chunk's own slots, so the workers write disjoint
    /// rows and flags. What the copy on one thread appends to the moved list and the component flag copies waits in each
    /// chunk's book instead, and joins them here in chunk order. So both lists end in the order of the copy on one thread,
    /// whatever order the workers ran in, and the table ends the same bit for bit. The rows that the sync point wrote stay
    /// on this thread: they follow the gameplay rows, and the component index, which the flag copies change, is one shared
    /// structure.
    /// </remarks>
    public void CopyDirtyRows()
    {
        Contract.Requires(!gameplayRunning);
        int count = ChunkCountFor(slotCount);
        if (copyParallelism != null && GameplayDirtyCount(count) >= parallelCopyThreshold)
        {
            Parallel.For(0, count, copyParallelism, copyChunk ??= CopyChunk);
            for (int c = 0; c < count; c++)
            {
                ChunkBook book = books[c];
                moved.AddRange(book.Moved);
                componentCopies.AddRange(book.ComponentCopies);
                book.Moved.Clear();
                book.ComponentCopies.Clear();
            }
        }
        else
        {
            for (int c = 0; c < count; c++)
                foreach (uint slot in books[c].DirtySlots)
                    CopyRow(slot, moved, componentCopies);
        }
        foreach (uint slot in syncDirty)
            CopyRow(slot, moved, componentCopies);
        syncDirty.Clear();
        foreach (uint slot in componentCopies)
        {
            componentIndex.Change(new Entity(slot, generation[slot]), frontComponents[slot], backComponents[slot]);
            frontComponents[slot] = backComponents[slot];
        }
        componentCopies.Clear();
    }

    /// <summary>
    /// The default of the fewest rows that gameplay wrote in a tick for which <see cref="CopyDirtyRows"/> copies them on
    /// the workers. Measured on an Apple M1 Max (see RowCopyBenchmarks in the benchmarks): the copy on one thread costs
    /// about 9 ns a row; on the workers it runs about 1.75 times as fast, after a fixed cost of about 20 us to hand it to
    /// them. So the workers break even at about 5,000 rows, and win by about 13 percent at 8,192. Below that, a busy
    /// machine made the parallel copy as often slower as faster: 4,096 rows took 57 us against 40 us on one thread, and
    /// the 8,000 rows of the Mixed tick benchmark copied faster on one thread.
    /// </summary>
    internal const int DefaultParallelCopyThreshold = 8192;

    // The copy of one chunk on a worker, kept so that a parallel copy allocates no delegate.
    private Action<int>? copyChunk;
    // The slots whose component flags the running CopyDirtyRows must still copy, in the order of the copy on one thread.
    private readonly List<uint> componentCopies = new();

    /// <summary>The rows that gameplay wrote in one chunk, on a worker of <see cref="CopyDirtyRows"/>, into the chunk's book.</summary>
    private void CopyChunk(int chunk)
    {
        ChunkBook book = books[chunk];
        foreach (uint slot in book.DirtySlots)
            CopyRow(slot, book.Moved, book.ComponentCopies);
    }

    /// <summary>The rows that gameplay wrote in this tick, in the first <paramref name="chunkCount"/> chunks.</summary>
    private int GameplayDirtyCount(int chunkCount)
    {
        int dirty = 0;
        for (int c = 0; c < chunkCount; c++)
            dirty += books[c].DirtySlots.Count;
        return dirty;
    }

    /// <summary>
    /// The live entities whose front transform changed, or that became live, since the last call, each once, in that
    /// order. Never a dead entity: one that died since it was listed is left out. The span is valid until the next call.
    /// </summary>
    public ReadOnlySpan<Entity> TakeMoved()
    {
        (moved, takenMoved) = (takenMoved, moved);
        moved.Clear();
        Span<Entity> taken = CollectionsMarshal.AsSpan(takenMoved);
        int kept = 0;
        foreach (Entity entity in taken)
        {
            // Releasing an entity cleared its slot's flags. A new entity in that slot is listed after it, with its own
            // generation, and clears the flag when its turn comes.
            if (!IsAlive(entity))
                continue;
            flags[entity.Index] &= ~SlotFlags.FrontMoved;
            taken[kept++] = entity;
        }
        CollectionsMarshal.SetCount(takenMoved, kept);
        return CollectionsMarshal.AsSpan(takenMoved);
    }

    // ---- Invariants -----------------------------------------------------------------------------------------------

    /// <summary>Checks every invariant in the class comment. O(capacity). Outside the gameplay part of a tick.</summary>
    internal void AssertInvariants()
    {
        Contract.Invariant(!gameplayRunning);
        Contract.Invariant(slotCount <= capacity && ChunkCountFor(capacity) <= chunks.Length && books.Length == chunks.Length);
        Contract.Invariant(generation.Length == capacity && state.Length == capacity && bodySlots.WordCount == BodySlots.WordsFor(capacity));

        var inFreeList = new bool[capacity];
        foreach (uint slot in freeSlots)
        {
            Contract.Invariant(slot < (uint)slotCount && state[slot] == SlotState.Free && !inFreeList[slot]);
            inFreeList[slot] = true;
        }
        var inIntentList = new bool[capacity];
        foreach (Entity entity in frontIntentEntities.AsSpan())
        {
            // An entity that died since the swap leaves the list when the list is next read.
            Contract.Invariant(IsAlive(entity) || intentEntitiesMayHoldDead);
            if (IsAlive(entity))
            {
                Contract.Invariant(!inIntentList[entity.Index]);
                inIntentList[entity.Index] = true;
            }
        }

        var dirtyListed = new bool[capacity];
        var intentWriterListed = new bool[capacity];
        var pooled = new bool[capacity];
        int pooledCount = 0;
        // The lists of a copy on the workers are empty outside CopyDirtyRows, which joins and empties them.
        Contract.Invariant(componentCopies.Count == 0);
        for (int c = 0; c < books.Length; c++)
        {
            ChunkBook book = books[c];
            Contract.Invariant(book.Moved.Count == 0 && book.ComponentCopies.Count == 0);
            foreach (uint slot in book.DirtySlots)
                dirtyListed[slot] = true;
            foreach (Entity writer in book.IntentWriters)
            {
                Contract.Invariant((int)writer.Index / chunkSize == c);
                // A writer that died since it wrote is skipped by the swap.
                if (!IsAlive(writer))
                    continue;
                Contract.Invariant((flags[writer.Index] & SlotFlags.IntentRegistered) != 0 && !intentWriterListed[writer.Index]);
                intentWriterListed[writer.Index] = true;
            }
            foreach (uint slot in book.SpawnPool)
            {
                Contract.Invariant(state[slot] == SlotState.Reserved && !pooled[slot]);
                pooled[slot] = true;
                pooledCount++;
            }
            int live = 0;
            for (int slot = c * chunkSize, end = Math.Min(slot + chunkSize, capacity); slot < end; slot++)
                if (state[slot] == SlotState.Live)
                    live++;
            Contract.Invariant(book.LiveCount == live);
        }
        foreach (uint slot in syncDirty)
            dirtyListed[slot] = true;
        var movedListed = new bool[capacity];
        foreach (Entity entity in moved)
            if (IsAlive(entity))
                movedListed[entity.Index] = true;

        int liveSlots = 0, reserved = 0, indexed = 0;
        for (int slot = 0; slot < capacity; slot++)
        {
            SlotState s = state[slot];
            SlotFlags f = flags[slot];
            if (slot >= slotCount)
                Contract.Invariant(s == SlotState.Free && generation[slot] == 0);
            else
                Contract.Invariant(generation[slot] != 0);
            if (s == SlotState.Free && slot < slotCount)
                Contract.Invariant(inFreeList[slot]);
            if (s == SlotState.Reserved)
                reserved++;
            Contract.Invariant(bodySlots.Contains((uint)slot) == (s == SlotState.Live && records[slot].Body != null));
            if (s != SlotState.Live)
            {
                foreach (IColumn column in columns)
                    Contract.Invariant(column.IsClear(slot));
                continue;
            }

            liveSlots++;
            ref readonly Record rec = ref records[slot];
            Contract.Invariant(rec.Components != null && rec.Wants != null && rec.Components.Length == rec.Wants.Length);
            Contract.Invariant(masks[slot] == MaskOf(rec.Wants!));
            if ((f & SlotFlags.TransformDirty) == 0)
                Contract.Invariant(BitEquals(frontTransform[slot], backTransform[slot]));
            if ((f & SlotFlags.RenderDirty) == 0)
                Contract.Invariant(BitEquals(frontRender[slot], backRender[slot]));
            if ((f & SlotFlags.ComponentsDirty) == 0)
                Contract.Invariant(frontComponents[slot] == backComponents[slot]);
            if ((f & SlotFlags.CollisionDirty) == 0)
                Contract.Invariant(frontCollision[slot] == backCollision[slot]);
            if ((f & RowsDirty) != 0)
                Contract.Invariant(dirtyListed[slot]);
            Contract.Invariant(backComponents[slot] == ComponentFlags.Of(rec.Components));
            if (rec.Body == null)
                Contract.Invariant(frontCollision[slot] == default && backCollision[slot] == default);
            Contract.Invariant(rec.BodyBoundingRadius == (rec.Body?.Shape.BoundingRadius() ?? 0));
            Contract.Invariant(rec.InteriorBoundingRadius == (rec.Body?.InteriorShape is { } inside ? inside.BoundingRadius() : rec.BodyBoundingRadius));
            var live = new Entity((uint)slot, generation[slot]);
            foreach (int id in frontComponents[slot])
            {
                Contract.Invariant(componentIndex.With(id).BinarySearch(live) >= 0);
                indexed++;
            }
            if ((f & SlotFlags.GameplayWroteTransform) != 0)
                Contract.Invariant((f & SlotFlags.TransformDirty) != 0 && rec.Body == null);
            if ((f & SlotFlags.IntentRegistered) == 0)
                Contract.Invariant(IsDefault(in backIntent[slot]));
            else
                Contract.Invariant(intentWriterListed[slot]);
            if (!inIntentList[slot])
                Contract.Invariant(IsDefault(in frontIntent[slot]));
            if ((f & SlotFlags.FrontMoved) != 0)
                Contract.Invariant(movedListed[slot]);
        }
        Contract.Invariant(liveSlots == liveCount);
        Contract.Invariant(reserved == pooledCount + reservationsOut);
        // Each live entity is in the set of every type of its front flags, so equal totals leave no other entries.
        int listed = 0;
        for (int id = 0; id < ComponentTypes.Capacity; id++)
            listed += componentIndex.With(id).Length;
        Contract.Invariant(listed == indexed);
    }

    // ---- Private --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The slot of a live entity: the one check behind every member that reads or writes an entity's data. Throws
    /// <see cref="StaleEntityException"/> for a stale or null handle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint LiveSlot(Entity entity)
    {
        if (!IsAlive(entity))
            StaleEntityException.Throw(entity);
        return entity.Index;
    }

    /// <summary>
    /// True if the handle names its slot's current reservation or live entity: what <see cref="Release"/> acts on. A
    /// fresh slot has generation 0 and is free, so the null handle never passes.
    /// </summary>
    private bool IsCurrent(Entity entity)
    {
        uint slot = entity.Index;
        return slot < (uint)generation.Length && generation[slot] == entity.Generation && state[slot] != SlotState.Free;
    }

    /// <summary>
    /// True if the handle names its slot's current reservation, and not a live entity: what <see cref="Activate"/> acts
    /// on. The null handle never passes, as with <see cref="IsCurrent"/>.
    /// </summary>
    private bool IsCurrentReservation(Entity entity) => IsCurrent(entity) && state[entity.Index] == SlotState.Reserved;

    private int ChunkCountFor(int slots) => (slots + chunkSize - 1) / chunkSize;

    private SlotColumn<T> AddSlotColumn<T>()
    {
        var column = new SlotColumn<T>(capacity);
        columns.Add(column);
        return column;
    }

    /// <summary>Takes a slot from the free list, or a fresh one, and reserves it.</summary>
    private uint AllocateSlot()
    {
        uint slot;
        if (freeSlots.Count > 0)
        {
            slot = freeSlots.Dequeue();
        }
        else
        {
            slot = (uint)slotCount++;
            Grow(slotCount);
            generation[slot] = 1;
        }
        state[slot] = SlotState.Reserved;
        return slot;
    }

    /// <summary>Grows every per-slot table and column, and the chunks over them. Outside the gameplay part of a tick.</summary>
    private void Grow(int needed)
    {
        if (needed <= capacity)
            return;
        int size = Math.Max(needed, Math.Max(capacity * 2, 64));
        Array.Resize(ref generation, size);
        Array.Resize(ref state, size);
        bodySlots.Grow(size);
        foreach (IColumn column in columns)
            column.Grow(size);
        capacity = size;

        int chunkCount = ChunkCountFor(size);
        if (chunks.Length < chunkCount)
        {
            int old = chunks.Length;
            Array.Resize(ref chunks, chunkCount);
            Array.Resize(ref books, chunkCount);
            for (int i = old; i < chunkCount; i++)
            {
                chunks[i] = new ChunkState(i);
                books[i] = new ChunkBook();
            }
        }
    }

    private static EventMask MaskOf(EventMask[] wants)
    {
        // Destroy is in every mask, so that a Destroy always gets through.
        EventMask mask = EventMask.Of<DestroyEvent>();
        foreach (ref readonly EventMask w in wants.AsSpan())
            mask.UnionWith(w);
        return mask;
    }

    /// <summary>
    /// Sets dirty bits on a slot, and lists it the first time: in its chunk's list during gameplay, where only the worker
    /// of the chunk writes, or in the list of the sync point outside it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkDirty(uint slot, SlotFlags bits)
    {
        ref SlotFlags f = ref flags[slot];
        if ((f & RowsDirty) == 0)
            (gameplayRunning ? books[slot / chunkSize].DirtySlots : syncDirty).Add(slot);
        f |= bits;
    }

    /// <summary>Lists the slot's entity in <paramref name="list"/> unless the moved list or a chunk's list of a parallel copy has it.</summary>
    private void MarkMoved(uint slot, List<Entity> list)
    {
        ref SlotFlags f = ref flags[slot];
        if ((f & SlotFlags.FrontMoved) != 0)
            return;
        f |= SlotFlags.FrontMoved;
        list.Add(new Entity(slot, generation[slot]));
    }

    /// <summary>
    /// Copies the dirty rows of one slot forward and clears its dirty bits. Its entity joins <paramref name="movedTo"/>
    /// when the copy changed its front transform, and its slot joins <paramref name="componentCopiesTo"/> when its component
    /// flags must still be copied, which only the sync thread does. It touches only this slot's rows and flags, so the
    /// workers of a parallel copy run it on disjoint slots, each with its chunk's lists.
    /// </summary>
    private void CopyRow(uint slot, List<Entity> movedTo, List<uint> componentCopiesTo)
    {
        ref SlotFlags f = ref flags[slot];
        SlotFlags dirty = f;
        if ((dirty & RowsDirty) == 0)
            return;
        f = dirty & ~(RowsDirty | SlotFlags.GameplayWroteTransform);
        if ((dirty & SlotFlags.TransformDirty) != 0)
        {
            frontTransform[slot] = backTransform[slot];
            MarkMoved(slot, movedTo);
        }
        if ((dirty & SlotFlags.RenderDirty) != 0)
            frontRender[slot] = backRender[slot];
        if ((dirty & SlotFlags.ComponentsDirty) != 0)
            componentCopiesTo.Add(slot);
        if ((dirty & SlotFlags.CollisionDirty) != 0)
            frontCollision[slot] = backCollision[slot];
    }

    /// <summary>True if two values are equal bit for bit. Unlike <c>==</c>, it tells 0 from -0 and treats NaN as equal to itself.</summary>
    private static bool BitEquals<T>(in T a, in T b) => BytesOf(in a).SequenceEqual(BytesOf(in b));

    /// <summary>True if every byte of <paramref name="value"/> is zero, as in <c>default</c>. References count as bytes.</summary>
    private static bool IsDefault<T>(in T value) => !BytesOf(in value).ContainsAnyExcept((byte)0);

    private static ReadOnlySpan<byte> BytesOf<T>(in T value) =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in value)), Unsafe.SizeOf<T>());
}
