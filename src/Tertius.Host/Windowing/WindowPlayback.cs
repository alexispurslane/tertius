using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;

namespace Tertius.Host.Windowing;

/// <summary>
/// A run in the window, on the simulation thread: each tick waits for its start time on the wall clock, and a capture
/// waits while the render thread draws it.
/// </summary>
/// <param name="pacer">The wall-clock pace of the ticks.</param>
/// <param name="captures">The handover of captures to the render thread.</param>
/// <param name="folder">Where the captures go, or null for free play, which captures nothing.</param>
/// <param name="secondsSinceStart">The wall clock: seconds since the run started.</param>
/// <param name="stop">Fires when the window closes.</param>
public sealed class WindowPlayback(
    TickPacer pacer,
    CaptureMailbox captures,
    PngFolder? folder,
    Func<double> secondsSinceStart,
    CancellationToken stop) : IPlayback
{
    private readonly TickPacer pacer = pacer;
    private readonly PngFolder? folder = folder;
    private readonly CaptureMailbox captures = captures;
    private readonly CancellationToken stop = stop;
    private readonly Func<double> secondsSinceStart = secondsSinceStart;

    // The ticks that this playback let run, and how far stalls moved the schedule, in wall-clock seconds.
    private long ticks;
    private double slip;

    /// <summary>
    /// Waits until the next tick's start time. A tick late by less than one tick runs at once and keeps the schedule. A
    /// tick that starts later than that, after a stall such as a capture or the first tick's compilation, moves the
    /// schedule by its delay, so the ticks after it keep their pace instead of rushing to catch up. No tick is skipped.
    /// Returns false once <c>stop</c> fires.
    /// </summary>
    public bool WaitForTick()
    {
        while (!stop.IsCancellationRequested)
        {
            double late = secondsSinceStart() - (pacer.StartOf(ticks) + slip);
            if (late >= 0)
            {
                if (late > pacer.SecondsPerTick)
                    slip += late;
                ticks++;
                return true;
            }

            // In slices of at most a second: the loop reads the clock again, and a very slow speed's wait fits a timeout.
            stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Min(-late, 1)));
        }

        return false;
    }

    /// <summary>
    /// Publishes the current state, hands the render thread a <see cref="PngCaptureSink"/> for the PNG file of
    /// <paramref name="name"/> in the folder, and waits until the render thread has drawn the state and the sink has
    /// written the file. Returns false if the window closes first.
    /// </summary>
    /// <exception cref="InvalidOperationException">The playback has no folder: free play captures nothing.</exception>
    public bool Capture(World world, string name)
    {
        if (folder is null)
            throw new InvalidOperationException(
                "Free play captures nothing, so its playback has no folder for captures.");
        // The simulation thread publishes, and the render thread, the triple buffer's one reader, draws it.
        world.PublishRenderSnapshot();
        try
        {
            captures.RequestAndWait(new PngCaptureSink(folder.PathOf(name)), stop);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
