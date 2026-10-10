using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using DestroyEvent = Tertius.Destroy;

namespace Tertius.Simulation;

/// <summary>Counters that describe the last tick.</summary>
/// <param name="LiveEntities">Entities that are live now.</param>
/// <param name="Capacity">Entity slots allocated in the tables.</param>
/// <param name="Frames">Live reference frames.</param>
/// <param name="RoutedEvents">Messages routed at the last sync point, each counted once.</param>
/// <param name="Deliveries">Inbox entries written at the last sync point.</param>
/// <param name="FallbackSpawns">Spawns, since the world was created, that ran out of their chunk's slot pool.</param>
public readonly record struct WorldStats(int LiveEntities, int Capacity, int Frames, int RoutedEvents, int Deliveries, long FallbackSpawns);

/// <summary>
/// The gameplay simulation: entities and their components, the message router, the parallel scheduler, the sync
/// point, the double-buffered snapshot and the render snapshot builder. See ARCHITECTURE.md, "Threads and the tick".
/// </summary>
/// <remarks>
/// Call every member from one thread, the ticking thread, except <see cref="Post"/>, which is thread-safe.
/// Outside <see cref="Tick"/>, <see cref="Spawn(SpawnDesc)"/> and <see cref="Destroy"/> take effect at once.
/// During a tick, components use <see cref="Self"/> instead, and their changes take effect at the sync point.
/// The world does not dispose the physics backend unless <see cref="WorldOptions.DisposePhysics"/> is set.
/// </remarks>
public sealed partial class World : IDisposable
{
    private readonly WorldOptions options;
    private readonly IPhysicsBackend? physics;
    private readonly PhysicsWorker? physicsWorker;
    private readonly PhysicsStepOutput stepOutput = new();
    private readonly PhysicsChanges physicsChanges = new();
    private readonly int chunkSize;
    private readonly ParallelOptions parallelOptions;
    private readonly Action<int> runChunk;
    private readonly ConcurrentQueue<Event> posted = new();
    private readonly List<string> errors = new();
    private readonly List<string> pendingLog = new();
    private bool inTick;
    private bool disposed;
    private long fallbackSpawnTotal;

    // Slot bookkeeping. A slot is Free (in freeSlots), Reserved (handed out for a spawn, or held in a spawn pool) or
    // Live. slotCount counts slots that were ever handed out. During a tick it grows only through the spawn fallback,
    // atomically, and the tables catch up at the sync point.
    private int capacity;
    private int slotCount;
    private int liveCount;
    private uint[] generation = [];
    private SlotState[] slotState = [];
    private SlotFlags[] flags = [];
    private readonly Queue<uint> freeSlots = new();

    // Double-buffered tables. During a tick everyone reads the front rows, and each entity writes only its own back
    // rows. The sync point copies dirty back rows forward, so front and back agree at the start of every tick.
    private TransformRow[] frontTransform = [], backTransform = [];
    private RenderData[] frontRender = [], backRender = [];
    private PhysicsIntent[] frontIntent = [], backIntent = [];
    private EventMask[] masks = [];
    private EntityRecord[] records = [];
    private ChunkState[] chunks = [];

    /// <summary>Creates an empty world. With no physics backend, bodies do not move and their intents are dropped.</summary>
    public World(WorldOptions? options = null, IPhysicsBackend? physics = null)
    {
        this.options = options ?? new WorldOptions();
        this.options.Validate();
        EngineEvents.Register();
        this.physics = physics;
        chunkSize = this.options.ChunkSize;
        DeltaTime = 1f / this.options.TickRate;
        Time = this.options.StartTime;
        parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = this.options.MaxDegreeOfParallelism };
        runChunk = RunChunk;
        renderChunk = BuildRenderChunk;
        EnsureCapacity(this.options.InitialCapacity);
        if (physics != null)
            physicsWorker = new PhysicsWorker(physics, stepOutput);
    }

    /// <summary>The reference frames. Mutate it only through the world; read it freely.</summary>
    public FrameTree Frames { get; } = new();

    /// <summary>Completed ticks. During a tick, the number of the tick that is running.</summary>
    public long TickNumber { get; private set; }

    /// <summary>Simulation time, in seconds. During a tick, the time at the start of the tick.</summary>
    public double Time { get; private set; }

    /// <summary>The fixed time step, in seconds.</summary>
    public float DeltaTime { get; }

    /// <summary>The blueprints that <see cref="Spawn(string, FrameHandle, in DPose)"/> and <see cref="Self.Spawn(string, FrameHandle, in DPose)"/> use.</summary>
    public Blueprints Blueprints { get; } = new();

    /// <summary>The settings the world was created with.</summary>
    public WorldOptions Options => options;

    /// <summary>The physics backend, or null.</summary>
    public IPhysicsBackend? Physics => physics;

    /// <summary>Render snapshots, one per tick. The render thread reads the latest one.</summary>
    public TripleBuffer<RenderSnapshot> RenderSnapshots { get; } = new(static () => new RenderSnapshot());

    /// <summary>The entity whose pose, composed with its <see cref="RenderData.ViewOffset"/>, is the camera.</summary>
    public Entity Camera { get; set; }

    /// <summary>Vertical field of view in radians.</summary>
    public float VerticalFov { get; set; } = MathF.PI / 3;

    /// <summary>The frame whose origin is the sun. If it is null, the sun shines straight down the camera frame's -Y axis.</summary>
    public FrameHandle SunFrame { get; set; }

    /// <summary>Linear color of the sun.</summary>
    public Vector3 SunColor { get; set; } = Vector3.One;

    /// <summary>Intensity of the sun.</summary>
    public float SunIntensity { get; set; } = 1;

    /// <summary>The hemisphere ambient light of the render snapshot.</summary>
    public AmbientLight Ambient { get; set; } = new() { SkyColor = new Vector3(0.08f), GroundColor = new Vector3(0.03f), Intensity = 1 };

    /// <summary>The front view. During a tick it shows the previous tick, like <see cref="Self.Snapshot"/>.</summary>
    public Snapshot Snapshot => new(this);

    /// <summary>Component exceptions and failed engine operations, oldest first. At most <see cref="WorldOptions.MaxErrors"/> entries.</summary>
    public IReadOnlyList<string> Errors => errors;

    /// <summary>Errors and engine notes. Raised on the ticking thread after the tick, never during it.</summary>
    public event Action<string>? Log;

    /// <summary>Counters of the last tick.</summary>
    public WorldStats Stats => new(liveCount, capacity, Frames.Count, lastRoutedEvents, lastDeliveries, fallbackSpawnTotal);

    /// <summary>Forgets every entry of <see cref="Errors"/>.</summary>
    public void ClearErrors() => errors.Clear();

    /// <summary>True if the handle names a live entity: the slot is live and the generation matches.</summary>
    public bool IsAlive(Entity entity) =>
        entity.Generation != 0 && entity.Index < (uint)capacity &&
        slotState[entity.Index] == SlotState.Live && generation[entity.Index] == entity.Generation;

    /// <summary>The entity's pose in its frame, from the front snapshot. Throws for a stale handle.</summary>
    public DPose GetPose(Entity entity) => FrontRow(entity).Pose;

    /// <summary>The frame the entity is in, from the front snapshot. Null for the owner of a root frame. Throws for a stale handle.</summary>
    public FrameHandle GetFrame(Entity entity) => FrontRow(entity).Frame;

    /// <summary>Linear velocity in frame axes, from the front snapshot.</summary>
    public Vector3 GetVelocity(Entity entity) => FrontRow(entity).LinearVelocity;

    /// <summary>Angular velocity in frame axes, from the front snapshot.</summary>
    public Vector3 GetAngularVelocity(Entity entity) => FrontRow(entity).AngularVelocity;

    /// <summary>The render row, from the front snapshot.</summary>
    public RenderData GetRender(Entity entity) => FrontRender(entity);

    /// <summary>The frame that this entity is, or null.</summary>
    public FrameHandle GetOwnedFrame(Entity entity)
    {
        ThrowIfStale(entity);
        return records[entity.Index].OwnedFrame;
    }

    /// <summary>A copy of the entity's components, in pipeline order.</summary>
    public IReadOnlyList<IComponent> GetComponents(Entity entity)
    {
        ThrowIfStale(entity);
        return (IComponent[])ComponentsOf((int)entity.Index).Clone();
    }

    /// <summary>The entity's first component that is a <typeparamref name="T"/>, or null.</summary>
    public T? GetComponent<T>(Entity entity) where T : class, IComponent
    {
        ThrowIfStale(entity);
        return FindComponent<T>(ComponentsOf((int)entity.Index));
    }

    /// <summary>Adds every live entity to <paramref name="results"/>, in slot order.</summary>
    public void CopyLiveEntities(List<Entity> results)
    {
        for (int slot = 0; slot < slotCount && slot < capacity; slot++)
            if (slotState[slot] == SlotState.Live)
                results.Add(new Entity((uint)slot, generation[slot]));
    }

    /// <summary>
    /// Injects a message from outside the simulation, such as host input. Thread-safe. The message joins the next
    /// routing, after the messages of the entities, and its recipients handle it on the tick after that.
    /// </summary>
    public void Post(in Event e)
    {
        if (!e.Target.HasValue)
            throw new ArgumentException("The message target has no value.", nameof(e));
        posted.Enqueue(e);
    }

    /// <summary>Stops the physics thread. Disposes the backend only if <see cref="WorldOptions.DisposePhysics"/> is set.</summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        physicsWorker?.Dispose();
        if (options.DisposePhysics)
            physics?.Dispose();
    }

    internal static T? FindComponent<T>(IComponent[] components) where T : class, IComponent
    {
        foreach (IComponent component in components)
            if (component is T match)
                return match;
        return null;
    }

    internal IComponent[] ComponentsOf(int slot) => records[slot].Components ?? [];

    internal ref readonly TransformRow FrontRow(int slot) => ref frontTransform[slot];

    internal ref readonly TransformRow FrontRow(Entity entity)
    {
        ThrowIfStale(entity);
        return ref frontTransform[entity.Index];
    }

    internal RenderData FrontRender(Entity entity)
    {
        ThrowIfStale(entity);
        return frontRender[entity.Index];
    }

    internal bool HasBody(int slot) => records[slot].Body != null;

    internal bool HasBody(Entity entity)
    {
        ThrowIfStale(entity);
        return records[entity.Index].Body != null;
    }

    internal Entity EntityAt(int slot) => new((uint)slot, generation[slot]);

    private void ThrowIfStale(Entity entity)
    {
        if (!IsAlive(entity))
            ThrowStale(entity);
    }

    [DoesNotReturn]
    private static void ThrowStale(Entity entity) =>
        throw new InvalidOperationException($"{entity} is stale or null: the entity is not live.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private void ThrowIfInTick(string operation)
    {
        ThrowIfDisposed();
        if (inTick)
            throw new InvalidOperationException($"World.{operation} cannot run during a tick. Components use Self instead.");
    }

    private int ChunkCountFor(int slots) => (slots + chunkSize - 1) / chunkSize;

    /// <summary>Grows every per-slot table. Runs only outside a tick or at the sync point.</summary>
    private void EnsureCapacity(int needed)
    {
        if (needed <= capacity)
            return;
        int size = Math.Max(needed, Math.Max(capacity * 2, 64));
        Array.Resize(ref generation, size);
        Array.Resize(ref slotState, size);
        Array.Resize(ref flags, size);
        Array.Resize(ref frontTransform, size);
        Array.Resize(ref backTransform, size);
        Array.Resize(ref frontRender, size);
        Array.Resize(ref backRender, size);
        Array.Resize(ref frontIntent, size);
        Array.Resize(ref backIntent, size);
        Array.Resize(ref masks, size);
        Array.Resize(ref records, size);
        Array.Resize(ref inboxStart, size + 1);
        capacity = size;

        int chunkCount = ChunkCountFor(size);
        if (chunks.Length < chunkCount)
        {
            int old = chunks.Length;
            Array.Resize(ref chunks, chunkCount);
            for (int i = old; i < chunkCount; i++)
                chunks[i] = new ChunkState(i);
        }
    }

    private static EventMask MaskOf(EventMask[] wants)
    {
        EventMask mask = EventMask.Of<DestroyEvent>();
        foreach (ref readonly EventMask w in wants.AsSpan())
            mask.UnionWith(w);
        return mask;
    }

    private void Report(string message, bool isError)
    {
        if (isError)
        {
            if (options.MaxErrors == 0)
            {
                pendingLog.Add(message);
                return;
            }
            if (errors.Count >= options.MaxErrors)
                errors.RemoveRange(0, errors.Count - options.MaxErrors + 1);
            errors.Add(message);
        }
        pendingLog.Add(message);
    }

    private void FlushLog()
    {
        if (pendingLog.Count == 0)
            return;
        Action<string>? log = Log;
        if (log != null)
            foreach (string message in pendingLog)
                log(message);
        pendingLog.Clear();
    }
}
