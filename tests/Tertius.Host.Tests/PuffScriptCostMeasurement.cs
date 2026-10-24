using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The measurement of task 6 of docs/plans/particles.md: the wall time of the checked-in scripts/puff-yard.json, headless
/// at 1280x720 with its four captures into PNG files, as the command runs it, with the default puff capacity of 4,096 and
/// with a capacity of 0, which turns puffs off. The two capacities alternate over <see cref="Rounds"/> rounds after one
/// round of warm-up, and the report gives the median of each and their difference per tick. It is an explicit test, so a
/// normal run reports it as not run. It runs in a Release build, with the variable <see cref="Variable"/> set to the file
/// of its report:
/// <c>TERTIUS_PUFF_COST=report.md dotnet test -c Release --project tests/Tertius.Host.Tests -- --filter-class "*PuffScriptCostMeasurement" --explicit only</c>
/// .
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class PuffScriptCostMeasurement
{
    public const string Variable = "TERTIUS_PUFF_COST";

    /// <summary>The rounds that count, each with one run at each capacity.</summary>
    public const int Rounds = 7;

    [Fact(Explicit = true)]
    public void WritesItsReportWhereTheVariableNamesAFile()
    {
        string reportPath = Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path
            ? path
            : throw new InvalidOperationException($"Set {Variable} to the file of the report.");
        Script script = Script.Load(Path.Combine(RepositoryRoot(), "scripts", "puff-yard.json"));
        int ticks = script.Steps.Sum(step => step.Value is RunStep run ? run.Ticks : 0);
        string folder = NewFolder();
        try
        {
            using var vulkan = VulkanRenderer.CreateHeadless(new()
                { Width = script.Window.Width, Height = script.Window.Height, Impostors = false, Validation = false });
            var seconds = new Dictionary<int, List<double>> { [4096] = [], [0] = [] };
            var puffs = new Dictionary<int, int>();
            for (int round = 0; round <= Rounds; round++)
            {
                // The order alternates, so that a drift of the machine's load falls on both capacities alike.
                int[] order = round % 2 == 0 ? [4096, 0] : [0, 4096];
                foreach (int capacity in order)
                {
                    using var world = new World(new() { DisposePhysics = true, PuffCapacity = capacity },
                        new JoltPhysicsBackend());
                    SceneInstance scene = DemoScenes.All.Single(s => s.Name == script.Scene).Build(world, vulkan);
                    using var playback = new HeadlessPlayback(vulkan, script.Window, new PngFolder(folder));
                    long start = Stopwatch.GetTimestamp();
                    ScriptRunner.Run(script: script, world: world, scene: scene, outputFolder: null,
                        playback: playback);
                    double elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
                    if (round > 0)
                        seconds[capacity].Add(elapsed);
                    puffs[capacity] = world.Puffs.Count;
                }
            }

            File.WriteAllText(reportPath, Report(ticks, seconds, puffs));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Report(int ticks, Dictionary<int, List<double>> seconds, Dictionary<int, int> puffs)
    {
        static double Median(List<double> values)
        {
            double[] sorted = [.. values.Order()];
            return sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        }

        var report = new StringBuilder();
        CultureInfo inv = CultureInfo.InvariantCulture;
        report.AppendLine(inv,
            $"scripts/puff-yard.json, {ticks} ticks, 4 captures at 1280x720, {Rounds} rounds after one of warm-up.");
        foreach (int capacity in new[] { 4096, 0 })
            report.AppendLine(inv,
                $"capacity {capacity}: median {Median(seconds[capacity]) * 1e3:F1} ms, runs {string.Join(", ", seconds[capacity].Select(s => (s * 1e3).ToString("F1", inv)))} ms, {puffs[capacity]} live puffs at the end");
        double difference = Median(seconds[4096]) - Median(seconds[0]);
        report.AppendLine(inv,
            $"difference: {difference * 1e3:F1} ms in all, {difference / ticks * 1e6:F1} us per tick");
        return report.ToString();
    }
}
