using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The bloom's chain on the CPU (ARCHITECTURE.md, "Bloom"), for the test that compares every level of the GPU with it:
/// the thresholded first downsample, Jimenez's 13 taps down, and the 3 × 3 tent up that adds into the larger level. Each
/// level stores half floats, as the GPU's images do.
/// </summary>
internal static class BloomChainModel
{
    /// <summary>The weights of the 6 × 6 texels under the 13 taps of the first downsample, times 128, row by row.</summary>
    private static readonly float[] TexelWeights =
    [
        1, 1, 2, 2, 1, 1,
        1, 5, 6, 6, 5, 1,
        2, 6, 8, 8, 6, 2,
        2, 6, 8, 8, 6, 2,
        1, 5, 6, 6, 5, 1,
        1, 1, 2, 2, 1, 1,
    ];

    /// <summary>One image: RGB per pixel, row by row from the top. A read outside it gives the nearest pixel.</summary>
    public readonly record struct Level(int Width, int Height, Vector3[] Pixels)
    {
        public Vector3 At(int x, int y) => Pixels[Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)];
    }

    /// <summary>The levels of <paramref name="bloom"/> after its chain over <paramref name="hdr"/>: level 0 holds every level summed.</summary>
    public static Level[] Levels(Level hdr, Bloom bloom)
    {
        var levels = new Level[bloom.Levels];
        levels[0] = Prefilter(hdr, bloom);
        for (int i = 1; i < levels.Length; i++)
            levels[i] = Down(levels[i - 1]);
        for (int i = levels.Length - 2; i >= 0; i--)
            levels[i] = Up(smaller: levels[i + 1], larger: levels[i]);
        return levels;
    }

    /// <summary>
    /// The first downsample: the thresholded light of the 6 × 6 texels around each output pixel, weighted, over the
    /// count of levels.
    /// </summary>
    private static Level Prefilter(Level hdr, Bloom bloom)
    {
        Level level = Halved(hdr);
        for (int y = 0; y < level.Height; y++)
        for (int x = 0; x < level.Width; x++)
        {
            Vector3 sum = Vector3.Zero;
            for (int v = 0; v < 6; v++)
            for (int u = 0; u < 6; u++)
                sum += TexelWeights[v * 6 + u] * Thresholded(hdr.At(2 * x - 2 + u, 2 * y - 2 + v), bloom);
            level.Pixels[y * level.Width + x] = Stored(sum / (128f * bloom.Levels));
        }

        return level;
    }

    /// <summary>Unity's quadratic soft threshold on the largest channel of <paramref name="color"/>.</summary>
    private static Vector3 Thresholded(Vector3 color, Bloom bloom)
    {
        float brightness = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        float width = bloom.Threshold * bloom.Knee;
        float soft = Math.Clamp(brightness - bloom.Threshold + width, 0, 2 * width);
        soft = soft * soft / (4 * width + 1e-5f);
        return color * MathF.Max(soft, brightness - bloom.Threshold) / MathF.Max(brightness, 1e-5f);
    }

    /// <summary>
    /// Jimenez's 13 taps of <paramref name="source"/> into a level of half its size. Each tap lies on a texel corner, so
    /// it is the mean of the four texels around that corner.
    /// </summary>
    private static Level Down(Level source)
    {
        Level level = Halved(source);
        for (int y = 0; y < level.Height; y++)
        for (int x = 0; x < level.Width; x++)
        {
            // The output pixel's center lies on the corner between the source texels 2p and 2p + 1.
            int cx = 2 * x + 1,
                cy = 2 * y + 1;
            Vector3 a = Corner(source, cx - 2, cy - 2),
                b = Corner(source, cx, cy - 2),
                c = Corner(source, cx + 2, cy - 2),
                d = Corner(source, cx - 1, cy - 1),
                e = Corner(source, cx + 1, cy - 1),
                f = Corner(source, cx - 2, cy),
                g = Corner(source, cx, cy),
                h = Corner(source, cx + 2, cy),
                i = Corner(source, cx - 1, cy + 1),
                j = Corner(source, cx + 1, cy + 1),
                k = Corner(source, cx - 2, cy + 2),
                l = Corner(source, cx, cy + 2),
                m = Corner(source, cx + 2, cy + 2);
            Vector3 inner = (d + e + i + j) * 0.25f;
            Vector3 corners =
                (a + b + f + g) * 0.25f + (b + c + g + h) * 0.25f + (f + g + k + l) * 0.25f + (g + h + l + m) * 0.25f;
            level.Pixels[y * level.Width + x] = Stored(inner * 0.5f + corners * 0.125f);
        }

        return level;
    }

    /// <summary>
    /// <paramref name="larger"/> plus the 3 × 3 tent of <paramref name="smaller"/>: nine bilinear taps one texel of the
    /// smaller level apart, weighted 1 2 1, 2 4 2, 1 2 1 over 16.
    /// </summary>
    private static Level Up(Level smaller, Level larger)
    {
        var level = new Level(Width: larger.Width, Height: larger.Height, Pixels: new Vector3[larger.Pixels.Length]);
        for (int y = 0; y < level.Height; y++)
        for (int x = 0; x < level.Width; x++)
        {
            // The pixel's center, in texels of the smaller level.
            float cx = 0.5f * (x + 0.5f),
                cy = 0.5f * (y + 0.5f);
            Vector3 sum = Vector3.Zero;
            for (int v = -1; v <= 1; v++)
            for (int u = -1; u <= 1; u++)
                sum += (2 - Math.Abs(u)) * (2 - Math.Abs(v)) * Bilinear(smaller, cx + u, cy + v);
            level.Pixels[y * level.Width + x] = Stored(larger.At(x, y) + sum / 16f);
        }

        return level;
    }

    /// <summary>The mean of the four texels of <paramref name="level"/> around the texel corner (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private static Vector3 Corner(Level level, int x, int y) =>
        (level.At(x - 1, y - 1) + level.At(x, y - 1) + level.At(x - 1, y) + level.At(x, y)) * 0.25f;

    /// <summary>The bilinear read of <paramref name="level"/> at (<paramref name="x"/>, <paramref name="y"/>), in texels, clamped to the edge.</summary>
    private static Vector3 Bilinear(Level level, float x, float y)
    {
        float left = MathF.Floor(x - 0.5f),
            top = MathF.Floor(y - 0.5f);
        float tx = x - 0.5f - left,
            ty = y - 0.5f - top;
        int x0 = (int)left,
            y0 = (int)top;
        Vector3 upper = Vector3.Lerp(level.At(x0, y0), level.At(x0 + 1, y0), tx);
        Vector3 lower = Vector3.Lerp(level.At(x0, y0 + 1), level.At(x0 + 1, y0 + 1), tx);
        return Vector3.Lerp(upper, lower, ty);
    }

    /// <summary>An empty level of half the size of <paramref name="source"/>, rounded up.</summary>
    private static Level Halved(Level source)
    {
        int width = (source.Width + 1) / 2,
            height = (source.Height + 1) / 2;
        return new(Width: width, Height: height, Pixels: new Vector3[width * height]);
    }

    /// <summary><paramref name="value"/> as an image of half floats holds it.</summary>
    private static Vector3 Stored(Vector3 value) => new((float)(Half)value.X, (float)(Half)value.Y, (float)(Half)value.Z);
}
