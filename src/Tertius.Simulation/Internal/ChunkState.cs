namespace Tertius.Simulation;

/// <summary>A message sent with <see cref="Outbox.SendAfter{T}"/>: it joins the routing of tick <see cref="Due"/>.</summary>
internal struct DelayedEvent
{
    public long Due;
    public Event Event;
}

/// <summary>A validated spawn: the components sorted by priority, their cached wants, and the topic subscriptions.</summary>
internal sealed record SpawnPlan(IComponent[] Components, EventMask[] Wants, Topic[] Subscriptions);

/// <summary>A spawn made during a tick. It becomes live at the sync point.</summary>
internal sealed record PendingSpawn(Entity Entity, SpawnDesc Desc, SpawnPlan Plan);

/// <summary>A subscribe or unsubscribe that a running entity asked for. Applied at the sync point.</summary>
internal readonly record struct SubscriptionChange(Entity Entity, Topic Topic, bool Subscribe);

/// <summary>A component that a running entity adds to or removes from itself. Applied right after its run.</summary>
internal readonly record struct ComponentChange(Entity Entity, IComponent Component, bool Add);

/// <summary>A log line or error from a chunk. The sync point reports it, in chunk order.</summary>
internal readonly record struct ChunkMessage(string Text, bool IsError);

/// <summary>
/// Everything that the entities of one chunk produce during a tick for the rest of the world: messages, spawns,
/// destroy and subscription requests, component changes, ray casts and log lines. Only the worker that runs the chunk writes it,
/// so the hot path needs no locks. The sync point reads chunks in chunk order, which makes the result independent of
/// which thread ran which chunk. The entity table keeps its own per-chunk bookkeeping (dirty rows, intents, spawn
/// pools) to itself.
/// </summary>
internal sealed class ChunkState(int index)
{
    public int Index { get; } = index;

    public readonly RefList<Event> Outbox = new(16);
    public readonly RefList<DelayedEvent> Delayed = new(4);
    public readonly List<PendingSpawn> Spawns = new();
    /// <summary>Entities of this chunk that asked to be destroyed, each once, in the order they asked.</summary>
    public readonly List<Entity> Destroys = new();
    public readonly List<SubscriptionChange> SubscriptionChanges = new();
    /// <summary>Component changes of the entity that is running. Applied right after its run.</summary>
    public readonly List<ComponentChange> ComponentChanges = new();
    /// <summary>The types of the components that this chunk's entities added this tick, for their class constructors.</summary>
    public readonly List<Type> AddedComponentTypes = new();
    public readonly List<ChunkMessage> Messages = new();
    /// <summary>The ray casts that this chunk's entities asked for, in the order they asked. The sync point runs them.</summary>
    public readonly RefList<RayRequest> RayRequests = new(4);

    /// <summary>Empties the lists of this tick. The router empties the outbox and the delayed list itself.</summary>
    public void EndTick()
    {
        Spawns.Clear();
        Destroys.Clear();
        SubscriptionChanges.Clear();
        ComponentChanges.Clear();
        AddedComponentTypes.Clear();
        Messages.Clear();
        RayRequests.Clear();
    }
}
