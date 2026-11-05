namespace Tertius.Rendering.Tests;

/// <summary>
/// A capture in flight (<see cref="RendererOptions.CapturesInFlight"/>): its frame stays on the GPU while the renderer
/// draws the next ones, and its sink takes the pixels when the renderer draws the frame's slot again, or at
/// <see cref="VulkanRenderer.FinishCaptures"/>. The pixels and the readings that the sink sees are those of a capture
/// that waits for its frame.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class CapturesInFlightTests
{
    private const int Width = 320;
    private const int Height = 180;

    [Fact]
    public void ACaptureInFlightReachesItsSinkAtFinishCapturesWithThePixelsOfACaptureThatWaits()
    {
        Capture waited = CaptureOfTheFirstFrame(capturesInFlight: false);

        Capture inFlight = CaptureOfTheFirstFrame(capturesInFlight: true);

        inFlight
            .Rgba.Should()
            .Equal(waited.Rgba, "both draw the same frame, and only the time of the hand-over differs");
    }

    [Fact]
    public void ACaptureInFlightReachesItsSinkWhenTheRendererDrawsItsSlotAgain()
    {
        const int framesInFlight = 2;
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: framesInFlight,
            capturesInFlight: true
        );
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            var sink = new MemorySink();
            renderer.CaptureNextFrame(sink);

            renderer.Render(snapshot, alpha: 1f);
            sink.Takes.Should().Be(0, "the captured frame stays in flight after its Render");
            for (int frame = 0; frame < framesInFlight; frame++)
                renderer.Render(snapshot, alpha: 1f);

            sink.Takes.Should().Be(1, "a draw in the captured frame's slot waits for that frame first");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void FinishCapturesHandsOverEachCaptureInFlightOnceInTheOrderDrawn()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, framesInFlight: 3, capturesInFlight: true);
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            var order = new List<string>();
            renderer.CaptureNextFrame(new NamedSink("first", order));
            renderer.Render(snapshot, alpha: 1f);
            renderer.CaptureNextFrame(new NamedSink("second", order));
            renderer.Render(snapshot, alpha: 1f);
            order.Should().BeEmpty("three slots hold both frames in flight");

            renderer.FinishCaptures();
            renderer.FinishCaptures();

            order.Should().Equal(["first", "second"]);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void FinishCapturesOfARendererThatWaitsForEachCaptureHandsNothingOverAgain()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            var sink = new MemorySink();
            renderer.CaptureNextFrame(sink);
            renderer.Render(snapshot, alpha: 1f);

            renderer.FinishCaptures();

            sink.Takes.Should().Be(1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AtEachHandOverTheRenderersReadingsAreThoseThatACaptureThatWaitsSees()
    {
        (long? Timings, long? Exposure)[] waited = ReadingsAtEachHandOver(capturesInFlight: false);

        (long? Timings, long? Exposure)[] inFlight = ReadingsAtEachHandOver(capturesInFlight: true);

        inFlight.Should().Equal(waited, "the readings at a hand-over describe the captured frame");
        waited.Select(reading => reading.Exposure).Should().OnlyHaveUniqueItems("each capture reads its own frame");
    }

    /// <summary>The capture of the first frame of the reference scene, handed over by <see cref="VulkanRenderer.FinishCaptures"/>.</summary>
    private static Capture CaptureOfTheFirstFrame(bool capturesInFlight)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, capturesInFlight: capturesInFlight);
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            var sink = new MemorySink();
            renderer.CaptureNextFrame(sink);
            renderer.Render(snapshot, alpha: 1f);

            renderer.FinishCaptures();

            sink.Takes.Should().Be(1);
            return sink.Image!;
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The frames of the renderer's GPU timings and of its exposure reading at the hand-over of each of four captures, one
    /// per render.
    /// </summary>
    private static (long? Timings, long? Exposure)[] ReadingsAtEachHandOver(bool capturesInFlight)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, capturesInFlight: capturesInFlight);
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            var readings = new List<(long? Timings, long? Exposure)>();
            var sink = new ReadingSink(renderer, readings);
            for (int capture = 0; capture < 4; capture++)
            {
                renderer.CaptureNextFrame(sink);
                renderer.Render(snapshot, alpha: 1f);
            }

            renderer.FinishCaptures();
            return [.. readings];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A sink that adds its name to <paramref name="order"/> at each hand-over.</summary>
    private sealed class NamedSink(string name, List<string> order) : ICaptureSink
    {
        public void Take(in CapturedImage image) => order.Add(name);
    }

    /// <summary>A sink that adds the frames of the renderer's readings to <paramref name="readings"/> at each hand-over.</summary>
    private sealed class ReadingSink(VulkanRenderer renderer, List<(long? Timings, long? Exposure)> readings)
        : ICaptureSink
    {
        public void Take(in CapturedImage image) =>
            readings.Add((renderer.LastFrameStats.GpuTimings?.Frame, renderer.LastFrameStats.Exposure?.Frame));
    }
}
