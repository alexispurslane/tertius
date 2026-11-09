namespace Tertius.Rendering.Tests;

/// <summary>
/// The embedded LTC table of the rectangle lights (ARCHITECTURE.md, "Rectangle lights"): it is what tools/Tertius.LtcFit
/// fits to our GGX lobe, so a change of the lobe without a new fit fails here, and it reads as the GPU samples it.
/// </summary>
public sealed class LtcTableTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(32)]
    [InlineData(63)]
    public void ARowOfTheTableIsTheFitOfOurGgxLobe(int row)
    {
        Half[] fitted = LtcFit.LtcFit.Row(row);

        int start = row * LtcTable.Size * LtcTable.Channels;
        double[] stored =
        [
            .. LtcTable.Shared.Texels.Slice(start, fitted.Length).ToArray().Select(texel => (double)texel),
        ];

        // Two steps of a half float, 2⁻⁹ of the value, plus a little at 0: another machine's sine and cosine can differ in
        // their last bit, which can move the simplex's path and flip a rounding. A changed lobe moves the fit by percents.
        stored
            .Should()
            .Equal(
                fitted.Select(texel => (double)texel),
                (a, b) => Math.Abs(a - b) <= Math.Abs(b) / 512 + 1e-4,
                "the table holds the fit of tools/Tertius.LtcFit, and each row starts from its own first guess"
            );
    }

    [Fact]
    public void ATableOfAnotherSizeIsRefused()
    {
        Action make = () => _ = new LtcTable(new Half[LtcTable.Size * LtcTable.Size]);

        make.Should().Throw<ArgumentException>().WithMessage("An LTC table holds 16384 halves, not 4096.*");
    }

    [Fact]
    public void HeadOnTheCosineOfARoughLobeIsRoundAboutTheNormal()
    {
        LtcMatrix inverse = LtcTable.Shared.Sample(roughness: 0.5, nDotV: 1);

        inverse.M02.Should().Be(0, "head on, the lobe does not lean");
        inverse.M20.Should().Be(0, "head on, the lobe does not lean");
        inverse.M00.Should().Be(1, "head on, the lobe is as wide along x as along y");
        inverse
            .M22.Should()
            .BeApproximately(0.5, 0.02, "the light's angle off the normal is twice the half vector's, so 2α");
    }
}
