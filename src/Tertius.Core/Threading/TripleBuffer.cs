namespace Tertius;

/// <summary>
/// Lock-free hand-over of the latest value from one writer thread to one reader thread. The writer fills
/// <see cref="WriteBuffer"/> and calls <see cref="Publish"/>. The reader calls <see cref="AcquireLatest"/>.
/// Neither side ever waits for the other.
/// </summary>
public sealed class TripleBuffer<T>(Func<T> factory)
    where T : class
{
    private const int FreshBit = 4;
    private readonly T[] buffers = [factory(), factory(), factory()];
    private int writeIndex;
    private int readIndex = 1;
    private int middle = 2;

    /// <summary>The buffer the writer owns. Valid until <see cref="Publish"/>.</summary>
    public T WriteBuffer => buffers[writeIndex];

    /// <summary>The buffer the reader owns, from the last successful acquire.</summary>
    public T ReadBuffer => buffers[readIndex];

    public void Publish()
    {
        int previous = Interlocked.Exchange(ref middle, writeIndex | FreshBit);
        writeIndex = previous & 3;
    }

    /// <summary>
    /// Takes the newest published buffer and returns it, if one arrived since the last call. Null when none arrived, and then
    /// <see cref="ReadBuffer"/> stays the current one.
    /// </summary>
    public T? AcquireLatest()
    {
        if ((Volatile.Read(ref middle) & FreshBit) == 0)
            return null;

        int previous = Interlocked.Exchange(ref middle, readIndex);
        readIndex = previous & 3;
        return buffers[readIndex];
    }
}
