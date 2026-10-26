using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Rendering;

/// <summary>
/// The irradiance around the camera: nine RGB coefficients of real spherical harmonics up to band 2, convolved with the
/// clamped cosine (docs/plans/environment-map.md, "The irradiance"). environment_irradiance.comp writes the same layout:
/// coefficient i in vec4 i, w unused.
/// </summary>
internal readonly struct Irradiance
{
    public const int Coefficients = 9;

    /// <summary>Y_00, the constant of band 0.</summary>
    private static readonly double Band0 = 0.5 * Math.Sqrt(1 / Math.PI);

    /// <summary>The factor of x, y and z in band 1.</summary>
    private static readonly double Band1 = Math.Sqrt(3 / (4 * Math.PI));

    /// <summary>The factor of xy, yz and xz in band 2.</summary>
    private static readonly double Band2Products = 0.5 * Math.Sqrt(15 / Math.PI);

    /// <summary>The factor of 3z² − 1 in band 2.</summary>
    private static readonly double Band2Zonal = 0.25 * Math.Sqrt(5 / Math.PI);

    /// <summary>The factor of x² − y² in band 2.</summary>
    private static readonly double Band2Squares = 0.25 * Math.Sqrt(15 / Math.PI);

    private readonly Nine coefficients;

    /// <summary>Holds the nine E_lm in the doc's order.</summary>
    /// <exception cref="ArgumentException">The span does not hold exactly nine coefficients.</exception>
    public Irradiance(ReadOnlySpan<Vector3> coefficients)
    {
        if (coefficients.Length != Coefficients)
            throw new ArgumentException(
                $"An irradiance has {Coefficients} coefficients, not {coefficients.Length}.",
                nameof(coefficients)
            );
        coefficients.CopyTo(this.coefficients);
    }

    /// <summary>The mean of the map's radiance over every direction: E_00 Y_00 / π.</summary>
    public Vector3 MeanRadiance => coefficients[0] * (float)(Band0 / Math.PI);

    /// <summary>E_lm, coefficient <paramref name="index"/> in the doc's order.</summary>
    public Vector3 this[int index] => coefficients[index];

    /// <summary>The nine real basis functions Y_lm at the unit <paramref name="direction"/>, in the doc's order.</summary>
    public static void Basis(Vector3 direction, Span<double> values)
    {
        double x = direction.X;
        double y = direction.Y;
        double z = direction.Z;
        values[0] = Band0;
        values[1] = Band1 * y;
        values[2] = Band1 * z;
        values[3] = Band1 * x;
        values[4] = Band2Products * x * y;
        values[5] = Band2Products * y * z;
        values[6] = Band2Zonal * (3 * z * z - 1);
        values[7] = Band2Products * x * z;
        values[8] = Band2Squares * (x * x - y * y);
    }

    /// <summary>
    /// Projects level 0 of a radiance map onto the basis and convolves it with the clamped cosine. The texels sum in
    /// layer, row and column order, so one map always gives the same bits.
    /// </summary>
    /// <param name="radiance">Level 0, layer then row then column, RGBA.</param>
    /// <exception cref="ArgumentException">The map does not hold six faces of <paramref name="faceSize"/>.</exception>
    public static Irradiance Project(ReadOnlySpan<Half> radiance, int faceSize)
    {
        if (radiance.Length != CubeFaces.Count * faceSize * faceSize * 4)
            throw new ArgumentException(
                $"Six faces of {faceSize} texels hold {CubeFaces.Count * faceSize * faceSize * 4} halves, not {radiance.Length}.",
                nameof(radiance)
            );

        Span<double> basis = stackalloc double[Coefficients];
        Span<double> sums = stackalloc double[Coefficients * 3];
        int texel = 0;
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        for (int y = 0; y < faceSize; y++)
        for (int x = 0; x < faceSize; x++, texel += 4)
        {
            Vector3 direction = CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / faceSize, (y + 0.5f) / faceSize);
            Basis(direction, basis);
            double solidAngle = CubeFaces.TexelSolidAngle(x, y, faceSize);
            double red = (double)radiance[texel] * solidAngle;
            double green = (double)radiance[texel + 1] * solidAngle;
            double blue = (double)radiance[texel + 2] * solidAngle;
            for (int index = 0; index < Coefficients; index++)
            {
                sums[3 * index] += red * basis[index];
                sums[3 * index + 1] += green * basis[index];
                sums[3 * index + 2] += blue * basis[index];
            }
        }

        Span<Vector3> coefficients = stackalloc Vector3[Coefficients];
        for (int index = 0; index < Coefficients; index++)
        {
            double cosine = CosineOfBand(index);
            coefficients[index] = new(
                (float)(cosine * sums[3 * index]),
                (float)(cosine * sums[3 * index + 1]),
                (float)(cosine * sums[3 * index + 2])
            );
        }

        return new(coefficients);
    }

    /// <summary>E(n), the irradiance on a surface with the unit <paramref name="normal"/>, clamped at 0.</summary>
    public Vector3 At(Vector3 normal)
    {
        Span<double> basis = stackalloc double[Coefficients];
        Basis(normal, basis);
        double red = 0;
        double green = 0;
        double blue = 0;
        for (int index = 0; index < Coefficients; index++)
        {
            Vector3 coefficient = coefficients[index];
            red += coefficient.X * basis[index];
            green += coefficient.Y * basis[index];
            blue += coefficient.Z * basis[index];
        }

        return Vector3.Max(new((float)red, (float)green, (float)blue), Vector3.Zero);
    }

    /// <summary>
    /// The change from <paramref name="a"/> to <paramref name="b"/>: the summed lengths of the nine coefficients'
    /// changes, as a share of the larger summed length, the rule of the impostors' other signature parts.
    /// </summary>
    public static float Change(in Irradiance a, in Irradiance b)
    {
        float difference = 0;
        float totalA = 0;
        float totalB = 0;
        for (int index = 0; index < Coefficients; index++)
        {
            difference += (a[index] - b[index]).Length();
            totalA += a[index].Length();
            totalB += b[index].Length();
        }

        return difference / MathF.Max(MathF.Max(totalA, totalB), 1e-6f);
    }

    /// <summary>Â_l of the clamped cosine for the band of coefficient <paramref name="index"/>.</summary>
    private static double CosineOfBand(int index)
    {
        if (index == 0)
            return Math.PI;
        if (index < 4)
            return 2 * Math.PI / 3;
        return Math.PI / 4;
    }

    [InlineArray(Coefficients)]
    private struct Nine
    {
        private Vector3 element;
    }
}

/// <summary>The irradiance that one finished frame's passes used.</summary>
internal readonly record struct IrradianceReading(long Frame, Irradiance Irradiance);
