using System.Diagnostics.CodeAnalysis;

namespace Tertius.Host.Windowing;

/// <summary>
/// One capture at a time from the simulation thread, which asks and waits, to the render thread, which draws it and
/// hands its pixels to the capture's sink.
/// </summary>
[SuppressMessage("Design", "CA1001",
    Justification =
        "A SemaphoreSlim holds an OS handle only after a read of AvailableWaitHandle, which this class never does.")]
public sealed class CaptureMailbox
{
    private readonly SemaphoreSlim handedOver = new(0);
    private ICaptureSink? pending;

    /// <summary>
    /// Asks for a capture into <paramref name="sink"/> and waits until the render thread has handed the sink its
    /// pixels. Throws <see cref="OperationCanceledException"/> if <paramref name="stop"/> fires first.
    /// </summary>
    public void RequestAndWait(ICaptureSink sink, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Volatile.Write(ref pending, sink);
        try
        {
            handedOver.Wait(stop);
        }
        catch (OperationCanceledException)
        {
            // The window closed: take back the request, unless the render thread took it already.
            Interlocked.CompareExchange(ref pending, null, sink);
            throw;
        }
    }

    /// <summary>Takes the sink of the capture that waits, if there is one. On the render thread.</summary>
    public bool TryTake([NotNullWhen(true)] out ICaptureSink? sink)
    {
        sink = Interlocked.Exchange(ref pending, null);
        return sink != null;
    }

    /// <summary>Tells the waiting simulation thread that its sink has the pixels of the capture. On the render thread.</summary>
    public void Done() => handedOver.Release();
}
