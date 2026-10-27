namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="ScreenSpaceLightSetting"/> and <see cref="RendererOptions.ScreenSpaceLight"/>: the zero case, the default
/// of the options, and the rules that refuse a setting (docs/plans/screen-space-illumination.md, "The setting").
/// </summary>
public sealed class ScreenSpaceLightSettingTests
{
    [Fact]
    public void TheDefaultValueIsNoScreenSpaceLight() =>
        (default(ScreenSpaceLightSetting) is NoScreenSpaceLight).Should().BeTrue("the zero case is the open estimate");

    [Fact]
    public void RendererOptionsDefaultToVisibilityBitmaskLightWithARadiusOfOneMeterAndThreeSlicesOfThreeStepsPerSide() =>
        (
            new RendererOptions().ScreenSpaceLight is VisibilityBitmaskLight
            {
                RadiusMeters: 1,
                Slices: 3,
                StepsPerSide: 3
            }
        )
            .Should()
            .BeTrue();

    [Fact]
    public void TheDefaultAndTheLimitsOfEveryFieldAreAccepted()
    {
        VisibilityBitmaskLight smallest = VisibilityBitmaskLight.Default with
        {
            RadiusMeters = float.Epsilon,
            Slices = 1,
            StepsPerSide = 1,
        };
        VisibilityBitmaskLight largest = VisibilityBitmaskLight.Default with
        {
            RadiusMeters = ScreenSpaceLightSetting.MaxRadiusMeters,
            Slices = ScreenSpaceLightSetting.MaxSlices,
            StepsPerSide = ScreenSpaceLightSetting.MaxStepsPerSide,
        };

        new ScreenSpaceLightSetting(VisibilityBitmaskLight.Default).Problem.Should().BeNull();
        new ScreenSpaceLightSetting(smallest).Problem.Should().BeNull();
        new ScreenSpaceLightSetting(largest).Problem.Should().BeNull();
        new ScreenSpaceLightSetting(new NoScreenSpaceLight()).Problem.Should().BeNull();
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ARadiusThatIsNotAFiniteNumberAboveZeroIsRefused(float radius) =>
        new ScreenSpaceLightSetting(VisibilityBitmaskLight.Default with { RadiusMeters = radius })
            .Problem.Should()
            .Contain("RadiusMeters");

    [Fact]
    public void ARadiusAboveTheLimitIsRefused() =>
        new ScreenSpaceLightSetting(
            VisibilityBitmaskLight.Default with
            {
                RadiusMeters = MathF.BitIncrement(ScreenSpaceLightSetting.MaxRadiusMeters),
            }
        )
            .Problem.Should()
            .Contain("RadiusMeters");

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void SlicesOutsideOneToSixteenAreRefused(int slices) =>
        new ScreenSpaceLightSetting(VisibilityBitmaskLight.Default with { Slices = slices })
            .Problem.Should()
            .Contain("Slices");

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void StepsPerSideOutsideOneToSixteenAreRefused(int steps) =>
        new ScreenSpaceLightSetting(VisibilityBitmaskLight.Default with { StepsPerSide = steps })
            .Problem.Should()
            .Contain("StepsPerSide");

    // CreateHeadless refuses a width of 0 just before it makes the device. So the message names the slices only when
    // the setting's refusal comes before that, and so before the device.
    [Fact]
    public void CreateHeadlessRefusesAnInvalidSettingBeforeItMakesADevice() =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 0,
                        Height = 64,
                        Impostors = false,
                        ScreenSpaceLight = VisibilityBitmaskLight.Default with { Slices = 0 },
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("*Slices*");
}
