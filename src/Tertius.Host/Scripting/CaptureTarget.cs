namespace Tertius.Host.Scripting;

/// <summary>
/// Where the captures of a run go, for the whole run: a <see cref="PngFolder"/>, which writes one PNG file for each capture,
/// or a <see cref="VideoFile"/>, which makes each capture the next frame of one video. A run names its outputs by the names
/// of its steps, so the playback gets the name of each capture and its target makes the output. See
/// docs/plans/blue-danube-demo.md, change 1.
/// This is a built-in C# 15 union, like <see cref="ScriptStep"/>: its cases are classes, so it is a struct around one
/// reference and allocates nothing beyond its case. The default value holds no case and matches the <c>null</c> pattern.
/// A null case makes the default value too, and <see cref="HeadlessPlayback"/> refuses it.
/// </summary>
public union CaptureTarget(PngFolder, VideoFile) : IEquatable<CaptureTarget>
{
    // The held case decides equality, by the value equality of its record. Two cases of different types are never equal.
    // The built-in union has only the equality of ValueType, which reads the field by reflection, and no == operator.
    public readonly bool Equals(CaptureTarget other) => Equals(Value, other.Value);
    public override bool Equals(object? obj) => obj is CaptureTarget other && Equals(other);
    public readonly override int GetHashCode() => Value?.GetHashCode() ?? 0;
    public static bool operator ==(CaptureTarget a, CaptureTarget b) => a.Equals(b);
    public static bool operator !=(CaptureTarget a, CaptureTarget b) => !a.Equals(b);

    // The built-in union's ToString gives only the name of the union.
    public readonly override string ToString() => Value?.ToString() ?? "CaptureTarget(none)";
}

/// <summary>
/// Captures as PNG files in one folder: the capture named <c>name</c> is <c>&lt;Folder&gt;/&lt;name&gt;.png</c>. A target
/// cannot exist without its folder, so a run with this target always has somewhere to write its images.
/// </summary>
public sealed record PngFolder
{
    /// <exception cref="ArgumentException"><paramref name="folder"/> is null, empty or only white space.</exception>
    public PngFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
    }

    /// <summary>The folder of the images. The PNG sink makes it if it is missing.</summary>
    public string Folder { get; }

    /// <summary>The path of the image of the capture named <paramref name="name"/>.</summary>
    public string PathOf(string name) => Path.Combine(Folder, name + ".png");
}

/// <summary>
/// Captures as the frames of one video, in the order taken, which one ffmpeg process encodes (
/// <see cref="FfmpegVideoSink"/>).
/// The video drops the names of the captures. Only a headless run has this target.
/// </summary>
public sealed record VideoFile
{
    /// <summary>The ending of a video's file name. The video is Matroska, which stays readable when ffmpeg stops early.</summary>
    public const string Extension = ".mkv";

    /// <summary>The lowest and the highest frame rate of a video, in frames per second.</summary>
    public const int MinFramesPerSecond = 1, MaxFramesPerSecond = 240;

    /// <summary>The frame rate of a video whose command line gives none, in frames per second.</summary>
    public const int DefaultFramesPerSecond = 30;

    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty, only white space, or does not end in
    /// <see cref="Extension"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="framesPerSecond"/> is outside
    /// <see cref="MinFramesPerSecond"/> to <see cref="MaxFramesPerSecond"/>.</exception>
    public VideoFile(string path, int framesPerSecond)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!HasVideoExtension(path))
            throw new ArgumentException($"A video file's name ends in {Extension}, and '{path}' does not.",
                nameof(path));
        ArgumentOutOfRangeException.ThrowIfLessThan(framesPerSecond, MinFramesPerSecond);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(framesPerSecond, MaxFramesPerSecond);
        Path = path;
        FramesPerSecond = framesPerSecond;
    }

    /// <summary>The file of the video. A video replaces an older file at the same path.</summary>
    public string Path { get; }

    /// <summary>How many frames the video plays each second.</summary>
    public int FramesPerSecond { get; }

    /// <summary>True if <paramref name="path"/> ends in <see cref="Extension"/>, in any case.</summary>
    public static bool HasVideoExtension(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
}
