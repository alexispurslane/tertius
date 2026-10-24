namespace Tertius.Rendering.Tests;

public sealed class FreeListTests
{
    [Fact]
    public void AllocationsAreAlignedAndDoNotOverlap()
    {
        var list = new FreeList(1024);
        ulong a = list.Allocate(10, 1)!.Value;
        ulong b = list.Allocate(100, 64)!.Value;
        ulong c = list.Allocate(32, 256)!.Value;
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
        ulong[] offsets = new ulong[8];
        for (int i = 0; i < offsets.Length; i++)
            offsets[i] = list.Allocate(512, 16)!.Value;
        list.Allocate(1, 1).Should().BeNull("the list is full");
        list.FreeRangeCount.Should().Be(0);

        // Free in an order that exercises merging with the previous range, the next range and both.
        foreach (int i in new[] { 1, 3, 2, 6, 7, 0, 5, 4 })
            list.Free(offsets[i], 512);
        list.IsEmpty.Should().BeTrue();
        list.FreeRangeCount.Should().Be(1);
        ulong whole = list.Allocate(4096, 16)!.Value;
        whole.Should().Be(0ul);
    }

    [Fact]
    public void AlignmentGapsStayFreeAndAreReused()
    {
        var list = new FreeList(1024);
        ulong small = list.Allocate(8, 1)!.Value;
        ulong aligned = list.Allocate(256, 256)!.Value;
        aligned.Should().Be(256ul);
        // The gap from 8 to 256 is free and takes a later small allocation.
        ulong filler = list.Allocate(200, 8)!.Value;
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
        ulong offset = list.Allocate(64, 16)!.Value;
        list.Free(offset, 64);
        FluentActions.Invoking(() => list.Free(offset, 64)).Should().ThrowExactly<InvalidOperationException>();
        FluentActions
            .Invoking(() => list.Allocate(8, 3))
            .Should()
            .ThrowExactly<ArgumentException>("the alignment is not a power of two");
        FluentActions
            .Invoking(() => list.Allocate(0, 16))
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>();
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
            if (list.Allocate(size, alignment) is { } offset)
            {
                (offset % alignment).Should().Be(0ul);
                // One assertion per allocation, not per pair: this loop runs millions of comparisons.
                bool overlaps = live.Exists(range =>
                    !(offset + size <= range.Offset || range.Offset + range.Size <= offset)
                );
                overlaps
                    .Should()
                    .BeFalse(
                        "step {0} allocated [{1}, {2}), which must not overlap a live range",
                        step,
                        offset,
                        offset + size
                    );
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
