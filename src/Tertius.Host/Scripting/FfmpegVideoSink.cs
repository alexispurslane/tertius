using System.Diagnostics;
using System.Globalization;

namespace Tertius.Host.Scripting;

/// <summary>
/// A capture sink that makes each capture the next frame of one video, which one ffmpeg process encodes. It writes the
/// pixels of each frame straight into ffmpeg's standard input, on the thread that calls <see cref="Take"/>, with no copy
/// and no allocation. ffmpeg encodes them losslessly with <c>libx264rgb</c> at quantizer 0 into a Matroska file, which the
/// edit encodes again. See docs/plans/blue-danube-demo.md, change 1.
/// <para>A run that reaches its end calls <see cref="Finish"/>, which reports a failure of ffmpeg. Every other way out
/// calls only <see cref="Dispose"/>, which ends the file too, so the video plays and holds every frame before the failure.
/// A video that took no frame leaves no file.</para>
/// <para>ffmpeg comes from the dev shell (<c>ffmpeg-headless</c> in the flake), on the search path.</para>
/// </summary>
public sealed class FfmpegVideoSink : ICaptureSink, IDisposable
{
    /// <summary>How many of the last lines of ffmpeg's error output a failure reports.</summary>
    public const int ErrorLinesKept = 20;

    private readonly Process ffmpeg;
    private readonly Stream input;
    private readonly string path;
    private readonly int width, height;
    // The last lines of ffmpeg's error output. A thread-pool callback adds them as ffmpeg writes them, so that ffmpeg never
    // blocks on a full error pipe.
    private readonly Queue<string> errorLines = new();
    private int framesWritten;
    // True once ffmpeg's input is closed and ffmpeg has exited.
    private bool ended;
    private bool disposed;

    private FfmpegVideoSink(Process ffmpeg, string path, int width, int height)
    {
        this.ffmpeg = ffmpeg;
        this.path = path;
        this.width = width;
        this.height = height;
        input = ffmpeg.StandardInput.BaseStream;
        ffmpeg.ErrorDataReceived += KeepErrorLine;
        ffmpeg.BeginErrorReadLine();
    }

    /// <summary>
    /// Starts ffmpeg for a video of <paramref name="size"/> into <paramref name="video"/>, and makes the folder of the
    /// file if it is missing. ffmpeg replaces an older file at that path.
    /// </summary>
    /// <exception cref="System.ComponentModel.Win32Exception">ffmpeg is not on the search path, or cannot start.</exception>
    public static FfmpegVideoSink Start(VideoFile video, ScriptWindow size)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(size);
        string full = Path.GetFullPath(video.Path);
        if (Path.GetDirectoryName(full) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("ffmpeg") { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in Arguments(full, size.Width, size.Height, video.FramesPerSecond))
            start.ArgumentList.Add(argument);
        var ffmpeg = new Process { StartInfo = start };
        try
        {
            ffmpeg.Start();
            return new FfmpegVideoSink(ffmpeg, full, size.Width, size.Height);
        }
        catch
        {
            ffmpeg.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The command line of ffmpeg: raw frames of <c>rgb0</c> pixels from standard input, so ffmpeg ignores the fourth byte
    /// of each pixel, into a lossless video. Matroska stays readable after ffmpeg stops early, before it writes the end of
    /// the file, where an MP4 file without its index at the end does not play. <c>-y</c> replaces an older file, so a
    /// shorter second run leaves no frame of the first one.
    /// </summary>
    private static string[] Arguments(string path, int width, int height, int framesPerSecond) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostats",
        "-f", "rawvideo", "-pixel_format", "rgb0", "-video_size", $"{width}x{height}",
        "-framerate", framesPerSecond.ToString(CultureInfo.InvariantCulture), "-i", "pipe:0",
        "-c:v", "libx264rgb", "-qp", "0", "-preset", "veryfast", "-f", "matroska", "-y", path,
    ];

    /// <summary>How many frames went whole into ffmpeg's input.</summary>
    public int FramesWritten => framesWritten;

    /// <summary>
    /// Writes the pixels of the next frame into ffmpeg's input. The write blocks only while ffmpeg's input queue is full.
    /// </summary>
    /// <exception cref="ArgumentException">The image is not the size of the video.</exception>
    /// <exception cref="InvalidOperationException">The video has ended.</exception>
    /// <exception cref="VideoStreamException">ffmpeg stopped reading, for example because it was killed or its disk is full.</exception>
    public void Take(in CapturedImage image)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ended)
            throw new InvalidOperationException($"The video {path} has ended, so it takes no more frames.");
        if (image.Width != width || image.Height != height)
            throw new ArgumentException($"A frame of {image.Width}x{image.Height} pixels does not fit the video {path}, which is {width}x{height}.", nameof(image));
        try
        {
            input.Write(image.Rgba);
        }
        // A broken pipe: ffmpeg has exited, or is about to.
        catch (IOException ex)
        {
            End();
            throw Failure(ex);
        }
        framesWritten++;
    }

    /// <summary>
    /// Closes ffmpeg's input and waits until ffmpeg has written the end of the file and exited.
    /// </summary>
    /// <exception cref="InvalidOperationException">The video has ended already.</exception>
    /// <exception cref="VideoStreamException">ffmpeg exited with a code other than 0.</exception>
    public void Finish()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ended)
            throw new InvalidOperationException($"The video {path} has ended already.");
        End();
        if (ffmpeg.ExitCode != 0)
            throw Failure(inner: null);
    }

    /// <summary>
    /// Ends the video if <see cref="Finish"/> did not: closes ffmpeg's input and waits for ffmpeg, which writes the end of
    /// the file. A failure of ffmpeg is not reported here. Deletes the file if the video took no frame.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (!ended)
            End();
        ffmpeg.Dispose();
        if (framesWritten > 0 || !File.Exists(path))
            return;
        // ffmpeg writes a file with no frame when its input closes before the first one. The run that led here fails
        // already, so a file that cannot be deleted is left, rather than hiding that failure behind this one.
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Closes ffmpeg's input and waits for ffmpeg to exit. The wait without a timeout also waits until the callback has read
    // the last line of ffmpeg's error output.
    private void End()
    {
        ended = true;
        try
        {
            input.Dispose();
        }
        // ffmpeg may have closed its end of the pipe already.
        catch (IOException)
        {
        }
        ffmpeg.WaitForExit();
    }

    private VideoStreamException Failure(Exception? inner)
    {
        string[] lines;
        lock (errorLines)
            lines = [.. errorLines];
        return new VideoStreamException(path, ffmpeg.ExitCode, framesWritten, lines, inner);
    }

    private void KeepErrorLine(object sender, DataReceivedEventArgs line)
    {
        if (line.Data is null)
            return;
        lock (errorLines)
        {
            if (errorLines.Count == ErrorLinesKept)
                errorLines.Dequeue();
            errorLines.Enqueue(line.Data);
        }
    }
}

/// <summary>
/// ffmpeg stopped before a video was complete, or failed at its end. The command reports it as a file problem, exit code 2.
/// </summary>
public sealed class VideoStreamException : IOException
{
    public VideoStreamException(string path, int exitCode, int framesWritten, IReadOnlyList<string> errorLines, Exception? inner)
        : base(Describe(path, exitCode, framesWritten, errorLines ?? throw new ArgumentNullException(nameof(errorLines))), inner)
    {
        Path = path;
        ExitCode = exitCode;
        FramesWritten = framesWritten;
        ErrorLines = errorLines;
    }

    /// <summary>The file of the video.</summary>
    public string Path { get; }

    /// <summary>ffmpeg's exit code.</summary>
    public int ExitCode { get; }

    /// <summary>How many frames went whole into ffmpeg's input before it stopped.</summary>
    public int FramesWritten { get; }

    /// <summary>The last lines of ffmpeg's error output, at most <see cref="FfmpegVideoSink.ErrorLinesKept"/>, oldest first.</summary>
    public IReadOnlyList<string> ErrorLines { get; }

    private static string Describe(string path, int exitCode, int framesWritten, IReadOnlyList<string> errorLines) =>
        $"ffmpeg stopped with exit code {exitCode} after {framesWritten} frames of the video {path}."
        + (errorLines.Count == 0
            ? " It wrote no error output."
            : $" The last lines of its error output:{Environment.NewLine}{string.Join(Environment.NewLine, errorLines)}");
}
