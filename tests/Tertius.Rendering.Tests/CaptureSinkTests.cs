using System.Numerics;
using StbImageSharp;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A capture: <see cref="VulkanRenderer.CaptureNextFrame"/> hands a sink the pixels of the next frame that
/// <see cref="VulkanRenderer.Render"/> draws, once, inside that call, on its thread, in RGBA order whatever the order of
/// the output.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class CaptureSinkTests
{
    private const int Width = 320;
    private const int Height = 180;

    private static VulkanRenderer Offscreen(out TestOutput output, bool bgra = false, bool canCapture = true)
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
            },
            (device, allocator, extent) =>
                made = new(device: device, allocator: allocator, extent: extent, bgra: bgra) { CanCapture = canCapture }
        );
        output = made!;
        return renderer;
    }

    /// <summary>The reference scene, with its red sphere and its blue sphere, made in <paramref name="renderer"/>.</summary>
    private static RenderSnapshot Scene(VulkanRenderer renderer) => TestScene.Create(renderer).Snapshot();

    [Fact]
    public void ACaptureHandsItsSinkTheFrameThatRenderDrawsBeforeRenderReturns()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            var sink = new MemorySink();

            renderer.CaptureNextFrame(sink);
            sink.Takes.Should().Be(0, "nothing is drawn until Render");
            renderer.Render(snapshot, alpha: 1f);

            sink.Takes.Should().Be(1);
            sink.Image!.Width.Should().Be(Width);
            sink.Image.Height.Should().Be(Height);
            Vector3 red = sink.Image.Color(Gpu.Project(point: TestScene.RedSphere, width: Width, height: Height));
            red.X.Should().BeGreaterThan(2 * red.Z, "the red sphere of the drawn frame is in the pixels");
            red.X.Should().BeGreaterThan(2 * red.Y);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACaptureIsHandedOverOnceAndNotForTheFramesAfterIt()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            var sink = new MemorySink();
            renderer.CaptureNextFrame(sink);

            renderer.Render(snapshot, alpha: 1f);
            renderer.Render(snapshot, alpha: 1f);
            renderer.Render(snapshot, alpha: 1f);

            sink.Takes.Should().Be(1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheSinkTakesThePixelsOnTheThreadThatCalledRender()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            var sink = new MemorySink();
            renderer.CaptureNextFrame(sink);
            int renderThread = 0;

            var thread = new Thread(() =>
            {
                renderThread = Environment.CurrentManagedThreadId;
                renderer.Render(snapshot, alpha: 1f);
            });
            thread.Start();
            thread.Join();

            sink.Takes.Should().Be(1);
            renderThread.Should().NotBe(Environment.CurrentManagedThreadId);
            sink.ThreadId.Should().Be(renderThread);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACaptureWhoseFrameTheOutputSkippedWaitsForTheNextFrameThatDraws()
    {
        VulkanRenderer renderer = Offscreen(out TestOutput output);
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            var sink = new MemorySink();
            renderer.CaptureNextFrame(sink);
            output.FramesToSkip = 2;

            renderer.Render(snapshot, alpha: 1f);
            renderer.Render(snapshot, alpha: 1f);
            renderer.LastFrameStats.Rendered.Should().BeFalse();
            sink.Takes.Should().Be(0, "a skipped frame has no pixels to hand over");

            renderer.Render(snapshot, alpha: 1f);
            renderer.LastFrameStats.Rendered.Should().BeTrue();
            sink.Takes.Should().Be(1, "the request waited for the first frame that was drawn");
            sink.Image!.Width.Should().Be(Width);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ABgraOutputHandsItsSinkThePixelsOfAnRgbaOutputInRgbaOrder()
    {
        static Capture Draw(bool bgra)
        {
            VulkanRenderer renderer = Offscreen(out _, bgra);
            try
            {
                return Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: Scene(renderer),
                    name: bgra ? "the BGRA output" : "the RGBA output"
                );
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        Capture rgba = Draw(bgra: false);
        Capture bgra = Draw(bgra: true);

        Vector3 red = rgba.Color(Gpu.Project(point: TestScene.RedSphere, width: Width, height: Height));
        red.X.Should().BeGreaterThan(2 * red.Z, "the scene has a red sphere, so a missed swap of red and blue shows");
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            bgra.Color(x, y).Should().Be(rgba.Color(x, y), "pixel ({0}, {1}) is the same in both outputs", x, y);
    }

    [Fact]
    public void ThePngSinkWritesThePixelsThatTheRendererHandedOverWithEveryPixelOpaque()
    {
        string folder = Directory.CreateTempSubdirectory("tertius-capture-").FullName;
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            string path = Path.Combine(folder, "scene.png");
            var memory = new MemorySink();
            renderer.CaptureNextFrame(new FanOutSink(memory, new PngCaptureSink(path)));

            renderer.Render(snapshot, alpha: 1f);

            var png = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
            png.Width.Should().Be(Width);
            png.Height.Should().Be(Height);
            byte[] expected = [.. memory.Image!.Rgba];
            for (int i = 3; i < expected.Length; i += 4)
                expected[i] = 255;
            png.Data.Should().Equal(expected);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ACaptureAfterTheOutputShrankHandsOverOnlyThePixelsOfTheSmallerFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderSnapshot snapshot = Scene(renderer);
            Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "before-shrink");

            renderer.Resize(Width / 2, Height / 2);
            Capture small = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "after-shrink");

            small.Width.Should().Be(Width / 2);
            small.Height.Should().Be(Height / 2);
            small.Rgba.Length.Should().Be(Width / 2 * (Height / 2) * 4);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACaptureWithNoSinkIsRefused()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            FluentActions.Invoking(() => renderer.CaptureNextFrame(null!)).Should().Throw<ArgumentNullException>();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACaptureFromAnOutputWhoseImagesCannotBeReadBackIsRefused()
    {
        VulkanRenderer renderer = Offscreen(out _, canCapture: false);
        try
        {
            FluentActions
                .Invoking(() => renderer.CaptureNextFrame(new MemorySink()))
                .Should()
                .Throw<NotSupportedException>();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACaptureFromADisposedRendererIsRefused()
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        Gpu.DisposeAndAssertClean(renderer);

        FluentActions
            .Invoking(() => renderer.CaptureNextFrame(new MemorySink()))
            .Should()
            .Throw<ObjectDisposedException>();
    }
}
