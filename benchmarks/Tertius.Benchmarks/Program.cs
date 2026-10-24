using System.Globalization;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Tertius.Benchmarks;

public static class Program
{
    /// <summary>
    /// Runs every benchmark with a short job, or the ones that the BenchmarkDotNet arguments select
    /// (for example <c>--filter *EventQuery*</c>). Build and run in Release:
    /// <c>dotnet run -c Release --project benchmarks/Tertius.Benchmarks</c>.
    /// </summary>
    public static void Main(string[] args)
    {
        // ter-sn28 profiling aid: --sample <scene> <seconds> <out folder> samples the sync point (see SyncSampler).
        if (args.Length == 3 && args[0] == "--quick")
        {
            SyncSampler.Quick(args[1], double.Parse(args[2], CultureInfo.InvariantCulture));
            return;
        }

        if (args.Length == 3 && args[0] == "--hash")
        {
            SyncSampler.Hash(args[1], int.Parse(args[2], CultureInfo.InvariantCulture));
            return;
        }

        // ter-sn28: --rowcopy <rows> <rounds> times the copy of the rows that gameplay wrote (see RowCopyBenchmarks).
        if (args.Length == 3 && args[0] == "--rowcopy")
        {
            RowCopyBenchmarks.Run(
                int.Parse(args[1], CultureInfo.InvariantCulture),
                int.Parse(args[2], CultureInfo.InvariantCulture)
            );
            return;
        }

        // ter-fb3w: --edges <rounds> times the scenes of EdgeRemovalBenchmarks in turn, a tick of each per round.
        if (args.Length == 2 && args[0] == "--edges")
        {
            EdgeRemovalBenchmarks.Interleaved(int.Parse(args[1], CultureInfo.InvariantCulture));
            return;
        }

        if (args.Length == 4 && args[0] == "--sample")
        {
            SyncSampler.Run(args[1], double.Parse(args[2], CultureInfo.InvariantCulture), args[3]);
            return;
        }

        // In-process: the benchmarks run in this process on this SDK, so BenchmarkDotNet does not need to generate,
        // restore and build a separate project for a preview runtime.
        IConfig config = DefaultConfig
            .Instance.AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance))
            .AddDiagnoser(MemoryDiagnoser.Default)
            .WithArtifactsPath(Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts"));

        if (args.Length == 0)
            BenchmarkRunner.Run([typeof(EventQueryBenchmarks), typeof(WorldTickBenchmarks)], config);
        else
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
