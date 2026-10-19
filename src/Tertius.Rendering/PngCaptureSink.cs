using StbImageWriteSharp;

namespace Tertius.Rendering;

/// <summary>
/// A capture sink that writes the pixels of one capture as a PNG file, with every pixel opaque. It writes the file on
/// the thread that calls <see cref="Take"/>, before <c>Take</c> returns, so the file exists when
/// <see cref="IRenderer.Render"/> returns. It makes the folder of the file if the folder is missing, and it replaces an
/// older file at the same path.
/// </summary>
public sealed class PngCaptureSink : ICaptureSink
{
    private readonly string path;

    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or only white space.</exception>
    public PngCaptureSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public void Take(in CapturedImage image)
    {
        // A copy: the pixels belong to the renderer's readback buffer, and the alpha byte of each pixel has no defined value.
        byte[] rgba = image.Rgba.ToArray();
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        using FileStream stream = File.Create(full);
        new ImageWriter().WritePng(rgba, image.Width, image.Height, ColorComponents.RedGreenBlueAlpha, stream);
    }
}
