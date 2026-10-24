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
/// The measurement of the demo's plumes (docs/plans/blue-danube-demo.md, "The plumes"): the wall time of the master of the
/// checked-in scripts/blue-danube.json, headless at 1280x720 into a video at 60 frames per second, as the command makes
/// it, with the default puff capacity of 4,096 and with a capacity of 0, which turns puffs off. Each capacity runs once,
/// in that order, because one run takes minutes. The report gives both times, their difference per frame, the peak count
/// of live puffs over every tick and over the captures, with the film of each peak, and the mean count at a capture. It
/// is an explicit test, so a normal run reports it as not run. It runs in a Release build, with the variable
/// <see cref="Variable"/> set to the file of its report:
/// <c>TERTIUS_PLUME_COST=report.md dotnet test -c Release --project tests/Tertius.Host.Tests -- --filter-class "*BlueDanubePlumeCostMeasurement" --explicit only</c>
/// .
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class BlueDanubePlumeCostMeasurement
{
    public const string Variable = "TERTIUS_PLUME_COST";

    [Fact(Explicit = true)]
    public void WritesItsReportWhereTheVariableNamesAFile()
    {
        string reportPath = Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path
            ? path
            : throw new InvalidOperationException($"Set {Variable} to the file of the report.");
        var script = Script.Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json"));
        string folder = NewFolder();
        var report = new StringBuilder();
        CultureInfo inv = CultureInfo.InvariantCulture;
        try
        {
            using var vulkan = VulkanRenderer.CreateHeadless(
                new()
                {
                    Width = script.Window.Width,
                    Height = script.Window.Height,
                    Impostors = false,
                }
            );
            var seconds = new Dictionary<int, double>();
            foreach (int capacity in new[] { 4096, 0 })
            {
                using var world = new World(
                    new() { DisposePhysics = true, PuffCapacity = capacity },
                    new JoltPhysicsBackend()
                );
                SceneInstance scene = DemoScenes.All.Single(s => s.Name == script.Scene).Build(world, vulkan);
                using var headless = new HeadlessPlayback(
                    vulkan,
                    script.Window,
                    new VideoFile(Path.Combine(folder, $"master-{capacity}.mkv"), 60)
                );
                var counting = new CountingPlayback(world, headless);
                long start = Stopwatch.GetTimestamp();
                ScriptRunner.Run(script: script, world: world, scene: scene, outputFolder: null, playback: counting);
                headless.Finish();
                seconds[capacity] = Stopwatch.GetElapsedTime(start).TotalSeconds;
                report.AppendLine(
                    inv,
                    $"capacity {capacity}: {seconds[capacity]:F1} s for {counting.Captures} frames and {counting.Ticks} ticks; "
                        + $"peak {counting.PeakInTick} live puffs after a tick, in the film of {counting.PeakInTickFilm}; "
                        + $"peak {counting.PeakAtCapture} at a capture, at {counting.PeakCapture}; mean {counting.SumAtCaptures / Math.Max(1, counting.Captures):F0} at a capture; "
                        + $"{counting.CappedTicks} ticks at the capacity"
                );
            }

            double difference = seconds[4096] - seconds[0];
            int frames = script.Steps.Sum(step => step.Value is FilmStep film ? film.Frames : 0);
            report.AppendLine(
                inv,
                $"difference: {difference:F1} s in all, {difference / frames * 1e3:F2} ms per frame"
            );
            File.WriteAllText(reportPath, report.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A playback that counts the live puffs of <paramref name="world"/> before each tick and at each capture, and passes on to
    /// <paramref name="inner"/>.</summary>
    private sealed class CountingPlayback(World world, IPlayback inner) : IPlayback
    {
        private string lastCapture = "the start";

        public long Ticks { get; private set; }
        public int Captures { get; private set; }
        public int PeakInTick { get; private set; }
        public string PeakInTickFilm { get; private set; } = "";
        public int PeakAtCapture { get; private set; }
        public string PeakCapture { get; private set; } = "";
        public long SumAtCaptures { get; private set; }
        public long CappedTicks { get; private set; }

        public bool WaitForTick()
        {
            int count = world.Puffs.Count;
            if (count > PeakInTick)
            {
                PeakInTick = count;
                PeakInTickFilm = lastCapture;
            }

            if (count > 0 && count >= world.Options.PuffCapacity)
                CappedTicks++;
            Ticks++;
            return inner.WaitForTick();
        }

        public bool Capture(World world, string name)
        {
            int count = world.Puffs.Count;
            if (count > PeakAtCapture)
            {
                PeakAtCapture = count;
                PeakCapture = name;
            }

            SumAtCaptures += count;
            Captures++;
            lastCapture = name;
            return inner.Capture(world, name);
        }
    }
}
