using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

public sealed class FreeListTests
{
    [Fact]
    public void AllocationsAreAlignedAndDoNotOverlap()
    {
        var list = new FreeList(1024);
        Assert.True(list.TryAllocate(10, 1, out ulong a));
        Assert.True(list.TryAllocate(100, 64, out ulong b));
        Assert.True(list.TryAllocate(32, 256, out ulong c));
        Assert.Equal(0ul, a);
        Assert.Equal(0ul, b % 64);
        Assert.Equal(0ul, c % 256);
        Assert.True(b >= a + 10);
        Assert.True(c >= b + 100);
        Assert.Equal(1024ul - 142, list.FreeBytes);
    }

    [Fact]
    public void FreeingMergesNeighborsBackIntoOneRange()
    {
        var list = new FreeList(4096);
        var offsets = new ulong[8];
        for (int i = 0; i < offsets.Length; i++)
            Assert.True(list.TryAllocate(512, 16, out offsets[i]));
        Assert.False(list.TryAllocate(1, 1, out _));
        Assert.Equal(0, list.FreeRangeCount);

        // Free in an order that exercises merging with the previous range, the next range and both.
        foreach (int i in new[] { 1, 3, 2, 6, 7, 0, 5, 4 })
            list.Free(offsets[i], 512);
        Assert.True(list.IsEmpty);
        Assert.Equal(1, list.FreeRangeCount);
        Assert.True(list.TryAllocate(4096, 16, out ulong whole));
        Assert.Equal(0ul, whole);
    }

    [Fact]
    public void AlignmentGapsStayFreeAndAreReused()
    {
        var list = new FreeList(1024);
        Assert.True(list.TryAllocate(8, 1, out ulong small));
        Assert.True(list.TryAllocate(256, 256, out ulong aligned));
        Assert.Equal(256ul, aligned);
        // The gap from 8 to 256 is free and takes a later small allocation.
        Assert.True(list.TryAllocate(200, 8, out ulong filler));
        Assert.InRange(filler, small + 8, aligned - 200);
        list.Free(aligned, 256);
        list.Free(filler, 200);
        list.Free(small, 8);
        Assert.True(list.IsEmpty);
        Assert.Equal(1, list.FreeRangeCount);
    }

    [Fact]
    public void DoubleFreeAndBadArgumentsThrow()
    {
        var list = new FreeList(256);
        Assert.True(list.TryAllocate(64, 16, out ulong offset));
        list.Free(offset, 64);
        Assert.Throws<InvalidOperationException>(() => list.Free(offset, 64));
        Assert.Throws<ArgumentException>(() => list.TryAllocate(8, 3, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.TryAllocate(0, 16, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.Free(250, 16));
    }

    [Fact]
    public void RandomAllocationsKeepTheBookkeepingConsistent()
    {
        var random = new Random(1234);
        var list = new FreeList(1 << 20);
        var live = new List<(ulong Offset, ulong Size)>();
        for (int step = 0; step < 5000; step++)
        {
            if (live.Count > 0 && random.Next(3) == 0)
            {
                int index = random.Next(live.Count);
                list.Free(live[index].Offset, live[index].Size);
                live.RemoveAt(index);
                continue;
            }
            ulong size = (ulong)random.Next(1, 4096);
            ulong alignment = 1ul << random.Next(0, 9);
            if (list.TryAllocate(size, alignment, out ulong offset))
            {
                Assert.Equal(0ul, offset % alignment);
                foreach ((ulong o, ulong s) in live)
                    Assert.True(offset + size <= o || o + s <= offset, "overlap");
                live.Add((offset, size));
            }
        }
        ulong used = 0;
        foreach ((_, ulong s) in live)
            used += s;
        Assert.Equal(list.Capacity - used, list.FreeBytes);
        foreach ((ulong o, ulong s) in live)
            list.Free(o, s);
        Assert.True(list.IsEmpty);
        Assert.Equal(1, list.FreeRangeCount);
    }
}
