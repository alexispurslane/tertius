namespace Tertius.Rendering.Tests;

/// <summary>
/// The push constants of the occlusion passes: the setting alone, as occlusion.glsl reads it. The camera comes from the
/// frame constants (docs/plans/screen-space-illumination.md, "Push constants and sets").
/// </summary>
public sealed class OcclusionPushConstantsTests
{
    [Fact]
    public void TheRadiusTheSlicesTheStepsAndTheFarStepsComeFromTheSetting()
    {
        var setting = new VisibilityBitmaskLight(
            RadiusMeters: 0.75f,
            Slices: 5,
            StepsPerSide: 7,
            FarSteps: 33,
            FarRays: FarRays.OnePerSlice
        );

        var push = OcclusionPushConstants.For(setting: setting, lastDenoise: false);

        push.RadiusMeters.Should().Be(0.75f);
        push.Slices.Should().Be(5u);
        push.StepsPerSide.Should().Be(7u);
        push.FarSteps.Should().Be(33u);
    }

    [Theory]
    [InlineData(FarRays.OnePerSlice, 0u)]
    [InlineData(FarRays.OnePerTexel, 1u)]
    public void TheFarRayPassTracesOneRayPerTexelOnlyUnderOnePerTexel(FarRays farRays, uint expected)
    {
        var push = OcclusionPushConstants.For(
            setting: VisibilityBitmaskLight.Default with { FarRays = farRays },
            lastDenoise: false
        );

        push.FarRayPerTexel.Should().Be(expected);
    }

    [Theory]
    [InlineData(true, 1u)]
    [InlineData(false, 0u)]
    public void OnlyTheLastDenoiseDispatchIsMarked(bool lastDenoise, uint expected)
    {
        var push = OcclusionPushConstants.For(setting: VisibilityBitmaskLight.Default, lastDenoise: lastDenoise);

        push.LastDenoise.Should().Be(expected);
    }
}
