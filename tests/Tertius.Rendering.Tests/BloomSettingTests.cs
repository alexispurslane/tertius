namespace Tertius.Rendering.Tests;

/// <summary>
/// The bloom setting (ARCHITECTURE.md, "Bloom"): its default value, the ranges that the renderer refuses when it is
/// made, and the setting of <see cref="RendererOptions.Bloom"/>, with no GPU.
/// </summary>
public sealed class BloomSettingTests
{
    private static Bloom Glow => Bloom.Default;

    /// <summary>One setting outside its range per rule of "Bloom", each named for the rule that it breaks.</summary>
    public static TheoryData<string, BloomSetting> SettingsOutsideTheirRange =>
        new()
        {
            { "threshold below 0", Glow with { Threshold = -0.01f } },
            { "threshold not a number", Glow with { Threshold = float.NaN } },
            { "threshold infinite", Glow with { Threshold = float.PositiveInfinity } },
            { "knee below 0", Glow with { Knee = -0.01f } },
            { "knee above 1", Glow with { Knee = 1.01f } },
            { "knee not a number", Glow with { Knee = float.NaN } },
            { "strength of 0", Glow with { Strength = 0 } },
            { "strength below 0", Glow with { Strength = -1 } },
            { "strength not a number", Glow with { Strength = float.NaN } },
            { "strength infinite", Glow with { Strength = float.PositiveInfinity } },
            { "levels of 0", Glow with { Levels = 0 } },
            { "levels above 14", Glow with { Levels = 15 } },
        };

    /// <summary>Settings at the edges of every range, which the renderer takes.</summary>
    public static TheoryData<string, BloomSetting> SettingsAtTheEdgesOfTheirRange =>
        new()
        {
            { "no bloom", new NoBloom() },
            { "the default bloom", Glow },
            { "a threshold of 0", Glow with { Threshold = 0 } },
            { "a knee of 0", Glow with { Knee = 0 } },
            { "a knee of 1", Glow with { Knee = 1 } },
            { "the smallest positive strength", Glow with { Strength = float.Epsilon } },
            { "1 level", Glow with { Levels = 1 } },
            { "14 levels", Glow with { Levels = Bloom.MaxLevels } },
        };

    [Fact]
    public void TheDefaultValueOfTheSettingIsNoBloom()
    {
        BloomSetting setting = default;

        setting.Should().Be(new BloomSetting(new NoBloom()), "no bloom needs no values, so it is a valid zero case");
    }

    [Fact]
    public void TheRendererOptionsDefaultToTheGlowOfTheDefaultBloom()
    {
        BloomSetting setting = new RendererOptions().Bloom;

        setting
            .Should()
            .Be(new BloomSetting(new Bloom(Threshold: 1, Knee: 0.5f, Strength: 0.25f, Levels: 6)))
            .And.Be(new BloomSetting(Bloom.Default));
    }

    [Theory]
    [MemberData(nameof(SettingsOutsideTheirRange))]
    public void ASettingOutsideItsRangeIsRefusedWhenTheRendererIsMade(string rule, BloomSetting setting) =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 64,
                        Height = 64,
                        Bloom = setting,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>("the setting breaks the rule '{0}'", rule);

    [Theory]
    [MemberData(nameof(SettingsOutsideTheirRange))]
    public void ASettingOutsideItsRangeHasAProblem(string rule, BloomSetting setting) =>
        setting.Problem.Should().NotBeNull("the setting breaks the rule '{0}'", rule);

    [Theory]
    [MemberData(nameof(SettingsAtTheEdgesOfTheirRange))]
    public void ASettingAtTheEdgesOfItsRangeHasNoProblem(string edge, BloomSetting setting) =>
        setting.Problem.Should().BeNull("the renderer takes {0}", edge);

    [Fact]
    public void AProblemNamesTheFieldAndItsValue()
    {
        BloomSetting setting = Glow with { Levels = 15 };

        setting.Problem.Should().Contain("Bloom.Levels").And.Contain("15");
    }
}
