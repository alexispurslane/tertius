using System.Text.Json;
using Tertius.Assets;
using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>Opt-in linear-light measurements of the actual helmet and shuttle at their recorded poses.</summary>
[Collection(MeasurementCollection.Name)]
public sealed class BloomSceneMeasurements
{
    [Fact(Explicit = true)]
    public void TheRecordedSceneReportsItsRadianceBeforeTheArtisticBloomResponse()
    {
        string folder =
            Environment.GetEnvironmentVariable("TERTIUS_BLOOM_SCENE_REPORT")
            ?? throw new InvalidOperationException("Set TERTIUS_BLOOM_SCENE_REPORT to the measurement folder.");
        string name = Environment.GetEnvironmentVariable("TERTIUS_BLOOM_SCENE") ?? "helmet";
        bool helmet = name == "helmet";
        bool continuous = name == "egress-film";
        bool bloom = Environment.GetEnvironmentVariable("TERTIUS_BLOOM_SCENE_GLOW") != "off";
        string scriptName = name switch
        {
            "helmet" => "bloom-helmet.json",
            "egress-film" => "bloom-shadow-sun.json",
            _ => "bloom-shadow-sun-settled.json",
        };
        var script = Script.Load(Path.Combine(RepositoryRoot(), "scripts", scriptName));
        using VulkanRenderer renderer = Headless(script, helmet, bloom);
        using World world = WorldTesting.NewWorld(backend: out _, debugChecks: false);
        SceneInstance scene = DemoScenes
            .For(ContentFolders.Find())
            .Single(s => s.Name == script.Scene)
            .Build(world, renderer);
        Directory.CreateDirectory(folder);
        var exposures = new List<float>();
        CaptureTarget target = continuous
            ? new VideoFile(Path.Combine(folder, name + VideoFile.Extension), 30)
            : new PngFolder(folder);
        using var playback = new HeadlessPlayback(
            renderer: renderer,
            size: script.Window,
            target: target,
            startVideo: FfmpegVideoSink.Start,
            handedOver: () => exposures.Add(renderer.LastFrameStats.Exposure?.Stops ?? 0)
        );
        ScriptRunner.Run(script: script, world: world, scene: scene, outputFolder: null, playback: playback);
        playback.Finish();
        WriteReport(
            renderer: renderer,
            script: script,
            folder: folder,
            name: name,
            helmet: helmet,
            glow: bloom,
            tick: world.TickNumber
        );
        File.WriteAllText(Path.Combine(folder, name + "-exposure.json"), JsonSerializer.Serialize(exposures));
        renderer.ValidationMessages.Should().BeEmpty();
    }

    private static VulkanRenderer Headless(Script script, bool helmet, bool bloom) =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Width = script.Window.Width,
                Height = script.Window.Height,
                Exposure = helmet ? new FixedExposure(6.0043726f) : AutoExposure.Default,
                Validation = true,
                Bloom = bloom ? Bloom.Default : new NoBloom(),
            }
        );

    private static void WriteReport(
        VulkanRenderer renderer,
        Script script,
        string folder,
        string name,
        bool helmet,
        bool glow,
        long tick
    )
    {
        float[] hdr = renderer.ReadHdr();
        float[] post = renderer.ReadPostHdr();
        float stops = renderer.LastFrameStats.Exposure?.Stops ?? 0;
        float exposure = MathF.Pow(2, stops);
        int width = (int)Math.Round(Math.Sqrt(hdr.Length / 4d * script.Window.Width / script.Window.Height));
        int height = hdr.Length / (4 * width);
        (int x, int y, int w, int h) = helmet ? (530, 316, 54, 54) : (280, 200, 320, 190);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, name + ".json"),
            JsonSerializer.Serialize(
                new
                {
                    Scene = name,
                    BloomEnabled = glow,
                    Tick = tick,
                    OutputWidth = script.Window.Width,
                    OutputHeight = script.Window.Height,
                    RenderWidth = width,
                    RenderHeight = height,
                    AppliedExposureStops = stops,
                    OriginalSceneUnits = "W/(m2 sr), before exposure",
                    BloomInputUnits = "W/(m2 sr) multiplied by exposure scale",
                    ValidationMessages = renderer.ValidationMessages.ToArray(),
                    OutputRegion = new[] { x, y, w, h },
                    SceneRadiance = Region(
                        rgba: hdr,
                        width: width,
                        height: height,
                        outputSize: (script.Window.Width, script.Window.Height),
                        rectangle: (x, y, w, h)
                    ),
                    ExposedSceneRadiance = Region(
                        rgba: [.. hdr.Select(value => value * exposure)],
                        width: width,
                        height: height,
                        outputSize: (script.Window.Width, script.Window.Height),
                        rectangle: (x, y, w, h)
                    ),
                    ReconstructedRadiance = Region(
                        rgba: post,
                        width: script.Window.Width,
                        height: script.Window.Height,
                        outputSize: (script.Window.Width, script.Window.Height),
                        rectangle: (x, y, w, h)
                    ),
                },
                new JsonSerializerOptions { WriteIndented = true }
            )
        );
    }

    private static object Region(
        float[] rgba,
        int width,
        int height,
        (int Width, int Height) outputSize,
        (int X, int Y, int Width, int Height) rectangle
    )
    {
        var values = new List<float>();
        for (
            int y = rectangle.Y * height / outputSize.Height;
            y < (rectangle.Y + rectangle.Height) * height / outputSize.Height;
            y++
        )
        for (
            int x = rectangle.X * width / outputSize.Width;
            x < (rectangle.X + rectangle.Width) * width / outputSize.Width;
            x++
        )
        {
            int i = 4 * (y * width + x);
            values.Add(MathF.Max(rgba[i], MathF.Max(rgba[i + 1], rgba[i + 2])));
        }
        values.Sort();
        return new
        {
            Pixels = values.Count,
            Maximum = values[^1],
            Mean = values.Average(),
            P50 = values[values.Count / 2],
            P90 = values[(int)(values.Count * 0.9)],
            P99 = values[(int)(values.Count * 0.99)],
            Above1024 = values.Count(v => v > 1024),
            Above8192 = values.Count(v => v > 8192),
        };
    }
}
