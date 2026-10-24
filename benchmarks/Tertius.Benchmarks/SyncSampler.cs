using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Tertius.Benchmarks;

/// <summary>
/// ter-sn28 profiling aid: CPU sampling of the sync point in this process, with the runtime's EventPipe sample profiler
/// (the provider that BenchmarkDotNet's EventPipeProfiler uses, which refuses the in-process toolchain). It ticks a scene
/// for a while, then prints the 30 functions with the most exclusive samples among the samples whose stack is inside
/// World.Sync, and the 30 with the most inclusive samples.
/// </summary>
internal static class SyncSampler
{
    /// <summary>Set by the benchmark class that the scene built: the hash of its world's state.</summary>
    internal static Func<ulong>? StateHash;

    /// <summary>Set by the benchmark class that the scene built, so that the quick run reports only its own ticks.</summary>
    internal static Action? RemarkProfile;

    /// <summary>
    /// Builds a scene by name: kepler, keplerEntities1k or keplerEntities100k (each also with Jolt at the end), mixed, tick10k,
    /// tick100k, parts10k or parts0, then the gameplay parallelism, 1 or -1, such as "kepler1".
    /// </summary>
    private static (Action Tick, Action Cleanup) Scene(string scene)
    {
        int dop = scene.EndsWith("-1") ? -1 : 1;
        string name = scene[..^(dop == -1 ? 2 : 1)];
        switch (name)
        {
            case "kepler":
            {
                var b = new KeplerFrameTickBenchmarks { MaxDegreeOfParallelism = dop };
                b.Setup();
                return (b.Tick, b.Cleanup);
            }
            case "keplerEntities1k":
            case "keplerEntities100k":
            case "keplerEntities1kJolt":
            case "keplerEntities100kJolt":
            {
                var b = new KeplerEntityTickBenchmarks
                {
                    MaxDegreeOfParallelism = dop,
                    Entities = name.Contains("100k") ? 100_000 : 1_000,
                    Jolt = name.EndsWith("Jolt")
                };
                b.Setup();
                return (b.Tick, b.Cleanup);
            }
            case "mixed":
            {
                var b = new MixedBodiesTickBenchmarks { MaxDegreeOfParallelism = dop };
                b.Setup();
                return (b.Tick, b.Cleanup);
            }
            case "parts10k":
            case "parts0":
            {
                var b = new PartPushTickBenchmarks
                    { MaxDegreeOfParallelism = dop, PartsPerCrate = name == "parts10k" ? 4 : 0 };
                b.Setup();
                return (b.Tick, b.Cleanup);
            }
            case "tick10k":
            {
                var b = new WorldTickBenchmarks { MaxDegreeOfParallelism = dop, Entities = 10_000 };
                b.Setup();
                return (b.Tick, b.Cleanup);
            }
            case "tick100k":
            {
                var b = new WorldTickBenchmarks { MaxDegreeOfParallelism = dop, Entities = 100_000 };
                b.Setup();
                return (b.Tick, b.Cleanup);
            }
            default:
                throw new ArgumentException($"Unknown scene {scene}.");
        }
    }

    /// <summary>
    /// Ticks a scene for 3 s, then for <paramref name="seconds"/> more, and prints the mean tick time and the profile of the
    /// sync point over the second part (the cleanup prints it).
    /// </summary>
    public static void Quick(string scene, double seconds)
    {
        (Action tick, Action cleanup) = Scene(scene);
        var warm = Stopwatch.StartNew();
        while (warm.Elapsed.TotalSeconds < 3)
            tick();
        // The benchmark classes mark the profile at the end of their setup; this run reports from here on.
        RemarkProfile?.Invoke();
        var run = Stopwatch.StartNew();
        long ticks = 0;
        while (run.Elapsed.TotalSeconds < seconds)
        {
            tick();
            ticks++;
        }

        Console.WriteLine($"QUICK|{scene}|mean tick|{run.Elapsed.TotalMilliseconds * 1000 / ticks:F2}|{ticks}");
        cleanup();
    }

    /// <summary>Ticks a scene <paramref name="ticks"/> times after its setup and prints the hash of the state (see
    /// <see cref="WorldStateHash"/>).</summary>
    public static void Hash(string scene, int ticks)
    {
        (Action tick, Action cleanup) = Scene(scene);
        for (int i = 0; i < ticks; i++)
            tick();
        Console.WriteLine($"HASH|{scene}|{ticks}|{StateHash?.Invoke():X16}");
        cleanup();
    }

    public static void Run(string scene, double seconds, string outDir)
    {
        (Action tick, Action cleanup) = Scene(scene);
        // Warm up so that tiered compilation settles.
        var warm = Stopwatch.StartNew();
        while (warm.Elapsed.TotalSeconds < 5)
            tick();

        Directory.CreateDirectory(outDir);
        string tracePath = Path.Combine(outDir, $"{scene}.nettrace");
        var client = new DiagnosticsClient(Environment.ProcessId);
        var providers = new List<EventPipeProvider>
        {
            new(name: "Microsoft-DotNETCore-SampleProfiler", eventLevel: EventLevel.Informational),
            new(name: "Microsoft-Windows-DotNETRuntime", eventLevel: EventLevel.Informational,
                keywords: (long)0x10 /* JIT */ | 0x8 /* Loader */)
        };
        using (EventPipeSession session = client.StartEventPipeSession(providers, requestRundown: true))
        {
            Task copy = Task.Run(() =>
            {
                using FileStream file = File.Create(tracePath);
                session.EventStream.CopyTo(file);
            });
            var run = Stopwatch.StartNew();
            long ticks = 0;
            while (run.Elapsed.TotalSeconds < seconds)
            {
                tick();
                ticks++;
            }

            session.Stop();
            copy.Wait();
            Console.WriteLine($"SAMPLER|{scene}|ticks {ticks} in {run.Elapsed.TotalSeconds:F1} s");
        }

        cleanup();

        string etlx = TraceLog.CreateFromEventPipeDataFile(tracePath);
        using var log = new TraceLog(etlx);
        var exclusive = new Dictionary<string, int>();
        var inclusive = new Dictionary<string, int>();
        int inSync = 0;
        var seen = new HashSet<string>();
        foreach (TraceEvent e in log.Events)
        {
            if (e.ProviderName != "Microsoft-DotNETCore-SampleProfiler")
                continue;
            TraceCallStack? stack = e.CallStack();
            if (stack == null)
                continue;
            bool sync = false;
            for (TraceCallStack? s = stack; s != null; s = s.Caller)
                if (s.CodeAddress.FullMethodName.Contains("World.Sync("))
                {
                    sync = true;
                    break;
                }

            if (!sync)
                continue;
            inSync++;
            // The sampler stops a thread at a GC poll, so the frame of the poll is not where the time went.
            TraceCallStack top0 = stack;
            while (top0.Caller != null && (Name(top0).Contains("PollGC") || Name(top0).Contains("SyncProfile.Lap")))
                top0 = top0.Caller;
            string top = Name(top0);
            exclusive[top] = exclusive.GetValueOrDefault(top) + 1;
            seen.Clear();
            for (TraceCallStack? s = stack; s != null; s = s.Caller)
            {
                string name = Name(s);
                if (seen.Add(name))
                    inclusive[name] = inclusive.GetValueOrDefault(name) + 1;
                if (name.Contains("World.Sync("))
                    break;
            }
        }

        Console.WriteLine($"SAMPLER|{scene}|samples in World.Sync: {inSync}");
        foreach ((string name, int count) in exclusive.OrderByDescending(p => p.Value).Take(30))
            Console.WriteLine($"SAMPLER|{scene}|excl|{count}|{100.0 * count / inSync:F1}%|{name}");
        foreach ((string name, int count) in inclusive.OrderByDescending(p => p.Value).Take(40))
            Console.WriteLine($"SAMPLER|{scene}|incl|{count}|{100.0 * count / inSync:F1}%|{name}");
    }

    private static string Name(TraceCallStack s)
    {
        string name = s.CodeAddress.FullMethodName;
        if (string.IsNullOrEmpty(name))
            name = $"?{s.CodeAddress.ModuleName}";
        // Drop the parameter list, keep the type and method.
        int paren = name.IndexOf('(');
        return paren > 0 ? name[..paren] + "(" : name;
    }
}
