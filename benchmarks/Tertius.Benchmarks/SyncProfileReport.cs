using System.Diagnostics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// ter-sn28 profiling aid: prints the mean time of each step of the sync point per tick, and its share of the sync
/// point, over the ticks since <see cref="Mark"/>. Each line starts with "SYNCPROFILE|" so that a script can pick it out
/// of the log.
/// </summary>
internal sealed class SyncProfileReport
{
    private long[] totalsAtMark = [];
    private long syncsAtMark;

    public void Mark(World world)
    {
        totalsAtMark = (long[])world.Profile.Totals.Clone();
        syncsAtMark = world.SyncCount;
    }

    public void Print(World world, string scene)
    {
        long syncs = world.SyncCount - syncsAtMark;
        if (syncs == 0)
            return;
        long[] totals = world.Profile.Totals;
        double sum = 0;
        var means = new double[totals.Length];
        for (int i = 0; i < totals.Length; i++)
        {
            means[i] = (totals[i] - totalsAtMark[i]) * 1e6 / Stopwatch.Frequency / syncs;
            sum += means[i];
        }
        for (int i = 0; i < totals.Length; i++)
            Console.WriteLine($"SYNCPROFILE|{scene}|{SyncProfile.Names[i]}|{means[i]:F2}|{(sum == 0 ? 0 : 100 * means[i] / sum):F1}");
        Console.WriteLine($"SYNCPROFILE|{scene}|total over {syncs} ticks|{sum:F2}|100.0");
    }
}
