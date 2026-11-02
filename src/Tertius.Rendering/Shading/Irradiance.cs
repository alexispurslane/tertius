using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Rendering;

/// <summary>
/// A map's diffuse light D as the CPU reads it back: the last level of its irradiance cube (docs/plans/environment-map.md,
/// "The irradiance"), <see cref="IrradianceCube.CoarsestSize"/> × <see cref="IrradianceCube.CoarsestSize"/> texels of each
/// face in layer, row and column order. Each texel holds the mean of D over a quarter of its face, and the 24 quarters
/// span the same solid angle. The impostors' signature of the light compares two of them.
/// </summary>
internal readonly struct Irradiance
{
    /// <summary>The texels of the cube's last level: 2 × 2 on each of the six faces.</summary>
    public const int Texels = CubeFaces.Count * IrradianceCube.CoarsestSize * IrradianceCube.CoarsestSize;

    /// <summary>The halves of the texels as the GPU stores them, RGBA16F.</summary>
    public const int Halves = Texels * 4;

    private readonly TexelArray texels;

    /// <summary>Holds the texels' light, in layer, row and column order.</summary>
    /// <exception cref="ArgumentException">The span does not hold exactly <see cref="Texels"/> values.</exception>
    public Irradiance(ReadOnlySpan<Vector3> texels)
    {
        if (texels.Length != Texels)
            throw new ArgumentException($"An irradiance has {Texels} texels, not {texels.Length}.", nameof(texels));
        texels.CopyTo(this.texels);
    }

    /// <summary>
    /// The mean of D over every direction, which equals the mean of the map's radiance over every direction: a surface
    /// facing each way in turn sees each direction of the map with the same total weight.
    /// </summary>
    public Vector3 MeanRadiance
    {
        get
        {
            Vector3 sum = Vector3.Zero;
            foreach (Vector3 texel in texels)
                sum += texel;
            return sum / Texels;
        }
    }

    /// <summary>The light of texel <paramref name="index"/>, in layer, row and column order.</summary>
    public Vector3 this[int index] => texels[index];

    /// <summary>The irradiance in the readback's RGBA halves, alpha ignored.</summary>
    /// <exception cref="ArgumentException">The span does not hold exactly <see cref="Halves"/> halves.</exception>
    public static Irradiance Of(ReadOnlySpan<Half> rgba)
    {
        if (rgba.Length != Halves)
            throw new ArgumentException($"An irradiance reads {Halves} halves, not {rgba.Length}.", nameof(rgba));
        Span<Vector3> values = stackalloc Vector3[Texels];
        for (int i = 0; i < values.Length; i++)
            values[i] = new((float)rgba[4 * i], (float)rgba[4 * i + 1], (float)rgba[4 * i + 2]);
        return new(values);
    }

    [InlineArray(Texels)]
    private struct TexelArray
    {
        private Vector3 element;
    }
}

/// <summary>The irradiance that one finished frame's passes used.</summary>
internal readonly record struct IrradianceReading(long Frame, Irradiance Irradiance);
