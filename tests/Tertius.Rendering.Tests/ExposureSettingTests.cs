namespace Tertius.Rendering.Tests;

/// <summary>
/// The exposure setting (docs/plans/auto-exposure.md, "The setting"): its default value, the ranges that the renderer
/// refuses when it is made, and the exposure that the first frame seeds.
/// </summary>
public sealed class ExposureSettingTests
{
    private static AutoExposure Auto => AutoExposure.Default;

    /// <summary>One setting outside its range per rule of "The setting", each named for the rule that it breaks.</summary>
    public static TheoryData<string, ExposureSetting> SettingsOutsideTheirRange =>
        new()
        {
            { "fixed stops not finite", new FixedExposure(float.NaN) },
            { "fixed stops above 64", new FixedExposure(64.5f) },
            { "fixed stops below -64", new FixedExposure(-64.5f) },
            { "lowest stops below -64", Auto with { MinStops = -64.5f } },
            { "highest stops not finite", Auto with { MaxStops = float.PositiveInfinity } },
            { "highest stops above 64", Auto with { MaxStops = 64.5f } },
            { "compensation not finite", Auto with { CompensationStops = float.NaN } },
            { "compensation above 64", Auto with { CompensationStops = 64.5f } },
            { "compensation below -64", Auto with { CompensationStops = -64.5f } },
            { "lowest stops above the highest", Auto with { MinStops = 2.5f, MaxStops = 2 } },
            { "dark share below 0", Auto with { DarkShare = -0.01f } },
            { "dark share not finite", Auto with { DarkShare = float.NaN } },
            { "bright share below 0", Auto with { BrightShare = -0.01f } },
            { "bright share not finite", Auto with { BrightShare = float.PositiveInfinity } },
            { "shares that add up to 1", Auto with { DarkShare = 0.5f, BrightShare = 0.5f } },
            { "time into the dark below 0", Auto with { IntoDarkSeconds = -1 } },
            { "time into the dark not finite", Auto with { IntoDarkSeconds = float.PositiveInfinity } },
            { "time into the light below 0", Auto with { IntoLightSeconds = -1 } },
            { "time into the light not finite", Auto with { IntoLightSeconds = float.NaN } },
        };

    /// <summary>Settings at the edges of every range, which the renderer takes.</summary>
    public static TheoryData<string, ExposureSetting> SettingsAtTheEdgesOfTheirRange =>
        new()
        {
            { "fixed stops of 64", new FixedExposure(64) },
            { "fixed stops of -64", new FixedExposure(-64) },
            {
                "the widest clamp and compensation",
                Auto with
                {
                    MinStops = -64,
                    MaxStops = 64,
                    CompensationStops = 64,
                }
            },
            { "the lowest compensation", Auto with { CompensationStops = -64 } },
            { "a clamp of one value", Auto with { MinStops = 3, MaxStops = 3 } },
            { "shares of 0", Auto with { DarkShare = 0, BrightShare = 0 } },
            { "shares just below 1 together", Auto with { DarkShare = 0.5f, BrightShare = 0.49f } },
            { "time constants of 0", Auto with { IntoDarkSeconds = 0, IntoLightSeconds = 0 } },
        };

    [Fact]
    public void TheDefaultValueOfTheSettingIsAFixedExposureOfZeroStops()
    {
        ExposureSetting setting = default;

        setting
            .Should()
            .Be(
                new ExposureSetting(new FixedExposure(0)),
                "zero stops multiply by exactly 1, which is the look without an exposure"
            );
    }

    [Theory]
    [MemberData(nameof(SettingsOutsideTheirRange))]
    public void ASettingOutsideItsRangeIsRefusedWhenTheRendererIsMade(string rule, ExposureSetting setting) =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 64,
                        Height = 64,
                        Exposure = setting,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>("the setting breaks the rule '{0}'", rule);

    [Theory]
    [MemberData(nameof(SettingsAtTheEdgesOfTheirRange))]
    public void ASettingAtTheEdgesOfItsRangeHasNoProblem(string edge, ExposureSetting setting) =>
        setting.Problem.Should().BeNull("the renderer takes {0}", edge);

    [Fact]
    public void AnAutomaticSettingSeedsZeroStopsInsideItsClamp()
    {
        ExposureSetting brightClamp = Auto with { MinStops = 2, MaxStops = 5 };
        ExposureSetting darkClamp = Auto with { MinStops = -5, MaxStops = -2 };
        ExposureSetting defaultClamp = Auto;

        (brightClamp.InitialStops, darkClamp.InitialStops, defaultClamp.InitialStops).Should().Be((2f, -2f, 0f));
    }

    [Fact]
    public void AFixedSettingSeedsItsStops()
    {
        ExposureSetting setting = new FixedExposure(-3.25f);

        setting.InitialStops.Should().Be(-3.25f);
    }
}
