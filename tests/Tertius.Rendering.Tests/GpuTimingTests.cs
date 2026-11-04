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

    /// <summary>A validating headless renderer that the test skips on a device without timestamps.</summary>
    private static VulkanRenderer TimedRenderer(
        int framesInFlight = 2,
        ScreenTraceSetting screenTrace = default,
        SubsurfaceSetting subsurface = default
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: framesInFlight,
            screenTrace: screenTrace,
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
    public void UnderTheScreenTracerItsPassesRunBetweenTheDirectPassAndTheComposite()
    {
        VulkanRenderer renderer = TimedRenderer(screenTrace: ScreenTrace.Default);
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);

            string[] passes = [.. renderer.LastFrameStats.GpuTimings!.Value.Passes.ToArray().Select(pass => pass.Pass)];
            passes
                .SkipWhile(pass => pass != "G-buffer")
                .Take(11)
                .Should()
                .Equal(
                    [
                        "G-buffer",
                        "surface test",
                        "depth pyramid",
                        "direct light",
                        "ray plan",
                        "reflection rays",
                        "diffuse rays",
                        "screen trace resolve",
                        "accumulate indirect diffuse",
                        "accumulate indirect specular",
                        "composite",
                    ],
                    "the rays read the pyramid and the direct light, the accumulations read the resolve's samples, and "
                        + "the composite reads the slow histories"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithNoScreenTraceTheFrameHasTheDirectPassAndTheCompositeAndNoTracerPass()
    {
        VulkanRenderer renderer = TimedRenderer(screenTrace: new NoScreenTrace());
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
                    "the composite takes the closed forms, so no pass sits between the direct pass and it"
                );
            passes.Should().NotContain(["depth pyramid", "ray plan", "screen trace resolve"]);
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
        // Every pass that a setting adds, each with the most passes it can add: the three SMAA passes and the tracer's
        // seven among them.
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
                Bloom = Bloom.Default,
                ScreenTrace = ScreenTrace.Default,
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

    [Fact(Skip = "Flaky in full Rendering runs, about half of them on 2026-11-03; skipped until ter-abun fixes it.")]
    public void APassThatRecordsNothingAddsNearlyNoTimeBetweenTheEndOfThePassBeforeItAndTheEndOfThePassAfterIt()
    {
        // With no screen tracer, the composite, a fragment pass, follows the direct pass in both renderers, so a stamp
        // after the direct pass that latches late waits on the same work in each, and no depth pyramid sits between the
        // G-buffer pass and the direct pass.
        VulkanRenderer without = TimedRenderer(screenTrace: new NoScreenTrace());
        VulkanRenderer with = TimedRenderer(screenTrace: new NoScreenTrace());
        try
        {
            // Between the G-buffer pass and the direct pass. On a driver whose stamps latch late, the empty pass's own
            // time can hold part of the next pass's work, and that pass's time then shows that much less, so only the time
            // from the G-buffer's stamp to the direct pass's stamp holds (ARCHITECTURE.md, "GPU timings").
            with.Graph.Add(FrameGraph.Stage.GBuffer, new EmptyPass());

            double[] fewest = FewestMilliseconds(FromTheGBufferToTheEndOfTheDirectPass, without, with);

            (fewest[1] - fewest[0])
                .Should()
                .BeLessThan(0.05, "the pass gave the GPU no work, only one more stamp to write");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(without);
            Gpu.DisposeAndAssertClean(with);
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
                .ContainSingle()
                .Which.Should()
                .BeLessThan(0.05, "the pass gave the GPU no work, and a mean over frames counts it as it is");
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
                // A fixed exposure and no tracer draw no warm-up draw, so each Render draws one frame.
                Exposure = new FixedExposure(0),
                ScreenTrace = new NoScreenTrace(),
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
    /// The fewest milliseconds that <paramref name="measure"/> reads from each renderer's timings over 64 frames of the
    /// test scene. The renderers draw in turn, so a load on the GPU from another process falls on each alike. Such a
    /// load stretches a pass's time in some frames, by the work of the other process that the GPU ran between two
    /// stamps, and the fewest is a frame that the load left alone (ARCHITECTURE.md, "GPU timings", gives the margin).
    /// </summary>
    private static double[] FewestMilliseconds(Func<PassTiming[], double> measure, params VulkanRenderer[] renderers)
    {
        RenderSnapshot[] snapshots = [.. renderers.Select(renderer => TestScene.Create(renderer).Snapshot())];
        double[] fewest = [.. renderers.Select(_ => double.MaxValue)];
        for (int frame = 0; frame < 64; frame++)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].Render(snapshots[i], 1f);
                if (renderers[i].LastFrameStats.GpuTimings is { } timings)
                    fewest[i] = Math.Min(fewest[i], measure(timings.Passes.ToArray()));
            }
        }

        return fewest;
    }

    /// <summary>
    /// The time from the G-buffer's stamp to the stamp of the direct pass: the passes after the G-buffer pass up to the
    /// direct pass.
    /// </summary>
    private static double FromTheGBufferToTheEndOfTheDirectPass(PassTiming[] passes)
    {
        int gBuffer = Array.FindIndex(passes, pass => pass.Pass == "G-buffer");
        int direct = Array.FindIndex(passes, pass => pass.Pass == "direct light");
        return passes[(gBuffer + 1)..(direct + 1)].Sum(pass => pass.Milliseconds);
    }

    private sealed class EmptyPass : RenderPass
    {
        public override string Name => "empty";

        public override void Record(FrameContext context) { }
    }
}
