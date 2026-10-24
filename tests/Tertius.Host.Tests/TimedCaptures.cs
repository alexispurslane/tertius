using System.Diagnostics;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>The real renderer of a headless run, with the time that its capture sinks take, summed and for the last capture.</summary>
internal sealed class TimedRenderer(VulkanRenderer inner) : IRenderer
{
    /// <summary>The wall-clock seconds inside every sink so far.</summary>
    public double SinkSeconds { get; private set; }

    /// <summary>The wall-clock seconds inside the sink of the last capture.</summary>
    public double LastSinkSeconds { get; private set; }

    public void CaptureNextFrame(ICaptureSink sink) => inner.CaptureNextFrame(new TimedSink(sink, this));

    public void Render(RenderSnapshot snapshot, float alpha) => inner.Render(snapshot, alpha);

    public void Resize(int width, int height) => inner.Resize(width, height);

    public MeshId CreateMesh(MeshData mesh) => inner.CreateMesh(mesh);

    public TextureId CreateTexture(TextureData texture) => inner.CreateTexture(texture);

    public MaterialId CreateMaterial(MaterialDesc material) => inner.CreateMaterial(material);

    public void Dispose() => inner.Dispose();

    public static double Seconds(long stopwatchTicks) => (double)stopwatchTicks / Stopwatch.Frequency;

    private sealed class TimedSink(ICaptureSink inner, TimedRenderer renderer) : ICaptureSink
    {
        public void Take(in CapturedImage image)
        {
            long start = Stopwatch.GetTimestamp();
            inner.Take(in image);
            renderer.LastSinkSeconds = Seconds(Stopwatch.GetTimestamp() - start);
            renderer.SinkSeconds += renderer.LastSinkSeconds;
        }
    }
}

/// <summary>The wall-clock seconds of one capture of a run.</summary>
/// <param name="Name">The capture's name.</param>
/// <param name="Ticks">From the end of the capture before it, or the start of the run, to the start of this one: the ticks and the steps between them.</param>
/// <param name="Capture">Inside the playback's capture: the snapshot, the draw, its fence wait, the readback and the sink.</param>
/// <param name="Sink">Inside the sink: the pipe write of a video, or the encode and the write of a PNG file.</param>
internal sealed record CaptureTime(string Name, double Ticks, double Capture, double Sink);

/// <summary>A playback that forwards every call to <paramref name="inner"/>, and times its captures and the ticks between them.</summary>
/// <param name="renderer">The timed renderer of the captures, for the time of each sink, or null to time the captures only.</param>
internal sealed class TimedPlayback(IPlayback inner, TimedRenderer? renderer = null) : IPlayback
{
    private long last = Stopwatch.GetTimestamp();

    /// <summary>The wall-clock seconds inside every capture so far.</summary>
    public double Seconds { get; private set; }

    /// <summary>The times of each capture, in order.</summary>
    public List<CaptureTime> Frames { get; } = [];

    public bool WaitForTick() => inner.WaitForTick();

    public bool Capture(World world, string name)
    {
        long start = Stopwatch.GetTimestamp();
        bool captured = inner.Capture(world, name);
        long end = Stopwatch.GetTimestamp();
        double capture = TimedRenderer.Seconds(end - start);
        Seconds += capture;
        Frames.Add(new(Name: name, Ticks: TimedRenderer.Seconds(start - last), Capture: capture,
            Sink: renderer?.LastSinkSeconds ?? 0));
        last = end;
        return captured;
    }
}
