namespace Tertius.Rendering.Tests;

/// <summary>The scene meter keeps dim and bright surface luminances distinct before display exposure is chosen.</summary>
public sealed class SceneExposureMeterTests
{
    [Theory]
    [InlineData(-80f)]
    [InlineData(-40f)]
    [InlineData(40f)]
    [InlineData(80f)]
    public void ASceneLuminanceKeepsItsStopsAcrossTheSupportedRadianceRange(float stops)
    {
        int bin = ExposureMeter.BinOf(MathF.Pow(2, stops));

        ExposureMeter.BinCenterStops(bin).Should().BeApproximately(stops + 1f / 16, 1e-5f);
    }

    [Theory]
    [InlineData(-64f)]
    [InlineData(0f)]
    [InlineData(64f)]
    public void ASceneReadingChoosesTheSameExposureRegardlessOfThePreviousView(float previous)
    {
        float target = ExposureMeter.TargetStops(
            reading: (TrimmedMeanStops: 0, BrightEdgeStops: 1),
            usedStops: previous,
            compensationStops: 0.5f,
            minStops: -64,
            maxStops: 64
        );

        target
            .Should()
            .BeApproximately(
                -1.9739312f,
                1e-6f,
                "scene luminance 1 maps to middle gray before adding half a stop of compensation"
            );
    }
}
