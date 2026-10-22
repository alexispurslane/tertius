namespace Tertius.Testing;

/// <summary>
/// Measures what warm code allocates on the calling thread, for the tests that say a path allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// The runtime sometimes allocates a few kilobytes on the calling thread once, for its own work, and more often when the
/// machine is busy, as when tools/test-all.sh runs every module at once. A loop of 40 runs of the simulation tests beside
/// the physics tests found it in about one run in 20, always in one window only: 7,400 bytes, then 0 and 0. A hot method
/// that the runtime compiles again is one cause. It is not the only one, because the allocation also came with tiered
/// compilation turned off.
/// </para>
/// <para>
/// An allocation in the measured code itself happens in every window. So <see cref="Fewest"/> runs the window up to three
/// times and keeps the fewest bytes. A one-time allocation of the runtime then does not fail a test, and a path that
/// allocates every time still does.
/// </para>
/// </remarks>
public static class Allocations
{
    /// <summary>
    /// The bytes that one run of <paramref name="window"/> allocates on this thread: the fewest of up to three runs. It stops
    /// at the first run that allocates nothing. Warm up the path before, because a first call allocates in every window
    /// that it runs in.
    /// </summary>
    /// <param name="window">
    /// The code to measure. Create the delegate before the call, as a lambda argument does, so that its own allocation falls
    /// outside the windows. Each run must leave the path as warm as the run before it.
    /// </param>
    public static long Fewest(Action window)
    {
        long fewest = long.MaxValue;
        for (int run = 0; run < 3 && fewest != 0; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            window();
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return fewest;
    }
}
