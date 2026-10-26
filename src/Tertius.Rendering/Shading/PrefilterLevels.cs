using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The levels of the specular map (docs/plans/environment-map.md, "The specular levels"). environment_prefilter.comp
/// and environment.glsl are the GLSL copies.
/// </summary>
internal static class PrefilterLevels
{
    public const int SmallestFaceSize = 4;
    public const int SamplesPerTexel = 64;
    public const float SampleLodBias = 1f;

    /// <summary>L: the levels of the specular map, from a power of two <paramref name="faceSize"/> down to 4 texels.</summary>
    public static int Count(int faceSize) => BitOperations.Log2((uint)(faceSize / SmallestFaceSize)) + 1;

    /// <summary>The levels of the radiance map's mip chain, from a power of two <paramref name="faceSize"/> down to 1 texel.</summary>
    public static int RadianceLevels(int faceSize) => BitOperations.Log2((uint)faceSize) + 1;

    /// <summary>
    /// The invocations of environment_prefilter.comp that share the samples of one texel of level
    /// <paramref name="level"/>: 1 for levels 0 and 1, then four times as many for each level of a quarter of the texels,
    /// up to one sample each. So every level from 1 on has as many invocations as level 1 until each sample has its own,
    /// and a texel of a small level waits on a chain of a few texture reads, not of all its samples.
    /// </summary>
    public static int Lanes(int level) => level <= 1 ? 1 : Math.Min(SamplesPerTexel, 1 << (2 * (level - 1)));

    /// <summary>The roughness that level <paramref name="level"/> of <paramref name="levels"/> holds, linear in perceptual roughness.</summary>
    public static float Roughness(int level, int levels) => (float)level / (levels - 1);

    /// <summary>The level that a reader samples for <paramref name="roughness"/>, after the roughness floor.</summary>
    public static float Lod(float roughness, int levels) => (float)new GgxLobe(roughness).Roughness * (levels - 1);

    /// <summary>
    /// The level of the radiance map that a prefilter sample of density <paramref name="pdf"/>, per sr, reads
    /// (Křivánek and Colbert 2008): half the log2 of the sample's solid angle over a texel's, plus the bias.
    /// </summary>
    public static float SampleLod(float pdf, int samples, int faceSize)
    {
        float sampleSolidAngle = 1f / (samples * pdf);
        float texelSolidAngle = 4 * MathF.PI / (6f * faceSize * faceSize);
        return MathF.Max(0.5f * MathF.Log2(sampleSolidAngle / texelSolidAngle) + SampleLodBias, 0);
    }
}
