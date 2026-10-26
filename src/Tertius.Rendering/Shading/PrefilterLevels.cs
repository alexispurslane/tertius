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

    /// <summary>The invocations of a workgroup of environment_prefilter.comp, one row of its table of samples each.</summary>
    public const int GroupInvocations = 64;

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

    /// <summary>
    /// The levels that one dispatch of environment_prefilter.comp prefilters at most: one storage image each, the four
    /// that every Vulkan device offers per stage.
    /// </summary>
    public const int LevelsPerDispatch = 4;

    /// <summary>The dispatches of environment_prefilter.comp for <paramref name="levels"/> levels.</summary>
    public static int Dispatches(int levels) => (levels + LevelsPerDispatch - 1) / LevelsPerDispatch;

    /// <summary>
    /// The first level and the count of levels that dispatch <paramref name="dispatch"/> of <paramref name="levels"/>
    /// prefilters: a run of <see cref="LevelsPerDispatch"/> levels, or the levels left for the last dispatch.
    /// </summary>
    public static (int First, int Count) LevelsOfDispatch(int dispatch, int levels)
    {
        int first = dispatch * LevelsPerDispatch;
        return (first, Math.Min(LevelsPerDispatch, levels - first));
    }

    /// <summary>
    /// The workgroups of level <paramref name="level"/> of a map of <paramref name="faceSize"/>: one for each
    /// <see cref="GroupInvocations"/> / <see cref="Lanes"/> texels of its six faces.
    /// </summary>
    public static int Workgroups(int level, int faceSize)
    {
        int size = faceSize >> level;
        int texelsPerGroup = GroupInvocations / Lanes(level);
        return (CubeFaces.Count * size * size + texelsPerGroup - 1) / texelsPerGroup;
    }

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
