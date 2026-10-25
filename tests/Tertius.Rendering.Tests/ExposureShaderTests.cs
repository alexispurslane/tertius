using System.Numerics;
using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The shaders of the exposure pass against their C# copy, <see cref="ExposureMeter"/> (docs/plans/auto-exposure.md,
/// "The GPU tests that pin the shaders to the C# copy"): the meter puts each surface pixel in the bin of its pre-exposed
/// luminance, and the adapt step moves the exposure as the C# copy does from the same histogram.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ExposureShaderTests
{
    /// <summary>The C# copy sums in index order and the shader in a tree, so the two round differently.</summary>
    private const float StopsTolerance = 1e-4f;

    [Fact]
    public void TheMeterCountsEachSurfacePixelInTheBinOfItsLuminance()
    {
        // A clamp of 0 to 0 holds the exposure at 0 stops, so the pre-exposed luminance is the radiance's.
        VulkanRenderer renderer = Renderer(AutoExposure.Default with { MinStops = 0, MaxStops = 0 });
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, sunIntensity: 0);
            snapshot.Ambient.Intensity = 0;
            // Saturated hues, so that no star of the sky between the squares shows one of their colors.
            Vector3[] hues = [new(1, 0.2f, 0.2f), new(0.2f, 1, 0.2f), new(0.2f, 0.2f, 1), new(1, 1, 0.2f)];
            int[] bins = [100, 116, 132, 148];
            var centers = new Vector3[bins.Length];
            for (int i = 0; i < bins.Length; i++)
            {
                float luminance = MathF.Pow(2, ExposureMeter.BinCenterStops(bins[i]));
                centers[i] = new((i - 1.5f) * 0.6f, 0, -3);
                AddGlowingSquare(
                    renderer: renderer,
                    snapshot: snapshot,
                    position: centers[i],
                    halfSize: 0.2f,
                    radiance: hues[i] * (luminance / ExposureMeter.Luminance(hues[i]))
                );
            }

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "meter-bins");
            uint[] histogram = renderer.ReadExposureHistogram();

            uint[] expected = new uint[ExposureMeter.Bins];
            for (int i = 0; i < bins.Length; i++)
            {
                Vector3 shown = image.Color(Gpu.Project(point: centers[i], width: Width, height: Height));
                expected[bins[i]] = (uint)PixelsOfColor(image, shown);
            }

            expected.Where(count => count > 0).Should().HaveCount(bins.Length, "each square shows its own color");
            histogram
                .Should()
                .Equal(
                    expected,
                    "each square's pixels fall in its bin, and the sky between the squares counts nowhere"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheAdaptStepMovesTheExposureAsTheCSharpCopyDoes(bool rising)
    {
        AutoExposure setting = AutoExposure.Default;
        VulkanRenderer renderer = Renderer(setting);
        try
        {
            RenderSnapshot snapshot = Sunlit();
            AddRampSquare(renderer: renderer, snapshot: snapshot, position: Ahead, rotation: FacingTheCamera);
            // Rising: the view settles in sunlight, and the sun goes out. Falling: the other way round.
            float sun = snapshot.Sun.Intensity;
            snapshot.Sun.Intensity = rising ? sun : 0;
            Still(renderer: renderer, snapshot: snapshot, time: 0);
            snapshot.Sun.Intensity = rising ? 0 : sun;

            const double frameTime = 1.0 / 30;
            snapshot.Time = frameTime;
            renderer.Render(snapshot, 1f);
            uint[] histogram = renderer.ReadExposureHistogram();
            // The two frames after it report the exposure of that frame and of the one after it.
            snapshot.Time = 2 * frameTime;
            renderer.Render(snapshot, 1f);
            float used = renderer.LastFrameStats.Exposure!.Value.Stops;
            renderer.Render(snapshot, 1f);
            float next = renderer.LastFrameStats.Exposure!.Value.Stops;

            // The frame before it drew at time 0.
            float step = (float)frameTime;
            float target = ExposureMeter.TargetStops(
                trimmedMeanStops: ExposureMeter.TrimmedMeanStops(histogram, setting.DarkShare, setting.BrightShare),
                usedStops: used,
                compensationStops: setting.CompensationStops,
                minStops: setting.MinStops,
                maxStops: setting.MaxStops
            );
            float expected = ExposureMeter.Approach(
                stops: used,
                target: target,
                intoDarkFactor: ExposureMeter.ApproachFactor(step, setting.IntoDarkSeconds),
                intoLightFactor: ExposureMeter.ApproachFactor(step, setting.IntoLightSeconds)
            );

            Math.Sign(target - used).Should().Be(rising ? 1 : -1, "the view changed in that direction");
            next.Should().BeApproximately(expected, StopsTolerance);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The number of pixels of <paramref name="image"/> that show exactly <paramref name="color"/>.</summary>
    private static int PixelsOfColor(Capture image, Vector3 color)
    {
        int count = 0;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
            if (image.Color(x, y) == color)
                count++;
        return count;
    }
}
