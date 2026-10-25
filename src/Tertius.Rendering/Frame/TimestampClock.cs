namespace Tertius.Rendering;

/// <summary>
/// The clock of the timestamps that a queue writes: how many low bits of a timestamp count, and how many nanoseconds one
/// tick lasts. Vulkan gives the bits per queue family (<c>timestampValidBits</c>) and the period per device
/// (<c>timestampPeriod</c>).
/// </summary>
/// <param name="ValidBits">The bits of a timestamp that count: 0 for a queue that writes no timestamps, else 36 to 64.</param>
/// <param name="Period">The nanoseconds per tick.</param>
internal readonly record struct TimestampClock(uint ValidBits, float Period)
{
    /// <summary>True when the queue writes timestamps whose differences are times.</summary>
    public bool Supported => ValidBits > 0 && float.IsFinite(Period) && Period > 0;

    /// <summary>
    /// The time from the tick <paramref name="start"/> to the later tick <paramref name="end"/>, in milliseconds. The
    /// difference wraps at the valid bits, as the counter does.
    /// </summary>
    public double Milliseconds(ulong start, ulong end)
    {
        ulong mask = ValidBits >= 64 ? ulong.MaxValue : (1UL << (int)ValidBits) - 1;
        return ((end - start) & mask) * (double)Period / 1e6;
    }

    /// <summary>
    /// Writes the time of each pass into <paramref name="timings"/>: pass i takes the time from timestamp i to timestamp
    /// i + 1. Writes nothing and returns false when a result is not available, which is no timing, never a time of 0.
    /// </summary>
    /// <param name="results">A timestamp and its availability for each query: one before the first pass, one after each pass.</param>
    /// <param name="passes">The name of each pass, in recording order.</param>
    /// <param name="timings">One entry per pass.</param>
    public bool Fill(ReadOnlySpan<ulong> results, ReadOnlySpan<string> passes, Span<PassTiming> timings)
    {
        for (int query = 0; query <= passes.Length; query++)
            if (results[2 * query + 1] == 0)
                return false;
        for (int pass = 0; pass < passes.Length; pass++)
            timings[pass] = new(passes[pass], Milliseconds(results[2 * pass], results[2 * pass + 2]));
        return true;
    }
}
