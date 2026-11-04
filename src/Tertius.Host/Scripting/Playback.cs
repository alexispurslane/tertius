using System.Diagnostics;
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
/// next frame of the video. For <see cref="Discard"/>, each capture is drawn, read back and dropped. A renderer that keeps
/// its captures in flight hands a capture to its sink during a later capture. A run that reaches its end calls
/// <see cref="FinishCaptures"/>, then <see cref="Finish"/>. Every way out disposes the playback, which ends the video, so the video plays after
/// a failure too.
/// </summary>
public sealed class HeadlessPlayback : IPlayback, IDisposable
{
    private readonly IRenderer renderer;

    private readonly CaptureTarget target;

    // The sink of the video target for the whole run, or null for a PNG folder.
    private readonly FfmpegVideoSink? video;

    // What runs after the sink of each capture took its pixels, such as the command's read of the renderer's readings, or
    // null.
    private readonly Action? handedOver;

    /// <summary>
    /// Resizes <paramref name="renderer"/> to <paramref name="size"/>, whatever size it was made with. For a video target,
    /// starts ffmpeg (<see cref="FfmpegVideoSink.Start(VideoFile, ScriptWindow)"/>), and throws its exception if ffmpeg cannot start or the
    /// folder of the video cannot be made.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="target"/> is the default value, which holds no case.</exception>
    public HeadlessPlayback(IRenderer renderer, ScriptWindow size, CaptureTarget target)
        : this(renderer: renderer, size: size, target: target, startVideo: FfmpegVideoSink.Start, handedOver: null) { }

    /// <summary>
    /// <see cref="HeadlessPlayback(IRenderer, ScriptWindow, CaptureTarget)"/> with the function that starts the video's
    /// ffmpeg, so that the command can take it from a test, and <paramref name="handedOver"/>, which runs after the sink
    /// of each capture took its pixels, on the thread of the hand-over, or null.
    /// </summary>
    internal HeadlessPlayback(
        IRenderer renderer,
        ScriptWindow size,
        CaptureTarget target,
        Func<VideoFile, ScriptWindow, FfmpegVideoSink> startVideo,
        Action? handedOver
    )
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(startVideo);
        if (target is null)
            throw new ArgumentException("A headless playback needs a capture target.", nameof(target));
        this.renderer = renderer;
        this.target = target;
        this.handedOver = handedOver;
        renderer.Resize(size.Width, size.Height);
        video = target is VideoFile file ? startVideo(file, size) : null;
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
            VideoFile => video ?? throw new UnreachableException("The constructor starts the video of a video target."),
            Discard => DroppedCapture.Sink,
            null => throw new InvalidOperationException("The headless playback has no capture target."),
        };
        world.PublishRenderSnapshot();
        // A snapshot was published just now, so the latest one is that one.
        RenderSnapshot snapshot = world.RenderSnapshots.AcquireLatest() ?? world.RenderSnapshots.ReadBuffer;
        renderer.CaptureNextFrame(handedOver is { } after ? new HandOverSink(sink, after) : sink);
        renderer.Render(snapshot, alpha: 1f);
        return true;
    }

    /// <summary>
    /// Hands every capture that the renderer still holds in flight to its sink, oldest first. A renderer whose captures
    /// wait for their frames holds none.
    /// </summary>
    /// <exception cref="VideoStreamException">The target is a video, and ffmpeg stopped reading its frames.</exception>
    public void FinishCaptures() => renderer.FinishCaptures();

    /// <summary>
    /// Ends the captures of a run that reached its end: hands over those still in flight (<see cref="FinishCaptures"/>),
    /// and for a video, closes ffmpeg's input and waits until ffmpeg has written the end of the file. A PNG folder has
    /// nothing more to end.
    /// </summary>
    /// <exception cref="VideoStreamException">ffmpeg failed at the end of the video.</exception>
    public void Finish()
    {
        FinishCaptures();
        video?.Finish();
    }

    /// <summary>Ends the video, if <see cref="Finish"/> did not, without reporting a failure of ffmpeg.</summary>
    public void Dispose() => video?.Dispose();

    /// <summary>A sink that hands each capture to <paramref name="inner"/>, then runs <paramref name="after"/>.</summary>
    private sealed class HandOverSink(ICaptureSink inner, Action after) : ICaptureSink
    {
        public void Take(in CapturedImage image)
        {
            inner.Take(in image);
            after();
        }
    }

    /// <summary>The sink of the <see cref="Discard"/> target: it takes the pixels that the renderer read back, and keeps none.</summary>
    private sealed class DroppedCapture : ICaptureSink
    {
        public static readonly DroppedCapture Sink = new();

        public void Take(in CapturedImage image) { }
    }
}
