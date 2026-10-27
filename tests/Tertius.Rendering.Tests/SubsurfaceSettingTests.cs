namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="SubsurfaceSetting"/> and <see cref="RendererOptions.Subsurface"/>: the zero case, the default of the
/// options and the rule that refuses a setting (docs/plans/skin-and-hair.md, decision 29), with no GPU.
/// </summary>
public sealed class SubsurfaceSettingTests
{
    [Fact]
    public void TheZeroValueIsNoSubsurface() =>
        (default(SubsurfaceSetting) is NoSubsurface)
            .Should()
            .BeTrue("the zero case is the look from before the scatter, which skin shades as the standard model");

    [Fact]
    public void TheDefaultTakesEightTapsPerSide() =>
        (new RendererOptions().Subsurface is SeparableSubsurface { TapsPerSide: 8 })
            .Should()
            .BeTrue("the renderer scatters skin by default, with Jimenez's high setting of 17 taps per pass");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SeparableSubsurface.MaxTapsPerSide + 1)]
    [InlineData(int.MaxValue)]
    public void TapsOutsideOneToSixteenAreRefused(int tapsPerSide) =>
        new SubsurfaceSetting(new SeparableSubsurface(tapsPerSide))
            .Problem.Should()
            .Contain("TapsPerSide")
            .And.Contain(tapsPerSide.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(1)]
    [InlineData(SeparableSubsurface.MaxTapsPerSide)]
    public void TapsAtTheEdgesOfOneToSixteenAreAccepted(int tapsPerSide) =>
        new SubsurfaceSetting(new SeparableSubsurface(tapsPerSide)).Problem.Should().BeNull();

    [Fact]
    public void NoSubsurfaceIsAccepted() => new SubsurfaceSetting(new NoSubsurface()).Problem.Should().BeNull();

    [Fact]
    public void TheRendererRefusesASettingWithAProblem()
    {
        Action create = () =>
            VulkanRenderer.CreateHeadless(
                new()
                {
                    Width = 16,
                    Height = 16,
                    Subsurface = new SeparableSubsurface(TapsPerSide: 0),
                }
            );

        create
            .Should()
            .Throw<ArgumentOutOfRangeException>("the options are checked before a device is made")
            .WithMessage("*TapsPerSide*");
    }
}
