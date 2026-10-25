namespace Tertius.Rendering;

/// <summary>A window's clock: the wall time of the last drawn frame, in s, or −∞ before the first.</summary>
internal readonly record struct WallClock(double LastSeconds);

/// <summary>A headless run's clock: the snapshot time of the last drawn frame, in s, or −∞ before the first.</summary>
internal readonly record struct FilmClock(double LastSeconds);

/// <summary>
/// The clock that steps the exposure's approach: the time between two drawn frames (docs/plans/auto-exposure.md,
/// "The clock"). A window reads the wall clock, and a headless run reads the snapshot's time.
/// </summary>
[GeneratedUnion(typeof(WallClock), typeof(FilmClock))]
internal readonly partial struct FrameClock
{
    /// <summary>The clock of a window renderer that has drawn no frame.</summary>
    public static FrameClock Window => new WallClock(double.NegativeInfinity);

    /// <summary>The clock of a headless renderer that has drawn no frame.</summary>
    public static FrameClock Film => new FilmClock(double.NegativeInfinity);

    /// <summary>
    /// The step from the last drawn frame to a frame at <paramref name="snapshotTime"/> and <paramref name="wallSeconds"/>,
    /// in s: +∞ before the first drawn frame, and 0 when the time did not move forward. And the clock whose last drawn
    /// frame is that frame.
    /// </summary>
    public (FrameClock Next, float Seconds) Step(double snapshotTime, double wallSeconds)
    {
        (FrameClock next, double last, double now) = this switch
        {
            WallClock(var lastSeconds) => (new FrameClock(new WallClock(wallSeconds)), lastSeconds, wallSeconds),
            FilmClock(var lastSeconds) => (new FrameClock(new FilmClock(snapshotTime)), lastSeconds, snapshotTime),
            null => throw new InvalidOperationException("A frame clock with no kind cannot step."),
        };
        double step = now - last;
        // A NaN fails the comparison too, so it steps by 0.
        return (next, step > 0 ? (float)step : 0);
    }
}
