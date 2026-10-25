namespace Tertius.Rendering.Tests;

/// <summary>The checks of a <see cref="Bloom"/> setting and of <see cref="RendererOptions.Bloom"/>, with no GPU.</summary>
public sealed class BloomSettingTests
{
    [Theory]
    [InlineData(-0.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ABloomRefusesAThresholdThatIsNegativeOrNotFinite(float threshold) =>
        FluentActions
            .Invoking(() => new Bloom(threshold: threshold, knee: 0.5f, strength: 1, levels: 6))
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithParameterName("threshold");

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(float.NaN)]
    public void ABloomRefusesAKneeOutsideZeroToOne(float knee) =>
        FluentActions
            .Invoking(() => new Bloom(threshold: 1, knee: knee, strength: 1, levels: 6))
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithParameterName("knee");

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ABloomRefusesAStrengthThatIsNotPositiveAndFinite(float strength) =>
        FluentActions
            .Invoking(() => new Bloom(threshold: 1, knee: 0.5f, strength: strength, levels: 6))
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithParameterName("strength");

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void ABloomRefusesLevelsOutsideOneToSixteen(int levels) =>
        FluentActions
            .Invoking(() => new Bloom(threshold: 1, knee: 0.5f, strength: 1, levels: levels))
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithParameterName("levels");

    [Theory]
    [InlineData(0f, 0f, 1)]
    [InlineData(0f, 1f, 16)]
    public void ABloomTakesTheEndsOfEachRange(float threshold, float knee, int levels) =>
        FluentActions
            .Invoking(() => new Bloom(threshold: threshold, knee: knee, strength: float.Epsilon, levels: levels))
            .Should()
            .NotThrow(
                "a threshold of 0, a knee of 0 or 1, the smallest positive strength, and 1 or 16 levels are valid"
            );

    [Fact]
    public void TheRendererRefusesABloomSettingThatHoldsNoCase() =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 64,
                        Height = 64,
                        Bloom = default,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithParameterName("options");
}
