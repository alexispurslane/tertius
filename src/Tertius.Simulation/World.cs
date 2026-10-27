using System.Diagnostics;
using System.Numerics;

namespace Tertius.Simulation;

/// <summary>Counters that describe the last tick.</summary>
/// <param name="LiveEntities">Entities that are live now.</param>
/// <param name="Capacity">Entity slots allocated in the tables.</param>
/// <param name="Frames">Live reference frames that gameplay sees: the semantic frames, without the numeric frames of the engine.</param>
/// <param name="RoutedEvents">Messages routed at the last sync point, each counted once.</param>
/// <param name="Deliveries">Inbox entries written at the last sync point.</param>
/// <param name="FallbackSpawns">Spawns, since the world was created, that ran out of their chunk's slot pool.</param>
public readonly record struct WorldStats(
    int LiveEntities,
    int Capacity,
    int Frames,
    int RoutedEvents,
    int Deliveries,
    long FallbackSpawns
);

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
/// to inboxes), <see cref="TopicSets"/> (who hears each topic), <see cref="Attachments"/>, <see cref="SpatialIndex"/>,
/// <see cref="PhysicsQueryRunner"/>, <see cref="RenderSnapshotBuilder"/> and <see cref="PuffTable"/>. The world owns the public API, the tick, the order in which the sync point
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
    private readonly PhysicsWorker? physicsWorker;
    private readonly PhysicsStepOutput stepOutput = new();
    private readonly PhysicsChanges physicsChanges = new();
    private readonly ParallelOptions parallelOptions;
    private readonly Action<int> runChunk;
    private readonly List<string> errors = [];
    private readonly List<string> pendingLog = [];
    private bool inTick;
    private bool disposed;
    private long fallbackSpawnTotal;

    private readonly TopicSets topics;
    private readonly Router router;
    private readonly RenderSnapshotBuilder renderBuilder;
    private readonly PhysicsQueryRunner queryRunner;

    // The heir of each frame of the destroy batch, for the puffs (see HeirOf), kept so that a destroy allocates no delegate.
    private readonly Func<FrameHandle, FrameHandle> heirOf;

    /// <summary>
    /// Creates an empty world. With no physics backend, bodies do not move and their intents are dropped. The first world
    /// of a process scans the loaded assemblies for component types (see <see cref="ComponentTypes"/>), and throws
    /// <see cref="InvalidOperationException"/> when they define a generic one or more than
    /// <see cref="ComponentTypes.Capacity"/>.
    /// </summary>
    public World(WorldOptions? options = null, IPhysicsBackend? physics = null)
    {
        Options = options ?? new WorldOptions();
        Options.Validate();
        EngineEvents.Register();
        ComponentTypes.ScanLoadedAssemblies();
        Physics = physics;
        DeltaTime = 1.0 / Options.TickRate;
        Time = Options.StartTime;
        parallelOptions = new() { MaxDegreeOfParallelism = Options.MaxDegreeOfParallelism };
        runChunk = RunChunk;

        // The subsystems with a step on the workers take them here, or null when the world has one thread. Each decides
        // from its own measured threshold whether a step is large enough for them.
        ParallelOptions? workers = Options.MaxDegreeOfParallelism == 1 ? null : parallelOptions;
        Entities = new(Options.ChunkSize, Options.InitialCapacity, workers);
        // The order of the subsystems is the order in which they let go of a released entity.
        topics = new(Entities);
        router = new(Entities, topics, workers);
        Attachments = new(Entities);
        Spatial = new(Entities, Frames, workers);
        renderBuilder = new(Entities, Frames, workers);
        queryRunner = new(
            world: this,
            physics: physics,
            router: router,
            reportError: message => Report(message, isError: true)
        );
        dyingMarks = Entities.AddColumn<bool>();
        characterStanding = Entities.AddColumn<bool>();
        Puffs = new(
            entities: Entities,
            frames: Frames,
            capacity: Options.PuffCapacity,
            reportError: message => Report(message, isError: true),
            workers: workers
        );
        heirOf = HeirOf;
        Frames.NumericOptions = Options.NumericFrames;
        if (physics != null)
            physicsWorker = new(physics, stepOutput);
    }

    /// <summary>
    /// The reference frames. Read it freely. Outside a tick, a game gives a frame a new gravity field with
    /// <see cref="SetFrameGravity"/>, which refuses a field that the engine cannot use. A frame on rails takes its orbit or
    /// its spin from a component of its owner (see <see cref="KeplerMotion"/> and <see cref="SpinMotion"/>), and a hull's
    /// owner changes the hull's motion in a tick with <see cref="BodyHandle.SetMotion"/>. Make every other change through
    /// the world.
    /// </summary>
    public FrameTree Frames { get; } = new();

    /// <summary>Completed ticks. During a tick, the number of the tick that is running.</summary>
    public long TickNumber { get; private set; }

    /// <summary>Simulation time, in seconds. During a tick, the time at the start of the tick.</summary>
    public double Time { get; private set; }

    /// <summary>The fixed time step, in seconds: 1 / <see cref="WorldOptions.TickRate"/>, rounded once to a double.</summary>
    public double DeltaTime { get; }

    /// <summary>The blueprints that <see cref="Spawn(string, FrameHandle, in DPose)"/> and
    /// <see cref="Self.Spawn(string, FrameHandle, in DPose)"/> use.</summary>
    public Blueprints Blueprints { get; } = new();

    /// <summary>The settings the world was created with.</summary>
    public WorldOptions Options { get; }

    /// <summary>The physics backend, or null.</summary>
    public IPhysicsBackend? Physics { get; }

    /// <summary>Render snapshots, one per tick. The render thread reads the latest one.</summary>
    public TripleBuffer<RenderSnapshot> RenderSnapshots { get; } = new(static () => new());

    /// <summary>The entity whose pose, composed with its <see cref="RenderData.ViewOffset"/>, is the camera.</summary>
    public Entity Camera { get; set; }

    /// <summary>Vertical field of view in radians.</summary>
    public float VerticalFov { get; set; } = MathF.PI / 3;

    /// <summary>
    /// Whether the render snapshot carries the outline of each frame that has a parent (<see cref="RenderSnapshot.Outlines"/>
    /// ).
    /// A view setting, as <see cref="Camera"/> is: the first snapshot built after a change shows it. Off by default. With it
    /// off, the builder does no outline work.
    /// </summary>
    public bool ShowFrameOutlines { get; set; }

    /// <summary>
    /// The hemisphere ambient light of the render snapshot. It lights only a renderer whose environment map is off
    /// (ARCHITECTURE.md, "The environment map").
    /// </summary>
    public AmbientLight Ambient { get; set; } =
        new()
        {
            SkyColor = new(0.08f),
            GroundColor = new(0.03f),
            Intensity = 1,
        };

    /// <summary>The front view. During a tick it shows the previous tick, like <see cref="Self.Snapshot"/>.</summary>
    public Snapshot Snapshot => new(this);

    /// <summary>Component exceptions and failed engine operations, oldest first. At most <see cref="WorldOptions.MaxErrors"/>
    /// entries.</summary>
    public IReadOnlyList<string> Errors => errors;

    /// <summary>Errors and engine notes. Raised on the ticking thread after the tick, never during it.</summary>
    public event Action<string>? Log;

    /// <summary>
    /// The events that the last sync point routed, each once, in routing order: the events that the sync point sends
    /// itself (<see cref="FrameChanged"/> and <see cref="Collision"/>), the entities' messages in chunk order, the delayed
    /// sends that were due, then the posted events. An event is here even if no entity wanted it. <see cref="Tertius.Tick"/>
    /// and <see cref="Spawned"/> go straight into each entity's own event list, so they are never here. The span is
    /// valid until the next sync point.
    /// </summary>
    public ReadOnlySpan<Event> RoutedEvents => router.Routed;

    /// <summary>Counters of the last tick.</summary>
    public WorldStats Stats =>
        new(
            LiveEntities: Entities.LiveCount,
            Capacity: Entities.Capacity,
            Frames: Frames.Count - Frames.NumericCount,
            RoutedEvents: router.RoutedCount,
            Deliveries: router.DeliveryCount,
            FallbackSpawns: fallbackSpawnTotal
        );

    // ---- For Self, BodyHandle and Snapshot ---------------------------------------------------------------------------
    // These views read and write the subsystems directly, through the same checked members that the world uses.

    /// <summary>The entity table, for the views over it.</summary>
    internal EntityTable Entities { get; }

    /// <summary>The attachment chains, for the views over them.</summary>
    internal Attachments Attachments { get; }

    /// <summary>The puffs (docs/plans/particles.md): the rows, their ranges by frame and the look table.</summary>
    internal PuffTable Puffs { get; }

    /// <summary>The spatial grids, for the entity queries with a Near.</summary>
    internal SpatialIndex Spatial { get; }

    /// <summary>Forgets every entry of <see cref="Errors"/>.</summary>
    public void ClearErrors() => errors.Clear();

    /// <summary>True if the handle names a live entity: the slot is live and the generation matches.</summary>
    public bool IsAlive(Entity entity) => Entities.IsAlive(entity);

    /// <summary>
    /// The entity's pose in <paramref name="frame"/>, from the front snapshot, through the frame tree in doubles. Throws
    /// <see cref="StaleEntityException"/> for a stale entity and <see cref="ArgumentException"/> for a stale frame.
    /// </summary>
    public DPose PoseIn(Entity entity, FrameHandle frame) =>
        FrameReads.PoseIn(frames: Frames, entity: entity, row: Entities.FrontRow(entity), to: LiveFrame(frame));

    /// <summary>
    /// The frame the entity is in, from the front snapshot. Null for the owner of a root frame. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public FrameHandle GetFrame(Entity entity) => Entities.FrontRow(entity).Frame;

    /// <summary>
    /// The linear velocity of the entity's origin, the point that its pose names, relative to <paramref name="frame"/>, in its
    /// axes, from the front snapshot. Throws
    /// <see cref="StaleEntityException"/> for a stale entity and <see cref="ArgumentException"/> for a stale frame.
    /// </summary>
    public Double3 VelocityIn(Entity entity, FrameHandle frame) =>
        FrameReads.VelocityIn(frames: Frames, entity: entity, row: Entities.FrontRow(entity), to: LiveFrame(frame));

    /// <summary>
    /// The entity's angular velocity relative to <paramref name="frame"/>, in its axes, from the front snapshot. Throws
    /// <see cref="StaleEntityException"/> for a stale entity and <see cref="ArgumentException"/> for a stale frame.
    /// </summary>
    public Vector3 AngularVelocityIn(Entity entity, FrameHandle frame) =>
        FrameReads.AngularVelocityIn(
            frames: Frames,
            entity: entity,
            row: Entities.FrontRow(entity),
            to: LiveFrame(frame)
        );

    /// <summary>Returns <paramref name="frame"/> if it is live. Throws <see cref="ArgumentException"/> if it is stale or null.</summary>
    private FrameHandle LiveFrame(FrameHandle frame) =>
        Frames.IsAlive(frame)
            ? frame
            : throw new ArgumentException($"Cannot read in {frame}: the frame is stale.", nameof(frame));

    /// <summary>The render row, from the front snapshot. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public RenderData GetRender(Entity entity) => Entities.FrontRender(entity);

    /// <summary>The frame that this entity is, or null. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public FrameHandle GetOwnedFrame(Entity entity) => Entities.OwnedFrame(entity);

    /// <summary>
    /// The description of the entity's physics body at its spawn, or null if it has none. A hull's motion can change after the
    /// spawn: <see cref="FrameTree.HullMotion"/> gives the motion that it had in the last finished step. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public BodyDesc? GetBody(Entity entity) => Entities.Body(entity);

    /// <summary>A copy of the entity's components, in pipeline order. Throws <see cref="StaleEntityException"/> for a stale handle.</summary>
    public IReadOnlyList<IComponent> GetComponents(Entity entity) => Entities.Components(entity).ToArray();

    /// <summary>
    /// The entity's first component that is a <typeparamref name="T"/>, or null if it has none. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public T? GetComponent<T>(Entity entity)
        where T : class, IComponent => FindComponent<T>(Entities.Components(entity));

    /// <summary>Adds every live entity to <paramref name="results"/>, in slot order.</summary>
    public void CopyLiveEntities(List<Entity> results)
    {
        foreach (Entity entity in Entities.LiveEntities)
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
        if (Options.DisposePhysics)
            Physics?.Dispose();
    }

    /// <summary>The members of a frame, in slot order, for the entity queries that start from them. Worker-safe during gameplay.</summary>
    internal ReadOnlySpan<Entity> FrameMembers(FrameHandle frame)
    {
        // A read of a set with queued changes merges them, which races with a read on another worker.
        Contract.Requires(!Entities.GameplayRunning || topics.MembersSettled);
        return topics.MembersOf(frame);
    }

    /// <summary>
    /// True if physics stood the character on walkable ground under gravity in the step of the last tick. False for any
    /// other entity. Throws <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    internal bool IsStanding(Entity entity) => characterStanding[entity];

    internal static T? FindComponent<T>(ReadOnlySpan<IComponent> components)
        where T : class, IComponent
    {
        foreach (IComponent component in components)
            if (component is T match)
                return match;
        return null;
    }

    internal void QueryRadius(FrameHandle frame, Double3 center, double radius, List<Entity> results) =>
        Spatial.QueryRadius(frame: frame, center: center, radius: radius, results: results);

    // ---- Invariants -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Checks the invariants of every subsystem, then the ones that span subsystems, which the world keeps by calling
    /// them in the right order. O(capacity). Outside a tick, or at the end of a sync point; with
    /// <see cref="WorldOptions.DebugChecks"/> set, the sync point runs it every tick.
    /// <list type="bullet">
    /// <item>No destroy batch is open.</item>
    /// <item>Every live entity is in a live frame and is a member of exactly that frame's topic, except the owner of a
    /// root frame, which is in no frame.</item>
    /// <item>Every live entity's physics home is its row frame or a live frame below it. When the home is below, the home
    /// position plus the home's offset is the pose's position, within rounding.</item>
    /// <item>Every frame's owner is live, owns that frame, and its transform row mirrors the frame's state, its owner view,
    /// to the bit. Step 7 of the sync point skips its pass over the owner views while the frame tree's owner view stamp has
    /// not changed since the pass of step 2, so a write of the tree that leaves the stamp as it is breaks this. Every live
    /// frame's topic resolves.</item>
    /// <item>A frame without a parent, or whose owner has no body, has no stored hull motion (see
    /// <see cref="FrameTree.HullMotion"/>). Every other frame has a stored hull motion that its owner's body can take: not a
    /// character, static for a body whose shape is or holds a mesh, and dynamic only with a mass above zero (see
    /// <see cref="BodyDesc.Problem"/>).</item>
    /// <item>Only a character can be standing.</item>
    /// <item>Every intent that the next physics step applies names its entity's frame (see
    /// <see cref="PhysicsIntent.Frame"/>).</item>
    /// <item>Every live entity among the entities that took a step record at the last step 2 without body or frame has
    /// neither (see <see cref="FollowFramelessEntities"/>).</item>
    /// <item>No live attached entity is in the intent list, and its front intent is empty: the flow of step 5 carried its
    /// push to its chain root (docs/plans/parts.md).</item>
    /// <item>The front row of every live attached entity names the frame of its chain root's front row. Step 7 keeps it,
    /// and the flow of step 5 relies on it.</item>
    /// <item>With a physics backend, the kept center of mass of every body is the one that the backend gives its shape.</item>
    /// </list>
    /// </summary>
    internal void AssertInvariants()
    {
        Entities.AssertInvariants();
        topics.AssertInvariants();
        router.AssertInvariants();
        Attachments.AssertInvariants();
        Spatial.AssertInvariants();
        renderBuilder.AssertInvariants();
        Puffs.AssertInvariants();

        Contract.Invariant(dying.Count == 0);
        foreach (Entity entity in Entities.IntentEntities.Span)
            Contract.Invariant(Entities.NextStepIntent(entity).Frame == Entities.BackRow(entity).Frame);
        foreach (Entity entity in framelessMovers)
            if (Entities.IsAlive(entity))
                Contract.Invariant(Entities.Body(entity) == null && Entities.OwnedFrame(entity).IsNull);
        foreach (Entity entity in Entities.IntentEntities.Span)
            Contract.Invariant(Attachments.ParentOf(entity).IsNull);
        foreach (Entity entity in Attachments.Attached)
        {
            Contract.Invariant(Entities.HasEmptyFrontIntent(entity));
            Contract.Invariant(Entities.FrontRow(entity).Frame == Entities.FrontRow(Attachments.RootOf(entity)).Frame);
        }

        if (Physics != null)
            foreach (Entity entity in Entities.BodyEntities)
                Contract.Invariant(
                    Entities.Body(entity) is { } body
                        && Entities.BodyCenterOfMass(entity) == Physics.ShapeCenterOfMass(body.Shape)
                );
        foreach (Entity entity in Entities.LiveEntities)
        {
            ref readonly TransformRow row = ref Entities.BackRow(entity);
            FrameHandle own = Entities.OwnedFrame(entity);
            if (row.Frame.IsNull)
                Contract.Invariant(!own.IsNull && Frames.Parent(own).IsNull);
            else
                Contract.Invariant(Frames.IsAlive(row.Frame) && topics.MembersOf(row.Frame).BinarySearch(entity) >= 0);
            // The physics home is the row frame or a numeric frame below it, whose semantic frame is the row frame.
            Contract.Invariant(
                row.PhysicsHome == row.Frame
                    || (
                        !row.Frame.IsNull
                        && Frames.IsAlive(row.PhysicsHome)
                        && Frames.SemanticOf(row.PhysicsHome) == row.Frame
                    )
            );
            if (row.PhysicsHome != row.Frame)
            {
                // The sums round at the scale of the larger term, a few times at most.
                Double3 offset = Frames.HomeOffset(row.PhysicsHome, row.Frame);
                double scale = Math.Max(offset.Length, row.HomePosition.Length);
                Contract.Invariant((offset + row.HomePosition - row.Pose.Position).Length <= 1e-12 * scale + 1e-9);
            }

            if (!own.IsNull)
            {
                Contract.Invariant(Frames.IsAlive(own) && own.Entity == entity);
                TransformRow mirror = FrameOwnerRow(own);
                Contract.Invariant(
                    row.Frame == mirror.Frame
                        && row.PhysicsHome == mirror.PhysicsHome
                        && Poses.BitEquals(row.Pose, mirror.Pose)
                        && Poses.BitEquals(row.HomePose, mirror.HomePose)
                );
                Contract.Invariant(
                    row.LinearVelocity.Equals(mirror.LinearVelocity)
                        && row.AngularVelocity.Equals(mirror.AngularVelocity)
                );
                BodyMotion? hullMotion = Frames.HullMotion(own);
                if (Frames.Parent(own).IsNull || Entities.Body(entity) is not { } hull)
                    Contract.Invariant(hullMotion is null);
                else
                    Contract.Invariant(
                        hullMotion is { } motion && (hull with { Motion = motion }).Problem(BodyRole.Hull) is null
                    );
            }

            if (characterStanding[entity])
                Contract.Invariant(Entities.Body(entity) is { Motion: BodyMotion.Character });
        }

        for (int i = 0; i < Frames.Count; i++)
        {
            FrameHandle frame = Frames.AtDense(i);
            // A numeric frame has no owner and no topic: nothing public can name it.
            if (Frames.IsNumeric(frame))
                continue;
            Contract.Invariant(
                Entities.IsAlive(frame.Entity) && Entities.OwnedFrame(frame.Entity) == frame && topics.IsLive(frame)
            );
        }

        // The numeric frames are planned for the next step, which applies the front intents.
        Frames.AssertInvariants(Entities.FrontIntents.Span);
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    /// <summary>Builds the render snapshot of the current front state and of the puff table into the write buffer and publishes it.</summary>
    private void BuildRenderSnapshot()
    {
        RenderSnapshot snapshot = RenderSnapshots.WriteBuffer;
        renderBuilder.Build(
            snapshot,
            new(
                Tick: TickNumber,
                Time: Time,
                Camera: Camera,
                VerticalFov: VerticalFov,
                Ambient: Ambient,
                ShowFrameOutlines: ShowFrameOutlines
            ),
            Puffs.View
        );
        snapshot.PublishedAt = Stopwatch.GetTimestamp();
        RenderSnapshots.Publish();
    }

    /// <summary>
    /// The heir of a frame for its puffs during a destroy batch: the frame itself when its owner is not in the batch, or else
    /// its nearest surviving ancestor, or null when none survives.
    /// </summary>
    private FrameHandle HeirOf(FrameHandle frame) => dyingMarks[frame.Entity] ? SurvivingAncestor(frame) : frame;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private void ThrowIfInTick(string operation)
    {
        ThrowIfDisposed();
        if (inTick)
            throw new InvalidOperationException(
                $"World.{operation} cannot run during a tick. Components use Self instead."
            );
    }

    private void Report(string message, bool isError)
    {
        if (isError)
        {
            if (Options.MaxErrors == 0)
            {
                pendingLog.Add(message);
                return;
            }

            if (errors.Count >= Options.MaxErrors)
                errors.RemoveRange(0, errors.Count - Options.MaxErrors + 1);
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
