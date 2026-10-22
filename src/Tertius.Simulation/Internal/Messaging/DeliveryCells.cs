using System.Numerics;

namespace Tertius.Simulation;

/// <summary>One recipient of one routed event: the recipient's slot and the event's index in the routed list.</summary>
internal readonly record struct Delivery(uint Recipient, int Event);

/// <summary>
/// The deliveries of one routing, grouped by block and by slot range, so that the expansion and the sort of the
/// <see cref="Router"/> can each run in independent work items. A block is a contiguous run of routed events, and a slot
/// range is a contiguous run of entity slots, as wide as a power of two. Cell (b, r) holds the deliveries of block b
/// whose recipients lie in slot range r, in routing order.
/// </summary>
/// <remarks>
/// Each block writes only its own cells, and each slot range reads only its own, so work items never share a cell. The
/// cells of a slot range, read in block order, list its deliveries in routing order, because every event of a block comes
/// before every event of the next. A routing on the sync thread uses one block and one slot range, which holds every
/// delivery in routing order.
/// </remarks>
internal sealed class DeliveryCells
{
    /// <summary>The shift that puts every slot below 2^31 into slot range 0: the one slot range of <see cref="ShapeOne"/>.</summary>
    private const int OneRangeShift = 31;

    private int slotCount;
    private int rangeShift = OneRangeShift;

    private RefList<Delivery>[] cells = [new(256)];

    // Per slot range, the index in the sorted deliveries of its first delivery.
    private int[] rangeStarts = new int[1];

    /// <summary>How many blocks the routed events are cut into.</summary>
    public int BlockCount { get; private set; } = 1;

    /// <summary>How many slot ranges the slots are cut into.</summary>
    public int RangeCount { get; private set; } = 1;

    /// <summary>The number of deliveries in every cell, as of the last <see cref="PlaceRanges"/>.</summary>
    public int Count { get; private set; }

    /// <summary>One block and one slot range over <paramref name="slots"/> slots, for a routing on the sync thread.</summary>
    public void ShapeOne(int slots)
    {
        slotCount = slots;
        BlockCount = 1;
        RangeCount = 1;
        rangeShift = OneRangeShift;
    }

    /// <summary>
    /// <paramref name="workItems"/> blocks, and slot ranges over <paramref name="slots"/> slots of the smallest
    /// power-of-two width that needs no more than <paramref name="workItems"/> of them, for a routing on the workers.
    /// </summary>
    public void Shape(int workItems, int slots)
    {
        int items = Math.Max(1, workItems);
        uint width = BitOperations.RoundUpToPowerOf2((uint)Math.Max(1, (slots + items - 1) / items));
        slotCount = slots;
        BlockCount = items;
        rangeShift = BitOperations.Log2(width);
        RangeCount = Math.Max(1, (int)(((uint)slots + width - 1) >> rangeShift));

        int cellCount = BlockCount * RangeCount;
        if (cells.Length < cellCount)
        {
            int old = cells.Length;
            Array.Resize(ref cells, cellCount);
            for (int i = old; i < cellCount; i++)
                cells[i] = new(64);
        }

        if (rangeStarts.Length < RangeCount)
            rangeStarts = new int[RangeCount];
    }

    /// <summary>The cells of one block, emptied, for the expansion of that block.</summary>
    public BlockCells Block(int block)
    {
        Span<RefList<Delivery>> mine = cells.AsSpan(block * RangeCount, RangeCount);
        foreach (RefList<Delivery> cell in mine)
            cell.Clear();
        return new(mine, rangeShift);
    }

    /// <summary>
    /// Gives each slot range its place in the sorted deliveries: after the deliveries of the slot ranges before it, in
    /// slot order. Sets <see cref="Count"/>.
    /// </summary>
    public void PlaceRanges()
    {
        int start = 0;
        for (int r = 0; r < RangeCount; r++)
        {
            rangeStarts[r] = start;
            for (int b = 0; b < BlockCount; b++)
                start += cells[b * RangeCount + r].Count;
        }

        Count = start;
    }

    /// <summary>The first slot and the end of slot range <paramref name="range"/>.</summary>
    public (int First, int End) Slots(int range) =>
        (range << rangeShift, range == RangeCount - 1 ? slotCount : (range + 1) << rangeShift);

    /// <summary>Where the deliveries of slot range <paramref name="range"/> start in the sorted deliveries.</summary>
    public int RangeStart(int range) => rangeStarts[range];

    /// <summary>The deliveries of block <paramref name="block"/> to slot range <paramref name="range"/>, in routing order.</summary>
    public ReadOnlySpan<Delivery> Cell(int block, int range) => cells[block * RangeCount + range].AsSpan();

    /// <summary>The cells of one block: <see cref="Add"/> puts a delivery into the cell of its recipient's slot range.</summary>
    public readonly ref struct BlockCells
    {
        private readonly Span<RefList<Delivery>> cells;
        private readonly int rangeShift;

        public BlockCells(Span<RefList<Delivery>> cells, int rangeShift)
        {
            this.cells = cells;
            this.rangeShift = rangeShift;
        }

        public void Add(Delivery delivery) => cells[(int)(delivery.Recipient >> rangeShift)].Add(delivery);
    }
}
