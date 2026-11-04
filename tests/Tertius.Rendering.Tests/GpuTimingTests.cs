namespace Tertius.Rendering.Tests;

/// <summary>
/// The GPU time of each pass in <see cref="RenderStats.GpuTimings"/>: timestamps around every pass of the frame graph,
/// read back without a stall when the renderer reuses the frame's slot, so the timings are those of the last frame
/// whose GPU work finished. The tests run where the device writes timestamps, as the development Mac's does.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class GpuTimingTests
{
    private const int Width = 320,
        Height = 180;

    /// <summary>The time in milliseconds that a pass that records nothing stays under.</summary>
    private const double NearlyNoTime = 0.05;

    /// <summary>The pairs of frames in each chunk of <see cref="SpanAddedBy"/>.</summary>
    private const int ChunkPairs = 128;

    /// <summary>The chunks that <see cref="SpanAddedBy"/> measures at most before it gives up.</summary>
    private const int MaxChunks = 16;

    /// <summary>A validating headless renderer that the test skips on a device without timestamps.</summary>
    private static VulkanRenderer TimedRenderer(
        int framesInFlight = 2,
        ScreenSpaceLightSetting screenSpaceLight = default,
        SubsurfaceSetting subsurface = default
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: framesInFlight,
            screenSpaceLight: screenSpaceLight,
            subsurface: subsurface
        );
        if (!renderer.TimesPasses)
        {
            Gpu.DisposeAndAssertClean(renderer);
            Assert.Skip($"{renderer.DeviceName} writes no timestamps on its graphics queue.");
        }

        return renderer;
    }

    [Fact]
    public void NoFrameHasGpuTimingsBeforeTheFirstFrameSlotIsReused()
    {
        VulkanRenderer renderer = TimedRenderer(framesInFlight: 2);
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            var reported = new List<GpuTimings?>();
            for (int i = 0; i < 2; i++)
            {
                renderer.Render(snapshot, 1f);
                reported.Add(renderer.LastFrameStats.GpuTimings);
            }

            reported.Should().AllSatisfy(timings => timings.Should().BeNull("no frame slot has been waited on again"));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void EachFrameReportsTheTimingsOfTheFrameThatUsedItsSlotOneRoundOfFramesInFlightBefore(int framesInFlight)
    {
        VulkanRenderer renderer = TimedRenderer(framesInFlight);
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            var timedFrames = new List<long?>();
            for (int i = 0; i < 6; i++)
            {
                renderer.Render(snapshot, 1f);
                timedFrames.Add(renderer.LastFrameStats.GpuTimings?.Frame);
            }

            timedFrames
                .Skip(framesInFlight)
                .Should()
                .Equal(
                    Enumerable.Range(0, 6 - framesInFlight).Select(frame => (long?)frame),
                    "frame n reads its slot right after the wait on the slot's fence, which frame n - {0} signaled",
                    framesInFlight
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheTimingsNameEveryPassOfTheFrameGraphInRecordingOrder()
    {
        VulkanRenderer renderer = TimedRenderer();
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);

            GpuTimings timings = renderer.LastFrameStats.GpuTimings!.Value;
            timings
                .Passes.ToArray()
                .Select(pass => pass.Pass)
                .Should()
                .Equal(renderer.Graph.Passes.Select(pass => pass.Name));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithVisibilityBitmaskLightTheFourOcclusionPassesRunBetweenTheDirectAndTheAmbientPass()
    {
        VulkanRenderer renderer = TimedRenderer(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);

            string[] passes = [.. renderer.LastFrameStats.GpuTimings!.Value.Passes.ToArray().Select(pass => pass.Pass)];
            passes
                .SkipWhile(pass => pass != "G-buffer")
                .Take(8)
                .Should()
                .Equal(
                    [
                        "G-buffer",
                        "surface test",
                        "direct light",
                        "occlusion depth",
                        "occlusion sectors",
                        "occlusion denoise",
                        "occlusion upsample",
                        "ambient light",
                    ],
                    "the occlusion passes read the finished G-buffer and the direct image, no pass of theirs reads the "
                        + "depth pyramid, and the ambient pass reads their estimate target"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithNoScreenSpaceLightTheFrameHasTheDirectAndTheAmbientPassAndNoOcclusionPass()
    {
        VulkanRenderer renderer = TimedRenderer(screenSpaceLight: new NoScreenSpaceLight());
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);

            string[] passes = [.. renderer.LastFrameStats.GpuTimings!.Value.Passes.ToArray().Select(pass => pass.Pass)];
            passes
                .SkipWhile(pass => pass != "G-buffer")
                .Take(4)
                .Should()
                .Equal(
                    ["G-buffer", "surface test", "direct light", "ambient light"],
                    "the ambient pass reads the cleared estimate target"
                );
            passes.Should().NotContain(pass => pass.StartsWith("occlusion"));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void UnderSeparableSubsurfaceTheTwoScatterPassesRunRightAfterTheAmbientPass()
    {
        VulkanRenderer renderer = TimedRenderer(subsurface: SeparableSubsurface.Default);
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);

            string[] passes = [.. renderer.LastFrameStats.GpuTimings!.Value.Passes.ToArray().Select(pass => pass.Pass)];
            passes
                .SkipWhile(pass => pass != "ambient light")
                .Take(3)
                .Should()
                .Equal(
                    ["ambient light", "scatter rows", "scatter columns"],
                    "the scatter blurs the diffuse light that the ambient pass completes"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void UnderNoSubsurfaceTheFrameHasNoScatterPass()
    {
        VulkanRenderer renderer = TimedRenderer(subsurface: new NoSubsurface());
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);

            renderer
                .LastFrameStats.GpuTimings!.Value.Passes.ToArray()
                .Should()
                .NotContain(
                    pass => pass.Pass.StartsWith("scatter"),
                    "the renderer makes no scatter pass without the setting"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithEverySettingOnTheFrameGraphHoldsNoMorePassesThanTheGpuTimerCanTime()
    {
        // Every pass that a setting adds, each with the most passes it can add: the half-resolution trace's upsample, the
        // three SMAA passes and the four occlusion passes among them.
        using var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = true,
                ImpostorTiles = 4,
                LocalShadows = LocalShadowAtlas.Default,
                Exposure = AutoExposure.Default,
                ScreenSpaceLight = VisibilityBitmaskLight.Default,
                Bloom = Bloom.Default,
                Reflections = ConeTracedReflections.Default with { Resolution = TraceResolution.Half },
                AntiAliasing = AntiAliasing.Smaa,
                EnvironmentMap = SceneEnvironmentMap.Default,
                Deformation = true,
                Subsurface = SeparableSubsurface.Default,
            }
        );

        renderer
            .Graph.Passes.Count()
            .Should()
            .BeLessThanOrEqualTo(GpuTimer.MaxPasses, "each pass needs a query of the frame's timestamp pool");
    }

    [Fact]
    public void ThePassesThatDrawTheReferenceSceneTakeGpuTime()
    {
        VulkanRenderer renderer = TimedRenderer();
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);

            PassTiming[] passes = renderer.LastFrameStats.GpuTimings!.Value.Passes.ToArray();
            passes
                .Should()
                .AllSatisfy(pass =>
                    pass.Milliseconds.Should()
                        .BeInRange(0, 1_000, "{0} is a time on a counter that only grows", pass.Pass)
                );
            passes
                .Where(pass => pass.Pass is "shadow maps" or "G-buffer" or "direct light" or "ambient light")
                .Should()
                .AllSatisfy(pass => pass.Milliseconds.Should().BePositive("{0} draws the reference scene", pass.Pass));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APassThatRecordsNothingAddsNearlyNoTimeToTheFramesGpuSpan()
    {
        VulkanRenderer renderer = TimedRenderer(framesInFlight: 1);
        try
        {
            renderer.Graph.Add(FrameGraph.Stage.GBuffer, new EmptyPass());

            if (SpanAddedBy(renderer, pass: "empty", bound: NearlyNoTime) is not { } added)
            {
                Assert.Skip(
                    $"The work of other processes stretched the frames of all {MaxChunks} chunks too much to resolve"
                        + $" {NearlyNoTime} ms of their spans."
                );
                return;
            }

            added
                .High.Should()
                .BeLessThan(NearlyNoTime, "the pass gave the GPU no work, only one more stamp to write ({0})", added);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void OnADriverWhoseStampsLatchWhenTheWorkBeforeThemEndsAPassThatRecordsNothingReportsNearlyNoTimeOfItsOwn()
    {
        VulkanRenderer renderer = TimedRenderer();
        try
        {
            if (renderer.StampsLatchLate)
            {
                Assert.Skip(
                    $"{renderer.DeviceName}'s stamps latch late, so a pass's own time can hold the next pass's work, and"
                        + " only the sum of neighbouring passes holds (ARCHITECTURE.md, \"GPU timings\")."
                );
            }

            // After the G-buffer pass, the costliest pass of the scene, so a stamp that took the G-buffer's time into the
            // empty pass's would show.
            renderer.Graph.Add(FrameGraph.Stage.GBuffer, new EmptyPass());

            FewestMilliseconds(passes => passes.Single(pass => pass.Pass == "empty").Milliseconds, renderer)
                .Should()
                .BeLessThan(NearlyNoTime, "the pass gave the GPU no work, and a mean over frames counts it as it is");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void OnMacOsWhereVulkanRunsOnMoltenVkTheRendererSaysThatItsStampsLatchLate()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            renderer
                .StampsLatchLate.Should()
                .Be(
                    OperatingSystem.IsMacOS(),
                    "MoltenVK samples each stamp in an encoder of its own that the next pass's work can delay, and the"
                        + " desktop drivers of Linux latch a stamp when the work before it ends"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFrameThatTheOutputSkipsStillReportsTheTimingsOfTheFrameThatItsWaitFoundFinished()
    {
        TestOutput? made = null;
        var renderer = VulkanRenderer.CreateOffscreen(
            new()
            {
                Validation = true,
                Width = Width,
                Bloom = new NoBloom(),
                Height = Height,
                Impostors = false,
                // A fixed exposure draws no metering draw, so each Render draws one frame.
                Exposure = new FixedExposure(0),
                AntiAliasing = AntiAliasing.None,
            },
            (device, allocator, extent) => made = new(device: device, allocator: allocator, extent: extent, bgra: false)
        );
        try
        {
            if (!renderer.TimesPasses)
                Assert.Skip($"{renderer.DeviceName} writes no timestamps on its graphics queue.");
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);

            made!.FramesToSkip = 1;
            renderer.Render(snapshot, 1f);

            renderer.LastFrameStats.Rendered.Should().BeFalse();
            GpuTimings? timings = renderer.LastFrameStats.GpuTimings;
            timings.Should().NotBeNull();
            timings!
                .Value.Frame.Should()
                .Be(1, "the skipped frame waited on the slot of frame 1 before it found no image");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The fewest milliseconds that <paramref name="measure"/> reads from the renderer's timings over 64 frames of the
    /// test scene. A load on the GPU from another process stretches a pass's time in some frames, by the work of the
    /// other process that the GPU ran between two stamps, and the fewest is a frame that the load left alone.
    /// </summary>
    private static double FewestMilliseconds(Func<PassTiming[], double> measure, VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
        double fewest = double.MaxValue;
        for (int frame = 0; frame < 64; frame++)
        {
            renderer.Render(snapshot, 1f);
            if (renderer.LastFrameStats.GpuTimings is { } timings)
                fewest = Math.Min(fewest, measure(timings.Passes.ToArray()));
        }

        return fewest;
    }

    /// <summary>
    /// How much the pass named <paramref name="pass"/> adds to the frame's GPU span, in milliseconds: an interval of the
    /// median of the spans that it adds, from the first chunk of <see cref="ChunkPairs"/> pairs of frames whose interval
    /// resolves <paramref name="bound"/>, or null when none of <see cref="MaxChunks"/> chunks does. An interval resolves
    /// the bound when it lies wholly below or wholly above it and is at most twice the bound wide. Only the span holds
    /// on every driver: on one whose stamps latch late, the stamp before the pass can latch after the pass's work, so no
    /// window of neighbouring stamps holds that work in every frame (ARCHITECTURE.md, "GPU timings"). The renderer must
    /// have one frame in flight, so no frame runs beside another on the GPU. The work of another process stretches some
    /// frames. A load that stretches nearly every frame widens the interval, and can push it below the bound for a pass
    /// that records work, so a chunk that it widened decides nothing. A later chunk can find a quieter stretch.
    /// </summary>
    private static SpanInterval? SpanAddedBy(VulkanRenderer renderer, string pass, double bound)
    {
        RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
        try
        {
            for (int chunk = 0; chunk < MaxChunks; chunk++)
            {
                SpanInterval interval = MedianInterval(AddedSpans(renderer, snapshot, pass));
                if (interval.High - interval.Low <= 2 * bound && (interval.High < bound || interval.Low > bound))
                    return interval;
            }

            return null;
        }
        finally
        {
            renderer.Graph.Skipped.Remove(pass);
        }
    }

    /// <summary>
    /// The span of a frame that records the pass named <paramref name="pass"/> minus the span of the frame right before
    /// it, which skips the pass, for <see cref="ChunkPairs"/> such pairs. The frames of a pair are next to each other in
    /// time, so they share the GPU's clock, which follows the GPU's duty cycle.
    /// </summary>
    private static double[] AddedSpans(VulkanRenderer renderer, RenderSnapshot snapshot, string pass)
    {
        double[] added = new double[ChunkPairs];
        int pairs = 0;
        GpuTimings? without = null;
        for (int frame = 0; pairs < ChunkPairs; frame++)
        {
            if (frame % 2 == 0)
                renderer.Graph.Skipped.Add(pass);
            else
                renderer.Graph.Skipped.Remove(pass);
            renderer.Render(snapshot, 1f);
            // The timings are those of an earlier frame, so the names of its passes say whether it recorded the pass, and
            // its number says whether it is the frame right after the one without the pass.
            if (renderer.LastFrameStats.GpuTimings is not { } timings)
                continue;
            if (!timings.Passes.ToArray().Any(timing => timing.Pass == pass))
                without = timings;
            else if (without is { } before && timings.Frame == before.Frame + 1)
            {
                added[pairs++] = timings.SpanMilliseconds - before.SpanMilliseconds;
                without = null;
            }
        }

        return added;
    }

    /// <summary>
    /// An interval that holds the median of the distribution that <paramref name="values"/> come from with a chance of
    /// about 99.7 %, whatever that distribution: the values at 1.5 √n places below and above the middle of the n sorted
    /// values, three standard deviations of the count of values below the median.
    /// </summary>
    private static SpanInterval MedianInterval(double[] values)
    {
        Array.Sort(values);
        int middle = values.Length / 2;
        int reach = (int)Math.Ceiling(1.5 * Math.Sqrt(values.Length));
        return new SpanInterval(Low: values[middle - reach], High: values[middle + reach]);
    }

    /// <summary>An interval of milliseconds that a pass adds to the frame's GPU span.</summary>
    private readonly record struct SpanInterval(double Low, double High);

    private sealed class EmptyPass : RenderPass
    {
        public override string Name => "empty";

        public override void Record(FrameContext context) { }
    }
}
