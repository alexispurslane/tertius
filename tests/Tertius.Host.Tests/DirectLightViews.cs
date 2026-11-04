using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The direct signal of one view of a checked-in script under one <see cref="DirectLightSetting"/>, and the comparison of
/// ReSTIR with the exact loop (docs/plans/temporal-renderer.md, section 7, "The bias"). A view is one capture of the
/// script: a still, or a frame of a film. Each pixel's light is the luminance of the direct signal's two parts, divided by
/// the frame's exposure scale, so that two runs whose exposures differ compare in plain radiance over the material factors.
/// </summary>
internal static class DirectLightViews
{
    /// <summary>
    /// Runs the script <paramref name="script"/> of the folder scripts until its capture <paramref name="capture"/>, under
    /// <paramref name="setting"/> at <paramref name="width"/> × <paramref name="height"/>, and returns that capture's
    /// direct light: the slow histories and the single draw's samples.
    /// </summary>
    public static DirectView Measure(string script, string capture, DirectLightSetting setting, int width, int height)
    {
        Script parsed = Script.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", script + ".json")));
        RendererOptions options = GpuTesting.LikeAScriptedRunOptions(width, height);
        options.Validation = false;
        options.DirectLight = setting;
        using var renderer = VulkanRenderer.CreateHeadless(options);
        using World world = NewJoltWorld();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == parsed.Scene).Build(world, renderer);
        using var inner = new HeadlessPlayback(renderer, new ScriptWindow(width, height), new Discard());
        var playback = new ReadingPlayback(inner, renderer, capture);
        ScriptRunner.Run(script: parsed, world: world, scene: scene, playback: playback);
        return playback.View ?? throw new InvalidOperationException($"The script {script} has no capture {capture}.");
    }

    /// <summary>The luminance of both parts of a direct signal image, per pixel, divided by <paramref name="scale"/>.</summary>
    private static double[] LuminanceOf(float[] diffuse, float[] specular, double scale)
    {
        var light = new double[diffuse.Length / 4];
        for (int pixel = 0; pixel < light.Length; pixel++)
        {
            int at = 4 * pixel;
            light[pixel] =
                (
                    0.2126 * (diffuse[at] + specular[at])
                    + 0.7152 * (diffuse[at + 1] + specular[at + 1])
                    + 0.0722 * (diffuse[at + 2] + specular[at + 2])
                ) / scale;
        }

        return light;
    }

    /// <summary>A playback that reads the direct signal after its one capture, and then stops the run.</summary>
    private sealed class ReadingPlayback(IPlayback inner, VulkanRenderer renderer, string capture) : IPlayback
    {
        public DirectView? View { get; private set; }

        public bool WaitForTick() => inner.WaitForTick();

        public bool Capture(World world, string name)
        {
            if (!inner.Capture(world, name))
                return false;
            if (name != capture)
                return true;
            double scale = Math.Pow(2, renderer.LastFrameStats.Exposure?.Stops ?? 0);
            View = new DirectView(
                History: LuminanceOf(renderer.ReadDirectHistory(0), renderer.ReadDirectHistory(1), scale),
                Sample: LuminanceOf(renderer.ReadDirectSample(0), renderer.ReadDirectSample(1), scale)
            );
            return false;
        }
    }
}

/// <summary>The direct light of one view, per pixel, row by row from the top: the slow histories and the single draw's samples.</summary>
internal sealed record DirectView(double[] History, double[] Sample);

/// <summary>
/// ReSTIR's error against the exact loop in one view, over the pixels where the exact loop gives more than a thousandth of
/// the view's mean light.
/// </summary>
/// <param name="Bias">The relative error of the view's summed history: (Σ ReSTIR − Σ exact) / Σ exact.</param>
/// <param name="HistoryError">The mean over the pixels of |ReSTIR − exact| / exact, of the accumulated histories.</param>
/// <param name="SampleError">The same of a single draw's samples: the noise before the accumulation.</param>
/// <param name="SampleBias">The relative error of the single draw's summed samples: the bias before the accumulation.</param>
/// <param name="Pixels">The pixels compared.</param>
internal readonly record struct DirectError(
    double Bias,
    double HistoryError,
    double SampleError,
    double SampleBias,
    int Pixels
)
{
    public static DirectError Of(DirectView exact, DirectView resampled)
    {
        double mean = exact.History.Average();
        double floor = 1e-3 * mean;
        double exactSum = 0,
            resampledSum = 0,
            exactSamples = 0,
            resampledSamples = 0,
            historyError = 0,
            sampleError = 0;
        int pixels = 0;
        for (int pixel = 0; pixel < exact.History.Length; pixel++)
        {
            double reference = exact.History[pixel];
            if (reference <= floor)
                continue;
            exactSum += reference;
            resampledSum += resampled.History[pixel];
            exactSamples += exact.Sample[pixel];
            resampledSamples += resampled.Sample[pixel];
            historyError += Math.Abs(resampled.History[pixel] - reference) / reference;
            sampleError += Math.Abs(resampled.Sample[pixel] - exact.Sample[pixel]) / reference;
            pixels++;
        }

        return new(
            Bias: (resampledSum - exactSum) / exactSum,
            HistoryError: historyError / pixels,
            SampleError: sampleError / pixels,
            SampleBias: (resampledSamples - exactSamples) / exactSamples,
            Pixels: pixels
        );
    }
}
