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
    public void DiagnosticPassLabelsDoNotChangeTheMeasuredScale()
    {
        var original = new RenderScaleController(FrameTimeTarget.Default);
        var renamed = new RenderScaleController(FrameTimeTarget.Default);
        for (long frame = 0; frame < 80; frame++)
        {
            float scale = original.Next(frame);
            renamed.Next(frame).Should().Be(scale);
            GpuTimings timings = Timings(frame: frame, scale: scale, renderCost: 24, outputWait: 10);
            original.Measure(timings);
            renamed.Measure(
                timings with
                {
                    Passes = timings.Passes.ToArray().Select(pass => pass with { Pass = "diagnostic label" }).ToArray(),
                }
            );
        }
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

    [Fact]
    public void ARepeatedMeasurementOfAFinishedFrameDoesNotMoveTheScaleAgain()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        controller.Next(0);
        GpuTimings timings = Timings(frame: 0, scale: 1, renderCost: 24, outputWait: 0);
        controller.Measure(timings);
        float afterFirst = controller.Next(1);

        controller.Measure(timings);

        controller.Next(2).Should().Be(afterFirst);
        controller.AssertInvariants();
    }

    [Fact]
    public void AStaleMeasurementCannotUseTheScaleOfANewerFrameInTheSameSlot()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        for (long frame = 0; frame <= 16; frame++)
            controller.Next(frame);

        controller.Measure(Timings(frame: 0, scale: 1, renderCost: 100, outputWait: 0));

        controller.Next(17).Should().Be(1);
        controller.AssertInvariants();
    }

    [Fact]
    public void DelayingTheSamePerAreaLoadDoesNotChangeTheScaleResponse()
    {
        var immediate = new RenderScaleController(FrameTimeTarget.Default);
        var delayed = new RenderScaleController(FrameTimeTarget.Default);
        immediate.Next(0);
        delayed.Next(0);
        delayed.Next(1);
        GpuTimings first = Timings(frame: 0, scale: 1, renderCost: 24, outputWait: 0);
        immediate.Measure(first);
        delayed.Measure(first);
        float immediateScale = immediate.Next(1);

        immediate.Measure(Timings(frame: 1, scale: immediateScale, renderCost: 24, outputWait: 0));
        delayed.Measure(Timings(frame: 1, scale: 1, renderCost: 24, outputWait: 0));

        delayed.Next(2).Should().BeApproximately(immediate.Next(2), 0.000001f);
        delayed.AssertInvariants();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    public void AnInvalidGpuCostDoesNotInfluenceThisOrLaterMeasurements(double milliseconds)
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        var reference = new RenderScaleController(FrameTimeTarget.Default);
        controller.Next(0);
        PassTiming[] invalid = Timings(frame: 0, scale: 1, renderCost: 24, outputWait: 0).Passes.ToArray();
        invalid[1] = invalid[1] with { Milliseconds = milliseconds };

        controller.Measure(new(0, invalid, 0));

        controller.Next(1).Should().Be(1);
        reference.Next(1);
        GpuTimings valid = Timings(frame: 1, scale: 1, renderCost: 24, outputWait: 0);
        controller.Measure(valid);
        reference.Measure(valid);
        controller.Next(2).Should().Be(reference.Next(2));
        controller.AssertInvariants();
    }

    [Fact]
    public void AnIncompletePassSpanDoesNotInfluenceLaterMeasurements()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        controller.Next(0);
        PassTiming[] incomplete = Timings(frame: 0, scale: 1, renderCost: 100, outputWait: 0).Passes.ToArray()[..^1];

        controller.Measure(new(0, incomplete, 103));

        controller.Next(1).Should().Be(1);
        controller.AssertInvariants();
    }

    [Fact]
    public void AFrameWithoutScalingWorkDoesNotContaminateTheCostModel()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);
        var reference = new RenderScaleController(FrameTimeTarget.Default);
        controller.Next(0);
        controller.Measure(Timings(frame: 0, scale: 1, renderCost: 0, outputWait: 0));
        controller.Next(1).Should().Be(1);
        reference.Next(1);
        GpuTimings valid = Timings(frame: 1, scale: 1, renderCost: 24, outputWait: 0);

        controller.Measure(valid);
        reference.Measure(valid);

        controller.Next(2).Should().Be(reference.Next(2));
        controller.AssertInvariants();
    }

    [Theory]
    [InlineData(0, 0.5f, 1)]
    [InlineData(-1, 0.5f, 1)]
    [InlineData(float.NaN, 0.5f, 1)]
    [InlineData(float.PositiveInfinity, 0.5f, 1)]
    [InlineData(16, 0.49f, 1)]
    [InlineData(16, 1.01f, 1)]
    [InlineData(16, float.NaN, 1)]
    [InlineData(16, 0.5f, 0.49f)]
    [InlineData(16, 0.5f, 1.01f)]
    [InlineData(16, 0.5f, float.NaN)]
    [InlineData(16, 0.75f, 0.5f)]
    public void AnInvalidFrameBudgetOrScaleRangeIsRefused(float milliseconds, float minimum, float maximum)
    {
        Action create = () => _ = new RenderScaleController(new(milliseconds, minimum, maximum));

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ANegativeFrameNumberIsRejectedBeforeRecordingItsScale()
    {
        var controller = new RenderScaleController(FrameTimeTarget.Default);

        Action record = () => controller.Next(-1);

        record.Should().Throw<ArgumentOutOfRangeException>();
        controller.Next(0).Should().Be(1);
        controller.AssertInvariants();
    }

    [Fact]
    public void TheCostGroupsGiveTheControllerTheSameScaleAsEveryPass()
    {
        var eachPass = new RenderScaleController(FrameTimeTarget.Default);
        var grouped = new RenderScaleController(FrameTimeTarget.Default);
        for (long frame = 0; frame < 80; frame++)
        {
            float scale = eachPass.Next(frame);
            grouped.Next(frame).Should().Be(scale, "a window without pass timestamps keeps the measured scale");
            GpuTimings timings = Timings(frame: frame, scale: scale, renderCost: 24 + frame % 7, outputWait: 1);
            eachPass.Measure(timings);
            PassTiming[] groups =
            [
                .. timings
                    .Passes.ToArray()
                    .GroupBy(pass => RenderScaleController.CostGroupOf(pass.Stage))
                    .Select(group => new PassTiming(
                        RenderScaleController.NameOf(group.Key),
                        group.Sum(pass => pass.Milliseconds)
                    )
                    {
                        Stage = group.Last().Stage,
                    }),
            ];
            grouped.Measure(timings with { Passes = groups });
        }
    }

    [Theory]
    [InlineData("EnvironmentPrefilter", "BeforeRenderSize")]
    [InlineData("GBuffer", "AtRenderSize")]
    [InlineData("Exposure", "AtRenderSize")]
    [InlineData("Upscale", "AfterRenderSize")]
    [InlineData("ToneMap", "Output")]
    public void EachStageBelongsToTheCostGroupThatTheControllerWeighsItIn(string stage, string group) =>
        RenderScaleController
            .CostGroupOf(Enum.Parse<FrameGraph.Stage>(stage))
            .Should()
            .Be(Enum.Parse<RenderScaleController.CostGroup>(group));

    private static GpuTimings Timings(long frame, float scale, double renderCost, double outputWait) =>
        new(
            frame,
            new PassTiming[]
            {
                new("deformation", 1) { Stage = FrameGraph.Stage.Deformation },
                new("G-buffer", renderCost * scale * scale) { Stage = FrameGraph.Stage.GBuffer },
                new("exposure", 0) { Stage = FrameGraph.Stage.Exposure },
                new("FSR 3.1", 1.5) { Stage = FrameGraph.Stage.Upscale },
                new("bloom", 0.5) { Stage = FrameGraph.Stage.Bloom },
                new("tone map", 0.2 + outputWait) { Stage = FrameGraph.Stage.ToneMap },
            },
            3.2 + renderCost * scale * scale + outputWait
        );
}
