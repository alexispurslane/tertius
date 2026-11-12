using System.Numerics;
using StbImageSharp;
using StbImageWriteSharp;

namespace Tertius.Rendering.Tests;

// The part of the tests' Capture that Flip.cs and FlipMap.cs use.
internal sealed class Capture(int width, int height, byte[] rgba)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Rgba { get; } = rgba;

    public static Capture Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        return new(image.Width, image.Height, image.Data);
    }

    public static Capture FromRgba(int width, int height, byte[] rgba) => new(width, height, rgba);

    public void Save(string path)
    {
        using FileStream stream = File.Create(path);
        new ImageWriter().WritePng(Rgba, Width, Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }

    public Vector3 Color(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        int i = (y * Width + x) * 4;
        return new Vector3(Rgba[i], Rgba[i + 1], Rgba[i + 2]) / 255f;
    }
}
