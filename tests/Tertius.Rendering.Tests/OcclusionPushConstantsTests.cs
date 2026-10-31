namespace Tertius.Rendering.Tests;

/// <summary>
/// The push constants of the occlusion passes: the setting, as occlusion.glsl reads it. The camera comes from the frame
/// constants (docs/plans/screen-space-illumination.md, "Push constants and sets").
/// </summary>
public sealed class OcclusionPushConstantsTests
{
    [Fact]
    public void TheRadiusTheSlicesAndTheStepsComeFromTheSetting()
    {
        var setting = new VisibilityBitmaskLight(RadiusMeters: 0.75f, Slices: 5, StepsPerSide: 7);

        var push = OcclusionPushConstants.For(setting: setting, lastDenoise: false);

        push.RadiusMeters.Should().Be(0.75f);
        push.Slices.Should().Be(5u);
        push.StepsPerSide.Should().Be(7u);
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
