using Tertius.Host.Scripting;

namespace Tertius.Host;

// A file of its own, because CSharpier cannot parse the union declaration (.csharpierignore).
public static partial class ScriptCommand
{
    /// <summary>
    /// The choice of <c>--video</c> of a headless run: a <see cref="VideoFile"/> that holds the captures, or a
    /// <see cref="Discard"/> of them, from <c>--video none</c>. A built-in C# 15 union, as <see cref="CaptureTarget"/> is,
    /// because its cases are classes.
    /// </summary>
    internal union HeadlessVideo(VideoFile, Discard);
}
