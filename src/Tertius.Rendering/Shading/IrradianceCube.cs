using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The irradiance cube of a map: D(n), the map's ambient diffuse radiance, for every unit normal n, convolved in each
/// drawn frame from the map's radiance (docs/plans/environment-map.md, "The irradiance"). environment_irradiance.comp is
/// the GLSL copy of <see cref="Convolve"/>.
/// </summary>
internal static class IrradianceCube
{
    /// <summary>The texels along one side of a face of the cube's level 0.</summary>
    public const int FaceSize = 16;

    /// <summary>
    /// The face size of the radiance map's level that the cube convolves, or the map's own level 0 when that is smaller.
    /// The GLSL copy is SourceFaceSize in environment_irradiance.comp.
    /// </summary>
    public const int SourceFaceSize = 32;

    /// <summary>
    /// The levels of the cube: 16, 8, 4 and 2 texels. Each texel of the last level holds the mean of D over a quarter of
    /// its face, so the mean of a face's four is the mean over the face.
    /// </summary>
    public const int Levels = 4;

    /// <summary>The texels along one side of a face of the cube's last level, which the CPU reads back.</summary>
    public const int CoarsestSize = FaceSize >> (Levels - 1);

    /// <summary>
    /// The invocations of environment_irradiance.comp that share a texel of level 0, each summing an eighth of the
    /// source. Eight measured fastest on the M1 Max: the prefilter took 0.460 ms, against 0.530 ms at 4 lanes, 0.481 ms
    /// at 16 and 0.68 ms at 32. The GLSL copy is Lanes in that shader.
    /// </summary>
    public const int Lanes = 8;

    /// <summary>The workgroups of 64 invocations that convolve one map's level 0, <see cref="Lanes"/> per texel: 192.</summary>
    public const int Workgroups = CubeFaces.Count * WorkgroupsPerFace;

    /// <summary>The workgroups that convolve one face of one map's level 0: 32.</summary>
    public const int WorkgroupsPerFace = FaceSize * FaceSize * Lanes / 64;

    /// <summary>The level of a radiance map of <paramref name="mapFaceSize"/> texels, a power of two, that the cube convolves.</summary>
    public static int SourceLevel(int mapFaceSize) =>
        BitOperations.Log2((uint)mapFaceSize) - BitOperations.Log2((uint)Math.Min(mapFaceSize, SourceFaceSize));

    /// <summary>
    /// D(n) at the unit <paramref name="normal"/>, in all four channels: the mean of the source level's texels over the
    /// hemisphere of n, each weighted by its solid angle times the clamped cosine max(n·ω, 0) of its center's direction ω.
    /// The weights add up to about π, and the mean divides by their sum, so a uniform map gives its own radiance. Its alpha
    /// is the open-sky share that a Lambert surface sees. The texels sum in layer, row and column order, so one map always
    /// gives the same bits.
    /// </summary>
    /// <param name="source">The source level, layer then row then column, RGBA.</param>
    /// <param name="sourceSize">The texels along one side of a face of <paramref name="source"/>.</param>
    /// <exception cref="ArgumentException">The source does not hold six faces of <paramref name="sourceSize"/>.</exception>
    public static Vector4 Convolve(ReadOnlySpan<Half> source, int sourceSize, Vector3 normal)
    {
        if (source.Length != CubeFaces.Count * sourceSize * sourceSize * 4)
            throw new ArgumentException(
                $"Six faces of {sourceSize} texels hold {CubeFaces.Count * sourceSize * sourceSize * 4} halves, not {source.Length}.",
                nameof(source)
            );

        Vector4 sum = Vector4.Zero;
        float weight = 0;
        int texel = 0;
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        for (int y = 0; y < sourceSize; y++)
        for (int x = 0; x < sourceSize; x++, texel += 4)
        {
            Vector3 direction = CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / sourceSize, (y + 0.5f) / sourceSize);
            float cosine = Vector3.Dot(normal, direction);
            if (cosine <= 0)
                continue;
            float w = cosine * (float)CubeFaces.TexelSolidAngle(x, y, sourceSize);
            sum +=
                new Vector4(
                    x: (float)source[texel],
                    y: (float)source[texel + 1],
                    z: (float)source[texel + 2],
                    w: (float)source[texel + 3]
                ) * w;
            weight += w;
        }

        // The texel whose center lies nearest n has n·ω > 0, so the weight is positive.
        return sum / weight;
    }
}
