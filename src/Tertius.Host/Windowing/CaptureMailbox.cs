namespace Tertius.Host.Windowing;

/// <summary>
/// One capture at a time from the simulation thread, which asks and waits, to the render thread, which draws it and
/// saves it.
/// </summary>
public sealed class CaptureMailbox
{
    private readonly SemaphoreSlim saved = new(0);
    private string? pending;

    /// <summary>
    /// Asks for a capture to <paramref name="path"/> and waits until the render thread has saved it. Throws
    /// <see cref="OperationCanceledException"/> if <paramref name="stop"/> fires first.
    /// </summary>
    public void RequestAndWait(string path, CancellationToken stop)
    {
        Volatile.Write(ref pending, path);
        try
        {
            saved.Wait(stop);
        }
        catch (OperationCanceledException)
        {
            // The window closed: take back the request, unless the render thread took it already.
            Interlocked.CompareExchange(ref pending, null, path);
            throw;
        }
    }

    /// <summary>Takes the path of the capture that waits, if there is one. On the render thread.</summary>
    public bool TryTake(out string path)
    {
        string? taken = Interlocked.Exchange(ref pending, null);
        path = taken ?? "";
        return taken != null;
    }

    /// <summary>Tells the waiting simulation thread that the capture it asked for is saved. On the render thread.</summary>
    public void Done() => saved.Release();
}
