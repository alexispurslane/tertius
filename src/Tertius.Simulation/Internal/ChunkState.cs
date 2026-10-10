using System.Numerics;

namespace Tertius.Simulation;

internal enum SlotState : byte
{
    /// <summary>In the free list.</summary>
    Free,
    /// <summary>Handed out for a spawn, or held in a chunk's spawn pool, but not live yet.</summary>
    Reserved,
    Live,
}

[Flags]
internal enum SlotFlags : ushort
{
    None = 0,
    /// <summary>The back transform row differs from the front row.</summary>
    TransformDirty = 1 << 0,
    /// <summary>The back render row differs from the front row.</summary>
    RenderDirty = 1 << 1,
    /// <summary>The entity is in its chunk's intent list for this tick.</summary>
    IntentRegistered = 1 << 2,
    /// <summary>The entity became live at the last sync and has not had its first tick yet.</summary>
    FirstTick = 1 << 3,
    DestroyRequested = 1 << 4,
    /// <summary>Gameplay wrote the transform this tick, through <see cref="Self.Transform"/>.</summary>
    GameplayWroteTransform = 1 << 5,
    /// <summary>The entity is in the current destroy batch.</summary>
    Dying = 1 << 6,
    /// <summary>The entity is in the list of slots whose spatial grid cell must be checked.</summary>
    GridPending = 1 << 7,
    /// <summary>The entity is in a sync-point work list. Used to keep such lists free of duplicates.</summary>
    Listed = 1 << 8,
    /// <summary>
    /// The slot was reused outside a tick, after the last routing. Its inbox still holds the old entity's messages,
    /// so the new entity ignores it until the next routing.
    /// </summary>
    InboxStale = 1 << 9,
}

/// <summary>One row of the transform table.</summary>
internal struct TransformRow
{
    /// <summary>The frame the entity is in. Null only for the owner of a root frame.</summary>
    public FrameHandle Frame;
    /// <summary>The pose in <see cref="Frame"/>.</summary>
    public DPose Pose;
    /// <summary>Linear velocity in frame axes.</summary>
    public Vector3 LinearVelocity;
    /// <summary>Angular velocity in frame axes.</summary>
    public Vector3 AngularVelocity;
}

/// <summary>Per-entity data that is cold, or that only the sync point touches.</summary>
internal struct EntityRecord
{
    /// <summary>Sorted by priority, stable for ties. Null for a slot that is not live.</summary>
    public IComponent[]? Components;
    /// <summary>The cached <see cref="IComponent.Wants"/> of each component.</summary>
    public EventMask[]? Wants;
    public BodyDesc? Body;
    /// <summary>The frame this entity is, if any.</summary>
    public FrameHandle OwnedFrame;
    /// <summary>The frame whose member set holds this entity. It follows the transform row's frame at each sync.</summary>
    public FrameHandle MemberFrame;
    /// <summary>Explicit topic subscriptions. Frame membership is separate.</summary>
    public List<Topic>? Subscriptions;
    public Entity AttachParent;
    /// <summary>The offset from the attachment parent. The entity itself may change it during its run.</summary>
    public Pose AttachOffset;
    /// <summary>This entity's node in the attachment tree, if it is attached or has attached children.</summary>
    public PoseNode AttachNode;
    /// <summary>Attached children, sorted by slot.</summary>
    public List<uint>? AttachChildren;
    public bool InGrid;
    public int GridFrame;
    public CellKey GridCell;
}

internal struct DelayedEvent
{
    public long Due;
    public Event Event;
}

/// <summary>A validated spawn: the components sorted by priority, their cached wants, and the topic subscriptions.</summary>
internal sealed record SpawnPlan(IComponent[] Components, EventMask[] Wants, Topic[] Subscriptions);

/// <summary>A spawn made during a tick. It becomes live at the sync point.</summary>
internal sealed record PendingSpawn(Entity Entity, SpawnDesc Desc, SpawnPlan Plan);

internal readonly record struct SubscriptionChange(uint Slot, Topic Topic, bool Subscribe);

internal readonly record struct ComponentChange(uint Slot, IComponent Component, bool Add);

internal readonly record struct ChunkMessage(string Text, bool IsError);

/// <summary>
/// Everything that the entities of one chunk produce during a tick. Only the worker that runs the chunk writes it,
/// so the hot path needs no locks. The sync point joins chunks in chunk order, which makes the result independent of
/// which thread ran which chunk.
/// </summary>
internal sealed class ChunkState(int index)
{
    public int Index { get; } = index;

    public readonly RefList<Event> Outbox = new(16);
    public readonly RefList<DelayedEvent> Delayed = new(4);
    public readonly List<PendingSpawn> Spawns = new();
    public readonly List<uint> Destroys = new();
    public readonly List<SubscriptionChange> SubscriptionChanges = new();
    public readonly List<uint> IntentSlots = new();
    public readonly List<uint> DirtySlots = new();
    /// <summary>Component changes of the entity that is running. Applied right after its run.</summary>
    public readonly List<ComponentChange> ComponentChanges = new();
    public readonly List<ChunkMessage> Messages = new();
    /// <summary>Free slots reserved for this chunk's spawns at the previous sync, in a deterministic order.</summary>
    public readonly Queue<uint> SpawnPool = new();

    public int LiveCount;
    public int SpawnsThisTick;
    public int LastSpawnCount;
    public int FallbackSpawns;

    public void EndTick()
    {
        Spawns.Clear();
        Destroys.Clear();
        SubscriptionChanges.Clear();
        IntentSlots.Clear();
        DirtySlots.Clear();
        ComponentChanges.Clear();
        Messages.Clear();
        LastSpawnCount = SpawnsThisTick;
        SpawnsThisTick = 0;
        FallbackSpawns = 0;
    }
}
