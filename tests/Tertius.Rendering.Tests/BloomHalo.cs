namespace Tertius.Rendering.Tests;

/// <summary>
/// Measurements of a halo from the bloom's level 0 (ARCHITECTURE.md, "Bloom"). After a frame, level 0 holds every level
/// summed, before the tone map multiplies it by the strength. It has half the output's size, so each of its texels covers
/// 2 × 2 output pixels. Every value is linear pre-exposed light: radiance times the exposure scale.
/// </summary>
internal static class BloomHalo
{
    /// <summary>
    /// The halo's light outside a square source, before the strength: the sum over the texels of level 0 whose 2 × 2 output
    /// pixels lie more than <paramref name="margin"/> px outside the square, of each texel's Rec. 709 luminance times its 4
    /// output pixels. Its unit is pre-exposed radiance times one output pixel. The square is <paramref name="side"/> px
    /// across and centered on the output pixel corner (<paramref name="centerX"/>, <paramref name="centerY"/>).
    /// </summary>
    public static double OutsideEnergy(
        float[] level0,
        int outputWidth,
        int outputHeight,
        int centerX,
        int centerY,
        int side,
        int margin
    )
    {
        (int width, int height) = LevelSize(outputWidth, outputHeight);
        int left = centerX - side / 2 - margin,
            right = centerX + side / 2 + margin,
            top = centerY - side / 2 - margin,
            bottom = centerY + side / 2 + margin;
        double sum = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // The texel covers output pixels 2x to 2x + 1 and 2y to 2y + 1.
            bool inside = 2 * x + 2 > left && 2 * x < right && 2 * y + 2 > top && 2 * y < bottom;
            if (!inside)
                sum += 4 * Luminance(rgba: level0, width: width, x: x, y: y);
        }

        return sum;
    }

    /// <summary>
    /// The halo's mean luminance before the strength, in rings around the output pixel corner
    /// (<paramref name="centerX"/>, <paramref name="centerY"/>): ring i holds the texels of level 0 whose centers lie from
    /// <paramref name="firstRadius"/> × 2^i to twice that, in output pixels.
    /// </summary>
    public static double[] RingMeans(
        float[] level0,
        int outputWidth,
        int outputHeight,
        int centerX,
        int centerY,
        double firstRadius,
        int rings
    )
    {
        (int width, int height) = LevelSize(outputWidth, outputHeight);
        double[] sums = new double[rings];
        int[] counts = new int[rings];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double r = Math.Sqrt(Math.Pow(2 * x + 1 - centerX, 2) + Math.Pow(2 * y + 1 - centerY, 2));
            if (r < firstRadius)
                continue;
            int ring = (int)Math.Floor(Math.Log2(r / firstRadius));
            if (ring >= rings)
                continue;
            sums[ring] += Luminance(rgba: level0, width: width, x: x, y: y);
            counts[ring]++;
        }

        return [.. sums.Select((sum, i) => counts[i] == 0 ? 0 : sum / counts[i])];
    }

    /// <summary>The size of level 0 for an output of <paramref name="outputWidth"/> × <paramref name="outputHeight"/> px.</summary>
    public static (int Width, int Height) LevelSize(int outputWidth, int outputHeight) =>
        ((outputWidth + 1) / 2, (outputHeight + 1) / 2);

    private static double Luminance(float[] rgba, int width, int x, int y)
    {
        int i = 4 * (y * width + x);
        return 0.2126 * rgba[i] + 0.7152 * rgba[i + 1] + 0.0722 * rgba[i + 2];
    }
}
