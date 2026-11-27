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

    [Theory]
    [InlineData(0f, false, false)]
    [InlineData(1f, false, false)]
    [InlineData(0f, true, false)]
    [InlineData(1f, true, false)]
    [InlineData(0f, false, true)]
    [InlineData(1f, false, true)]
    [InlineData(0f, true, true)]
    [InlineData(1f, true, true)]
    public void TheFirstShownAmbientDestinationMatchesItsIndependentSettledControl(float source, bool trace, bool fsr)
    {
        FilmFrame expected = AmbientDestination(trace, fsr);
        VulkanRenderer renderer = AutoRenderer(trace, fsr);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
            snapshot.Ambient.Intensity = 0;
            if (source == 0)
                AddGraySquare(renderer, snapshot);
            else
                AddGlowingSquare(
                    renderer: renderer,
                    snapshot: snapshot,
                    position: Ahead,
                    halfSize: FillsTheView,
                    radiance: new Vector3(ShaderInput.RadianceLimit)
                );
            FilmFrame incoming = Still(renderer: renderer, snapshot: snapshot, time: 0);
            incoming.Stops.Should().Be(source == 0 ? 64 : -64);
            long before = renderer.LastFrameStats.Exposure!.Value.Frame;
            snapshot.Instances.Clear();
            AddGraySquare(renderer, snapshot);
            snapshot.Ambient.Intensity = 1;
            snapshot.CameraCuts++;

            FilmFrame cut = Still(renderer: renderer, snapshot: snapshot, time: 1.0 / 60);

            cut.Stops.Should().BeApproximately(expected.Stops, 1f / 16);
            cut.Luma.Should().BeApproximately(expected.Luma, 2f / 255);
            renderer.LastFrameStats.Exposure!.Value.Frame.Should().Be(before + 1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static VulkanRenderer AutoRenderer(bool trace, bool fsr) =>
        Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 1,
            exposure: AutoExposure.Default with
            {
                MinStops = -64,
                MaxStops = 64,
            },
            screenTrace: trace ? ScreenTrace.Default : new NoScreenTrace(),
            upscaling: fsr ? new Fsr3Upscaling(new FixedRenderScale(1), 0) : new NoUpscaling(),
            warmUpFrames: 0,
            outputDither: false
        );

    private static FilmFrame AmbientDestination(bool trace, bool fsr)
    {
        VulkanRenderer renderer = AutoRenderer(trace, fsr);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
            snapshot.Ambient.Intensity = 1;
            AddGraySquare(renderer, snapshot);
            FilmFrame[] control = Film(renderer: renderer, snapshot: snapshot, start: 0, frameSeconds: 0, frames: 4);
            control[^1].Stops.Should().BeApproximately(control[^2].Stops, 1e-5f);
            return control[^1];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
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
