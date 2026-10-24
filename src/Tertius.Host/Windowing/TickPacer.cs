namespace Tertius.Host.Windowing;

/// <summary>
/// The wall-clock pace of the ticks in the window: when each tick may start, and the interpolation alpha of the render
/// thread. A tick of <c>tickSeconds</c> of simulation time lasts <c>tickSeconds / speed</c> seconds of wall-clock time.
/// </summary>
public sealed class TickPacer
{
    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> for a tick length or a speed that is not positive and finite.</summary>
    public TickPacer(double tickSeconds, double speed)
    {
        if (!(double.IsFinite(tickSeconds) && tickSeconds > 0))
            throw new ArgumentOutOfRangeException(
                nameof(tickSeconds),
                tickSeconds,
                "The tick length must be positive and finite."
            );
        if (!(double.IsFinite(speed) && speed > 0))
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "The speed must be positive and finite.");
        SecondsPerTick = tickSeconds / speed;
    }

    /// <summary>Wall-clock seconds per tick.</summary>
    public double SecondsPerTick { get; }

    /// <summary>The wall-clock seconds after the start at which the tick with the 0-based <paramref name="index"/> may start.</summary>
    public double StartOf(long index) => index * SecondsPerTick;

    /// <summary>
    /// The interpolation alpha for a render snapshot published <paramref name="secondsSincePublish"/> ago: 0 draws the
    /// previous tick, 1 the newest. Clamped to 0 to 1.
    /// </summary>
    public float Alpha(double secondsSincePublish) => (float)Math.Clamp(secondsSincePublish / SecondsPerTick, 0, 1);
}
