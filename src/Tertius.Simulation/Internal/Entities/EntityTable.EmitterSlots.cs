using System.Numerics;

namespace Tertius.Simulation;

internal sealed partial class EntityTable
{
    /// <summary>
    /// The set of the slots whose front render row holds an emitter that can make puffs (see
    /// <see cref="PuffEmitter.CanMakePuffs"/>), one bit per slot: bit (slot % 64) of word slot / 64. The births of the puffs
    /// read it instead of every render row (docs/plans/particles.md, "Two per-slot values in the entity table"). The copy
    /// of a render row sets or clears the bit of its slot, the release of a slot clears it, and the copy after a spawn sets
    /// the bits of the spawns whose rows hold such an emitter (see <see cref="newEmitters"/>). It is private to the table,
    /// because slots are the table's private storage index.
    /// </summary>
    /// <remarks>
    /// The workers of a parallel <see cref="CopyDirtyRows"/> copy the rows of different chunks at once, and the slots of two
    /// chunks can share a word when a chunk holds fewer than 64 slots. So a change of a bit is an atomic or and an atomic and
    /// on its word. Each slot's bit has one writer, so the result is the same in any order.
    /// </remarks>
    private sealed class EmitterSlots
    {
        private ulong[] words = [];

        /// <summary>The number of words of 64 slots that the set holds.</summary>
        public int WordCount => words.Length;

        /// <summary>The bits of the 64 slots from 64 × <paramref name="index"/> on, the lowest bit for the lowest slot.</summary>
        public ulong Word(int index) => words[index];

        /// <summary>Makes the set cover <paramref name="capacity"/> slots. The slots that it covered keep their bits.</summary>
        public void Grow(int capacity) => Array.Resize(ref words, BodySlots.WordsFor(capacity));

        public bool Contains(uint slot) => (Volatile.Read(ref words[slot / 64]) & Bit(slot)) != 0;

        /// <summary>Sets the bit of <paramref name="slot"/> to <paramref name="emits"/>. Safe on the workers of a parallel copy.</summary>
        public void Set(uint slot, bool emits)
        {
            if (Contains(slot) == emits)
                return;
            ref ulong word = ref words[slot / 64];
            if (emits)
                Interlocked.Or(ref word, Bit(slot));
            else
                Interlocked.And(ref word, ~Bit(slot));
        }

        private static ulong Bit(uint slot) => 1UL << (int)(slot % 64);
    }

    // The slots whose front render row holds an emitter that can make puffs.
    private readonly EmitterSlots emitterSlots = new();

    // The entities that a spawn made live since the last CopyDirtyRows with an emitter that can make puffs. A spawn writes
    // both render rows, so no copy would set their bits. The next copy sets them, so between a spawn of step 6 and step 8 a new
    // entity's bit is clear: it was not in the step that the sync point closes. One that died since is skipped.
    private readonly List<Entity> newEmitters = new();

    /// <summary>
    /// The live entities whose front render row holds an emitter that can make puffs, in slot order, as handles: the set bits
    /// of the table's emitter slots, a word of 64 slots at a time. Use it with <c>foreach</c>. It allocates nothing. Nothing
    /// may release or make live an entity while it runs. Outside the gameplay part of a tick.
    /// </summary>
    public EmitterEntityEnumerator EmitterEntities => new(this);

    /// <summary>The enumerator of <see cref="EmitterEntities"/>.</summary>
    public struct EmitterEntityEnumerator
    {
        private readonly EntityTable? table;
        private readonly int words;
        private int word;
        private ulong bits;
        private Entity current;

        internal EmitterEntityEnumerator(EntityTable table)
        {
            this.table = table;
            words = BodySlots.WordsFor(table.SlotCount);
            word = -1;
            bits = 0;
            current = default;
        }

        public readonly EmitterEntityEnumerator GetEnumerator() => this;

        public readonly Entity Current => current;

        public bool MoveNext()
        {
            while (bits == 0)
            {
                if (++word >= words)
                    return false;
                bits = table!.emitterSlots.Word(word);
            }
            uint slot = (uint)(word * 64 + BitOperations.TrailingZeroCount(bits));
            bits &= bits - 1;
            current = new Entity(slot, table!.generation[slot]);
            return true;
        }
    }

    /// <summary>Sets the emitter bits of the entities that spawns made live since the last copy. Part of <see cref="CopyDirtyRows"/>.</summary>
    private void SetNewEmitterSlots()
    {
        foreach (Entity entity in newEmitters)
            if (IsAlive(entity))
                emitterSlots.Set(entity.Index, frontRender[entity.Index].Emitter.CanMakePuffs);
        newEmitters.Clear();
    }

    /// <summary>
    /// The invariant of the emitter bits: a slot's bit is set exactly when it is live, its front render row holds an emitter
    /// that can make puffs, and no spawn since the last copy waits to set it. Part of <see cref="AssertInvariants"/>.
    /// </summary>
    private void AssertEmitterInvariants()
    {
        Contract.Invariant(emitterSlots.WordCount == BodySlots.WordsFor(capacity));
        var waiting = new bool[capacity];
        foreach (Entity entity in newEmitters)
            if (IsAlive(entity))
                waiting[entity.Index] = true;
        for (int slot = 0; slot < capacity; slot++)
        {
            bool emits = state[slot] == SlotState.Live && !waiting[slot] && frontRender[slot].Emitter.CanMakePuffs;
            Contract.Invariant(emitterSlots.Contains((uint)slot) == emits);
        }
    }
}
