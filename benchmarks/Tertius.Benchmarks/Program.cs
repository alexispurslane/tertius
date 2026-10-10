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
        // In-process: the benchmarks run in this process on this SDK, so BenchmarkDotNet does not need to generate,
        // restore and build a separate project for a preview runtime.
        IConfig config = DefaultConfig.Instance
            .AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance))
            .AddDiagnoser(MemoryDiagnoser.Default)
            .WithArtifactsPath(Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts"));

        if (args.Length == 0)
            BenchmarkRunner.Run([typeof(EventQueryBenchmarks), typeof(WorldTickBenchmarks)], config);
        else
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
