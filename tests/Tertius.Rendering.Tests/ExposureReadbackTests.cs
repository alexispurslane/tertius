namespace Tertius.Rendering.Tests;

/// <summary>
/// The exposure in <see cref="RenderStats.Exposure"/> (docs/plans/auto-exposure.md, "Writers and readers"): each frame
/// copies the exposure that its passes use into its slot's readback buffer, and the renderer reads that copy after the
/// slot's fence. So a frame that is not captured reports the exposure of the frame that used its slot one round of
/// frames in flight before, and a captured frame, whose capture waits on its own fence, reports its own.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ExposureReadbackTests
{
    private const int Width = 160,
        Height = 90;

    [Fact]
    public void TheStatsReportTheExposureThatAFrameUsedFramesInFlightFramesLater()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 2,
            exposure: new FixedExposure(1.5f)
        );
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int frame = 0; frame <= 5; frame++)
                renderer.Render(snapshot, 1f);

            renderer
                .LastFrameStats.Exposure.Should()
                .Be(new ExposureReading(Frame: 3, Stops: 1.5f), "frame 5 waited on the slot of frame 3");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFirstFramesInFlightFramesReportNoExposure()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 3,
            exposure: new FixedExposure(1)
        );
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            var reported = new List<ExposureReading?>();
            for (int frame = 0; frame < 4; frame++)
            {
                renderer.Render(snapshot, 1f);
                reported.Add(renderer.LastFrameStats.Exposure);
            }

            reported
                .Should()
                .Equal(
                    [null, null, null, new ExposureReading(Frame: 0, Stops: 1)],
                    "no frame slot has been waited on again before frame 3"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACapturedFrameReportsItsOwnExposure()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 2,
            exposure: new FixedExposure(0.5f)
        );
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int frame = 0; frame < 3; frame++)
                renderer.Render(snapshot, 1f);
            Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "captured frame 3");

            renderer
                .LastFrameStats.Exposure.Should()
                .Be(new ExposureReading(Frame: 3, Stops: 0.5f), "the capture waited until frame 3 finished");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFrameAfterACapturedFrameKeepsTheCapturedFramesReadingOverAnOlderFrames()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 2,
            exposure: new FixedExposure(0.5f)
        );
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int frame = 0; frame < 3; frame++)
                renderer.Render(snapshot, 1f);
            Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "captured frame 3");
            renderer.Render(snapshot, 1f);

            renderer
                .LastFrameStats.Exposure.Should()
                .Be(
                    new ExposureReading(Frame: 3, Stops: 0.5f),
                    "frame 4 waited on the slot of frame 2, which finished before frame 3"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFrameThatTheOutputSkipsKeepsTheLastReading()
    {
        TestOutput? made = null;
        var renderer = VulkanRenderer.CreateOffscreen(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = false,
                Exposure = new FixedExposure(-2),
            },
            (device, allocator, extent) => made = new(device: device, allocator: allocator, extent: extent, bgra: false)
        );
        try
        {
            RenderSnapshot snapshot = TestScene.Create(renderer).Snapshot();
            for (int frame = 0; frame < 3; frame++)
                renderer.Render(snapshot, 1f);

            made!.FramesToSkip = 1;
            renderer.Render(snapshot, 1f);

            (renderer.LastFrameStats.Rendered, renderer.LastFrameStats.Exposure)
                .Should()
                .Be(
                    (false, new ExposureReading(Frame: 1, Stops: -2)),
                    "the skipped frame waited on the slot of frame 1 before it found no image"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
