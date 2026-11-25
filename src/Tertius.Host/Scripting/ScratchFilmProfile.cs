#pragma warning disable CA1001, CA1869, CA1305, CA1307, CA1310, CA1822, CA2000
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Tertius.Simulation;

namespace Tertius.Host.Scripting;

/// <summary>
/// SCRATCH (t88-hjjm): per film section, the wall time of the section and the differences of every
/// <see cref="ScratchProfile"/> span, of the sync point's SyncProfile steps, of the Jolt counters, of the garbage
/// collector and of the process's CPU time between the section's start and end. Also one CSV row per tick. The variable
/// TERTIUS_SCRATCH_PROFILE names the JSON file; the tick CSV goes beside it. Not for merge.
/// </summary>
internal sealed class ScratchFilmProfile
{
    private readonly World world;
    private readonly string path;
    private readonly StreamWriter ticks;
    private readonly List<Dictionary<string, object>> sections = [];
    private readonly List<double> tickMs = [];
    private readonly long[] spanStart = new long[ScratchProfile.Names.Length];
    private readonly long[] countStart = new long[ScratchProfile.Names.Length];
    private readonly long[] tickSpans = new long[ScratchProfile.Names.Length];
    private long[] syncStart = [];
    private long wallStart,
        updatedStart,
        skippedStart,
        activeStart,
        walkingStart,
        charactersStart,
        allocatedStart;
    private int gen0Start,
        gen1Start,
        gen2Start;
    private TimeSpan pauseStart,
        cpuStart;
    private string name = "";
    private int every,
        frames,
        section = -1;
    private double maxTickMs;
    private long maxTick;

    public ScratchFilmProfile(World world)
    {
        this.world = world;
        path = Environment.GetEnvironmentVariable("TERTIUS_SCRATCH_PROFILE") ?? "scratch-profile.json";
        ticks = new StreamWriter(Path.ChangeExtension(path, ".ticks.csv"), append: false);
        ticks.WriteLine("tick,section,tick_us,gameplay_us,physics_wait_us,sync_us,physics_step_us,jolt_us,characters_us,systems_updated,active_bodies");
    }

    private static double Ms(long stopwatchUnits) => stopwatchUnits * 1000.0 / Stopwatch.Frequency;

    public void Begin(string filmName, int filmEvery, int filmFrames)
    {
        name = filmName;
        every = filmEvery;
        frames = filmFrames;
        section++;
        tickMs.Clear();
        maxTickMs = 0;
        Array.Copy(ScratchProfile.Totals, spanStart, spanStart.Length);
        Array.Copy(ScratchProfile.Counts, countStart, countStart.Length);
        Array.Copy(ScratchProfile.Totals, tickSpans, tickSpans.Length);
        syncStart = (long[])world.ScratchSyncTotals.Clone();
        updatedStart = ScratchProfile.SystemsUpdated;
        skippedStart = ScratchProfile.SystemsSkipped;
        activeStart = ScratchProfile.ActiveRigidBodies;
        walkingStart = ScratchProfile.WalkingCharacters;
        charactersStart = ScratchProfile.CharactersUpdated;
        ScratchProfile.SlowestSystemUpdate = 0;
        ScratchProfile.SlowestSystemFrame = "";
        gen0Start = GC.CollectionCount(0);
        gen1Start = GC.CollectionCount(1);
        gen2Start = GC.CollectionCount(2);
        pauseStart = GC.GetTotalPauseDuration();
        allocatedStart = GC.GetTotalAllocatedBytes();
        using (var process = Process.GetCurrentProcess())
            cpuStart = process.TotalProcessorTime;
        wallStart = Stopwatch.GetTimestamp();
    }

    private long updatedTick,
        activeTick;

    public void TickTook(long stopwatchUnits, long tickNumber)
    {
        double ms = Ms(stopwatchUnits);
        tickMs.Add(ms);
        if (ms > maxTickMs)
        {
            maxTickMs = ms;
            maxTick = tickNumber;
        }

        long[] t = ScratchProfile.Totals;
        string Us(int span) => ((t[span] - tickSpans[span]) * 1e6 / Stopwatch.Frequency).ToString("F0", CultureInfo.InvariantCulture);
        ticks.Write(tickNumber.ToString(CultureInfo.InvariantCulture));
        ticks.Write(',');
        ticks.Write(section.ToString(CultureInfo.InvariantCulture));
        ticks.Write(',');
        ticks.Write((ms * 1000).ToString("F0", CultureInfo.InvariantCulture));
        foreach (int span in (int[])[ScratchProfile.Gameplay, ScratchProfile.PhysicsWait, ScratchProfile.Sync,
                     ScratchProfile.PhysicsStep, ScratchProfile.PhysicsSystems, ScratchProfile.PhysicsCharacters])
        {
            ticks.Write(',');
            ticks.Write(Us(span));
        }

        ticks.Write(',');
        ticks.Write((ScratchProfile.SystemsUpdated - updatedTick).ToString(CultureInfo.InvariantCulture));
        ticks.Write(',');
        ticks.Write((ScratchProfile.ActiveRigidBodies - activeTick).ToString(CultureInfo.InvariantCulture));
        ticks.WriteLine();
        updatedTick = ScratchProfile.SystemsUpdated;
        activeTick = ScratchProfile.ActiveRigidBodies;
        Array.Copy(t, tickSpans, tickSpans.Length);
    }

    public void End()
    {
        long wall = Stopwatch.GetTimestamp() - wallStart;
        TimeSpan cpu;
        using (var process = Process.GetCurrentProcess())
            cpu = process.TotalProcessorTime - cpuStart;
        double[] sorted = [.. tickMs.Order()];
        double Quantile(double q) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        var spans = new Dictionary<string, object>();
        for (int i = 0; i < ScratchProfile.Names.Length; i++)
            spans[ScratchProfile.Names[i]] = new Dictionary<string, object>
            {
                ["ms"] = Math.Round(Ms(ScratchProfile.Totals[i] - spanStart[i]), 3),
                ["count"] = ScratchProfile.Counts[i] - countStart[i],
            };
        long[] syncNow = world.ScratchSyncTotals;
        var sync = new Dictionary<string, object>();
        for (int i = 0; i < syncNow.Length; i++)
            sync[World.ScratchSyncNames[i]] = Math.Round(Ms(syncNow[i] - syncStart[i]), 3);
        int tickCount = tickMs.Count;
        var row = new Dictionary<string, object>
        {
            ["section"] = name,
            ["index"] = section,
            ["every"] = every,
            ["frames"] = frames,
            ["ticks"] = tickCount,
            ["wallMs"] = Math.Round(Ms(wall), 3),
            ["cpuMs"] = Math.Round(cpu.TotalMilliseconds, 3),
            ["tickMsP50"] = Math.Round(Quantile(0.50), 4),
            ["tickMsP95"] = Math.Round(Quantile(0.95), 4),
            ["tickMsP99"] = Math.Round(Quantile(0.99), 4),
            ["tickMsMax"] = Math.Round(maxTickMs, 4),
            ["tickOfMax"] = maxTick,
            ["spans"] = spans,
            ["syncSteps"] = sync,
            ["joltSystemsUpdated"] = ScratchProfile.SystemsUpdated - updatedStart,
            ["joltSystemsSkipped"] = ScratchProfile.SystemsSkipped - skippedStart,
            ["joltActiveRigidBodiesSum"] = ScratchProfile.ActiveRigidBodies - activeStart,
            ["joltWalkingCharactersSum"] = ScratchProfile.WalkingCharacters - walkingStart,
            ["charactersUpdated"] = ScratchProfile.CharactersUpdated - charactersStart,
            ["slowestJoltUpdateMs"] = Math.Round(Ms(ScratchProfile.SlowestSystemUpdate), 3),
            ["slowestJoltUpdate"] = ScratchProfile.SlowestSystemFrame,
            ["gcGen0"] = GC.CollectionCount(0) - gen0Start,
            ["gcGen1"] = GC.CollectionCount(1) - gen1Start,
            ["gcGen2"] = GC.CollectionCount(2) - gen2Start,
            ["gcPauseMs"] = Math.Round((GC.GetTotalPauseDuration() - pauseStart).TotalMilliseconds, 3),
            ["allocatedMB"] = Math.Round((GC.GetTotalAllocatedBytes() - allocatedStart) / 1e6, 3),
            ["unixEnd"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
        };
        sections.Add(row);
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"scratch-section {name} frames {frames} every {every} ticks {tickCount} wall {Ms(wall) / 1000:F1} s, {Ms(wall) / Math.Max(1, tickCount):F2} ms/tick, tick p50 {Quantile(0.5):F2} p95 {Quantile(0.95):F2} max {maxTickMs:F1} ms"
            )
        );
    }

    public void Write()
    {
        ticks.Dispose();
        var report = new Dictionary<string, object>
        {
            ["spanNames"] = ScratchProfile.Names,
            ["syncProfileCompiled"] = world.ScratchSyncTotals.Any(v => v != 0),
            ["processorCount"] = Environment.ProcessorCount,
            ["gcServer"] = System.Runtime.GCSettings.IsServerGC,
            ["sections"] = sections,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"scratch-profile {path}");
    }
}
