namespace Tertius.Rendering.Tests;

/// <summary>
/// John Hable's filmic curve, <see cref="ToneCurve.Hable"/>, on the CPU with his published constants (GDC 2010,
/// "Uncharted 2: HDR Lighting"), and its inverse. It works per channel, so a test can turn a shown color back into the
/// radiance of the HDR image.
/// </summary>
internal static class HableCurve
{
    /// <summary>Hable's linear white point W: the input of his curve that shows as white.</summary>
    public const double White = 11.2;

    /// <summary>Hable's exposure bias, which his curve applies before the curve itself.</summary>
    public const double ExposureBias = 2;

    /// <summary>The smallest HDR channel that the curve shows as white: W over the bias.</summary>
    public const double Brightest = White / ExposureBias;

    /// <summary>The linear color that the curve gives a linear HDR channel, clamped to [0, 1].</summary>
    public static double Shown(double radiance) => Math.Clamp(F(ExposureBias * radiance) / F(White), 0, 1);

    /// <summary>The HDR channel that the curve shows as the linear color <paramref name="shown"/>, by bisection.</summary>
    public static double Radiance(double shown)
    {
        double low = 0,
            high = Brightest;
        for (int step = 0; step < 60; step++)
        {
            double middle = 0.5 * (low + high);
            if (Shown(middle) < shown)
                low = middle;
            else
                high = middle;
        }

        return 0.5 * (low + high);
    }

    /// <summary>Hable's f(x).</summary>
    private static double F(double x)
    {
        const double a = 0.15,
            b = 0.50,
            c = 0.10,
            d = 0.20,
            e = 0.02,
            f = 0.30;
        return (x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f) - e / f;
    }
}
