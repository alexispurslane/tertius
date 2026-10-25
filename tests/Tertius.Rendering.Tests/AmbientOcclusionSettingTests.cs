namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="AmbientOcclusionSetting"/> and <see cref="RendererOptions.AmbientOcclusion"/>: the zero case, the default
/// of the options, and the rules that refuse a setting (docs/plans/ambient-occlusion.md, "The setting").
/// </summary>
public sealed class AmbientOcclusionSettingTests
{
    [Fact]
    public void TheDefaultValueIsNoAmbientOcclusion() =>
        (default(AmbientOcclusionSetting) is NoAmbientOcclusion).Should().BeTrue("the zero case is today's look");

    [Fact]
    public void RendererOptionsDefaultToGroundTruthOcclusionWithARadiusOfOneMeterAndThreeSlicesOfThreeStepsPerSide() =>
        (new RendererOptions().AmbientOcclusion is GroundTruthOcclusion { RadiusMeters: 1, Slices: 3, StepsPerSide: 3 })
            .Should()
            .BeTrue();

    [Fact]
    public void TheDefaultAndTheLimitsOfEveryFieldAreAccepted()
    {
        GroundTruthOcclusion smallest = GroundTruthOcclusion.Default with
        {
            RadiusMeters = float.Epsilon,
            Slices = 1,
            StepsPerSide = 1,
        };
        GroundTruthOcclusion largest = GroundTruthOcclusion.Default with
        {
            RadiusMeters = AmbientOcclusionSetting.MaxRadiusMeters,
            Slices = AmbientOcclusionSetting.MaxSlices,
            StepsPerSide = AmbientOcclusionSetting.MaxStepsPerSide,
        };

        new AmbientOcclusionSetting(GroundTruthOcclusion.Default).Problem.Should().BeNull();
        new AmbientOcclusionSetting(smallest).Problem.Should().BeNull();
        new AmbientOcclusionSetting(largest).Problem.Should().BeNull();
        new AmbientOcclusionSetting(new NoAmbientOcclusion()).Problem.Should().BeNull();
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ARadiusThatIsNotAFiniteNumberAboveZeroIsRefused(float radius) =>
        new AmbientOcclusionSetting(GroundTruthOcclusion.Default with { RadiusMeters = radius })
            .Problem.Should()
            .Contain("RadiusMeters");

    [Fact]
    public void ARadiusAboveTheLimitIsRefused() =>
        new AmbientOcclusionSetting(
            GroundTruthOcclusion.Default with
            {
                RadiusMeters = MathF.BitIncrement(AmbientOcclusionSetting.MaxRadiusMeters),
            }
        )
            .Problem.Should()
            .Contain("RadiusMeters");

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void SlicesOutsideOneToSixteenAreRefused(int slices) =>
        new AmbientOcclusionSetting(GroundTruthOcclusion.Default with { Slices = slices })
            .Problem.Should()
            .Contain("Slices");

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void StepsPerSideOutsideOneToSixteenAreRefused(int steps) =>
        new AmbientOcclusionSetting(GroundTruthOcclusion.Default with { StepsPerSide = steps })
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
                        AmbientOcclusion = GroundTruthOcclusion.Default with { Slices = 0 },
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("*Slices*");
}
