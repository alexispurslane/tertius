using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>The active rectangle and the delayed GPU-time feedback of dynamic resolution.</summary>
public sealed class RenderScaleTests
{
    [Theory]
    [InlineData(1920, 1080, 2f / 3, 1280, 720)]
    [InlineData(1919, 1079, 2f / 3, 1272, 712)]
    [InlineData(7, 5, 0.5f, 8, 8)]
    public void EachActiveAxisRoundsDownToEightPixels(
        int width,
        int height,
        float scale,
        int expectedWidth,
        int expectedHeight
    )
    {
        UpscalingSetting setting = new Fsr3Upscaling(new FixedRenderScale(scale), 0);
        Extent2D active = setting.RenderExtent(new((uint)width, (uint)height), scale);
        active.Width.Should().Be((uint)expectedWidth);
        active.Height.Should().Be((uint)expectedHeight);
    }

    [Fact]
    public void NoUpscalingPreservesTheExactOutputSizeWithoutRounding()
    {
        UpscalingSetting setting = new NoUpscaling();
        setting.RenderExtent(new(159, 91), 0.5f).Should().Be(new Extent2D(159, 91));
    }

    [Fact]
    public void ASteadyLoadSettlesAtTheGpuTimeTarget()
    {
        var controller = new RenderScaleController(new(1000f / 60, 0.5f, 1));
        float scale = 1;
        for (long frame = 0; frame < 100; frame++)
        {
            scale = controller.Next(frame);
            controller.Measure(Timings(frame: frame, scale: scale, renderCost: 24, outputWait: 0));
            controller.AssertInvariants();
        }
        (3 + 24 * scale * scale).Should().BeApproximately(15f, 0.45f);
    }

    [Fact]
    public void ASuddenLoadFallsWithinTenFrames()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        float scale = 1;
        for (long frame = 0; frame < 10; frame++)
        {
            scale = controller.Next(frame);
            controller.Measure(Timings(frame: frame, scale: scale, renderCost: 40, outputWait: 0));
        }
        scale.Should().BeLessThan(0.63f);
    }

    [Fact]
    public void PresentationWaitsDoNotChangeTheScale()
    {
        var plain = new RenderScaleController(FrameTimeTarget.Default);
        var waited = new RenderScaleController(FrameTimeTarget.Default);
        for (long frame = 0; frame < 80; frame++)
        {
            float scale = plain.Next(frame);
            waited.Next(frame).Should().Be(scale);
            plain.Measure(Timings(frame: frame, scale: scale, renderCost: 24, outputWait: 0));
            waited.Measure(Timings(frame: frame, scale: scale, renderCost: 24, outputWait: 10));
        }
    }

    [Fact]
    public void MissingAndUnknownMeasurementsDoNotChangeTheScale()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        controller.Next(0).Should().Be(1);
        controller.Measure(Timings(frame: 90, scale: 1, renderCost: 100, outputWait: 0));
        controller.Next(100).Should().Be(1);
    }

    [Fact]
    public void MeasurementsUseTheScaleOfTheirFrameRatherThanTheCurrentScale()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        var inFlight = new Queue<(long Frame, float Scale)>();
        float scale = 1;
        for (long frame = 0; frame < 120; frame++)
        {
            scale = controller.Next(frame);
            inFlight.Enqueue((frame, scale));
            if (inFlight.Count <= 3)
                continue;
            (long measured, float drawnScale) = inFlight.Dequeue();
            controller.Measure(Timings(frame: measured, scale: drawnScale, renderCost: 24, outputWait: 0));
        }
        (3 + 24 * scale * scale).Should().BeApproximately(15f, 0.45f);
    }

    [Fact]
    public void ALoadBeyondTheRangeStopsAtTheMinimumScale()
    {
        var controller = new RenderScaleController(new(1000f / 60, 0.6f, 0.9f));
        for (long frame = 0; frame < 100; frame++)
        {
            float scale = controller.Next(frame);
            controller.Measure(Timings(frame: frame, scale: scale, renderCost: 100, outputWait: 0));
        }
        controller.Next(100).Should().BeApproximately(0.6f, 0.0001f);
    }

    [Fact]
    public void AGapSmallerThanThreePercentLeavesTheScaleUnchanged()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        for (long frame = 0; frame < 20; frame++)
        {
            float scale = controller.Next(frame);
            controller.Measure(Timings(frame: frame, scale: scale, renderCost: 12.2, outputWait: 0));
        }
        controller.Next(20).Should().Be(1);
    }

    private static GpuTimings Timings(long frame, float scale, double renderCost, double outputWait) =>
        new(
            frame,
            new PassTiming[]
            {
                new("deformation", 1),
                new("G-buffer", renderCost * scale * scale),
                new("exposure", 0),
                new("FSR 3.1", 1.5),
                new("bloom", 0.5),
                new("tone map", 0.2 + outputWait),
            },
            3.2 + renderCost * scale * scale + outputWait
        );
}
