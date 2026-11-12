using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="LocalShadowSetting"/>, <see cref="RendererOptions.LocalShadows"/> and
/// <see cref="RendererOptions.CascadedLights"/>: the zero case, the defaults of the options, and the rules that refuse a
/// setting when the renderer is made (docs/plans/local-light-shadows.md, "The setting").
/// </summary>
public sealed class LocalShadowSettingTests
{
    private static LocalShadowAtlas Atlas => LocalShadowAtlas.Default;

    /// <summary>One setting outside its range per rule of "The setting", with the field that the refusal names.</summary>
    public static TheoryData<string, LocalShadowSetting, string> SettingsOutsideTheirRange =>
        new()
        {
            { "no tiles", Atlas with { Tiles = 0 }, "LocalShadowAtlas.Tiles" },
            { "fewer than no tiles", Atlas with { Tiles = -1 }, "LocalShadowAtlas.Tiles" },
            { "tiles below 64 texels", Atlas with { TileSize = 32 }, "LocalShadowAtlas.TileSize" },
            { "tiles above 4,096 texels", Atlas with { TileSize = 8192 }, "LocalShadowAtlas.TileSize" },
            { "tiles of no texels", Atlas with { TileSize = 0 }, "LocalShadowAtlas.TileSize" },
            {
                "tiles of a side that is not a power of two",
                Atlas with
                {
                    TileSize = 500,
                },
                "LocalShadowAtlas.TileSize"
            },
        };

    /// <summary>Settings at the edges of every range, which the renderer takes.</summary>
    public static TheoryData<string, LocalShadowSetting> SettingsAtTheEdgesOfTheirRange =>
        new()
        {
            { "no local shadows", new NoLocalShadows() },
            { "the default atlas", Atlas },
            { "1 tile", Atlas with { Tiles = 1 } },
            { "tiles of 64 texels", Atlas with { TileSize = LocalShadowSetting.MinTileSize } },
            { "tiles of 4,096 texels", Atlas with { TileSize = LocalShadowSetting.MaxTileSize } },
        };

    [Fact]
    public void AnAtlasLeavesTheChoiceOfItsReadsToTheDriver()
    {
        Atlas.Reads.Should().Be(LocalShadowReads.ForDriver, "the faster way to read a tile differs by driver");
    }

    [Fact]
    public void OnMoltenVkATileIsReadAsSixteenCornerMeans()
    {
        VulkanRenderer
            .TileReadsFor(LocalShadowReads.ForDriver, DriverId.Moltenvk)
            .Should()
            .Be(
                LocalShadowReads.SixteenCornerMeans,
                "Apple's shader compiler holds the 36 values of nine gathers at once, and the direct pass pays for them"
            );
    }

    [Theory]
    [InlineData(DriverId.NvidiaProprietary)]
    [InlineData(DriverId.MesaRadv)]
    [InlineData(DriverId.IntelOpenSourceMesa)]
    [InlineData(DriverId.MesaLlvmpipe)]
    public void OnEveryOtherDriverATileIsReadAsNineGathers(DriverId driver)
    {
        VulkanRenderer
            .TileReadsFor(LocalShadowReads.ForDriver, driver)
            .Should()
            .Be(LocalShadowReads.NineGathers, "a gather is the cheapest way to four comparisons where each read costs");
    }

    [Theory]
    [InlineData(LocalShadowReads.NineGathers, DriverId.Moltenvk)]
    [InlineData(LocalShadowReads.SixteenCornerMeans, DriverId.NvidiaProprietary)]
    public void AnAtlasThatNamesItsReadsKeepsThemOnEveryDriver(LocalShadowReads reads, DriverId driver)
    {
        VulkanRenderer.TileReadsFor(reads, driver).Should().Be(reads, "a named choice is not the driver's to change");
    }

    [Fact]
    public void TheDefaultValueOfTheSettingIsNoLocalShadows()
    {
        LocalShadowSetting setting = default;

        setting
            .Should()
            .Be(
                new LocalShadowSetting(new NoLocalShadows()),
                "no local shadows needs no values, so it is a valid zero case"
            );
    }

    [Fact]
    public void ANewRendererShadowsItsNearLightsAndOneFarLight()
    {
        var options = new RendererOptions();

        options
            .LocalShadows.Should()
            .Be(new LocalShadowSetting(new LocalShadowAtlas(Tiles: 64, TileSize: 512)), "64 tiles of 512² take 64 MB");
        options.CascadedLights.Should().Be(1, "one far light gets cascades, as the sun does today");
    }

    [Theory]
    [MemberData(nameof(SettingsAtTheEdgesOfTheirRange))]
    public void ASettingAtTheEdgesOfItsRangeHasNoProblem(string edge, LocalShadowSetting setting) =>
        setting.Problem.Should().BeNull("the renderer takes {0}", edge);

    [Theory]
    [MemberData(nameof(SettingsOutsideTheirRange))]
    public void ASettingOutsideItsRangeHasAProblemThatNamesItsField(
        string rule,
        LocalShadowSetting setting,
        string field
    ) => setting.Problem.Should().Contain(field, "the setting breaks the rule '{0}'", rule);

    [Fact]
    public void AProblemNamesTheValue() =>
        new LocalShadowSetting(Atlas with { TileSize = 500 }).Problem.Should().Contain("500");

    // CreateHeadless refuses a width of 0 just before it makes the device. So the message names the field only when the
    // setting's refusal comes before that, and so before the device.
    [Theory]
    [MemberData(nameof(SettingsOutsideTheirRange))]
    public void ASettingOutsideItsRangeIsRefusedWhenTheRendererIsMade(
        string rule,
        LocalShadowSetting setting,
        string field
    ) =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 0,
                        Height = 64,
                        LocalShadows = setting,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>("the setting breaks the rule '{0}'", rule)
            .WithMessage($"*{field}*");

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void CascadedLightsOutsideZeroToFourAreRefused(int cascadedLights) =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 0,
                        Height = 64,
                        CascadedLights = cascadedLights,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage($"*CascadedLights*{cascadedLights}*");

    // The width of 0 is the next refusal, so a message about the width shows that the count passed.
    [Theory]
    [InlineData(0)]
    [InlineData(RendererOptions.MaxCascadedLights)]
    public void CascadedLightsFromZeroToFourAreTaken(int cascadedLights) =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 0,
                        Height = 64,
                        CascadedLights = cascadedLights,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("*positive Width and Height*");
}
