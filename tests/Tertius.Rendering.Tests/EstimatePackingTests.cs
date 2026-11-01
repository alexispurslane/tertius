using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="EstimatePacking"/>: the C# side of the packed estimate of Shaders/estimate.glsl, its open texel and the
/// reads of a packed image's halves into the bounce, the occlusion and the open share
/// (docs/plans/screen-space-illumination.md, "The estimate of a texel").
/// </summary>
public sealed class EstimatePackingTests
{
    [Fact]
    public void TheOpenTexelHoldsNoBounceFullVisibilityAndAnOpenShareOfOne()
    {
        float[] halves = HalvesOf(EstimatePacking.OpenTexel);

        EstimatePacking.BounceAndOcclusion(halves).Should().Equal(0f, 0f, 0f, 1f);
        EstimatePacking.OpenShare(halves).Should().Equal(1f, 1f, 1f);
    }

    [Fact]
    public void ATexelsFirstFourHalvesAreItsBounceAndItsOcclusion()
    {
        float[] halves = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0f, 1f, 2f, 3f, 0.25f, 0f, 0f, 0f, 0f];

        EstimatePacking.BounceAndOcclusion(halves).Should().Equal(0.1f, 0.2f, 0.3f, 0.4f, 1f, 2f, 3f, 0.25f);
    }

    [Fact]
    public void ATexelsOpenShareIsOneMinusTheBlockedShareInItsFifthToSeventhHalves()
    {
        float[] halves = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.75f, 0.25f, 0f, 1f, 2f, 3f, 0.25f, 1f, 0f, 0.125f, 0f];

        EstimatePacking.OpenShare(halves).Should().Equal(0.5f, 0.25f, 0.75f, 0f, 1f, 0.875f);
    }

    /// <summary>
    /// The eight halves of a texel of four words, in the order of a copy to memory: packHalf2x16 puts its first half in
    /// the low 16 bits of a word, and the device stores the words little-endian.
    /// </summary>
    private static float[] HalvesOf(ClearColorValue texel) =>
        [
            .. new[] { texel.Uint32_0, texel.Uint32_1, texel.Uint32_2, texel.Uint32_3 }.SelectMany(word =>
                new[] { (ushort)(word & 0xFFFF), (ushort)(word >> 16) }.Select(bits =>
                    (float)BitConverter.UInt16BitsToHalf(bits)
                )
            ),
        ];
}
