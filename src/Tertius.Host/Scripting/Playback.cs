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
    /// Draws the world's current state from its camera with interpolation alpha 1, and gives it to the playback's capture
    /// target as the capture named <paramref name="name"/>: a PNG file of that name, or the next frame of a video. Returns
    /// false to stop the run instead, for example when the user closed the window while the capture waited.
    /// </summary>
    bool Capture(World world, string name);
}

/// <summary>
/// A headless run: ticks back to back, and captures drawn on the calling thread with a headless renderer at the size of
/// the script's window, into the run's capture target. For a <see cref="PngFolder"/>, each capture is a PNG file of its
/// name. For a <see cref="VideoFile"/>, the playback starts one ffmpeg process when it is made, and each capture is the
/// next frame of the video. A run that reaches its end calls <see cref="Finish"/>. Every way out disposes the playback,
/// which ends the video, so the video plays after a failure too.
/// </summary>
public sealed class HeadlessPlayback : IPlayback, IDisposable
{
    private readonly IRenderer renderer;
    private readonly CaptureTarget target;
    // The sink of the video target for the whole run, or null for a PNG folder.
    private readonly FfmpegVideoSink? video;

    /// <summary>
    /// Resizes <paramref name="renderer"/> to <paramref name="size"/>, whatever size it was made with. For a video target,
    /// starts ffmpeg (<see cref="FfmpegVideoSink.Start"/>), and throws its exception if ffmpeg cannot start or the
    /// folder of the video cannot be made.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="target"/> is the default value, which holds no case.</exception>
    public HeadlessPlayback(IRenderer renderer, ScriptWindow size, CaptureTarget target) : this(renderer, size, target, FfmpegVideoSink.Start)
    {
    }

    /// <summary>
    /// <see cref="HeadlessPlayback(IRenderer, ScriptWindow, CaptureTarget)"/> with the function that starts the video's
    /// ffmpeg, so that the command can take it from a test.
    /// </summary>
    internal HeadlessPlayback(IRenderer renderer, ScriptWindow size, CaptureTarget target, Func<VideoFile, ScriptWindow, FfmpegVideoSink> startVideo)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(startVideo);
        if (!target.HasValue)
            throw new ArgumentException("A headless playback needs a capture target.", nameof(target));
        this.renderer = renderer;
        this.target = target;
        renderer.Resize(size.Width, size.Height);
        video = target.TryGetValue(out VideoFile? file) ? startVideo(file, size) : null;
    }

    public bool WaitForTick() => true;

    /// <summary>
    /// The render snapshot is built now, so a capture before the first tick, or right after a camera step, shows the
    /// current state. This thread publishes the snapshot and reads it, so it is the triple buffer's only reader.
    /// </summary>
    /// <exception cref="VideoStreamException">The target is a video, and ffmpeg stopped reading its frames.</exception>
    public bool Capture(World world, string name)
    {
        ICaptureSink sink = target switch
        {
            PngFolder folder => new PngCaptureSink(folder.PathOf(name)),
            VideoFile => video!,
            _ => throw new InvalidOperationException($"The headless playback has no sink for the target {target}."),
        };
        world.PublishRenderSnapshot();
        // A snapshot was published just now, so the latest one is that one.
        world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot);
        renderer.CaptureNextFrame(sink);
        renderer.Render(snapshot, alpha: 1f);
        return true;
    }

    /// <summary>
    /// Ends the captures of a run that reached its end. For a video, closes ffmpeg's input and waits until ffmpeg has
    /// written the end of the file. A PNG folder has nothing to end.
    /// </summary>
    /// <exception cref="VideoStreamException">ffmpeg failed at the end of the video.</exception>
    public void Finish() => video?.Finish();

    /// <summary>Ends the video, if <see cref="Finish"/> did not, without reporting a failure of ffmpeg.</summary>
    public void Dispose() => video?.Dispose();
}
