using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// The body pairs of one system by body, so that a body that leaves the system visits only its own pairs (ter-9sri). It
/// holds pair keys (see <see cref="FrameWorld.PairKey(BodyID, BodyID)"/>), and <see cref="FrameWorld"/> keeps it equal to
/// its pair tables. A set that a body no longer needs waits in a pool, so pairs that come and go allocate nothing.
/// </summary>
/// <remarks>
/// Invariants, which <see cref="AssertInvariants"/> checks:
/// <list type="bullet">
/// <item>Each pair is in the set of each of its two bodies, and in no other set.</item>
/// <item>No set is empty: a body whose last pair goes leaves the index.</item>
/// <item><see cref="Count"/> is the number of pairs.</item>
/// </list>
/// </remarks>
internal sealed class BodyPairIndex
{
    // The pairs of each body that has one, by the body's full ID.
    private readonly Dictionary<uint, HashSet<ulong>> pairsByBody = [];

    // Empty sets, for the next bodies that get a pair.
    private readonly Stack<HashSet<ulong>> spareSets = [];

    /// <summary>The number of pairs in the index.</summary>
    public int Count { get; private set; }

    /// <summary>Adds the pair <paramref name="key"/>, which the index does not hold.</summary>
    public void Add(ulong key)
    {
        Contract.Requires(!Contains(key) && Low(key) != High(key));
        SetOf(Low(key)).Add(key);
        SetOf(High(key)).Add(key);
        Count++;
        Contract.Ensures(Contains(key) && pairsByBody[High(key)].Contains(key));
    }

    /// <summary>Removes the pair <paramref name="key"/>, which the index holds.</summary>
    public void Remove(ulong key)
    {
        Contract.Requires(Contains(key));
        RemoveFrom(Low(key), key);
        RemoveFrom(High(key), key);
        Count--;
        Contract.Ensures(
            !Contains(key) && !(pairsByBody.TryGetValue(High(key), out HashSet<ulong>? high) && high.Contains(key))
        );
    }

    /// <summary>True when the index holds the pair <paramref name="key"/>.</summary>
    public bool Contains(ulong key) =>
        pairsByBody.TryGetValue(Low(key), out HashSet<ulong>? pairs) && pairs.Contains(key);

    /// <summary>True when the index holds a pair of <paramref name="body"/>.</summary>
    public bool HasPairs(BodyID body) => pairsByBody.ContainsKey(body.ID);

    /// <summary>Appends to <paramref name="into"/> the key of every pair of <paramref name="body"/>.</summary>
    public void CopyPairsOf(BodyID body, List<ulong> into)
    {
        if (pairsByBody.TryGetValue(body.ID, out HashSet<ulong>? pairs))
            into.AddRange(pairs);
    }

    /// <summary>Removes every pair.</summary>
    public void Clear()
    {
        foreach (HashSet<ulong> pairs in pairsByBody.Values)
        {
            pairs.Clear();
            spareSets.Push(pairs);
        }

        pairsByBody.Clear();
        Count = 0;
    }

    /// <summary>Checks the invariants (see the remarks of this type). Costs O(pairs).</summary>
    public void AssertInvariants()
    {
        int entries = 0;
        foreach ((uint body, HashSet<ulong> pairs) in pairsByBody)
        {
            Contract.Invariant(pairs.Count > 0);
            foreach (ulong key in pairs)
            {
                bool ofBody = Low(key) == body || High(key) == body;
                uint other = Low(key) == body ? High(key) : Low(key);
                Contract.Invariant(
                    ofBody && pairsByBody.TryGetValue(other, out HashSet<ulong>? others) && others.Contains(key)
                );
            }

            entries += pairs.Count;
        }

        Contract.Invariant(entries == 2 * Count);
    }

    /// <summary>The full ID of the body of a pair key with the lower ID.</summary>
    private static uint Low(ulong key) => (uint)(key >> 32);

    /// <summary>The full ID of the body of a pair key with the higher ID.</summary>
    private static uint High(ulong key) => (uint)key;

    private HashSet<ulong> SetOf(uint body)
    {
        if (pairsByBody.TryGetValue(body, out HashSet<ulong>? pairs))
            return pairs;
        pairs = spareSets.Count > 0 ? spareSets.Pop() : [with(FrameWorld.PairKeyHash.Instance)];
        pairsByBody.Add(body, pairs);
        return pairs;
    }

    private void RemoveFrom(uint body, ulong key)
    {
        HashSet<ulong> pairs = pairsByBody[body];
        pairs.Remove(key);
        if (pairs.Count > 0)
            return;
        pairsByBody.Remove(body);
        spareSets.Push(pairs);
    }
}
