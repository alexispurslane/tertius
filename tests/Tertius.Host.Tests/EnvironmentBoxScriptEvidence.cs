using System.Text.Json;
using Tertius.Demo;
using Tertius.Rendering;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>Bounded real-demo stills and per-draw history for the combined box and FSR renderer.</summary>
[Collection(MeasurementCollection.Name)]
public sealed class EnvironmentBoxScriptEvidence
{
    [Theory(Explicit = true)]
    [InlineData("ter-7tnv-terminal")]
    [InlineData("ter-7tnv-box-cabin")]
    public void TwoBoxSceneCapturesAdvanceTheirSnapshotWithoutResettingFsr(string scriptName)
    {
        string folder = Environment.GetEnvironmentVariable("TERTIUS_BOX_EVIDENCE") is { Length: > 0 } path
            ? path
            : throw new InvalidOperationException("Set TERTIUS_BOX_EVIDENCE to the evidence folder.");
        string destination = Path.Combine(folder, scriptName);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var observer = new DrawObserver();
        VulkanRenderer? renderer = null;
        int code = ScriptCommand.Run(
            args:
            [
                "--script",
                Path.Combine(RepositoryRoot(), "scripts", scriptName + ".json"),
                "--headless",
                "--out",
                destination,
                "--upscaling",
                "fsr3",
                "--render-scale",
                "0.6666667",
            ],
            scenes: DemoScenes.All,
            output: output,
            error: error,
            createRenderer: options =>
            {
                options.Validation = true;
                renderer = VulkanRenderer.CreateHeadless(options);
                renderer.Graph.Add(FrameGraph.Stage.Overlay, observer);
                return renderer;
            }
        );
        File.WriteAllText(Path.Combine(destination, "run.log"), output.ToString() + error);
        File.WriteAllText(
            Path.Combine(destination, "history.json"),
            JsonSerializer.Serialize(
                new
                {
                    codeTree = File.ReadAllLines(Path.Combine(folder, "code-tree.txt"))[0],
                    scriptName,
                    outputWidth = 1280,
                    outputHeight = 720,
                    upscaling = "FSR 3.1, fixed scale 0.6666667, sharpness 0",
                    warmUpFrames = RendererOptions.DefaultWarmUpFrames,
                    draws = observer.Draws,
                    validationMessages = renderer?.ValidationMessages,
                },
                new JsonSerializerOptions { WriteIndented = true }
            )
        );
        code.Should().Be(ScriptCommand.Ok, error.ToString());
        renderer.Should().NotBeNull();
        renderer!.ValidationMessages.Should().BeEmpty();
        observer.Draws.Should().HaveCount(RendererOptions.DefaultWarmUpFrames + 2);
        observer.Draws.Count(draw => draw.Reset).Should().Be(1);
        observer.Draws[0].DrawIndex.Should().Be(0);
        observer.Draws[^1].DrawIndex.Should().Be(RendererOptions.DefaultWarmUpFrames + 1);
        observer.Draws[^1].Time.Should().BeApproximately(observer.Draws[^2].Time + 1.0 / 60, 1e-9);
        observer.Draws.Should().OnlyContain(draw => draw.RenderWidth == 848 && draw.RenderHeight == 480);
        string[] images = Directory.GetFiles(destination, "*.png");
        images.Should().HaveCount(2);
        foreach (string image in images)
        {
            var decoded = Png.Load(image);
            (decoded.Width, decoded.Height).Should().Be((1280, 720));
        }
    }

    private sealed record Draw(double Time, long DrawIndex, bool Reset, int RenderWidth, int RenderHeight);

    private sealed class DrawObserver : RenderPass
    {
        public override string Name => "box capture history";

        public List<Draw> Draws { get; } = [];

        public override void Record(FrameContext context) =>
            Draws.Add(
                new(
                    Time: context.Snapshot.Time,
                    DrawIndex: context.History.DrawIndex,
                    Reset: context.History.Reset,
                    RenderWidth: context.Scene.Camera.Width,
                    RenderHeight: context.Scene.Camera.Height
                )
            );
    }
}
