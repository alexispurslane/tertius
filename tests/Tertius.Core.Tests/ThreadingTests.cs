namespace Tertius.Core.Tests;

public sealed class ThreadingTests
{
    private sealed class Box
    {
        public long Value;
    }

    [Fact]
    public async Task TripleBufferDeliversLatestAndNeverTears()
    {
        var buffer = new TripleBuffer<Box>(() => new());
        const long last = 200_000;
        Task writer = Task.Run(() =>
        {
            for (long i = 1; i <= last; i++)
            {
                buffer.WriteBuffer.Value = i;
                buffer.Publish();
            }
        }, TestContext.Current.CancellationToken);

        long seen = 0;
        while (seen < last)
            if (buffer.TryAcquireLatest(out Box box))
            {
                Assert.True(box.Value > seen, $"went backward: {box.Value} after {seen}");
                seen = box.Value;
            }

        await writer;
        Assert.Equal(last, seen);
    }

    [Fact]
    public void TripleBufferReportsNothingNew()
    {
        var buffer = new TripleBuffer<Box>(() => new());
        Assert.False(buffer.TryAcquireLatest(out _));
        buffer.WriteBuffer.Value = 7;
        buffer.Publish();
        Assert.True(buffer.TryAcquireLatest(out Box b));
        Assert.Equal(7, b.Value);
        Assert.False(buffer.TryAcquireLatest(out Box same));
        Assert.Same(b, same);
    }
}
