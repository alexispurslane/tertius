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
        LtcTable
            .Shared.Texels.Slice(start, fitted.Length)
            .ToArray()
            .Should()
            .Equal(fitted, "the table holds the fit of tools/Tertius.LtcFit, and each row starts from its own first guess");
    }

    [Fact]
    public void HeadOnTheCosineOfARoughLobeIsRoundAboutTheNormal()
    {
        LtcMatrix inverse = LtcTable.Shared.Sample(roughness: 0.5, nDotV: 1);

        inverse.M02.Should().Be(0, "head on, the lobe does not lean");
        inverse.M20.Should().Be(0, "head on, the lobe does not lean");
        inverse.M00.Should().Be(1, "head on, the lobe is as wide along x as along y");
        inverse.M22.Should().BeApproximately(0.5, 0.02, "the light's angle off the normal is twice the half vector's, so 2α");
    }
}
