namespace Tertius.Rendering.Tests;

/// <summary>
/// The key share of a light at a screen corner (docs/plans/light-pairs.md, "Key and fill"): which lights a corner shades
/// in full, and how a light moves between its full response and its fill response. A light's loss is what its fill
/// response loses of its full response at the corner.
/// </summary>
public sealed class KeyAndFillTests
{
    /// <summary>
    /// The key shares of lights of <paramref name="losses"/> at a corner of <paramref name="split"/> whose whole direct
    /// light is <paramref name="whole"/>.
    /// </summary>
    private static double[] KeyShares(KeyAndFill split, double whole, params double[] losses)
    {
        double threshold = split.Threshold(losses: losses, whole: whole);
        return [.. losses.Select(loss => KeyAndFill.KeyShare(loss, threshold))];
    }

    [Fact]
    public void ACornerWithNoMoreLightsThanItsKeyLightsShadesEveryLightInFull() =>
        KeyShares(new KeyAndFill(KeyLights: 3), 256, 5, 2, 200).Should().Equal(1, 1, 1);

    [Fact]
    public void ALightWhoseFillResponseLosesNothingHasNoKeyShare() =>
        KeyShares(new KeyAndFill(KeyLights: 3), 256, 5, 0, 200).Should().Equal(1, 0, 1);

    [Fact]
    public void ALightWhoseLossIsNegligibleInTheCornersWholeLightHasNoKeyShare()
    {
        // The negligible loss of a whole light of 512 is 2: the second light, of 1.9, stays below it though the corner
        // has key lights to spare.
        double[] shares = KeyShares(new KeyAndFill(KeyLights: 3), 512, 5, 1.9, 200);

        shares.Should().Equal(1, 0, 1);
    }

    [Fact]
    public void ALightJustAboveTheNegligibleLossTakesASmallKeyShare()
    {
        // 2.5 is one and a quarter times the negligible loss of 2.
        double[] shares = KeyShares(new KeyAndFill(KeyLights: 3), 512, 2.5);

        shares[0].Should().BeApproximately(0.25, 1e-12);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void AtMostTheKeyLightsOfACornerHaveAKeyShare(int keyLights)
    {
        double[] losses = [0.4, 7, 3, 3.5, 0.9, 12, 2.2, 5, 1.1, 6.5];

        double[] shares = KeyShares(new KeyAndFill(keyLights), 0, losses);

        shares.Count(share => share > 0).Should().BeLessThanOrEqualTo(keyLights);
    }

    [Fact]
    public void TheLightsWithAKeyShareAreTheOnesOfTheGreatestLoss()
    {
        double[] shares = KeyShares(new KeyAndFill(KeyLights: 2), 46, 1, 9, 2, 30, 4);

        // The third greatest loss, of 4, is the threshold: 9 and 30 are both more than twice it.
        shares.Should().Equal(0, 1, 0, 1, 0);
    }

    [Fact]
    public void ALightBetweenOneAndTwoThresholdsSharesItsLightBetweenFullAndFill()
    {
        double[] shares = KeyShares(new KeyAndFill(KeyLights: 1), 10, 6, 4);

        // 6 is one and a half times the threshold of 4.
        shares[0].Should().BeApproximately(0.5, 1e-12);
        shares[1].Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2000)]
    public void ALightThatPassesAnotherInLossChangesNoShareAtOnce(double whole)
    {
        var split = new KeyAndFill(KeyLights: 2);
        // The second light rises from below the third to above the first, in small steps. With a whole light of 2,000,
        // it also crosses the negligible loss of about 7.8.
        double[] before = KeyShares(split, whole, 5, 1, 3, 2.5);
        for (double loss = 1; loss <= 20; loss += 0.01)
        {
            double[] after = KeyShares(split, whole, 5, loss, 3, 2.5);
            for (int light = 0; light < after.Length; light++)
                Math.Abs(after[light] - before[light])
                    .Should()
                    .BeLessThan(0.02, "light {0} must not jump at loss {1}", light, loss);
            before = after;
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(1000)]
    public void TheSharesStayTheSameWhenEveryLossAndTheWholeLightTakeOneFactor(double whole)
    {
        var split = new KeyAndFill(KeyLights: 2);
        double[] losses = [0.4, 7, 3, 3.5, 0.9];

        double[] dim = KeyShares(split, whole * 1e-6, [.. losses.Select(loss => loss * 1e-6)]);

        // The exposure scales every light's shown radiance alike, so it must not move a light between full and fill.
        double[] plain = KeyShares(split, whole, losses);
        plain.Should().Contain(share => share > 0);
        for (int light = 0; light < plain.Length; light++)
            dim[light].Should().BeApproximately(plain[light], 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void AKeyLightCountOutsideOneToFourHasAProblem(int keyLights) =>
        new LightPairSetting(new KeyAndFill(keyLights)).Problem.Should().Contain("KeyAndFill.KeyLights");

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AKeyLightCountFromOneToFourHasNoProblem(int keyLights) =>
        new LightPairSetting(new KeyAndFill(keyLights)).Problem.Should().BeNull();

    [Fact]
    public void TheDefaultValueOfTheSettingShadesEveryPair()
    {
        (default(LightPairSetting) is EveryLightPair).Should().BeTrue();
        default(LightPairSetting).Problem.Should().BeNull();
    }

    [Fact]
    public void TheRenderersDefaultIsAKeyAndFillThatTheRendererCanUse()
    {
        LightPairSetting chosen = new RendererOptions().LightPairs;

        (chosen is KeyAndFill).Should().BeTrue();
        chosen.Problem.Should().BeNull();
    }
}
