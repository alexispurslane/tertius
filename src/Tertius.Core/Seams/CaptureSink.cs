namespace Tertius;

/// <summary>
/// Where the pixels of one capture go. The renderer knows nothing about files: <see cref="IRenderer.CaptureNextFrame"/>
/// takes a sink, and the renderer hands the sink the pixels of the next frame that it draws.
/// </summary>
public interface ICaptureSink
{
    /// <summary>
    /// Takes the pixels of one captured frame. The renderer calls it inside the <see cref="IRenderer.Render"/> call that
    /// drew the frame, or for a capture in flight inside a later <c>Render</c> or <see cref="IRenderer.FinishCaptures"/>,
    /// on the thread of that call, after the GPU finished the frame. The pixels are valid only until this method returns,
    /// so a sink that keeps them copies them.
    /// </summary>
    void Take(in CapturedImage image);
}

/// <summary>
/// The pixels of one captured frame, as the renderer hands them to an <see cref="ICaptureSink"/>.
/// </summary>
public readonly ref struct CapturedImage
{
    /// <exception cref="ArgumentOutOfRangeException">The width or the height is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="rgba"/> is not exactly 4 bytes for each pixel.</exception>
    public CapturedImage(ReadOnlySpan<byte> rgba, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        // In 64 bits: a large size must not wrap around to the length of a short span.
        if (rgba.Length != (long)width * height * 4)
            throw new ArgumentException(
                $"A {width}x{height} image needs {(long)width * height * 4} bytes, not {rgba.Length}.",
                nameof(rgba)
            );
        Rgba = rgba;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// <see cref="Width"/> × <see cref="Height"/> × 4 bytes, rows from the top, each pixel red, green, blue with sRGB
    /// encoding, then a fourth byte that holds no defined value.
    /// </summary>
    public ReadOnlySpan<byte> Rgba { get; }

    /// <summary>The width of the drawn frame, in pixels.</summary>
    public int Width { get; }

    /// <summary>The height of the drawn frame, in pixels.</summary>
    public int Height { get; }
}
