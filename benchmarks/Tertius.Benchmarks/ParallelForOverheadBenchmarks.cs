using BenchmarkDotNet.Attributes;

namespace Tertius.Benchmarks;

/// <summary>
/// ter-sn28 profiling aid: what one Parallel.For costs on this machine with an empty body, over <see cref="Items"/>
/// items, to estimate the gain of a parallel step of the sync point. Back-to-back loops find the workers still spinning;
/// a real sync point step comes a few microseconds after the gameplay loop or another step.
/// </summary>
public class ParallelForOverheadBenchmarks
{
    private static readonly ParallelOptions Options = new() { MaxDegreeOfParallelism = -1 };
    private readonly Action<int> empty = _ => { };

    [Params(8, 64, 1024)] public int Items;

    [Benchmark]
    public void ParallelFor() =>
        Parallel.For(fromInclusive: 0, toExclusive: Items, parallelOptions: Options, body: empty);

    [Benchmark]
    public void SerialFor()
    {
        for (int i = 0; i < Items; i++)
            empty(i);
    }
}
