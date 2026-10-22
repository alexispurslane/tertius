using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>The FLIP value of each pixel of two compared images, from 0 (no visible change) to 1. Row-major, top row first.</summary>
internal sealed class FlipMap
{
    /// <summary>
    /// The colors of the heat map, at errors 0, 0.25, 0.5, 0.75, 0.9 and 1: black through purple, red and orange to pale
    /// yellow, close to the magma map of the FLIP tools. Their luma grows at each stop, so a larger error always draws
    /// brighter.
    /// </summary>
    private static readonly (float Error, Vector3 Color)[] HeatStops =
    [
        (0f, new(0.001f, 0.000f, 0.014f)),
        (0.25f, new(0.232f, 0.060f, 0.438f)),
        (0.5f, new(0.550f, 0.161f, 0.506f)),
        (0.75f, new(0.869f, 0.288f, 0.409f)),
        (0.9f, new(0.993f, 0.553f, 0.384f)),
        (1f, new(0.987f, 0.991f, 0.750f))
    ];

    private readonly float[] values;

    public FlipMap(int width, int height, float[] values)
    {
        if (values.Length != width * height)
            throw new ArgumentException($"A {width}×{height} map needs {width * height} values, not {values.Length}.",
                nameof(values));
        Width = width;
        Height = height;
        this.values = values;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<float> Values => values;
    public float this[int x, int y] => values[y * Width + x];

    public float Max => values.Max();

    /// <summary>The mean FLIP value over the image, the paper's one-number summary.</summary>
    public float Mean => values.Average();

    /// <summary>How many pixels have a FLIP value above <paramref name="threshold"/>.</summary>
    public int CountAbove(float threshold) => values.Count(v => v > threshold);

    /// <summary>The map as an image: each pixel's error as a color of the heat ramp.</summary>
    public Capture HeatMap()
    {
        byte[] rgba = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            Vector3 color = HeatColor(values[i]);
            rgba[4 * i] = (byte)MathF.Round(color.X * 255);
            rgba[4 * i + 1] = (byte)MathF.Round(color.Y * 255);
            rgba[4 * i + 2] = (byte)MathF.Round(color.Z * 255);
            rgba[4 * i + 3] = 255;
        }

        return Capture.FromRgba(Width, Height, rgba);
    }

    private static Vector3 HeatColor(float error)
    {
        error = Math.Clamp(error, 0f, 1f);
        int upper = 1;
        while (upper < HeatStops.Length - 1 && HeatStops[upper].Error < error)
            upper++;
        (float lowError, Vector3 lowColor) = HeatStops[upper - 1];
        (float highError, Vector3 highColor) = HeatStops[upper];
        return Vector3.Lerp(lowColor, highColor, (error - lowError) / (highError - lowError));
    }
}
