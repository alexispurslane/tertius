using System.Diagnostics;

namespace Tertius.Physics.Tests;

/// <summary>
/// Frame systems that are created and disposed on many threads at once, as parallel tests and two backends created at
/// once do. joltc keeps one process-wide map from each Jolt physics system to its joltc wrapper, which every create and
/// every destroy changes. native/joltc-system-map-lock.patch guards it with one mutex. Without that patch, two inserts at
/// the same time corrupt the map, and a later create probes it forever or crashes the process (ter-7sep).
/// </summary>
public sealed class ConcurrentSystemCreationTests
{
    // With an unpatched library, 32 threads of 3,000 systems each hung or crashed in 9 of 15 runs, and 8 threads of 3,000
    // in none of 10 (2026-10-25).
    private const int Threads = 32;
    private const int SystemsPerThread = 3000;

    /// <summary>The time that all threads get to finish. A run on a patched library takes about a second.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    [Fact]
    public void FrameSystemsCreatedAndDisposedOnManyThreadsAtOnceAllFinish()
    {
        JoltRuntime.EnsureInitialized();
        // Small systems are quick to create, so the threads spend most of their time in joltc's map.
        var options = new JoltBackendOptions
        {
            MaxBodiesPerFrame = 16,
            MaxBodyPairsPerFrame = 16,
            MaxContactConstraintsPerFrame = 16,
        };
        int created = 0;
        using var start = new Barrier(Threads);
        Thread[] threads =
        [
            .. Enumerable
                .Range(0, Threads)
                .Select(_ => new Thread(() =>
                {
                    start.SignalAndWait();
                    for (int i = 0; i < SystemsPerThread; i++)
                    {
                        using var world = new FrameWorld(FrameHandle.Null, options, static _ => { });
                        Interlocked.Increment(ref created);
                    }
                })
                {
                    // A thread that hangs in joltc must not keep the test process alive after the run.
                    IsBackground = true,
                }),
        ];
        foreach (Thread thread in threads)
            thread.Start();
        var clock = Stopwatch.StartNew();
        bool finished = threads.All(thread => thread.Join(Max(Limit - clock.Elapsed, TimeSpan.Zero)));

        finished.Should().BeTrue("every create and destroy takes joltc's map lock, so no thread probes a corrupt map");
        created.Should().Be(Threads * SystemsPerThread);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
