using System.Numerics;
using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>A cut keeps raw ambient radiance independent of the exposure that produced its indirect light.</summary>
[Collection(GpuCollection.Name)]
public sealed class AmbientCutSourceTests
{
    [Theory]
    [InlineData(-64f, false, false)]
    [InlineData(64f, false, false)]
    [InlineData(-64f, true, false)]
    [InlineData(64f, true, false)]
    [InlineData(-64f, false, true)]
    [InlineData(64f, false, true)]
    [InlineData(-64f, true, true)]
    [InlineData(64f, true, true)]
    public void ACutKeepsTheSameAmbientSceneRadianceAtBothProducingExposureEndpoints(
        float incoming,
        bool trace,
        bool volume
    )
    {
        float expected = CutRadiance(0, trace, volume);

        float actual = CutRadiance(incoming, trace, volume);

        actual
            .Should()
            .BeApproximately(
                expected,
                expected * 0.005f,
                "the raw ambient fallback must survive before the destination exposure is chosen"
            );
    }

    private static float CutRadiance(float stops, bool trace, bool volume)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 1,
            exposure: new FixedExposure(stops),
            screenTrace: trace ? ScreenTrace.Default : new NoScreenTrace(),
            warmUpFrames: 0,
            outputDither: false,
            lightVolume: volume
        );
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
            snapshot.Ambient.Intensity = 0;
            AddGraySquare(renderer, snapshot);
            Still(renderer: renderer, snapshot: snapshot, time: 0);
            snapshot.Ambient.Intensity = 1;
            snapshot.CameraCuts++;

            Still(renderer: renderer, snapshot: snapshot, time: 1.0 / 60);

            return renderer.ReadHdr()[(Height / 2 * Width + Width / 2) * 4];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
