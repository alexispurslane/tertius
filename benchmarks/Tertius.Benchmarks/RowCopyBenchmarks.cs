using System.Diagnostics;
using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// ter-sn28: the measurement behind <see cref="EntityTable.DefaultParallelCopyThreshold"/>. It times step 8a of the sync
/// point, <see cref="EntityTable.CopyDirtyRows"/>, alone, on the sync thread and on all workers, for one count of rows that
/// gameplay wrote, and prints the median time of each. Run it in Release with
/// <c>dotnet run -c Release --project benchmarks/Tertius.Benchmarks -- --rowcopy &lt;rows&gt; &lt;rounds&gt;</c> for a few row
/// counts: the threshold is the smallest count at which the workers win clearly.
/// </summary>
/// <remarks>
/// As in a tick, gameplay writes the transforms on the workers, a chunk per work item, so the rows are spread over the
/// cores' caches, and a spin of 200 us stands for the serial sync steps between the gameplay loop and the copy, in which
/// idle workers start to sleep. A round times 40 copies of each variant after 10 that warm up, and takes their median;
/// the variants alternate in each round, and their order turns from round to round, so that a burst of load on a busy
/// machine falls on both. It prints the median, the smallest and the largest of the round medians.
/// </remarks>
internal static class RowCopyBenchmarks
{
    private const int ChunkSize = 64;
    private static readonly ParallelOptions AllWorkers = new() { MaxDegreeOfParallelism = -1 };

    /// <summary>A table with <see cref="Entities"/> live, which copies on the workers from one row up, or never.</summary>
    private sealed class Table
    {
        public readonly EntityTable Entities;
        public readonly Entity[] Live;
        private long tick;

        public Table(int rows, bool parallel)
        {
            Entities = new(ChunkSize, rows, parallel ? AllWorkers : null) { ParallelCopyThreshold = 1 };
            Live = new Entity[rows];
            for (int i = 0; i < rows; i++)
            {
                Live[i] = Entities.Reserve();
                Entities.Activate(
                    entity: Live[i],
                    row: new(),
                    render: new(),
                    components: [],
                    wants: [],
                    body: null,
                    bodyCenterOfMass: Vector3.Zero,
                    ownedFrame: FrameHandle.Null
                );
            }

            Entities.TakeMoved();
        }

        /// <summary>One tick in which gameplay writes every row; returns the time of the copy in us.</summary>
        public double Tick()
        {
            long now = ++tick;
            Entities.BeginTick(spawnPoolSize: 0);
            Parallel.For(
                fromInclusive: 0,
                toExclusive: (Live.Length + ChunkSize - 1) / ChunkSize,
                parallelOptions: AllWorkers,
                body: chunk =>
                {
                    for (int i = chunk * ChunkSize, end = Math.Min(Live.Length, i + ChunkSize); i < end; i++)
                        Entities.WritableTransform(Live[i]).Position = new(now, i, 0);
                }
            );
            Entities.EndGameplay();
            long spinEnd = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5000;
            while (Stopwatch.GetTimestamp() < spinEnd) { }

            long start = Stopwatch.GetTimestamp();
            Entities.CopyDirtyRows();
            long stop = Stopwatch.GetTimestamp();
            Entities.TakeMoved();
            Entities.SwapIntents(keep: false);
            Entities.EndTick();
            return (stop - start) * 1e6 / Stopwatch.Frequency;
        }
    }

    public static void Run(int rows, int rounds)
    {
        EngineEvents.Register();
        (string Name, Table Table)[] variants =
        [
            ("sync thread", new(rows, parallel: false)),
            ("all workers", new(rows, parallel: true)),
        ];
        List<double>[] medians = [.. variants.Select(_ => new List<double>())];
        double[] times = new double[40];
        for (int round = 0; round < rounds; round++)
        for (int k = 0; k < variants.Length; k++)
        {
            int v = (k + round) % variants.Length;
            for (int i = 0; i < 10; i++)
                variants[v].Table.Tick();
            for (int i = 0; i < times.Length; i++)
                times[i] = variants[v].Table.Tick();
            Array.Sort(times);
            medians[v].Add(times[times.Length / 2]);
        }

        for (int v = 0; v < variants.Length; v++)
        {
            List<double> m = medians[v];
            m.Sort();
            Console.WriteLine(
                $"ROWCOPY|{rows} rows|{variants[v].Name}|median {m[m.Count / 2]:F1} us|min {m[0]:F1}|max {m[^1]:F1}|{m.Count} rounds"
            );
        }
    }
}
