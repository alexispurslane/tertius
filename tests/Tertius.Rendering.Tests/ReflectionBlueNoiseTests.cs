using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The reflection rays' draws from the blue-noise mask, through probe shaders that call the tracer's functions: each
/// block's numbers fill the unit square over a period, the next period reads the mask at another shift, and one draw
/// is blue over the blocks (ARCHITECTURE.md, "Randomness and determinism").
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ReflectionBlueNoiseTests
{
    /// <summary>The probe's image after one frame of a renderer of the given size.</summary>
    private static float[] Probe(string shader, int width, int height)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            var probe = ScreenTraceProbe.On(renderer, shader);
            renderer.Render(Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0), 1f);
            float[] values = probe.Read();
            values.Should().HaveCount(4 * width * height);
            return values;
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The variance of a value of each cell of a <paramref name="side"/>² grid after a toroidal 5 × 5 box blur, as a
    /// share of white noise's.
    /// </summary>
    private static double BlurredVarianceShare(Func<int, double> valueOf, int side)
    {
        double At(int x, int y) => valueOf(((y + side) % side) * side + (x + side) % side);
        double mean = 0,
            variance = 0,
            blurred = 0;
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
                mean += At(x, y);
        mean /= side * side;
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                variance += Math.Pow(At(x, y) - mean, 2);
                double sum = 0;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        sum += At(x + dx, y + dy);
                blurred += Math.Pow(sum / 25 - mean, 2);
            }
        // White noise keeps 1/25 of its variance through a box of 25 cells.
        return blurred / (variance / 25);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ABlocksReflectionNumbersOverOnePeriodFillEachOfTheSixtyFourStrataOfTheUnitSquareOnce(int period)
    {
        const int draws = 2 * BlueNoiseMask.Frames,
            blocks = 32;
        float[] values = Probe("reflection_blue_noise_sequence_probe.comp", width: draws, height: blocks);

        for (int block = 0; block < blocks; block++)
        {
            var strata = new HashSet<int>();
            for (int draw = period * BlueNoiseMask.Frames; draw < (period + 1) * BlueNoiseMask.Frames; draw++)
            {
                int texel = 4 * (block * draws + draw);
                strata.Add((int)values[texel] * 8 + (int)values[texel + 1]);
            }
            strata.Should().HaveCount(BlueNoiseMask.Frames, $"block {block} takes each stratum once in period {period}");
        }
    }

    [Fact]
    public void TheNextPeriodReadsEachBlocksMaskBytesInAnotherOrder()
    {
        const int draws = 2 * BlueNoiseMask.Frames,
            blocks = 32;
        float[] values = Probe("reflection_blue_noise_sequence_probe.comp", width: draws, height: blocks);

        for (int block = 0; block < blocks; block++)
        {
            int changed = 0;
            for (int draw = 0; draw < BlueNoiseMask.Frames; draw++)
            {
                int first = 4 * (block * draws + draw),
                    next = first + 4 * BlueNoiseMask.Frames;
                if (values[first + 2] != values[next + 2] || values[first + 3] != values[next + 3])
                    changed++;
            }
            changed
                .Should()
                .BeGreaterThan(
                    BlueNoiseMask.Frames / 2,
                    $"the shift of each period moves block {block} to another cell of the mask, so a pixel's long mean averages new values"
                );
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void OneDrawsRadialNumbersAndAnglesKeepUnderAThirdOfWhiteNoisesVarianceThroughAFiveByFiveBlurOverTheBlocks(
        int period
    )
    {
        const int side = BlueNoiseMask.Side;
        float[] values = Probe("reflection_blue_noise_frame_probe.comp", width: side, height: side);
        int first = 2 * period;

        // The angle wraps, so its spread is that of its point on the circle, as the GGX draw takes φ = 2π u.
        double angleCos = BlurredVarianceShare(cell => Math.Cos(2 * Math.PI * values[4 * cell + first]), side);
        double angleSin = BlurredVarianceShare(cell => Math.Sin(2 * Math.PI * values[4 * cell + first]), side);
        double radial = BlurredVarianceShare(cell => values[4 * cell + first + 1], side);

        new[] { angleCos, angleSin, radial }
            .Should()
            .AllSatisfy(share =>
                share.Should().BeLessThan(1 / 3.0, "a draw's numbers over the blocks are blue noise, and white noise keeps 1")
            );
    }

    [Fact]
    public void AReflectionRayTakesItsDirectionFromTheMaskAndNotFromPcg3d()
    {
        const int width = 96,
            height = 64;
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f), starIrradiance: 3);
            scene.Instances.Add(ReflectionTesting.Floor(renderer, ReflectionTesting.Mirror(roughness: 0.3f)));
            var probe = ScreenTraceProbe.On(renderer, "reflection_draw_probe.comp");
            renderer.Render(scene, 1f);
            float[] values = probe.Read();

            int[] starting = [.. Enumerable.Range(0, width * height).Where(pixel => values[4 * pixel + 2] == 1)];
            starting.Length.Should().BeGreaterThan(width * height / 4, "the glossy floor fills the lower half of the view");
            starting
                .Should()
                .OnlyContain(pixel => values[4 * pixel] == 1, "each reflection ray takes the direction of the mask's numbers");
            starting
                .Count(pixel => values[4 * pixel + 1] == 1)
                .Should()
                .BeLessThan(starting.Length / 10, "pcg3d's numbers alone give another direction at almost every pixel");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
