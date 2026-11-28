using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A camera cut opens at the independently settled destination exposure. Movie frames step at 60 Hz, below the still
/// threshold. A window-clock renderer records every presentation, so hidden metering cannot show an intermediate frame.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class CameraCutExposureTests
{
    private const double FrameSeconds = 1.0 / 60;
    private const float ToleranceStops = 1f / 16;

    [Fact]
    public void AMovieCutShadesTheDestinationOnceBeforeShowingIt()
    {
        VulkanRenderer renderer = Renderer(AutoExposure.Default);
        try
        {
            RenderSnapshot snapshot = View(renderer, 1361);
            Still(renderer: renderer, snapshot: snapshot, time: 0);
            long before = renderer.LastFrameStats.Exposure!.Value.Frame;
            Gpu.Star(snapshot: snapshot, irradiance: 0.001f);
            snapshot.CameraCuts++;

            Still(renderer: renderer, snapshot: snapshot, time: FrameSeconds);

            renderer
                .LastFrameStats.Exposure!.Value.Frame.Should()
                .Be(before + 1, "a cut needs one destination shading evaluation");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A ramp lit only by the star at <paramref name="irradiance"/> W/m², with one named camera frame.</summary>
    private static RenderSnapshot View(VulkanRenderer renderer, float irradiance)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: irradiance);
        snapshot.Ambient.Intensity = 0;
        snapshot.CameraCuts = 1;
        TestChains.Of(snapshot, (7, Quaternion.Identity));
        AddRampSquare(renderer: renderer, snapshot: snapshot, position: Ahead, rotation: FacingTheCamera);
        return snapshot;
    }

    /// <summary>The destination's exposure and center luma after eight independent still captures at the identical pose.</summary>
    private static FilmFrame Destination(float irradiance, ExposureSetting? exposure = null, int settlingCaptures = 8)
    {
        VulkanRenderer renderer = Renderer(exposure ?? AutoExposure.Default);
        try
        {
            RenderSnapshot snapshot = View(renderer, irradiance);
            FilmFrame[] settled = Film(
                renderer: renderer,
                snapshot: snapshot,
                start: 0,
                frameSeconds: 0,
                frames: settlingCaptures
            );
            settled[^1].Stops.Should().BeApproximately(settled[^2].Stops, ToleranceStops, "the control must settle");
            return settled[^1];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(1361f)]
    [InlineData(3f)]
    public void ACutReachesAVeryDimDestinationThatNeedsManyColdMeteringDraws(float source)
    {
        AutoExposure exposure = AutoExposure.Default with { CompensationStops = -13 };
        FilmFrame expected = Destination(irradiance: 1e-12f, exposure: exposure, settlingCaptures: 64);
        VulkanRenderer renderer = Renderer(exposure);
        try
        {
            RenderSnapshot snapshot = View(renderer, source);
            Film(renderer: renderer, snapshot: snapshot, start: 0, frameSeconds: 0, frames: 8);
            Gpu.Star(snapshot: snapshot, irradiance: 1e-12f);
            snapshot.CameraCuts++;

            FilmFrame first = Still(renderer: renderer, snapshot: snapshot, time: FrameSeconds);

            first
                .Stops.Should()
                .BeApproximately(expected.Stops, ToleranceStops, "a cold seed alone does not settle the view");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(-13f, -14f, 10f, 1361f, 0.001f)]
    [InlineData(-13f, -14f, 10f, 3f, 0.001f)]
    [InlineData(-2f, -14f, 10f, 1361f, 0.001f)]
    [InlineData(2f, -14f, 10f, 0.001f, 1361f)]
    [InlineData(0f, -64f, 64f, 1e12f, 1e-12f)]
    [InlineData(0f, -64f, 64f, 1e-12f, 1e12f)]
    public void ACutMatchesTheSettledDestinationWithValidCompensationAndWideExposureRanges(
        float compensation,
        float minimum,
        float maximum,
        float source,
        float destination
    )
    {
        AutoExposure exposure = AutoExposure.Default with
        {
            MinStops = minimum,
            MaxStops = maximum,
            CompensationStops = compensation,
        };
        FilmFrame expected = Destination(destination, exposure);
        VulkanRenderer renderer = Renderer(exposure);
        try
        {
            RenderSnapshot snapshot = View(renderer, source);
            Film(renderer: renderer, snapshot: snapshot, start: 0, frameSeconds: 0, frames: 8);
            Gpu.Star(snapshot: snapshot, irradiance: destination);
            snapshot.CameraCuts++;

            FilmFrame first = Still(renderer: renderer, snapshot: snapshot, time: FrameSeconds);

            first.Stops.Should().BeApproximately(expected.Stops, ToleranceStops);
            first.Luma.Should().BeApproximately(expected.Luma, 2f / 255);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(1361f, 3f)]
    [InlineData(3f, 1361f)]
    [InlineData(1361f, 0.001f)]
    [InlineData(0.001f, 1361f)]
    public void TheFirstMovieFrameAfterACutMatchesTheIndependentlySettledDestination(float source, float destination)
    {
        FilmFrame expected = Destination(destination);
        VulkanRenderer renderer = Renderer(AutoExposure.Default);
        try
        {
            RenderSnapshot snapshot = View(renderer, source);
            Film(renderer: renderer, snapshot: snapshot, start: 0, frameSeconds: 0, frames: 8);

            Gpu.Star(snapshot: snapshot, irradiance: destination);
            snapshot.CameraCuts++;
            FilmFrame first = Film(
                renderer: renderer,
                snapshot: snapshot,
                start: FrameSeconds,
                frameSeconds: FrameSeconds,
                frames: 1
            )[0];

            first
                .Stops.Should()
                .BeApproximately(expected.Stops, ToleranceStops, "the displayed draw uses destination exposure");
            first
                .Luma.Should()
                .BeApproximately(expected.Luma, 2f / 255, "the destination is visible in that same frame");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ConsecutiveCutsMeterARevisitedViewAfterItsLightChanges()
    {
        FilmFrame changedDestination = Destination(0.03f);
        VulkanRenderer renderer = Renderer(AutoExposure.Default);
        try
        {
            RenderSnapshot snapshot = View(renderer, 3);
            Film(renderer: renderer, snapshot: snapshot, start: 0, frameSeconds: 0, frames: 8);
            Gpu.Star(snapshot: snapshot, irradiance: 1361);
            snapshot.CameraCuts++;
            Film(renderer: renderer, snapshot: snapshot, start: FrameSeconds, frameSeconds: FrameSeconds, frames: 1);

            Gpu.Star(snapshot: snapshot, irradiance: 0.03f);
            snapshot.CameraCuts++;
            FilmFrame returned = Film(
                renderer: renderer,
                snapshot: snapshot,
                start: 2 * FrameSeconds,
                frameSeconds: FrameSeconds,
                frames: 1
            )[0];

            returned.Stops.Should().BeApproximately(changedDestination.Stops, ToleranceStops);
            returned.Luma.Should().BeApproximately(changedDestination.Luma, 2f / 255);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AContinuousReferenceFrameHandoffKeepsGradualExposureAdaptation()
    {
        FilmFrame destination = Destination(0.03f);
        VulkanRenderer renderer = Renderer(AutoExposure.Default);
        try
        {
            RenderSnapshot snapshot = View(renderer, 1361);
            float source = Film(renderer: renderer, snapshot: snapshot, start: 0, frameSeconds: 0, frames: 8)[^1].Stops;
            Gpu.Star(snapshot: snapshot, irradiance: 0.03f);
            TestChains.Of(snapshot, (8, Quaternion.Identity));

            FilmFrame[] moving = Film(
                renderer: renderer,
                snapshot: snapshot,
                start: FrameSeconds,
                frameSeconds: FrameSeconds,
                frames: 3
            );

            moving[0].Stops.Should().BeApproximately(source, ToleranceStops, "a handoff keeps the previous exposure");
            moving[1].Stops.Should().BeGreaterThan(moving[0].Stops);
            moving[2].Stops.Should().BeGreaterThan(moving[1].Stops);
            moving[2].Stops.Should().BeLessThan(destination.Stops - 1, "adaptation still takes time");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACameraCutKeepsManualExposureFixed()
    {
        VulkanRenderer renderer = Renderer(new FixedExposure(-2));
        try
        {
            RenderSnapshot snapshot = View(renderer, 1361);
            Still(renderer: renderer, snapshot: snapshot, time: 0);
            Gpu.Star(snapshot: snapshot, irradiance: 0.001f);
            snapshot.CameraCuts++;

            FilmFrame cut = Still(renderer: renderer, snapshot: snapshot, time: FrameSeconds);

            cut.Stops.Should().Be(-2);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(1361f, 0.001f)]
    [InlineData(0.001f, 1361f)]
    public void EveryPresentationOfAWindowCutAlreadyUsesTheSettledDestination(float source, float destination)
    {
        FilmFrame expected = Destination(destination);
        PresentationOutput? made = null;
        var renderer = VulkanRenderer.CreateOffscreen(
            options: new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Exposure = AutoExposure.Default,
                Bloom = new NoBloom(),
                Upscaling = new NoUpscaling(),
                ScreenTrace = new NoScreenTrace(),
                EnvironmentMap = new NoEnvironmentMap(),
                LocalShadows = new NoLocalShadows(),
                LightVolume = false,
                LightPairs = new EveryLightPair(),
                Subsurface = new NoSubsurface(),
                Impostors = false,
                OutputDither = false,
            },
            createOutput: (device, allocator, extent) =>
                made = new(new(device: device, allocator: allocator, extent: extent, bgra: false)),
            headless: false
        );
        try
        {
            PresentationOutput output = made!;
            output.ReadAppliedStops = () => renderer.ReadExposureStops().PreviousStops;
            RenderSnapshot snapshot = View(renderer, source);
            renderer.Render(snapshot, 1f);
            output.PresentedStops.Clear();

            Gpu.Star(snapshot: snapshot, irradiance: destination);
            snapshot.CameraCuts++;
            Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "first window cut presentation");

            output.PresentedStops.Should().ContainSingle("metering draws must never reach presentation");
            output.PresentedStops[0].Should().BeApproximately(expected.Stops, ToleranceStops);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACutWithNoSurfaceCountsShowsTheSettingsNeutralExposure(bool fsr)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 1,
            exposure: AutoExposure.Default,
            upscaling: fsr ? new Fsr3Upscaling(new FixedRenderScale(1), 0) : new NoUpscaling(),
            warmUpFrames: 0,
            outputDither: false
        );
        try
        {
            RenderSnapshot snapshot = View(renderer, 1361);
            Still(renderer: renderer, snapshot: snapshot, time: 0);
            snapshot.Instances.Clear();
            Gpu.Star(snapshot: snapshot, irradiance: 0);
            snapshot.CameraCuts++;

            FilmFrame cut = Still(renderer: renderer, snapshot: snapshot, time: FrameSeconds);

            renderer.ReadExposureHistogram().Sum(value => (long)value).Should().Be(0);
            cut.Stops.Should().Be(0);
            renderer.ReadExposureStops().Should().Be((0f, 0f));
            if (fsr)
                renderer.ReadFsrExposureRatio().Should().Be(1);
            Still(renderer: renderer, snapshot: snapshot, time: 2 * FrameSeconds).Stops.Should().Be(0);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The real offscreen target with a read of applied exposure after every call of the presentation boundary.</summary>
    private sealed class PresentationOutput(HeadlessTarget target) : IOutputTarget
    {
        public Func<float>? ReadAppliedStops { get; set; }
        public List<float> PresentedStops { get; } = [];
        public Format Format => target.Format;
        public Extent2D Extent => target.Extent;
        public bool EncodeSrgbInShader => target.EncodeSrgbInShader;
        public bool BgraOrder => target.BgraOrder;
        public bool CanCapture => target.CanCapture;

        public GpuImage? Acquire(FrameResources frame) => target.Acquire(frame);

        public void FinishRecording(BarrierBatch barriers, CommandBuffer commandBuffer) =>
            target.FinishRecording(barriers, commandBuffer);

        public void SubmitAndPresent(FrameResources frame)
        {
            target.SubmitAndPresent(frame);
            if (ReadAppliedStops is { } read)
                PresentedStops.Add(read());
        }

        public void Resize(Extent2D extent) => target.Resize(extent);

        public void Dispose() => target.Dispose();
    }
}
