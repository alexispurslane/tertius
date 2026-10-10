namespace Tertius.Rendering;

/// <summary>
/// Holds GPU resources that commands already recorded may still use, such as a vertex buffer replaced by a larger
/// one. A resource retired while frame N is recorded is destroyed once frame N is known to be complete.
/// </summary>
internal sealed class DeferredReleases : IDisposable
{
    private readonly Queue<(long Frame, IDisposable Resource)> pending = new();

    public int Count => pending.Count;

    public void Retire(IDisposable resource, long frame) => pending.Enqueue((frame, resource));

    /// <summary>Destroys everything retired during frames up to and including <paramref name="completedFrame"/>.</summary>
    public void Collect(long completedFrame)
    {
        while (pending.Count > 0 && pending.Peek().Frame <= completedFrame)
            pending.Dequeue().Resource.Dispose();
    }

    /// <summary>Destroys everything. The device must be idle.</summary>
    public void Dispose()
    {
        while (pending.Count > 0)
            pending.Dequeue().Resource.Dispose();
    }
}
