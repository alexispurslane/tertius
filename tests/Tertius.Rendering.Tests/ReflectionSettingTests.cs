namespace Tertius.Rendering.Tests;

/// <summary>
/// <see cref="ReflectionSetting"/> and <see cref="RendererOptions.Reflections"/>: the zero case, the default of the
/// options, the rules that refuse a setting, and the roughness fade (docs/plans/screen-space-reflections.md, "The
/// setting"), with no GPU.
/// </summary>
public sealed class ReflectionSettingTests
{
    [Fact]
    public void TheDefaultValueIsNoReflections() =>
        (default(ReflectionSetting) is NoReflections).Should().BeTrue("the zero case is the look from before SSR");

    [Fact]
    public void TheDefaultTracesUpToARoughnessOf0Point6In128Steps() =>
        (new RendererOptions().Reflections is ConeTracedReflections { MaxRoughness: 0.6f, MaxSteps: 128 })
            .Should()
            .BeTrue("the renderer reflects by default, with Unreal Engine 4's maximum roughness");

    [Fact]
    public void TheDefaultAndTheLimitsOfEveryFieldAreAccepted()
    {
        ConeTracedReflections smallest = ConeTracedReflections.Default with
        {
            MaxRoughness = MathF.BitIncrement((float)GgxLobe.MinRoughness),
            MaxSteps = 1,
        };
        ConeTracedReflections largest = ConeTracedReflections.Default with
        {
            MaxRoughness = 1,
            MaxSteps = ConeTracedReflections.MaxStepsLimit,
        };

        new ReflectionSetting(ConeTracedReflections.Default).Problem.Should().BeNull();
        new ReflectionSetting(smallest).Problem.Should().BeNull();
        new ReflectionSetting(largest).Problem.Should().BeNull();
        new ReflectionSetting(new NoReflections()).Problem.Should().BeNull();
    }

    [Theory]
    [InlineData(0.045f)]
    [InlineData(0f)]
    [InlineData(-0.5f)]
    [InlineData(1.0001f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void AMaximumRoughnessAtOrBelowTheFloorAboveOneOrNotFiniteIsRefused(float maxRoughness) =>
        new ReflectionSetting(ConeTracedReflections.Default with { MaxRoughness = maxRoughness })
            .Problem.Should()
            .Contain("MaxRoughness");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1025)]
    public void StepsOutsideOneTo1024AreRefused(int maxSteps) =>
        new ReflectionSetting(ConeTracedReflections.Default with { MaxSteps = maxSteps })
            .Problem.Should()
            .Contain("MaxSteps");

    // The struct case has a zero value, which only the renderer can refuse. CreateHeadless refuses a width of 0 just
    // before it makes the device, so the message names the roughness only when the refusal comes before the device.
    [Fact]
    public void TheRendererRefusesAConeTracedReflectionsOfZerosBeforeItMakesADevice() =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 0,
                        Height = 64,
                        Impostors = false,
                        Reflections = new ConeTracedReflections(),
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("*MaxRoughness*");

    // The pyramids' sets are allocated for a side of 16,384 px. A device may allow a larger image, so the refusal comes
    // before the device, where no driver limit can refuse the size first.
    [Theory]
    [InlineData(16_385, 64)]
    [InlineData(64, 32_768)]
    public void TheRendererRefusesReflectionsOnAScreenWithASideAbove16384PxBeforeItMakesADevice(
        int width,
        int height
    ) =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = width,
                        Height = height,
                        Impostors = false,
                        Reflections = ConeTracedReflections.Default,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("*16384 px a side*");

    [Theory]
    [InlineData(0.045f, 1f)]
    [InlineData(0.3f, 1f)]
    [InlineData(0.45f, 0.5f)]
    [InlineData(0.6f, 0f)]
    [InlineData(0.9f, 0f)]
    public void TheRoughnessFadeIsFullUpToHalfTheMaximumAndZeroAtIt(float roughness, float fade) =>
        ConeTracedReflections
            .Default.RoughnessFade(roughness)
            .Should()
            .BeApproximately(fade, 1e-6f, "the fade falls linearly from half the maximum of 0.6 to the maximum");

    [Fact]
    public void TheTracesPushConstantsCarryTheMaximumRoughnessAndTheSteps()
    {
        var push = ReflectionPushConstants.For(new(MaxRoughness: 0.3f, MaxSteps: 200));

        (push.MaxRoughness, push.MaxSteps).Should().Be((0.3f, 200u));
    }
}
