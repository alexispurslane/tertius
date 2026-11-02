using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The C# side of the packed estimate of a texel (Shaders/estimate.glsl, docs/plans/screen-space-illumination.md, "The
/// estimate of a texel"): its format, the open texel that a clear writes, and the reads of a packed image's halves.
/// Term A, term B and the estimate target hold the estimate as seven half floats in an RGBA32UI texel, two to a word as
/// packHalf2x16 packs them, in this order: G.r, G.g, G.b, V, (1 − S).r, (1 − S).g, (1 − S).b and an unused 0. A change
/// of the layout changes estimate.glsl and this type, and nothing else.
/// </summary>
internal static class EstimatePacking
{
    /// <summary>The format of a packed image: four 32-bit words, a storage, sampled and transfer format that Vulkan requires.</summary>
    public const Format Format = Format.R32G32B32A32Uint;

    /// <summary>
    /// How many half floats a texel holds, in the order of the summary: packHalf2x16 puts its first half in the low 16
    /// bits of each word, so a texel's 16 bytes in memory hold its halves in order.
    /// </summary>
    public const int HalvesPerTexel = 8;

    /// <summary>The bits of the half float 1.0.</summary>
    private const uint HalfOne = 0x3C00;

    /// <summary>
    /// The four words of the open estimate, as packEstimate packs openEstimate(): G = 0, V = 1 and 1 − S = 0, so every
    /// half is 0 but V, in the high half of the second word.
    /// </summary>
    public static ClearColorValue OpenTexel => new(uint32_0: 0, uint32_1: HalfOne << 16, uint32_2: 0, uint32_3: 0);

    /// <summary>
    /// The bounce G, pre-exposed, and the occlusion V, RGBA per texel, of <paramref name="halves"/>, the
    /// <see cref="HalvesPerTexel"/> halves per texel of a packed image as a copy to memory holds them.
    /// </summary>
    public static float[] BounceAndOcclusion(ReadOnlySpan<float> halves)
    {
        float[] rgba = new float[4 * (halves.Length / HalvesPerTexel)];
        for (int texel = 0; texel < rgba.Length / 4; texel++)
            halves.Slice(HalvesPerTexel * texel, 4).CopyTo(rgba.AsSpan(4 * texel, 4));
        return rgba;
    }

    /// <summary>
    /// The open share S, RGB per texel, from 0 to 1 in each channel, of <paramref name="halves"/>, as
    /// <see cref="BounceAndOcclusion"/> takes them: 1 − the blocked share that the fifth to seventh halves hold.
    /// </summary>
    public static float[] OpenShare(ReadOnlySpan<float> halves)
    {
        float[] rgb = new float[3 * (halves.Length / HalvesPerTexel)];
        for (int texel = 0; texel < rgb.Length / 3; texel++)
        for (int channel = 0; channel < 3; channel++)
            rgb[3 * texel + channel] = 1 - halves[HalvesPerTexel * texel + 4 + channel];
        return rgb;
    }
}
