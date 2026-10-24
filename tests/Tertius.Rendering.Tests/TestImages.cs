namespace Tertius.Rendering.Tests;

/// <summary>Hand-made opaque images for the FLIP and reference image tests.</summary>
internal static class TestImages
{
    /// <summary>An image whose pixel (x, y) has the color that <paramref name="color"/> gives it.</summary>
    public static Capture Fill(int width, int height, Func<int, int, (byte R, byte G, byte B)> color)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            (byte r, byte g, byte b) = color(x, y);
            int i = (y * width + x) * 4;
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
            rgba[i + 3] = 255;
        }

        return Capture.FromRgba(width, height, rgba);
    }

    public static Capture Uniform(int width, int height, byte r, byte g, byte b) =>
        Fill(width, height, (_, _) => (r, g, b));

    /// <summary>A uniform gray: all three channels hold <paramref name="value"/>, display-encoded (sRGB).</summary>
    public static Capture Gray(int width, int height, byte value) => Uniform(width: width, height: height, r: value, g: value, b: value);

    /// <summary>A gray image with the pixels of <paramref name="changed"/> set to another gray.</summary>
    public static Capture GrayWith(int width, int height, byte value, Func<int, int, bool> changed,
        byte changedValue) =>
        Fill(width, height, (x, y) =>
        {
            byte v = changed(x, y) ? changedValue : value;
            return (v, v, v);
        });
}
