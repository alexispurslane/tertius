namespace Tertius.Simulation;

/// <summary>
/// A sorted set of entity slots: the members of a frame, or the subscribers of a topic. Changes queue up and apply in
/// one merge pass at <see cref="Commit"/>, so a batch of k changes to a set of n slots costs O(n + k log k) instead of
/// k array shifts. Readers see only committed state, in slot order, which makes topic expansion deterministic.
/// </summary>
internal sealed class SubscriberList
{
    private uint[] items = new uint[4];
    private uint[] spare = new uint[4];
    private int count;

    // The desired membership of each slot touched since the last commit. A later change to the same slot wins.
    private readonly Dictionary<uint, bool> pending = new();
    private uint[] pendingSlots = new uint[4];
    private bool[] pendingWants = new bool[4];

    public int Count => count;

    public ReadOnlySpan<uint> Items => items.AsSpan(0, count);

    public bool HasPending => pending.Count > 0;

    public bool Contains(uint slot) => Array.BinarySearch(items, 0, count, slot) >= 0;

    /// <summary>Queues a change. Returns true when this is the first queued change since the last commit.</summary>
    public bool Queue(uint slot, bool member)
    {
        bool first = pending.Count == 0;
        pending[slot] = member;
        return first;
    }

    public void Commit()
    {
        int k = pending.Count;
        if (k == 0)
            return;
        if (pendingSlots.Length < k)
        {
            pendingSlots = new uint[Math.Max(k, pendingSlots.Length * 2)];
            pendingWants = new bool[pendingSlots.Length];
        }
        int j = 0;
        foreach (KeyValuePair<uint, bool> change in pending)
        {
            pendingSlots[j] = change.Key;
            pendingWants[j] = change.Value;
            j++;
        }
        pending.Clear();
        Array.Sort(pendingSlots, pendingWants, 0, k);

        if (spare.Length < count + k)
            spare = new uint[Math.Max(count + k, spare.Length * 2)];
        int i = 0, p = 0, w = 0;
        while (i < count || p < k)
        {
            if (p == k || (i < count && items[i] < pendingSlots[p]))
            {
                spare[w++] = items[i++];
                continue;
            }
            uint slot = pendingSlots[p];
            bool want = pendingWants[p++];
            if (i < count && items[i] == slot)
                i++;
            if (want)
                spare[w++] = slot;
        }
        (items, spare) = (spare, items);
        count = w;
    }

    public void Clear()
    {
        count = 0;
        pending.Clear();
    }
}
