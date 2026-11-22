using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>A synthetic draw for the feedback tests: it draws at exactly the requested scale.</summary>
internal static class RenderScaleTesting
{
    extension(RenderScaleController controller)
    {
        /// <summary>Records a draw of <paramref name="frame"/> at the requested scale and returns that scale.</summary>
        public float Next(long frame)
        {
            float scale = controller.RequestedScale;
            controller.Drew(frame: frame, realizedScale: scale);
            return scale;
        }
    }
}

/// <summary>The active rectangle and the delayed GPU-time feedback of dynamic resolution.</summary>
public sealed class RenderScaleTests
{
    private static readonly UpscalingSetting Dynamic = new Fsr3Upscaling(new FrameTimeTarget(16, 0.5f, 0.9f), 0);

    [Theory]
    [InlineData(1920, 1080, 2f / 3, 1280, 720)]
    [InlineData(1920, 1080, 0.5f, 960, 540)]
    [InlineData(1920, 1080, 0.8f, 1536, 864)]
    [InlineData(1080, 1920, 2f / 3, 720, 1280)]
    [InlineData(96, 80, 0.73f, 66, 55)]
    public void TheActiveSizeIsTheLargestMultipleOfTheReducedOutputRatioAtOrBelowTheScale(
        int width,
        int height,
        float scale,
        int expectedWidth,
        int expectedHeight
    )
    {
        UpscalingSetting setting = new Fsr3Upscaling(new FixedRenderScale(scale), 0);

        Extent2D active = setting.RenderExtent(new((uint)width, (uint)height), scale);

        active.Should().Be(new Extent2D((uint)expectedWidth, (uint)expectedHeight));
    }

    [Theory]
    [InlineData(1920, 1080)] // 16:9, 120 steps
    [InlineData(1080, 1920)] // portrait
    [InlineData(1440, 1080)] // 4:3
    [InlineData(2560, 1080)] // 64:27, 40 steps
    [InlineData(1680, 1050)] // 8:5
    [InlineData(1080, 2400)] // a tall portrait phone, 9:20
    [InlineData(1368, 768)] // 57:32, 24 steps
    [InlineData(96, 80)] // 6:5, 16 steps
    [InlineData(64, 56)] // 8:7, 8 steps: four choices from 0.5 to 0.9
    public void EveryActiveSizeHasExactlyTheOutputsAspectInsideTheScaleLimits(int width, int height)
    {
        for (float scale = 0.45f; scale <= 1.001f; scale += 0.01f)
        {
            Extent2D active = Dynamic.RenderExtent(new((uint)width, (uint)height), scale);

            ((ulong)active.Width * (uint)height)
                .Should()
                .Be((ulong)active.Height * (uint)width, "{0} × {1} at {2} has the output's ratio", width, height, scale);
            (active.Width / (float)width).Should().BeInRange(0.5f, 0.9f, "the limits hold at a request of {0}", scale);
        }
    }

    [Theory]
    [InlineData(1919, 1079)]
    [InlineData(7, 5)]
    [InlineData(1, 1)]
    public void AFixedOutputWithNoActiveSizeInsideTheScaleLimitsIsRefused(int width, int height)
    {
        UpscalingSetting setting = new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0);
        var output = new Extent2D((uint)width, (uint)height);

        setting.Supports(output).Should().BeFalse("no multiple of the reduced ratio lies from 0.5 to 2/3");
        Action select = () => setting.RenderExtent(output, 2f / 3);
        select.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(1919, 1080, 1920, 1080)]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(1081, 1920, 1080, 1920)]
    [InlineData(1441, 1080, 1440, 1080)]
    [InlineData(361, 641, 360, 640)]
    [InlineData(1918, 1080, 1920, 1080)] // 959:540 has one size from 0.5 to 0.9, and a time target needs the four eighths
    [InlineData(7, 5, 8, 8)]
    [InlineData(3, 1080, 8, 1080)] // a narrow axis snaps up to the grid, not down to nothing
    public void AWindowSnapsToTheNearestOutputThatHasActiveSizesAndKeepsOneThatHas(
        int width,
        int height,
        int expectedWidth,
        int expectedHeight
    )
    {
        Extent2D output = Dynamic.SnapOutput(new((uint)width, (uint)height));

        output.Should().Be(new Extent2D((uint)expectedWidth, (uint)expectedHeight));
        Dynamic.Supports(output).Should().BeTrue();
        Dynamic.SnapOutput(output).Should().Be(output, "a supported output stays as it is");
    }

    [Theory]
    [InlineData(1366, 768, 1368, 768)] // 683:384 has only 0.5 and 1
    [InlineData(1680, 1050, 1680, 1050)] // 210 steps of 8:5
    [InlineData(1600, 900, 1600, 900)]
    [InlineData(3440, 1440, 3440, 1440)]
    [InlineData(1360, 768, 1360, 768)] // 16 steps of 85:48: 9 sizes from 0.5 to 1
    public void AWindowOfATimeTargetHasAtLeastAsManySizesAsTheEighthsInsideItsLimits(
        int width,
        int height,
        int expectedWidth,
        int expectedHeight
    )
    {
        UpscalingSetting setting = new Fsr3Upscaling(FrameTimeTarget.Default, 0);

        Extent2D output = setting.SnapOutput(new((uint)width, (uint)height));

        output.Should().Be(new Extent2D((uint)expectedWidth, (uint)expectedHeight));
    }

    [Fact]
    public void AFixedScaleDrawsAnOutputWithOneSizeInsideItsLimits()
    {
        UpscalingSetting setting = new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0);
        var output = new Extent2D(1366, 768);

        setting.Supports(output).Should().BeTrue("a headless output cannot change, and half of it lies in the limits");
        setting.RenderExtent(output, 2f / 3).Should().Be(new Extent2D(683, 384));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    public void AMinimizedWindowKeepsItsEmptySize(int width, int height)
    {
        var output = new Extent2D((uint)width, (uint)height);

        Dynamic.SnapOutput(output).Should().Be(output);
        Dynamic.Supports(output).Should().BeFalse("an empty output has nothing to draw");
    }

    [Fact]
    public void AWindowOfATimeTargetSnapsRatherThanKeepNativeAsItsOnlyChoice()
    {
        UpscalingSetting setting = new Fsr3Upscaling(FrameTimeTarget.Default, 0);

        Extent2D output = setting.SnapOutput(new(1919, 1080));

        output.Should().Be(new Extent2D(1920, 1080), "1919 × 1080 has only native, and a time target needs a choice");
        setting.RenderExtent(output, 0.5f).Should().Be(new Extent2D(960, 540));
    }

    [Theory]
    [InlineData(1919, 1080)]
    [InlineData(7, 5)]
    [InlineData(1, 1)]
    public void ANativeOnlySettingKeepsEveryWindowSizeAsItIs(int width, int height)
    {
        var output = new Extent2D((uint)width, (uint)height);
        foreach (
            UpscalingSetting setting in new UpscalingSetting[]
            {
                new NoUpscaling(),
                new Fsr3Upscaling(new FixedRenderScale(1), 0),
                new Fsr3Upscaling(new FrameTimeTarget(16, 1, 1), 0),
            }
        )
        {
            setting.SnapOutput(output).Should().Be(output);
            setting.RenderExtent(output, 1).Should().Be(output);
        }
    }

    [Theory]
    [InlineData(1, 1, 64, 2)]
    [InlineData(3, 2, 64, 2)]
    [InlineData(42, 42, 64, 42)]
    [InlineData(1, 100, 2, 100)]
    [InlineData(100, 1, 100, 2)]
    [InlineData(84, 70, 84, 70)]
    public void FsrPadsOnlyItsAllocationToALongerSideOf64AndAShorterOf2(
        int width,
        int height,
        int allocatedWidth,
        int allocatedHeight
    ) =>
        Fsr3Upscaler
            .AllocationSize(new((uint)width, (uint)height))
            .Should()
            .Be(new Extent2D((uint)allocatedWidth, (uint)allocatedHeight));

    [Fact]
    public void AWindowOfAFixedScaleSnapsToTheGridOf8SoThatItsScaleIsAnEighth()
    {
        UpscalingSetting setting = new Fsr3Upscaling(new FixedRenderScale(0.75f), 0);

        Extent2D output = setting.SnapOutput(new(1919, 1081));

        output.Should().Be(new Extent2D(1920, 1080), "a grid of 2 would give 1920 × 1082, with 0.5 as its only size");
        setting.RenderExtent(output, 0.75f).Should().Be(new Extent2D(1440, 810));
    }

    [Fact]
    public void ATargetWithNoEighthInsideItsLimitsStillNeedsTwoSizes()
    {
        UpscalingSetting setting = new Fsr3Upscaling(new FrameTimeTarget(16, 0.8f, 0.85f), 0);

        setting.Supports(new(1013, 1012)).Should().BeFalse("1013 × 1012 has no size from 0.8 to 0.85");
        setting
            .SnapOutput(new(1013, 1012))
            .Should()
            .Be(new Extent2D(1020, 1020), "the grid grows to 20 px, the first with two sizes: 16/20 and 17/20");
    }

    [Fact]
    public void LimitsTooCloseForTwoSizesOfAnyWindowAreReported()
    {
        // Two sizes 1/q apart cannot both lie within one float of 0.5 for any grid q up to 65,536 px.
        UpscalingSetting setting = new Fsr3Upscaling(new FrameTimeTarget(16, 0.5f, MathF.BitIncrement(0.5f)), 0);

        Action snap = () => setting.SnapOutput(new(1919, 1080));

        snap.Should().Throw<NotSupportedException>().WithMessage("*65,536 px*");
    }

    [Fact]
    public void ARequestBelowTheLimitsDrawsTheSmallestSizeInsideThem()
    {
        // 45 × 27 has 9 steps of 5 × 3, and 0.5 of 9 is 4.5, so the smallest size inside the limits is 5 steps.
        Dynamic.RenderExtent(new(45, 27), 0.3f).Should().Be(new Extent2D(25, 15));
    }

    [Fact]
    public void ALowerLimitThatIsTheFloatOfAStepAdmitsThatStep()
    {
        // 0.6f lies above 0.6, and 72 of 120 rounds to 0.6f, so 72 steps lie inside the limits.
        UpscalingSetting setting = new Fsr3Upscaling(new FrameTimeTarget(16, 0.6f, 1), 0);

        setting.RenderExtent(new(1920, 1080), 0.5f).Should().Be(new Extent2D(1152, 648));
    }

    [Fact]
    public void AScaleThatIsTheFloatOfAStepDrawsThatStep()
    {
        // 0.7f lies below 0.7, and 84 of 120 rounds to 0.7f, so 84 steps lie at or below the scale.
        UpscalingSetting setting = new Fsr3Upscaling(new FixedRenderScale(0.7f), 0);

        setting.RenderExtent(new(1920, 1080), 0.7f).Should().Be(new Extent2D(1344, 756));
    }

    [Fact]
    public void TheDelayedTimingsOfADrawCountTheScaleThatTheDrawRealized()
    {
        var controller = new RenderScaleController(new(16, 0.5f, 0.9f));
        controller.RequestedScale.Should().Be(0.9f);

        // The exact-ratio size realized 0.875 of the requested 0.9: 1680 × 945 of a 1920 × 1080 output.
        controller.Drew(frame: 0, realizedScale: 0.875f);
        controller.Measure(Timings(frame: 0, scale: 0.875f, renderCost: 40, outputWait: 0));

        // The fixed work is 3 ms and the work at the render size 40 ms per unit area, so 14.4 ms wants
        // sqrt(11.4 / 40), and a shrink moves a quarter of the way there.
        double wanted = Math.Sqrt(11.4 / 40);
        controller.RequestedScale.Should().BeApproximately((float)(0.9 + 0.25 * (wanted - 0.9)), 1e-6f);
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

    [Theory]
    [InlineData(0.49f)]
    [InlineData(0.95f)]
    [InlineData(float.NaN)]
    public void ADrawAtAScaleOutsideTheLimitsIsRefused(float realized)
    {
        var controller = new RenderScaleController(new(16, 0.5f, 0.9f));

        Action record = () => controller.Drew(frame: 0, realizedScale: realized);

        record.Should().Throw<ArgumentOutOfRangeException>();
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
