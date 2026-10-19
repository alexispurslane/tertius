using Tertius.Rendering;
using Tertius.Simulation;

namespace Tertius.Host.Scripting;

/// <summary>
/// How a scripted run meets the world outside the simulation: when each tick may run, and how a capture is drawn. A run
/// with no playback runs its ticks back to back and cannot capture.
/// </summary>
public interface IPlayback
{
    /// <summary>Waits until the next tick may run. Returns false to stop the run, for example when the user closed the window.</summary>
    bool WaitForTick();

    /// <summary>
    /// Draws the world's current state from its camera with interpolation alpha 1, and writes it to <paramref name="path"/>.
    /// Returns false to stop the run instead, for example when the user closed the window while the capture waited.
    /// </summary>
    bool Capture(World world, string path);
}

/// <summary>
/// A headless run: ticks back to back, and captures drawn on the calling thread with a headless renderer at the size of
/// the script's window.
/// </summary>
public sealed class HeadlessPlayback : IPlayback
{
    private readonly IRenderer renderer;

    /// <summary>Resizes <paramref name="renderer"/> to <paramref name="size"/>, whatever size it was made with.</summary>
    public HeadlessPlayback(IRenderer renderer, ScriptWindow size)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(size);
        this.renderer = renderer;
        renderer.Resize(size.Width, size.Height);
    }

    public bool WaitForTick() => true;

    /// <summary>
    /// The render snapshot is built now, so a capture before the first tick, or right after a camera step, shows the
    /// current state. This thread publishes the snapshot and reads it, so it is the triple buffer's only reader.
    /// </summary>
    public bool Capture(World world, string path)
    {
        world.PublishRenderSnapshot();
        // A snapshot was published just now, so the latest one is that one.
        world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot);
        renderer.CaptureNextFrame(new PngCaptureSink(path));
        renderer.Render(snapshot, alpha: 1f);
        return true;
    }
}
