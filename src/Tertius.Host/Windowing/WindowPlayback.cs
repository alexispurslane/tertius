using Tertius.Host.Scripting;
using Tertius.Simulation;

namespace Tertius.Host.Windowing;

/// <summary>
/// A run in the window, on the simulation thread: each tick waits for its start time on the wall clock, and a capture
/// waits while the render thread draws it.
/// </summary>
/// <param name="pacer">The wall-clock pace of the ticks.</param>
/// <param name="captures">The handover of captures to the render thread.</param>
/// <param name="stop">Fires when the window closes.</param>
/// <param name="secondsSinceStart">The wall clock: seconds since the run started.</param>
public sealed class WindowPlayback(TickPacer pacer, CaptureMailbox captures, CancellationToken stop, Func<double> secondsSinceStart) : IPlayback
{
    private readonly TickPacer pacer = pacer;
    private readonly CaptureMailbox captures = captures;
    private readonly CancellationToken stop = stop;
    private readonly Func<double> secondsSinceStart = secondsSinceStart;

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
            stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(-late));
        }
        return false;
    }

    // The ticks that this playback let run, and how far stalls moved the schedule, in wall-clock seconds.
    private long ticks;
    private double slip;

    /// <summary>Publishes the current state and waits until the render thread has saved it. Throws <see cref="OperationCanceledException"/> if the window closes first.</summary>
    public void Capture(World world, string path)
    {
        // The simulation thread publishes, and the render thread, the triple buffer's one reader, draws it.
        world.PublishRenderSnapshot();
        captures.RequestAndWait(path, stop);
    }
}
