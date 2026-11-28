using System.Numerics;

namespace Tertius.Rendering.Tests;

public sealed partial class AccumulatorRenderTests
{
    /// <summary>Glossy filtering computes physical moments without squaring physical radiance directly.</summary>
    [Collection(ProcessEnvironmentCollection.Name)]
    public sealed class SceneRadianceFiltering
    {
        [Theory]
        [InlineData("original")]
        [InlineData("precise-capped")]
        [InlineData("precise-uncapped")]
        public void EachGlossyFastHistoryParityUsesTheLastCompletedDraw(string mode)
        {
            string? previousMode = Environment.GetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY");
            VulkanRenderer? renderer = null;
            try
            {
                Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", mode);
                (VulkanRenderer created, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: SignalParameters.Specular(0),
                    roughness: 0.8f
                );
                renderer = created;
                float[] radiances = [1e20f, 3e20f, 5e20f, 7e20f];
                float[] expectedMeans = [1e20f, 2e20f, 3e20f, 4e20f];
                for (int frame = 0; frame < radiances.Length; frame++)
                {
                    sample.Value = new(new Vector3(radiances[frame]), 1);
                    if (frame == 0)
                        renderer.Render(scene, 1);
                    else
                        Continue(renderer, scene, frames: 1);
                    FastOf(renderer, sample)[4 * Center]
                        .Should()
                        .BeApproximately(expectedMeans[frame], expectedMeans[frame] * 1e-5f);
                    CenterOf(renderer, sample, 0)
                        .Light.X.Should()
                        .BeApproximately(expectedMeans[frame], expectedMeans[frame] * 1e-5f);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", previousMode);
                if (renderer is not null)
                    Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Theory]
        [InlineData("original", 1e-30f)]
        [InlineData("original", 1e30f)]
        [InlineData("precise-capped", 1e-30f)]
        [InlineData("precise-capped", 1e30f)]
        [InlineData("precise-uncapped", 1e-30f)]
        [InlineData("precise-uncapped", 1e30f)]
        public void TheGlossyClampKeepsThePhysicalSpreadOfVerySmallAndLargeSamples(string mode, float radiance)
        {
            string? previousMode = Environment.GetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY");
            string? previousTrace = Environment.GetEnvironmentVariable("TERTIUS_REFLECTION_HISTORY_AGREEMENT");
            VulkanRenderer? renderer = null;
            try
            {
                Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", mode);
                Environment.SetEnvironmentVariable(
                    "TERTIUS_REFLECTION_HISTORY_AGREEMENT",
                    mode == "original" ? null : "control-trace"
                );
                (VulkanRenderer created, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: SignalParameters.Specular(0),
                    roughness: 0.8f
                );
                renderer = created;
                sample.Samples = (column, row) => new(new Vector3(radiance * (1 + (column + row) % 2)), 1);
                renderer.Render(scene, 1);
                Continue(renderer, scene, frames: 8);
                TemporalSignal signal = sample.Signal;
                int parity = (int)((renderer.DrawnFrames - 1) & 1);
                float[] fast = renderer.ReadImage(signal.Fast(0, parity));
                float[] slow = renderer.ReadImage(signal.Slow(0));
                fast.Should().OnlyContain(value => float.IsFinite(value));
                slow.Should().OnlyContain(value => float.IsFinite(value));
                CenterOf(renderer, sample, 0).Light.X.Should().BeInRange(radiance * 0.99f, radiance * 2.01f);
                if (signal.AccumulationTrace(0) is not { } trace)
                    return;
                float[] sigma = renderer.ReadImageLayer(trace, 7);
                double[] values =
                [
                    .. Enumerable
                        .Range(-1, 3)
                        .SelectMany(y =>
                            Enumerable
                                .Range(-1, 3)
                                .Select(x => (double)fast[4 * ((Height / 2 + y) * Width + Width / 2 + x)])
                        ),
                ];
                double mean = values.Average();
                double expected = Math.Sqrt(values.Average(value => (value - mean) * (value - mean)));
                ((double)sigma[4 * Center]).Should().BeApproximately(expected, expected * 0.001);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", previousMode);
                Environment.SetEnvironmentVariable("TERTIUS_REFLECTION_HISTORY_AGREEMENT", previousTrace);
                if (renderer is not null)
                    Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }
}
