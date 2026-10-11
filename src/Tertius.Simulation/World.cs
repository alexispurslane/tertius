using System.Diagnostics;
using System.Numerics;

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
/// <para>
/// The world is the orchestrator. The state lives in internal subsystems, each of which keeps its own invariants:
/// <see cref="EntityTable"/> (slots, every per-slot table, the double-buffered rows), <see cref="Router"/> (outboxes
/// to inboxes), <see cref="TopicSets"/> (who hears each topic), <see cref="Attachments"/>, <see cref="SpatialIndex"/>
/// and <see cref="RenderSnapshotBuilder"/>. The world owns the public API, the tick, the order in which the sync point
/// calls the subsystems, and the invariants that span them, which <see cref="AssertInvariants"/> lists.
/// </para>
/// <para>
/// Every member that takes an <see cref="Entity"/> checks the handle on every call, in every build: the subsystems
/// check every handle they are given. A member that reads or writes the entity throws
/// <see cref="StaleEntityException"/> for a stale or null handle; <see cref="Destroy"/>, <see cref="RemoveComponent"/>
/// and <see cref="Unsubscribe"/>, whose goal already holds for a dead entity, return false instead.
/// </para>
/// </remarks>
public sealed partial class World : IDisposable
{
    private readonly WorldOptions options;
    private readonly IPhysicsBackend? physics;
    private readonly PhysicsWorker? physicsWorker;
    private readonly PhysicsStepOutput stepOutput = new();
    private readonly PhysicsChanges physicsChanges = new();
    private readonly ParallelOptions parallelOptions;
    private readonly Action<int> runChunk;
    private readonly List<string> errors = new();
    private readonly List<string> pendingLog = new();
    private bool inTick;
    private bool disposed;
    private long fallbackSpawnTotal;

    private readonly EntityTable entities;
    private readonly TopicSets topics;
    private readonly Router router;
    private readonly Attachments attachments;
    private readonly SpatialIndex spatial;
    private readonly RenderSnapshotBuilder renderBuilder;

    /// <summary>Creates an empty world. With no physics backend, bodies do not move and their intents are dropped.</summary>
    public World(WorldOptions? options = null, IPhysicsBackend? physics = null)
    {
        this.options = options ?? new WorldOptions();
        this.options.Validate();
        EngineEvents.Register();
        this.physics = physics;
        DeltaTime = 1f / this.options.TickRate;
        Time = this.options.StartTime;
        parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = this.options.MaxDegreeOfParallelism };
        runChunk = RunChunk;

        entities = new EntityTable(this.options.ChunkSize, this.options.InitialCapacity);
        // The order of the subsystems is the order in which they let go of a released entity.
        topics = new TopicSets(entities);
        router = new Router(entities, topics);
        attachments = new Attachments(entities);
        spatial = new SpatialIndex(entities, Frames);
        renderBuilder = new RenderSnapshotBuilder(entities, Frames, parallelOptions);
        dyingMarks = entities.AddColumn<bool>();
        if (physics != null)
            physicsWorker = new PhysicsWorker(physics, stepOutput);
    }

    /// <summary>
    /// The reference frames. Read it freely. Outside a tick, a game can give a frame a new motion driver with
    /// <see cref="FrameTree.SetDriver"/>, for example to park a ship, and physics follows at its next step. Make every
    /// other change through the world.
    /// </summary>
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
    public WorldStats Stats => new(entities.LiveCount, entities.Capacity, Frames.Count, router.RoutedCount, router.DeliveryCount, fallbackSpawnTotal);

    /// <summary>Forgets every entry of <see cref="Errors"/>.</summary>
    public void ClearErrors() => errors.Clear();

    /// <summary>True if the handle names a live entity: the slot is live and the generation matches.</summary>
    public bool IsAlive(Entity entity) => entities.IsAlive(entity);

    /// <summary>The entity's pose in its frame, from the front snapshot. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public DPose GetPose(Entity entity) => entities.FrontRow(entity).Pose;

    /// <summary>
    /// The frame the entity is in, from the front snapshot. Null for the owner of a root frame. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public FrameHandle GetFrame(Entity entity) => entities.FrontRow(entity).Frame;

    /// <summary>Linear velocity in frame axes, from the front snapshot. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public Vector3 GetVelocity(Entity entity) => entities.FrontRow(entity).LinearVelocity;

    /// <summary>Angular velocity in frame axes, from the front snapshot. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public Vector3 GetAngularVelocity(Entity entity) => entities.FrontRow(entity).AngularVelocity;

    /// <summary>The render row, from the front snapshot. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public RenderData GetRender(Entity entity) => entities.FrontRender(entity);

    /// <summary>The frame that this entity is, or null. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public FrameHandle GetOwnedFrame(Entity entity) => entities.OwnedFrame(entity);

    /// <summary>A copy of the entity's components, in pipeline order. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public IReadOnlyList<IComponent> GetComponents(Entity entity) => entities.Components(entity).ToArray();

    /// <summary>
    /// The entity's first component that is a <typeparamref name="T"/>, or null if it has none. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public T? GetComponent<T>(Entity entity) where T : class, IComponent => FindComponent<T>(entities.Components(entity));

    /// <summary>Adds every live entity to <paramref name="results"/>, in slot order.</summary>
    public void CopyLiveEntities(List<Entity> results)
    {
        foreach (Entity entity in entities.LiveEntities)
            results.Add(entity);
    }

    /// <summary>
    /// Injects a message from outside the simulation, such as host input. Thread-safe. The message joins the next
    /// routing, after the messages of the entities, and its recipients handle it on the tick after that.
    /// </summary>
    public void Post(in Event e)
    {
        if (!e.Target.HasValue)
            throw new ArgumentException("The message target has no value.", nameof(e));
        router.Post(e);
    }

    /// <summary>
    /// Builds a render snapshot of the current state and publishes it. The sync point does this every tick; call it
    /// yourself to show the world before the first tick. Outside a tick only.
    /// </summary>
    public void PublishRenderSnapshot()
    {
        ThrowIfInTick(nameof(PublishRenderSnapshot));
        BuildRenderSnapshot();
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

    // ---- For Self, BodyHandle and Snapshot ---------------------------------------------------------------------------
    // These views read and write the subsystems directly, through the same checked members that the world uses.

    /// <summary>The entity table, for the views over it.</summary>
    internal EntityTable Entities => entities;

    /// <summary>The attachment chains, for the views over them.</summary>
    internal Attachments Attachments => attachments;

    internal static T? FindComponent<T>(ReadOnlySpan<IComponent> components) where T : class, IComponent
    {
        foreach (IComponent component in components)
            if (component is T match)
                return match;
        return null;
    }

    internal void QueryRadius(FrameHandle frame, Double3 center, double radius, List<Entity> results) =>
        spatial.QueryRadius(frame, center, radius, results);

    // ---- Invariants -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Checks the invariants of every subsystem, then the ones that span subsystems, which the world keeps by calling
    /// them in the right order. O(capacity). Outside a tick, or at the end of a sync point; with
    /// <see cref="WorldOptions.DebugChecks"/> set, the sync point runs it every tick.
    /// <list type="bullet">
    /// <item>No destroy batch is open.</item>
    /// <item>Every live entity is in a live frame and is a member of exactly that frame's topic, except the owner of a
    /// root frame, which is in no frame.</item>
    /// <item>Every frame's owner is live, owns that frame, and its transform row mirrors the frame's state. Every live
    /// frame's topic resolves.</item>
    /// </list>
    /// </summary>
    internal void AssertInvariants()
    {
        entities.AssertInvariants();
        topics.AssertInvariants();
        router.AssertInvariants();
        attachments.AssertInvariants();
        spatial.AssertInvariants();
        renderBuilder.AssertInvariants();

        Contract.Invariant(dying.Count == 0);
        foreach (Entity entity in entities.LiveEntities)
        {
            ref readonly TransformRow row = ref entities.BackRow(entity);
            FrameHandle own = entities.OwnedFrame(entity);
            if (row.Frame.IsNull)
                Contract.Invariant(!own.IsNull && Frames.Parent(own).IsNull);
            else
                Contract.Invariant(Frames.IsAlive(row.Frame) && topics.MembersOf(row.Frame).BinarySearch(entity) >= 0);
            if (!own.IsNull)
            {
                Contract.Invariant(Frames.IsAlive(own) && own.Entity == entity);
                TransformRow mirror = FrameOwnerRow(own);
                Contract.Invariant(row.Frame == mirror.Frame && Poses.BitEquals(row.Pose, mirror.Pose));
                Contract.Invariant(row.LinearVelocity.Equals(mirror.LinearVelocity) && row.AngularVelocity.Equals(mirror.AngularVelocity));
            }
        }
        for (int i = 0; i < Frames.Count; i++)
        {
            FrameHandle frame = Frames.AtDense(i);
            Contract.Invariant(entities.IsAlive(frame.Entity) && entities.OwnedFrame(frame.Entity) == frame && topics.IsLive(frame));
        }
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    /// <summary>Builds the render snapshot of the current front state into the write buffer and publishes it.</summary>
    private void BuildRenderSnapshot()
    {
        RenderSnapshot snapshot = RenderSnapshots.WriteBuffer;
        renderBuilder.Build(snapshot, new RenderView(TickNumber, Time, Camera, VerticalFov, SunFrame, SunColor, SunIntensity, Ambient));
        snapshot.PublishedAt = Stopwatch.GetTimestamp();
        RenderSnapshots.Publish();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private void ThrowIfInTick(string operation)
    {
        ThrowIfDisposed();
        if (inTick)
            throw new InvalidOperationException($"World.{operation} cannot run during a tick. Components use Self instead.");
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
