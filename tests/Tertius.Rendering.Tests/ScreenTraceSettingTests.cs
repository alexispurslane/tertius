namespace Tertius.Rendering.Tests;

/// <summary>
/// The screen tracer's setting (docs/plans/temporal-renderer.md, section 6): its default value, the ranges that the
/// renderer refuses when it is made, and the warm-up draws of a still, with no GPU.
/// </summary>
public sealed class ScreenTraceSettingTests
{
    private static ScreenTrace Trace => ScreenTrace.Default;

    /// <summary>One setting outside its range per rule, each named for the rule that it breaks.</summary>
    public static TheoryData<string, ScreenTraceSetting> SettingsOutsideTheirRange =>
        new()
        {
            { "maximum roughness below the hand-over band", Trace with { MaxRoughness = 0.09f } },
            { "maximum roughness above 1", Trace with { MaxRoughness = 1.01f } },
            { "maximum roughness not a number", Trace with { MaxRoughness = float.NaN } },
            { "reflection steps of 0", Trace with { ReflectionSteps = 0 } },
            { "reflection steps above 4096", Trace with { ReflectionSteps = 4097 } },
            { "diffuse steps of 0", Trace with { DiffuseSteps = 0 } },
            { "diffuse steps above 4096", Trace with { DiffuseSteps = 4097 } },
        };

    /// <summary>Settings at the edges of every range, which the renderer takes.</summary>
    public static TheoryData<string, ScreenTraceSetting> SettingsAtTheEdgesOfTheirRange =>
        new()
        {
            { "no screen tracer", new NoScreenTrace() },
            { "the default tracer", Trace },
            { "a maximum roughness of the band's width", Trace with { MaxRoughness = ScreenTrace.HandOverWidth } },
            { "a maximum roughness of 1", Trace with { MaxRoughness = 1 } },
            { "1 step of each kind", Trace with { ReflectionSteps = 1, DiffuseSteps = 1 } },
            { "4096 steps of each kind", Trace with { ReflectionSteps = 4096, DiffuseSteps = 4096 } },
        };

    [Fact]
    public void TheDefaultValueOfTheSettingIsNoScreenTracer()
    {
        ScreenTraceSetting setting = default;

        setting.Should().Be(new ScreenTraceSetting(new NoScreenTrace()), "no tracer needs no values");
    }

    [Fact]
    public void TheRendererOptionsDefaultToTheDesignsTracerAndThirtyTwoWarmUpDraws()
    {
        var options = new RendererOptions();

        options
            .ScreenTrace.Should()
            .Be(new ScreenTraceSetting(new ScreenTrace(MaxRoughness: 0.5f, ReflectionSteps: 96, DiffuseSteps: 62)));
        options.WarmUpFrames.Should().Be(32);
    }

    [Theory]
    [MemberData(nameof(SettingsOutsideTheirRange))]
    public void ASettingOutsideItsRangeIsRefusedWhenTheRendererIsMade(string rule, ScreenTraceSetting setting) =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 64,
                        Height = 64,
                        ScreenTrace = setting,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>("the setting breaks the rule '{0}'", rule);

    [Theory]
    [MemberData(nameof(SettingsOutsideTheirRange))]
    public void ASettingOutsideItsRangeHasAProblem(string rule, ScreenTraceSetting setting) =>
        setting.Problem.Should().NotBeNull("the setting breaks the rule '{0}'", rule);

    [Theory]
    [MemberData(nameof(SettingsAtTheEdgesOfTheirRange))]
    public void ASettingAtTheEdgesOfItsRangeHasNoProblem(string edge, ScreenTraceSetting setting) =>
        setting.Problem.Should().BeNull("the renderer takes {0}", edge);

    [Fact]
    public void AProblemNamesTheFieldAndItsValue()
    {
        ScreenTraceSetting setting = Trace with { DiffuseSteps = 4097 };

        setting.Problem.Should().Contain("ScreenTrace.DiffuseSteps").And.Contain("4097");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(RendererOptions.MaxWarmUpFrames + 1)]
    public void AWarmUpCountOutsideZeroTo1024IsRefusedWhenTheRendererIsMade(int warmUpFrames) =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 64,
                        Height = 64,
                        WarmUpFrames = warmUpFrames,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage($"*WarmUpFrames must be 0 to 1024, not {warmUpFrames}*");
}
