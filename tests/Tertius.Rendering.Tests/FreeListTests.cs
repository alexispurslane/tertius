using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

public sealed class FreeListTests
{
    [Fact]
    public void AllocationsAreAlignedAndDoNotOverlap()
    {
        var list = new FreeList(1024);
        list.TryAllocate(10, 1, out ulong a).Should().BeTrue();
        list.TryAllocate(100, 64, out ulong b).Should().BeTrue();
        list.TryAllocate(32, 256, out ulong c).Should().BeTrue();
        a.Should().Be(0ul);
        (b % 64).Should().Be(0ul);
        (c % 256).Should().Be(0ul);
        b.Should().BeGreaterThanOrEqualTo(a + 10);
        c.Should().BeGreaterThanOrEqualTo(b + 100);
        list.FreeBytes.Should().Be(1024ul - 142);
    }

    [Fact]
    public void FreeingMergesNeighborsBackIntoOneRange()
    {
        var list = new FreeList(4096);
        var offsets = new ulong[8];
        for (int i = 0; i < offsets.Length; i++)
            list.TryAllocate(512, 16, out offsets[i]).Should().BeTrue();
        list.TryAllocate(1, 1, out _).Should().BeFalse("the list is full");
        list.FreeRangeCount.Should().Be(0);

        // Free in an order that exercises merging with the previous range, the next range and both.
        foreach (int i in new[] { 1, 3, 2, 6, 7, 0, 5, 4 })
            list.Free(offsets[i], 512);
        list.IsEmpty.Should().BeTrue();
        list.FreeRangeCount.Should().Be(1);
        list.TryAllocate(4096, 16, out ulong whole).Should().BeTrue();
        whole.Should().Be(0ul);
    }

    [Fact]
    public void AlignmentGapsStayFreeAndAreReused()
    {
        var list = new FreeList(1024);
        list.TryAllocate(8, 1, out ulong small).Should().BeTrue();
        list.TryAllocate(256, 256, out ulong aligned).Should().BeTrue();
        aligned.Should().Be(256ul);
        // The gap from 8 to 256 is free and takes a later small allocation.
        list.TryAllocate(200, 8, out ulong filler).Should().BeTrue();
        filler.Should().BeInRange(small + 8, aligned - 200);
        list.Free(aligned, 256);
        list.Free(filler, 200);
        list.Free(small, 8);
        list.IsEmpty.Should().BeTrue();
        list.FreeRangeCount.Should().Be(1);
    }

    [Fact]
    public void DoubleFreeAndBadArgumentsThrow()
    {
        var list = new FreeList(256);
        list.TryAllocate(64, 16, out ulong offset).Should().BeTrue();
        list.Free(offset, 64);
        FluentActions.Invoking(() => list.Free(offset, 64)).Should().ThrowExactly<InvalidOperationException>();
        FluentActions.Invoking(() => list.TryAllocate(8, 3, out _)).Should().ThrowExactly<ArgumentException>("the alignment is not a power of two");
        FluentActions.Invoking(() => list.TryAllocate(0, 16, out _)).Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => list.Free(250, 16)).Should().ThrowExactly<ArgumentOutOfRangeException>();
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
                (offset % alignment).Should().Be(0ul);
                // One assertion per allocation, not per pair: this loop runs millions of comparisons.
                bool overlaps = live.Exists(range => !(offset + size <= range.Offset || range.Offset + range.Size <= offset));
                overlaps.Should().BeFalse("step {0} allocated [{1}, {2}), which must not overlap a live range", step, offset, offset + size);
                live.Add((offset, size));
            }
        }
        ulong used = 0;
        foreach ((_, ulong s) in live)
            used += s;
        list.FreeBytes.Should().Be(list.Capacity - used);
        foreach ((ulong o, ulong s) in live)
            list.Free(o, s);
        list.IsEmpty.Should().BeTrue();
        list.FreeRangeCount.Should().Be(1);
    }
}
