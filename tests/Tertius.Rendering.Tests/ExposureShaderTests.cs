using System.Numerics;
using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The shaders of the exposure pass against their C# copy, <see cref="ExposureMeter"/> (docs/plans/auto-exposure.md,
/// "The GPU tests that pin the shaders to the C# copy"): the meter reads the top-left pixel of each 2 × 2 block of the
/// image and puts each such surface pixel in the bin of its pre-exposed luminance, and the adapt step moves the exposure
/// as the C# copy does from the same histogram.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ExposureShaderTests
{
    /// <summary>The C# copy sums in index order and the shader in a tree, so the two round differently.</summary>
    private const float StopsTolerance = 1e-4f;

    /// <summary>
    /// How far the target from one pixel of each 2 × 2 block may lie from the target of a meter of every pixel, in stops:
    /// a twentieth of a stop, under half a bin. Measured: about 0.016 stops on the two ramps, which fall by 0.05 stops per
    /// pixel at 160 × 90, while a block's top-left pixel lies half a pixel from the block's center, and 0 on the bright
    /// object over the dark ground.
    /// </summary>
    private const float HalfMeterTolerance = 0.05f;

    [Theory]
    [InlineData(640, 360)]
    [InlineData(641, 361)]
    public void TheMeterCountsOnePixelOfEach2By2Block(int width, int height)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: height,
            framesInFlight: 1,
            exposure: AutoExposure.Default
        );
        try
        {
            RenderSnapshot snapshot = Sunlit();
            AddGraySquare(renderer, snapshot);
            Still(renderer: renderer, snapshot: snapshot, time: 0);
            uint[] histogram = renderer.ReadExposureHistogram();

            // The gray square fills the view, so every block's top-left pixel is a surface pixel. The block of an odd
            // image's last column or row holds only that column or row, and it counts as any other block.
            long blocks = (long)((width + 1) / 2) * ((height + 1) / 2);
            histogram.Sum(count => (long)count).Should().Be(blocks, "the meter counts one pixel of each 2 × 2 block");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData("ramp in sunlight")]
    [InlineData("ramp with the sun out")]
    [InlineData("bright object over dark ground")]
    public void TheTargetFromOnePixelOfEach2By2BlockLiesWithinATwentiethOfAStopOfTheTargetFromEveryPixel(string scene)
    {
        AutoExposure setting = AutoExposure.Default;
        VulkanRenderer renderer = Renderer(setting);
        try
        {
            RenderSnapshot snapshot = FillingScene(renderer, scene);
            float used = Still(renderer: renderer, snapshot: snapshot, time: 0).Stops;
            // The histogram and the HDR image are both of the frame that read the still's exposure a moment after it,
            // which used that exposure, so the two meters read one image.
            uint[] metered = renderer.ReadExposureHistogram();
            uint[] everyPixel = HistogramOfEveryPixel(renderer.ReadHdr());

            float meteredTarget = Target(metered, setting, used);
            float everyPixelTarget = Target(everyPixel, setting, used);

            meteredTarget
                .Should()
                .BeApproximately(
                    everyPixelTarget,
                    HalfMeterTolerance,
                    "a meter of a quarter of the pixels keeps the shares of the luminances in view"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheMeterCountsTheTopLeftPixelOfEach2By2BlockInTheBinOfItsLuminance()
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
                expected[bins[i]] = (uint)MeteredPixelsOfColor(image, shown);
            }

            expected.Where(count => count > 0).Should().HaveCount(bins.Length, "each square shows its own color");
            histogram
                .Should()
                .Equal(
                    expected,
                    "each square's metered pixels fall in its bin, and the sky between the squares counts nowhere"
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
            // The two frames after it report the exposure of that frame and of the one after it. Each draws once,
            // because its step is one real-time frame.
            snapshot.Time = 2 * frameTime;
            renderer.Render(snapshot, 1f);
            float used = renderer.LastFrameStats.Exposure!.Value.Stops;
            snapshot.Time = 3 * frameTime;
            renderer.Render(snapshot, 1f);
            float next = renderer.LastFrameStats.Exposure!.Value.Stops;

            // The still before it lies at time 0.
            const float step = (float)frameTime;
            float target = ExposureMeter.TargetStops(
                reading: ExposureMeter.Reading(histogram, setting.DarkShare, setting.BrightShare),
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

    [Fact]
    public void TheAdaptStepTakesTheHighlightTargetAsTheCSharpCopyDoes()
    {
        // A compensation, so that the shader must add it after it takes the lower target, as the C# copy does. Time
        // constants of 0, so that the step reaches the target and the test pins the target itself, not 1 % of it.
        AutoExposure setting = AutoExposure.Default with
        {
            CompensationStops = 0.5f,
            IntoDarkSeconds = 0,
            IntoLightSeconds = 0,
        };
        VulkanRenderer renderer = Renderer(setting);
        try
        {
            RenderSnapshot snapshot = BrightObjectOverDarkGround(renderer);
            Still(renderer: renderer, snapshot: snapshot, time: 0);
            // Half the sun, so the square darkens by a stop and the target rises.
            snapshot.Sun.Intensity /= 2;

            const double frameTime = 1.0 / 30;
            snapshot.Time = frameTime;
            renderer.Render(snapshot, 1f);
            uint[] histogram = renderer.ReadExposureHistogram();
            snapshot.Time = 2 * frameTime;
            renderer.Render(snapshot, 1f);
            float used = renderer.LastFrameStats.Exposure!.Value.Stops;
            snapshot.Time = 3 * frameTime;
            renderer.Render(snapshot, 1f);
            float next = renderer.LastFrameStats.Exposure!.Value.Stops;

            const float step = (float)frameTime;
            (float TrimmedMeanStops, float BrightEdgeStops) reading = ExposureMeter
                .Reading(histogram, setting.DarkShare, setting.BrightShare)!
                .Value;
            float target = ExposureMeter.TargetStops(
                reading: reading,
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

            (reading.BrightEdgeStops - reading.TrimmedMeanStops)
                .Should()
                .BeGreaterThan(-MathF.Log2(ExposureMeter.MiddleGray), "the highlight target must be the lower one");
            next.Should().BeApproximately(expected, StopsTolerance);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The number of pixels of <paramref name="image"/> that the meter reads, the top-left pixel of each 2 × 2 block,
    /// that show exactly <paramref name="color"/>.
    /// </summary>
    private static int MeteredPixelsOfColor(Capture image, Vector3 color)
    {
        int count = 0;
        for (int y = 0; y < image.Height; y += 2)
        for (int x = 0; x < image.Width; x += 2)
            if (image.Color(x, y) == color)
                count++;
        return count;
    }

    /// <summary>A scene of the exposure tests whose surfaces fill the view, so that every pixel is a surface pixel.</summary>
    private static RenderSnapshot FillingScene(VulkanRenderer renderer, string scene) =>
        scene switch
        {
            "ramp in sunlight" => RampView(renderer, sunOut: false),
            "ramp with the sun out" => RampView(renderer, sunOut: true),
            "bright object over dark ground" => BrightObjectOverDarkGround(renderer),
            _ => throw new ArgumentOutOfRangeException(nameof(scene), scene, "not a scene of these tests"),
        };

    /// <summary>The ramp square that fills the view, in sunlight, or with the sun out when <paramref name="sunOut"/>.</summary>
    private static RenderSnapshot RampView(VulkanRenderer renderer, bool sunOut)
    {
        RenderSnapshot snapshot = Sunlit();
        if (sunOut)
            snapshot.Sun.Intensity = 0;
        AddRampSquare(renderer: renderer, snapshot: snapshot, position: Ahead, rotation: FacingTheCamera);
        return snapshot;
    }

    /// <summary>
    /// The histogram of a meter of every pixel at full resolution, over the HDR image that
    /// <see cref="VulkanRenderer.ReadHdr"/> read. Every pixel counts as a surface pixel, so the scene must fill the
    /// view.
    /// </summary>
    private static uint[] HistogramOfEveryPixel(float[] rgba)
    {
        uint[] bins = new uint[ExposureMeter.Bins];
        for (int i = 0; i < rgba.Length; i += 4)
            bins[ExposureMeter.BinOf(ExposureMeter.Luminance(new(x: rgba[i], y: rgba[i + 1], z: rgba[i + 2])))]++;
        return bins;
    }

    /// <summary>The target that the C# copy computes from <paramref name="histogram"/> for a frame that used <paramref name="used"/>.</summary>
    private static float Target(uint[] histogram, AutoExposure setting, float used) =>
        ExposureMeter.TargetStops(
            reading: ExposureMeter.Reading(histogram, setting.DarkShare, setting.BrightShare),
            usedStops: used,
            compensationStops: setting.CompensationStops,
            minStops: setting.MinStops,
            maxStops: setting.MaxStops
        );
}
