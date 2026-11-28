using System.Text.Json;
using Silk.NET.Vulkan;
using Tertius.Assets;
using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>Opt-in captures of the two released film cuts, with each shown frame's meter and draw count.</summary>
[Collection(MeasurementCollection.Name)]
public sealed class CameraCutSceneMeasurements
{
    [Fact(Explicit = true)]
    public void TheReleasedFilmCutsReportTheirFirstPresentedDestinationFrames()
    {
        string folder =
            Environment.GetEnvironmentVariable("TERTIUS_CUT_REPORT")
            ?? throw new InvalidOperationException("Set TERTIUS_CUT_REPORT to the capture folder.");
        string control = Environment.GetEnvironmentVariable("TERTIUS_CUT_CONTROL") ?? "film";
        bool window = Environment.GetEnvironmentVariable("TERTIUS_CUT_WINDOW") == "1";
        bool fsr = Environment.GetEnvironmentVariable("TERTIUS_CUT_FSR") == "1";
        string scriptName = control switch
        {
            "outside" => "issue4-cut-outside-control.json",
            "return" => "issue4-cut-return-control.json",
            _ => "issue4-cut-first-present.json",
        };
        Script script = Script.Load(Path.Combine(RepositoryRoot(), "scripts", scriptName));
        ExposureSetting exposure =
            Environment.GetEnvironmentVariable("TERTIUS_CUT_MANUAL") == "1"
                ? new FixedExposure(2)
                : AutoExposure.Default;
        RendererOptions options = Options(script, exposure, fsr);
        PresentationBoundary? boundary = null;
        using VulkanRenderer renderer = VulkanRenderer.CreateOffscreen(
            options: options,
            createOutput: (device, allocator, extent) =>
                boundary = new(new(device: device, allocator: allocator, extent: extent, bgra: false)),
            headless: !window
        );
        List<object> readings = CaptureScript(
            script: script,
            renderer: renderer,
            boundary: boundary ?? throw new InvalidOperationException("No output target."),
            folder: folder,
            control: control
        );

        File.WriteAllText(
            Path.Combine(folder, "readings.json"),
            JsonSerializer.Serialize(
                new
                {
                    Script = scriptName,
                    Gpu = renderer.DeviceName,
                    Clock = window ? "window clock with an offscreen presentation target" : "headless film clock",
                    Control = control,
                    Fsr = fsr,
                    FixedExposure = exposure is FixedExposure,
                    OutputWidth = options.Width,
                    OutputHeight = options.Height,
                    WarmUpDraws = options.WarmUpFrames,
                    Frames = readings,
                    Validation = renderer.ValidationMessages.ToArray(),
                },
                new JsonSerializerOptions { WriteIndented = true }
            )
        );
        renderer.ValidationMessages.Should().BeEmpty();
    }

    private static RendererOptions Options(Script script, ExposureSetting exposure, bool fsr) =>
        new()
        {
            Width = script.Window.Width,
            Height = script.Window.Height,
            Validation = true,
            FramesInFlight = 1,
            WarmUpFrames = 0,
            Exposure = exposure,
            Upscaling = fsr ? new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0) : new NoUpscaling(),
            OutputDither = false,
        };

    private static List<object> CaptureScript(
        Script script,
        VulkanRenderer renderer,
        PresentationBoundary boundary,
        string folder,
        string control
    )
    {
        using World world = WorldTesting.NewWorld(backend: out _, debugChecks: false);
        SceneInstance scene = DemoScenes
            .For(ContentFolders.Find())
            .Single(s => s.Name == script.Scene)
            .Build(world, renderer);
        Directory.CreateDirectory(folder);
        using var playback = new HeadlessPlayback(
            renderer: renderer,
            size: script.Window,
            target: new PngFolder(folder)
        );
        var measured = new MeasuredPlayback(playback: playback, renderer: renderer, boundary: boundary, folder: folder);

        ScriptRunner.Run(script: script, world: world, scene: scene, outputFolder: null, playback: measured);
        if (control != "film")
            for (int i = 0; i < 8; i++)
                measured.Capture(world, $"settled-{i:00}");
        playback.Finish();

        return measured.Readings;
    }

    private sealed class MeasuredPlayback(
        HeadlessPlayback playback,
        VulkanRenderer renderer,
        PresentationBoundary boundary,
        string folder
    ) : IPlayback
    {
        public List<object> Readings { get; } = [];

        public bool WaitForTick() => true;

        public bool Capture(World world, string name)
        {
            long draws = renderer.DrawnFrames;
            int presentations = boundary.Presentations;
            bool captured = playback.Capture(world, name);
            uint[] histogram = renderer.ReadExposureHistogram();
            float[] hdr = renderer.ReadHdr();
            int width = (int)renderer.LastScene.Constants.Viewport.X;
            int height = (int)renderer.LastScene.Constants.Viewport.Y;
            int center = 4 * (height / 2 * width + width / 2);
            var shown = Png.Load(Path.Combine(folder, name + ".png"))
                .At(renderer.OutputSize.Width / 2, renderer.OutputSize.Height / 2);
            float applied = (
                renderer.LastFrameStats.Exposure ?? throw new InvalidOperationException("No completed exposure.")
            ).Stops;
            float target = ExposureMeter.TargetStops(
                reading: ExposureMeter.Reading(
                    histogram,
                    AutoExposure.Default.DarkShare,
                    AutoExposure.Default.BrightShare
                ),
                usedStops: applied,
                compensationStops: AutoExposure.Default.CompensationStops,
                minStops: AutoExposure.Default.MinStops,
                maxStops: AutoExposure.Default.MaxStops
            );
            Readings.Add(
                new
                {
                    Capture = name,
                    Tick = world.TickNumber,
                    Exposure = renderer.LastFrameStats.Exposure,
                    SceneTargetStops = target,
                    DestinationShadingDraws = renderer.DrawnFrames - draws,
                    Presentations = boundary.Presentations - presentations,
                    SurfaceCounts = histogram.Sum(count => (long)count),
                    ActiveWidth = width,
                    ActiveHeight = height,
                    CenterSceneRadiance = hdr[center..(center + 3)],
                    MaximumSceneRadiance = hdr.Where((_, index) => index % 4 != 3).Max(),
                    CenterSrgb = new[] { shown.R, shown.G, shown.B },
                    Histogram = histogram,
                }
            );
            return captured;
        }
    }

    /// <summary>Counts the real renderer's submissions to its output target, including any hidden presentations.</summary>
    private sealed class PresentationBoundary(HeadlessTarget target) : IOutputTarget
    {
        public int Presentations { get; private set; }
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
            Presentations++;
        }

        public void Resize(Extent2D extent) => target.Resize(extent);

        public void Dispose() => target.Dispose();
    }
}
