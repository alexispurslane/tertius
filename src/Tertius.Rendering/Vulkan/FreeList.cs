namespace Tertius.Rendering;

/// <summary>
/// First-fit range allocator for one memory block. Free ranges are kept sorted by offset and merged with their
/// neighbors on free, so the list stays short. Pure bookkeeping without Vulkan calls.
/// </summary>
internal sealed class FreeList
{
    private readonly List<Range> free = [];

    public FreeList(ulong capacity)
    {
        ArgumentOutOfRangeException.ThrowIfZero(capacity);
        Capacity = capacity;
        FreeBytes = capacity;
        free.Add(new(0, capacity));
    }

    public ulong Capacity { get; }
    public ulong FreeBytes { get; private set; }
    public bool IsEmpty => FreeBytes == Capacity;

    /// <summary>The number of separate free ranges. A test hook for fragmentation.</summary>
    public int FreeRangeCount => free.Count;

    /// <summary>Takes <paramref name="size"/> bytes at an offset that is a multiple of <paramref name="alignment"/>.</summary>
    public bool TryAllocate(ulong size, ulong alignment, out ulong offset)
    {
        ArgumentOutOfRangeException.ThrowIfZero(size);
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentException("Alignment must be a power of two.", nameof(alignment));

        for (int i = 0; i < free.Count; i++)
        {
            Range range = free[i];
            ulong aligned = (range.Offset + alignment - 1) & ~(alignment - 1);
            ulong end = range.Offset + range.Size;
            if (aligned + size > end || aligned + size < aligned)
                continue;

            // Keep the alignment gap in front and the remainder behind as free ranges.
            ulong front = aligned - range.Offset;
            ulong back = end - (aligned + size);
            if (front > 0 && back > 0)
            {
                free[i] = new(range.Offset, front);
                free.Insert(i + 1, new(aligned + size, back));
            }
            else if (front > 0)
            {
                free[i] = new(range.Offset, front);
            }
            else if (back > 0)
            {
                free[i] = new(aligned + size, back);
            }
            else
            {
                free.RemoveAt(i);
            }

            FreeBytes -= size;
            offset = aligned;
            return true;
        }

        offset = 0;
        return false;
    }

    /// <summary>Returns a range that <see cref="TryAllocate"/> handed out.</summary>
    public void Free(ulong offset, ulong size)
    {
        if (size == 0 || offset + size > Capacity)
            throw new ArgumentOutOfRangeException(nameof(size));

        // Index of the first free range that starts after the freed one.
        int lo = 0, hi = free.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (free[mid].Offset < offset)
                lo = mid + 1;
            else
                hi = mid;
        }

        int next = lo;
        bool mergePrevious = next > 0 && free[next - 1].Offset + free[next - 1].Size == offset;
        bool mergeNext = next < free.Count && offset + size == free[next].Offset;
        if ((next > 0 && free[next - 1].Offset + free[next - 1].Size > offset) ||
            (next < free.Count && offset + size > free[next].Offset))
            throw new InvalidOperationException($"Double free or overlap at offset {offset}, size {size}.");

        if (mergePrevious && mergeNext)
        {
            Range previous = free[next - 1];
            free[next - 1] = new(previous.Offset, previous.Size + size + free[next].Size);
            free.RemoveAt(next);
        }
        else if (mergePrevious)
        {
            Range previous = free[next - 1];
            free[next - 1] = new(previous.Offset, previous.Size + size);
        }
        else if (mergeNext)
        {
            free[next] = new(offset, size + free[next].Size);
        }
        else
        {
            free.Insert(next, new(offset, size));
        }

        FreeBytes += size;
    }

    private readonly record struct Range(ulong Offset, ulong Size);
}
