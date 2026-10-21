namespace Tertius.Simulation;

/// <summary>
/// The look table of the puffs (docs/plans/particles.md, "The look table"): each look that live puffs use, once, with the
/// count of the puffs that use it. A row holds the index of its look here. A birth looks its emitter's look up by value,
/// and equal looks share one entry. A puff keeps the entry of its birth when its emitter changes its look. An entry whose
/// count falls to 0 leaves the table, and a later new look takes its index first.
/// </summary>
/// <remarks>
/// The free indices form a stack, and the births and deaths come in the same order in every run, so the indices are the
/// same in every run. The dictionary's hash codes change from process to process, but they only find entries: no result
/// depends on them. Invariants, checked by <see cref="AssertInvariants"/>: each entry's count equals the number of rows
/// that name it, the dictionary maps each entry in use to its index and holds nothing else, the free stack holds exactly
/// the other indices below the end of the table, and every look in use is valid.
/// </remarks>
internal sealed class PuffLooks
{
    private PuffLook[] looks = new PuffLook[4];
    private int[] users = new int[4];
    // Scratch of IsCoherent: the number of rows that name each entry. It grows with the table.
    private int[] rowsNaming = new int[4];
    // The entries from 0 to end have been used. The free ones among them are on the stack.
    private int end;
    private readonly Stack<int> free = new();
    private readonly Dictionary<PuffLook, int> index = new();

    /// <summary>The looks by index, with the free entries, which are the default look.</summary>
    public ReadOnlySpan<PuffLook> All => looks.AsSpan(0, end);

    /// <summary>The look of entry <paramref name="entry"/>.</summary>
    public ref readonly PuffLook this[int entry] => ref looks[entry];

    /// <summary>The number of puffs that use entry <paramref name="entry"/>, 0 for a free entry.</summary>
    public int UsersOf(int entry) => (uint)entry < (uint)end ? users[entry] : 0;

    /// <summary>The number of entries in use.</summary>
    public int InUse => index.Count;

    /// <summary>
    /// The index of the entry of <paramref name="look"/>, which a valid look must be, after <paramref name="puffs"/> more
    /// puffs use it. When no entry holds an equal look, the look takes a free index, or a new one at the end.
    /// </summary>
    public int Use(in PuffLook look, int puffs)
    {
        Contract.Requires(look.IsValid && puffs > 0);
        if (!index.TryGetValue(look, out int entry))
        {
            if (!free.TryPop(out entry))
            {
                entry = end++;
                if (entry == looks.Length)
                {
                    Array.Resize(ref looks, looks.Length * 2);
                    Array.Resize(ref users, users.Length * 2);
                    Array.Resize(ref rowsNaming, rowsNaming.Length * 2);
                }
            }
            looks[entry] = look;
            index.Add(look, entry);
        }
        users[entry] += puffs;
        return entry;
    }

    /// <summary>One puff of entry <paramref name="entry"/> dies. The entry leaves the table with its last puff.</summary>
    public void Release(int entry)
    {
        Contract.Requires(entry < end && users[entry] > 0);
        if (--users[entry] > 0)
            return;
        index.Remove(looks[entry]);
        looks[entry] = default;
        free.Push(entry);
    }

    /// <summary>Checks the invariants of the class comment against the live <paramref name="rows"/>.</summary>
    public void AssertInvariants(ReadOnlySpan<Puff> rows) => Contract.Invariant(IsCoherent(rows));

    /// <summary>
    /// True when the invariants of the class comment hold against the live <paramref name="rows"/>. It allocates nothing
    /// once the table has grown, so that a contract on the sync point can call it.
    /// </summary>
    public bool IsCoherent(ReadOnlySpan<Puff> rows)
    {
        Span<int> named = rowsNaming.AsSpan(0, end);
        named.Clear();
        foreach (Puff row in rows)
        {
            if (row.Look < 0 || row.Look >= end)
                return false;
            named[row.Look]++;
        }
        // A free entry is marked by -1 in place of the count of rows that name it, which must be 0.
        foreach (int entry in free)
        {
            if (entry >= end || named[entry] != 0)
                return false;
            named[entry] = -1;
        }
        int inUse = 0;
        for (int entry = 0; entry < end; entry++)
        {
            if (named[entry] < 0)
            {
                if (users[entry] != 0 || looks[entry] != default)
                    return false;
                continue;
            }
            inUse++;
            if (users[entry] != named[entry] || users[entry] == 0 || !looks[entry].IsValid)
                return false;
            if (!index.TryGetValue(looks[entry], out int found) || found != entry)
                return false;
        }
        return index.Count == inUse;
    }
}
