using Silk.NET.Vulkan;

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

    /// <summary>
    /// The bytes that the fill pass writes: 128 MiB. It added about 0.43 ms to the span on the development Mac (Apple M1
    /// Max), and a desktop GPU that writes 1 TB/s takes about 0.13 ms, still well above <see cref="NearlyNoTime"/>.
    /// </summary>
    private const ulong FillBytes = 128ul << 20;

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
                        "composite",
                    ],
                    "the occlusion passes read the finished G-buffer and the direct light, no pass of theirs reads the "
                        + "depth pyramid, and the composite reads their estimate target"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithNoScreenSpaceLightTheFrameHasTheDirectPassAndTheCompositeAndNoOcclusionPass()
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
                    ["G-buffer", "surface test", "direct light", "composite"],
                    "the composite reads the cleared estimate target"
                );
            passes.Should().NotContain(pass => pass.StartsWith("occlusion"));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void UnderSeparableSubsurfaceTheTwoScatterPassesRunRightAfterTheComposite()
    {
        VulkanRenderer renderer = TimedRenderer(subsurface: SeparableSubsurface.Default);
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);

            string[] passes = [.. renderer.LastFrameStats.GpuTimings!.Value.Passes.ToArray().Select(pass => pass.Pass)];
            passes
                .SkipWhile(pass => pass != "composite")
                .Take(3)
                .Should()
                .Equal(
                    ["composite", "scatter rows", "scatter columns"],
                    "the scatter blurs the diffuse light that the composite writes"
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
                .Where(pass => pass.Pass is "shadow maps" or "G-buffer" or "direct light" or "composite")
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

            SpanAddedBy(renderer, "empty")
                .High.Should()
                .BeLessThan(NearlyNoTime, "the pass gave the GPU no work, only one more stamp to write");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APassThatFillsABufferAddsMoreThanNearlyNoTimeToTheFramesGpuSpan()
    {
        VulkanRenderer renderer = TimedRenderer(framesInFlight: 1);
        try
        {
            renderer.Graph.Add(FrameGraph.Stage.GBuffer, new FillPass(renderer.Allocator, FillBytes));

            SpanAddedBy(renderer, "fill")
                .Low.Should()
                .BeGreaterThan(NearlyNoTime, "the span holds the work of every pass, and the fill is GPU work");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFirstNarrowChunkThatLiesWhollyOnOneSideOfTheBoundDecidesHowMuchAPassAddsToTheSpan()
    {
        SpanInterval[] chunks =
        [
            new(Low: -0.4, High: 0.03),
            new(Low: 0.03, High: 0.07),
            new(Low: 0.06, High: 0.12),
            new(Low: -0.02, High: 0.03),
        ];

        DecidingInterval(chunks)
            .Should()
            .Be(chunks[2], "the first chunk is too wide and the second lies across the bound, so neither decides");
    }

    [Fact]
    public void WhenNoChunkDecidesAChunkAtMostTheBoundWideThatLiesAcrossItConcludesThatThePassAddsAboutTheBound()
    {
        SpanInterval[] chunks = [new(Low: -0.4, High: 0.3), new(Low: 0.02, High: 0.09), new(Low: 0.03, High: 0.07)];

        SpanInterval added = DecidingInterval(chunks);

        added.Should().Be(chunks[2], "it is the narrowest chunk");
        added.Concludes.Should().BeTrue("a pass that adds about the bound must fail, not skip");
    }

    [Fact]
    public void AChunkWiderThanTheBoundThatLiesAcrossItConcludesNothing()
    {
        // The narrowest chunks of a run of the empty pass beside one demo render (2026-11-04).
        SpanInterval[] chunks = [new(Low: 0.0063, High: 0.0698), new(Low: -0.0001, High: 0.0881)];

        DecidingInterval(chunks)
            .Concludes.Should()
            .BeFalse("the work of another process can spread the pairs of a pass that adds less than the bound");
    }

    [Fact]
    public void ANarrowChunkThatLiesWhollyBelowTheBoundDecidesThatThePassAddsNearlyNoTime()
    {
        SpanInterval[] chunks = [new(Low: -0.02, High: 0.03), new(Low: 0.06, High: 0.12)];

        DecidingInterval(chunks).Should().Be(chunks[0], "it is the first chunk, and it lies wholly below the bound");
    }

    [Fact]
    public void AChunkMoreThanTwiceTheBoundWideDecidesNothingEvenWhenItLiesWhollyAboveTheBound()
    {
        // The work of another process stretches some frames, and a load can push a wide chunk's median either way
        // (ARCHITECTURE.md, "GPU timings").
        SpanInterval[] chunks = [new(Low: 0.06, High: 0.18), new(Low: 0.07, High: 0.11)];

        DecidingInterval(chunks).Should().Be(chunks[1], "the first chunk is 0.12 ms wide");
    }

    [Fact]
    public void TheIntervalOfAChunksMedianHoldsTheMedianOfASkewedDistributionInNearlyEveryChunk()
    {
        // An exponential distribution of mean 1, whose median is ln 2. It is skewed, as the spans are when the work of
        // another process stretches some frames.
        var random = new Random(17);
        double median = Math.Log(2);
        int held = 0;
        for (int chunk = 0; chunk < 1000; chunk++)
        {
            double[] values = [.. Enumerable.Range(0, ChunkPairs).Select(_ => -Math.Log(1 - random.NextDouble()))];
            SpanInterval interval = MedianInterval(values);
            if (interval.Low < median && median < interval.High)
                held++;
        }

        held.Should()
            .BeGreaterThanOrEqualTo(
                990,
                "the interval holds the median with a chance of about 99.7 %, whatever the distribution of the values"
            );
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
    public void TheRendererSaysThatItsStampsLatchLateOnMoltenVkAndOnNoOtherDriver()
    {
        // The Mac has two drivers, MoltenVK and KosmicKrisp, so the operating system does not name the driver. A device of
        // the test's own reads it.
        DriverId driver;
        using (var probe = VulkanDevice.Create(validation: false, surfaceExtensions: [], createSurface: null))
        {
            driver = probe.Driver;
        }

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            renderer
                .StampsLatchLate.Should()
                .Be(
                    driver == DriverId.Moltenvk,
                    "MoltenVK samples each stamp in an encoder of its own that the next pass's work can delay, the"
                        + " desktop drivers of Linux latch a stamp when the work before it ends, and KosmicKrisp's stamps"
                        + " read their encoder's start, early rather than late (ter-dgen)"
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
                // A fixed exposure draws no warm-up draw, so each Render draws one frame.
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
    /// How much the pass named <paramref name="pass"/> adds to the frame's GPU span, in milliseconds, as an interval of
    /// the median of the spans that it adds. It measures chunks of <see cref="ChunkPairs"/> pairs of frames
    /// (<see cref="AddedSpans"/>), at most <see cref="MaxChunks"/> of them, and returns the interval of the first chunk
    /// that <see cref="SpanInterval.Decides"/>. If no chunk does, it returns the narrowest interval, and when that one
    /// <see cref="SpanInterval.Concludes"/>, it lies across the bound: the pass adds about the bound, and a caller's
    /// assertion on either side fails. Otherwise the test skips, because the work of other processes stretched the frames
    /// too much to resolve the bound.
    /// <para>
    /// Only the span holds on every driver: on one whose stamps latch late, the stamp before the pass can latch after the
    /// pass's work, so a window that starts at that stamp can miss the work (ARCHITECTURE.md, "GPU timings"). The
    /// renderer must have one frame in flight, so no frame runs beside another on the GPU.
    /// </para>
    /// </summary>
    private static SpanInterval SpanAddedBy(VulkanRenderer renderer, string pass)
    {
        RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
        try
        {
            SpanInterval added = DecidingInterval(
                Enumerable.Range(0, MaxChunks).Select(_ => MedianInterval(AddedSpans(renderer, snapshot, pass)))
            );
            if (!added.Concludes)
            {
                Assert.Skip(
                    $"The work of other processes stretched the frames too much to resolve {NearlyNoTime} ms of their"
                        + $" spans: no interval of {MaxChunks} chunks decided, and the narrowest, {added}, is more than"
                        + " that wide."
                );
            }

            return added;
        }
        finally
        {
            renderer.Graph.Skipped.Remove(pass);
        }
    }

    /// <summary>
    /// The first of <paramref name="chunks"/> that <see cref="SpanInterval.Decides"/>, or the narrowest of them if none
    /// does. It reads no chunk after the one that decides, so a caller can measure each chunk on demand.
    /// </summary>
    private static SpanInterval DecidingInterval(IEnumerable<SpanInterval> chunks)
    {
        var narrowest = new SpanInterval(Low: double.NegativeInfinity, High: double.PositiveInfinity);
        foreach (SpanInterval interval in chunks)
        {
            if (interval.Decides)
                return interval;
            if (interval.Width < narrowest.Width)
                narrowest = interval;
        }

        return narrowest;
    }

    /// <summary>
    /// What the pass named <paramref name="pass"/> adds to the span of a frame, for <see cref="ChunkPairs"/> pairs of
    /// frames: the span of a frame that records the pass minus the span of a frame next to it in time that skips it. The
    /// frames alternate in runs of two of each kind, and a pair is the last frame of a run and the first frame of the
    /// next run. So both frames of a pair follow a frame of the same kind, and what a frame leaves behind for the next
    /// frame, such as the work of another process that it delayed or a raised clock, lands in both frames of the pair.
    /// The frames of a pair share the GPU's clock, which follows the GPU's duty cycle. Fails if the pairs do not form
    /// within <c>4 * ChunkPairs + 8</c> frames.
    /// </summary>
    private static double[] AddedSpans(VulkanRenderer renderer, RenderSnapshot snapshot, string pass)
    {
        const int maxFrames = 4 * ChunkPairs + 8;
        double[] added = new double[ChunkPairs];
        int pairs = 0;
        // The last two frames that the timer reported. The timings are those of an earlier frame, so the names of its
        // passes say whether it recorded the pass.
        ReportedFrame? older = null,
            newer = null;
        for (int frame = 0; pairs < ChunkPairs; frame++)
        {
            if (frame == maxFrames)
                Assert.Fail($"Only {pairs} of {ChunkPairs} pairs of frames formed in {maxFrames} frames.");
            if (frame % 4 < 2)
                renderer.Graph.Skipped.Add(pass);
            else
                renderer.Graph.Skipped.Remove(pass);
            renderer.Render(snapshot, 1f);
            if (renderer.LastFrameStats.GpuTimings is not { } timings)
                continue;

            var latest = new ReportedFrame(
                Frame: timings.Frame,
                RecordsThePass: timings.Passes.ToArray().Any(timing => timing.Pass == pass),
                SpanMilliseconds: timings.SpanMilliseconds
            );
            if (older is { } first && newer is { } last && latest.EndsARunThat(last, first))
            {
                added[pairs++] = latest.RecordsThePass
                    ? latest.SpanMilliseconds - last.SpanMilliseconds
                    : last.SpanMilliseconds - latest.SpanMilliseconds;
            }

            (older, newer) = (newer, latest);
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

    /// <summary>A frame whose timings the timer reported: its number, whether it recorded the measured pass, and its span.</summary>
    private readonly record struct ReportedFrame(long Frame, bool RecordsThePass, double SpanMilliseconds)
    {
        /// <summary>
        /// Whether this frame and <paramref name="last"/> form a pair of <see cref="AddedSpans"/>: three frames in a row,
        /// <paramref name="first"/>, <paramref name="last"/> and this one, where <paramref name="last"/> is of the same
        /// kind as <paramref name="first"/> and this one is of the other kind.
        /// </summary>
        public bool EndsARunThat(ReportedFrame last, ReportedFrame first) =>
            Frame == last.Frame + 1
            && last.Frame == first.Frame + 1
            && last.RecordsThePass == first.RecordsThePass
            && RecordsThePass != last.RecordsThePass;
    }

    /// <summary>An interval of milliseconds that a pass adds to the frame's GPU span.</summary>
    private readonly record struct SpanInterval(double Low, double High)
    {
        public double Width => High - Low;

        /// <summary>
        /// Whether the interval resolves <see cref="NearlyNoTime"/>: it is at most twice the bound wide, and it lies
        /// wholly below or wholly above the bound. A wider interval decides nothing. The work of another process stretches
        /// some frames, which widens the interval of their chunk, and a load that stretches nearly every frame can push the
        /// median of a pass that records work below the bound. Beside one demo render, the intervals of the empty pass
        /// were 0.03 to 0.9 ms wide. Beside three, more than 95 % of the frames were stretched, and the intervals were 0.2
        /// to 4 ms wide.
        /// </summary>
        public bool Decides => Width <= 2 * NearlyNoTime && (High < NearlyNoTime || Low > NearlyNoTime);

        /// <summary>
        /// Whether a test can conclude from the interval: it <see cref="Decides"/>, or it is at most
        /// <see cref="NearlyNoTime"/> wide. An interval that narrow lies across the bound only for a pass that adds about
        /// the bound. A wider interval across the bound can come from a load that spread the pairs of a pass that adds
        /// less. On the development Mac (2026-11-04), none of 9 intervals of the empty pass that were at most the bound
        /// wide lay across it, and 28 of 47 such intervals of fills of 4 KiB to 4 MiB did. Beside one demo render, 4
        /// intervals of the empty pass 0.06 to 0.09 ms wide lay across it.
        /// </summary>
        public bool Concludes => Decides || Width <= NearlyNoTime;
    }

    private sealed class EmptyPass : RenderPass
    {
        public override string Name => "empty";

        public override void Record(FrameContext context) { }
    }

    /// <summary>A pass that fills a buffer of GPU memory in every frame that records it: GPU work of a known size.</summary>
    private sealed class FillPass(DeviceAllocator allocator, ulong bytes) : RenderPass
    {
        private GpuBuffer? buffer;

        public override string Name => "fill";

        public override void Record(FrameContext context)
        {
            buffer ??= GpuBuffer.Create(
                device: context.Device,
                allocator: allocator,
                size: bytes,
                usage: BufferUsageFlags.TransferDstBit,
                memoryUsage: MemoryUsage.GpuOnly,
                name: "fill"
            );
            context.Barriers.Buffer(buffer, BufferUse.TransferDestination);
            context.Barriers.Flush(context.Vk, context.CommandBuffer);
            context.Vk.CmdFillBuffer(
                commandBuffer: context.CommandBuffer,
                dstBuffer: buffer.Handle,
                dstOffset: 0,
                size: bytes,
                data: 7
            );
        }

        public override void Dispose()
        {
            buffer?.Dispose();
            base.Dispose();
        }
    }
}
