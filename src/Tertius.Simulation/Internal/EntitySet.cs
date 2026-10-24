namespace Tertius.Simulation;

/// <summary>
/// A sorted set of entity handles: the members of a frame, the subscribers of a topic, or the attached entities. Changes
/// queue up and apply in one merge pass before the set is next read, so a batch of k changes to a set of n entities
/// costs O(n + k log k) instead of k array shifts. A reader always sees every change made so far, in slot order, so the
/// result does not depend on the order in which the changes were made.
/// </summary>
/// <remarks>
/// It holds handles, not slots, so that its owner can hand its contents across the owner's interface as they are. It
/// is private storage of <see cref="TopicSets"/> and <see cref="Attachments"/>: it never reaches an entity's data
/// through a handle, so it has nothing to check, and its owners check every handle before they queue it. Every owner
/// takes a released entity out with <see cref="Queue"/>, so a set holds only live entities, and two live entities never
/// share a slot: the set's order, by slot and then by generation, is slot order.
/// <para>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>The items are in strictly increasing order.</item>
/// <item>A read sees every queued change; a later change to an entity wins over an earlier one.</item>
/// </list>
/// A span from <see cref="Items"/> stays valid while changes queue, until the set is read again after a change. Not
/// thread-safe.
/// </para>
/// </remarks>
internal sealed class EntitySet
{
    private Entity[] items = new Entity[4];
    private Entity[] spare = new Entity[4];
    private int count;

    // The desired membership of each entity changed since the last merge, by sort key. A later change wins.
    private readonly Dictionary<ulong, bool> pending = [];
    private ulong[] pendingKeys = new ulong[4];
    private bool[] pendingWants = new bool[4];

    /// <summary>The entities in the set, in slot order, with every queued change applied.</summary>
    public ReadOnlySpan<Entity> Items
    {
        get
        {
            if (pending.Count > 0)
                Merge();
            return items.AsSpan(0, count);
        }
    }

    /// <summary>True while a change is queued: the next read of <see cref="Items"/> merges, so it writes the set.</summary>
    public bool HasQueuedChanges => pending.Count > 0;

    /// <summary>Queues adding (<paramref name="member"/> true) or removing an entity.</summary>
    public void Queue(Entity entity, bool member) => pending[SortKey(entity)] = member;

    /// <summary>Empties the set, queued changes included.</summary>
    public void Clear()
    {
        count = 0;
        pending.Clear();
    }

    /// <summary>Checks that the items are sorted and unique. Applies the queued changes first.</summary>
    internal void AssertInvariants()
    {
        ReadOnlySpan<Entity> entities = Items;
        for (int i = 1; i < entities.Length; i++)
            Contract.Invariant(SortKey(entities[i - 1]) < SortKey(entities[i]));
    }

    /// <summary>The set's order: by slot, then by generation, as <see cref="Entity.CompareTo"/> orders handles.</summary>
    private static ulong SortKey(Entity entity) => ((ulong)entity.Index << 32) | entity.Generation;

    private static Entity FromSortKey(ulong key) => new((uint)(key >> 32), (uint)key);

    private void Merge()
    {
        int k = pending.Count;
        if (pendingKeys.Length < k)
        {
            pendingKeys = new ulong[Math.Max(k, pendingKeys.Length * 2)];
            pendingWants = new bool[pendingKeys.Length];
        }

        int j = 0;
        foreach (KeyValuePair<ulong, bool> change in pending)
        {
            pendingKeys[j] = change.Key;
            pendingWants[j] = change.Value;
            j++;
        }

        pending.Clear();
        Array.Sort(keys: pendingKeys, items: pendingWants, index: 0, length: k);

        if (spare.Length < count + k)
            spare = new Entity[Math.Max(count + k, spare.Length * 2)];
        int i = 0,
            p = 0,
            w = 0;
        while (i < count || p < k)
        {
            if (p == k || (i < count && SortKey(items[i]) < pendingKeys[p]))
            {
                spare[w++] = items[i++];
                continue;
            }

            ulong key = pendingKeys[p];
            bool want = pendingWants[p++];
            if (i < count && SortKey(items[i]) == key)
                i++;
            if (want)
                spare[w++] = FromSortKey(key);
        }

        (items, spare) = (spare, items);
        count = w;
    }
}
