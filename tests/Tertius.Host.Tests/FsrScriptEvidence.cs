using System.Text.Json;
using Tertius.Demo;
using Tertius.Rendering;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>Explicit real-demo captures and draw-history evidence for the temporal upscale, without a timing claim.</summary>
[Collection(MeasurementCollection.Name)]
public sealed class FsrScriptEvidence
{
    [Fact(Explicit = true)]
    public void TheNativeAndFsrCapturesAdvanceDrawHistoryThroughCameraCutsAndMovingFrames()
    {
        string folder = Environment.GetEnvironmentVariable("TERTIUS_FSR_EVIDENCE") is { Length: > 0 } path
            ? path
            : throw new InvalidOperationException("Set TERTIUS_FSR_EVIDENCE to the evidence folder.");
        string codeTree = File.ReadAllLines(Path.Combine(folder, "code-tree.txt"))[0];
        string script = Path.Combine(RepositoryRoot(), "scripts", "temporal-upscale-validation.json");
        foreach (string mode in new[] { "off", "fsr3" })
        {
            string destination = Path.Combine(folder, mode);
            using var output = new StringWriter();
            using var error = new StringWriter();
            VulkanRenderer? renderer = null;
            int code = ScriptCommand.Run(
                args: ["--script", script, "--headless", "--out", destination, "--upscaling", mode],
                scenes: DemoScenes.All,
                output: output,
                error: error,
                createRenderer: options =>
                {
                    options.Validation = true;
                    renderer = VulkanRenderer.CreateHeadless(options);
                    return renderer;
                }
            );
            code.Should().Be(ScriptCommand.Ok, error.ToString());
            renderer.Should().NotBeNull();
            VulkanRenderer rendered = renderer!;
            rendered.DrawnFrames.Should().Be(163);
            rendered.LastDraw.Should().Be((56L, false));
            rendered.ValidationMessages.Should().BeEmpty();
            File.WriteAllText(Path.Combine(destination, "run.log"), output.ToString());
            File.WriteAllText(
                Path.Combine(destination, "history.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        codeTree,
                        mode,
                        outputWidth = 1920,
                        outputHeight = 1080,
                        renderWidth = rendered.LastScene.Camera.Width,
                        renderHeight = rendered.LastScene.Camera.Height,
                        drawnFrames = rendered.DrawnFrames,
                        drawIndex = rendered.LastDraw.DrawIndex,
                        reset = rendered.LastDraw.Reset,
                        warmUpFrames = RendererOptions.DefaultWarmUpFrames,
                        validationMessages = rendered.ValidationMessages.Count,
                    },
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
    }
}
