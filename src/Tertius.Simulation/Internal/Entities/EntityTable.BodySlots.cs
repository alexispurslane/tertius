using System.Numerics;

namespace Tertius.Simulation;

internal sealed partial class EntityTable
{
    /// <summary>
    /// The set of the slots that are live with a body, one bit per slot: bit (slot % 64) of word slot / 64. A pass over the
    /// bodies (see <see cref="BodyEntities"/>) reads it a word at a time, so it costs a step per 64 slots and one per body.
    /// The table adds a slot when it makes it live with a body and removes it when it releases it. A body is fixed from the
    /// spawn to the release, so nothing else changes the set. It is private to the table, because slots are the table's
    /// private storage index.
    /// </summary>
    private sealed class BodySlots
    {
        private ulong[] words = [];

        /// <summary>The number of words of 64 slots that the set holds.</summary>
        public int WordCount => words.Length;

        /// <summary>The bits of the 64 slots from 64 × <paramref name="index"/> on, the lowest bit for the lowest slot.</summary>
        public ulong Word(int index) => words[index];

        /// <summary>Makes the set cover <paramref name="capacity"/> slots. The slots that it covered keep their bits.</summary>
        public void Grow(int capacity) => Array.Resize(ref words, WordsFor(capacity));

        public void Add(uint slot) => words[slot / 64] |= Bit(slot);

        public void Remove(uint slot) => words[slot / 64] &= ~Bit(slot);

        public bool Contains(uint slot) => (words[slot / 64] & Bit(slot)) != 0;

        /// <summary>The number of words that cover <paramref name="slots"/> slots.</summary>
        public static int WordsFor(int slots) => (slots + 63) / 64;

        private static ulong Bit(uint slot) => 1UL << (int)(slot % 64);
    }

    /// <summary>
    /// The live entities with a body, in slot order, as handles: the set bits of the table's <see cref="BodySlots"/>, a
    /// word of 64 slots at a time. Use it with <c>foreach</c>; it allocates nothing. Nothing may release or make live an
    /// entity while it runs.
    /// </summary>
    public struct BodyEntityEnumerator
    {
        private readonly EntityTable? table;
        private readonly int words;
        private int word;
        private ulong bits;
        private Entity current;

        internal BodyEntityEnumerator(EntityTable table)
        {
            this.table = table;
            words = BodySlots.WordsFor(table.SlotCount);
            word = -1;
            bits = 0;
            current = default;
        }

        public readonly BodyEntityEnumerator GetEnumerator() => this;

        public readonly Entity Current => current;

        public bool MoveNext()
        {
            while (bits == 0)
            {
                if (++word >= words)
                    return false;
                bits = table!.bodySlots.Word(word);
            }
            uint slot = (uint)(word * 64 + BitOperations.TrailingZeroCount(bits));
            bits &= bits - 1;
            current = new Entity(slot, table!.generation[slot]);
            return true;
        }
    }
}
