using System.Numerics;
using Tertius.Testing;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFirstGlossyDestinationMatchesItsIndependentControlFromBothExposureEndpoints(bool brightSource)
    {
        (FilmFrame expected, float[] raw) = GlossyDestination();
        VulkanRenderer renderer = AutoRenderer(trace: true, fsr: false);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
            snapshot.Ambient.Intensity = 0;
            AddGlowingSquare(
                renderer: renderer,
                snapshot: snapshot,
                position: Ahead,
                halfSize: FillsTheView,
                radiance: brightSource ? new Vector3(ShaderInput.RadianceLimit) : Vector3.Zero
            );
            long sourceBefore = renderer.DrawnFrames;
            FilmFrame incoming = Still(renderer: renderer, snapshot: snapshot, time: 0);
            incoming.Stops.Should().Be(brightSource ? -64 : 64);
            LogGlossy(
                stage: brightSource ? "bright source" : "dark source",
                renderer: renderer,
                frame: incoming,
                draws: renderer.DrawnFrames - sourceBefore
            );
            long before = renderer.LastFrameStats.Exposure!.Value.Frame;
            snapshot.Instances.Clear();
            AddGlossySquare(renderer, snapshot);
            snapshot.Ambient.Intensity = 1;
            snapshot.CameraCuts++;

            FilmFrame cut = Still(renderer: renderer, snapshot: snapshot, time: 1.0 / 60);

            LogGlossy(
                stage: "first cut",
                renderer: renderer,
                frame: cut,
                draws: renderer.LastFrameStats.Exposure!.Value.Frame - before
            );
            float[] actual = CenterRadiance(renderer);
            for (int channel = 0; channel < 3; channel++)
                actual[channel].Should().BeApproximately(raw[channel], raw[channel] * 0.005f);
            cut.Stops.Should().BeApproximately(expected.Stops, 1f / 16);
            cut.Luma.Should().BeApproximately(expected.Luma, 2f / 255);
            renderer.LastFrameStats.Exposure!.Value.Frame.Should().Be(before + 1);
            AssertGlossyContinuation(renderer: renderer, snapshot: snapshot, expected: expected, raw: raw);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static void AssertGlossyContinuation(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        FilmFrame expected,
        float[] raw
    )
    {
        for (int frame = 2; frame <= 4; frame++)
        {
            FilmFrame following = Still(renderer: renderer, snapshot: snapshot, time: frame / 60.0);
            following.Stops.Should().BeApproximately(expected.Stops, 1f / 16);
            CenterRadiance(renderer).Should().OnlyContain(value => float.IsFinite(value) && value > 0);
        }
        snapshot.CameraCuts++;
        long before = renderer.DrawnFrames;
        FilmFrame repeated = Still(renderer: renderer, snapshot: snapshot, time: 5.0 / 60);
        repeated.Stops.Should().BeApproximately(expected.Stops, 1f / 16);
        float[] actual = CenterRadiance(renderer);
        for (int channel = 0; channel < 3; channel++)
            actual[channel].Should().BeApproximately(raw[channel], raw[channel] * 0.005f);
        renderer.DrawnFrames.Should().Be(before + 1);
    }

    private static (FilmFrame Frame, float[] Raw) GlossyDestination()
    {
        VulkanRenderer renderer = AutoRenderer(trace: true, fsr: false);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
            snapshot.Ambient.Intensity = 1;
            AddGlossySquare(renderer, snapshot);
            long before = renderer.DrawnFrames;
            FilmFrame cold = Still(renderer: renderer, snapshot: snapshot, time: 0);
            LogGlossy(
                stage: "independent cold control",
                renderer: renderer,
                frame: cold,
                draws: renderer.DrawnFrames - before
            );
            FilmFrame[] control = Film(renderer: renderer, snapshot: snapshot, start: 0, frameSeconds: 0, frames: 6);
            before = renderer.DrawnFrames;
            FilmFrame settled = Still(renderer: renderer, snapshot: snapshot, time: 0);
            LogGlossy(
                stage: "independent settled control",
                renderer: renderer,
                frame: settled,
                draws: renderer.DrawnFrames - before
            );
            settled.Stops.Should().BeApproximately(control[^1].Stops, 1e-5f);
            // Restart only the independent destination's sampler. A cut has one sample draw, whereas a still has two.
            // This reference has destination exposure already and the same sample index as the tested first cut.
            snapshot.CameraCuts++;
            before = renderer.DrawnFrames;
            FilmFrame reference = Still(renderer: renderer, snapshot: snapshot, time: 1.0 / 60);
            LogGlossy(
                stage: "independent one-draw estimator reference",
                renderer: renderer,
                frame: reference,
                draws: renderer.DrawnFrames - before
            );
            (renderer.DrawnFrames - before).Should().Be(1);
            reference.Stops.Should().BeApproximately(settled.Stops, 1f / 16);
            return (settled, CenterRadiance(renderer));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static float[] CenterRadiance(VulkanRenderer renderer)
    {
        float[] hdr = renderer.ReadHdr();
        int center = (Height / 2 * Width + Width / 2) * 4;
        return hdr[center..(center + 3)];
    }

    private static void LogGlossy(string stage, VulkanRenderer renderer, FilmFrame frame, long draws)
    {
        float[] hdr = renderer.ReadHdr();
        int center = (Height / 2 * Width + Width / 2) * 4;
        TestContext.Current.TestOutputHelper!.WriteLine(
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    stage,
                    rawHdrRgb = hdr[center..(center + 3)],
                    usedExposureStops = frame.Stops,
                    centerLuma = frame.Luma,
                    sceneDraws = draws,
                }
            )
        );
    }

    private static void AddGlossySquare(VulkanRenderer renderer, RenderSnapshot snapshot) =>
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(FillsTheView)),
                material: renderer.CreateMaterial(
                    new(BaseColor: Vector4.One, Roughness: 0, Model: new StandardShading(Metallic: 1))
                ),
                position: Ahead,
                scale: Vector3.One,
                rotation: FacingTheCamera,
                chainDepth: TestChains.CamerasFrame
            )
        );

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
