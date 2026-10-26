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
        ScreenSpaceLightSetting screenSpaceLight = default
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: framesInFlight,
            screenSpaceLight: screenSpaceLight
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
                .Take(7)
                .Should()
                .Equal(
                    [
                        "G-buffer",
                        "direct light",
                        "occlusion depth",
                        "occlusion sectors",
                        "occlusion denoise",
                        "occlusion upsample",
                        "ambient light",
                    ],
                    "the occlusion passes read the finished G-buffer, and the ambient pass reads their estimate target"
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
                .Take(3)
                .Should()
                .Equal(
                    ["G-buffer", "direct light", "ambient light"],
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
    public void APassThatRecordsNothingReportsTheTimeItTookWhichIsNearlyZero()
    {
        VulkanRenderer renderer = TimedRenderer();
        try
        {
            // After the G-buffer pass, the costliest pass of the scene, so a stamp that took the G-buffer's time into the
            // empty pass's would show.
            renderer.Graph.Add(FrameGraph.Stage.GBuffer, new EmptyPass());
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int i = 0; i < 3; i++)
                renderer.Render(snapshot, 1f);

            renderer
                .LastFrameStats.GpuTimings!.Value.Passes.ToArray()
                .Should()
                .ContainSingle(pass => pass.Pass == "empty")
                .Which.Milliseconds.Should()
                .BeLessThan(0.05, "the pass gave the GPU no work, and a mean over frames counts it as it is");
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

    private sealed class EmptyPass : RenderPass
    {
        public override string Name => "empty";

        public override void Record(FrameContext context) { }
    }
}
