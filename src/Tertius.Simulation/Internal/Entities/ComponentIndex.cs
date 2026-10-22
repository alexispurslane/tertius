namespace Tertius.Simulation;

/// <summary>
/// For each component type, the live entities whose front component flags hold it, in slot order: the index that an
/// entity query with a <c>With</c> type starts from. It is <see cref="EntityTable"/>'s private storage, which the table
/// changes whenever an entity's front flags change. Changes queue up in each type's set and apply when the set is next
/// read, or at <see cref="Settle"/>.
/// </summary>
/// <remarks>
/// Invariants, which <see cref="EntityTable.AssertInvariants"/> checks against the front flags: an entity is in the set
/// of a type exactly when it is live and its front flags hold the type. After <see cref="Settle"/>, no set has a queued
/// change, so any number of threads may read the sets at once until the next change.
/// </remarks>
internal sealed class ComponentIndex
{
    private readonly EntitySet?[] sets = new EntitySet?[ComponentTypes.Capacity];

    /// <summary>True when no set has a queued change.</summary>
    public bool IsSettled
    {
        get
        {
            foreach (EntitySet? set in sets)
                if (set is { HasQueuedChanges: true })
                    return false;
            return true;
        }
    }

    /// <summary>The entities with the type <paramref name="typeId"/>, in slot order. Thread-safe once settled.</summary>
    public ReadOnlySpan<Entity> With(int typeId) => sets[typeId] is { } set ? set.Items : default;

    /// <summary>Queues a new live entity with front flags <paramref name="types"/>.</summary>
    public void Add(Entity entity, in ComponentFlags types)
    {
        foreach (int id in types)
            Queue(id, entity, true);
    }

    /// <summary>Queues the removal of an entity whose front flags are <paramref name="types"/>.</summary>
    public void Remove(Entity entity, in ComponentFlags types)
    {
        foreach (int id in types)
            Queue(id, entity, false);
    }

    /// <summary>Queues the change of an entity's front flags from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public void Change(Entity entity, in ComponentFlags from, in ComponentFlags to)
    {
        foreach (int id in from.Except(to))
            Queue(id, entity, false);
        foreach (int id in to.Except(from))
            Queue(id, entity, true);
    }

    /// <summary>Applies every queued change, so that readers on other threads only read. It visits every type's set once.</summary>
    public void Settle()
    {
        foreach (EntitySet? set in sets)
            if (set is { HasQueuedChanges: true })
                _ = set.Items;
        Contract.Ensures(IsSettled);
    }

    private void Queue(int typeId, Entity entity, bool member) => (sets[typeId] ??= new()).Queue(entity, member);
}
