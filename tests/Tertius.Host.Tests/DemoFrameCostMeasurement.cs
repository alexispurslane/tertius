using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The measurement that docs/plans/blue-danube-demo.md asks of the demo ticket ter-2n31, under "Cost to measure" of change
/// 1: the headless frame time of the demo scene itself. It runs the checked-in scripts/blue-danube.json from its start
/// through the film <see cref="LastFilm"/>, the walk on the surface and the person at its seat in the cabin, into a video at
/// 1280x720 and 60 frames per second, as the command does. It reports, for each film, the means per frame of the ticks,
/// of the capture's build of the render snapshot with its draw, fence wait and readback, and of the video's pipe write. It is an explicit test, so a
/// normal run reports it as not run. It runs in a Release build, with the variable <see cref="Variable"/> set to the file of
/// its report:
/// <c>TERTIUS_DEMO_COST=report.md dotnet test -c Release --project tests/Tertius.Host.Tests -- --filter-class "*DemoFrameCostMeasurement" --explicit only</c>
/// .
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class DemoFrameCostMeasurement
{
    public const string Variable = "TERTIUS_DEMO_COST";

    /// <summary>The last film of the stretch that the measurement runs.</summary>
    public const string LastFilm = "seated";

    [Fact(Explicit = true)]
    public void WritesItsReportWhereTheVariableNamesAFile()
    {
        string reportPath = Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path
            ? path
            : throw new InvalidOperationException($"Set {Variable} to the file of the report.");
        Script script = Stretch(Script.Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json")), LastFilm);
        string folder = NewFolder();
        try
        {
            // As the command makes them.
            using var world = new World(new() { DisposePhysics = true }, new JoltPhysicsBackend());
            using var vulkan = VulkanRenderer.CreateHeadless(
                new()
                {
                    Width = script.Window.Width,
                    Height = script.Window.Height,
                    Impostors = false,
                    Validation = false,
                }
            );
            var timed = new TimedRenderer(vulkan);
            SceneInstance scene = DemoScenes.All.Single(s => s.Name == script.Scene).Build(world, vulkan);
            using var playback = new HeadlessPlayback(
                timed,
                script.Window,
                new VideoFile(Path.Combine(folder, "demo.mkv"), 60)
            );
            var watched = new TimedPlayback(playback, timed);
            long start = Stopwatch.GetTimestamp();
            ScriptRunner.Run(script: script, world: world, scene: scene, outputFolder: null, playback: watched);
            long ran = Stopwatch.GetTimestamp();
            playback.Finish();
            long finished = Stopwatch.GetTimestamp();
            File.WriteAllText(
                reportPath,
                Report(
                    script: script,
                    frames: watched.Frames,
                    run: TimedRenderer.Seconds(ran - start),
                    finish: TimedRenderer.Seconds(finished - ran)
                )
            );
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The steps of <paramref name="full"/> from its start through its film <paramref name="lastFilm"/>. Throws when the script has no such film.</summary>
    internal static Script Stretch(Script full, string lastFilm)
    {
        int last = full.Steps.ToList().FindIndex(step => step.Value is FilmStep film && film.Name == lastFilm);
        return last < 0
            ? throw new InvalidOperationException($"The script has no film '{lastFilm}' to end the measurement at.")
            : full with
            {
                Steps = [.. full.Steps.Take(last + 1)],
            };
    }

    /// <summary>
    /// The stretch of the measurement is the start of a script through the film that it names, and a name that is not a
    /// film of the script stops the measurement, where it would measure an empty script.
    /// </summary>
    [Fact]
    public void TheStretchEndsAtItsLastFilmAndRefusesAFilmThatTheScriptLacks()
    {
        var full = Script.Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json"));

        Script stretch = Stretch(full, LastFilm);
        Action missing = () => Stretch(full, "no-such-film");

        stretch.Steps[^1].Value.Should().BeOfType<FilmStep>().Which.Name.Should().Be(LastFilm);
        stretch.Steps.Should().HaveCountLessThan(full.Steps.Count);
        missing.Should().Throw<InvalidOperationException>().WithMessage("*no-such-film*");
    }

    private static string Report(Script script, List<CaptureTime> frames, double run, double finish)
    {
        static string Ms(double seconds) => (seconds * 1000).ToString("F2", CultureInfo.InvariantCulture);
        var report = new StringBuilder();
        report.AppendLine("# The frame time of the demo scene").AppendLine();
        report
            .AppendLine(
                $"Machine: {Machine()}, {Environment.ProcessorCount} logical processors, {RuntimeInformation.OSDescription}. Optimized build of Host: {IsOptimized()}. Validation off."
            )
            .AppendLine();
        report
            .AppendLine(
                $"scripts/blue-danube.json from its start through the film {LastFilm}, {frames.Count} frames at {script.Window.Width}x{script.Window.Height} into a video at 60 frames per second. Wall-clock means per frame, in ms."
            )
            .AppendLine();
        report.AppendLine(
            "| Film | Camera | Frames | Ticks per frame | Ticks | Snapshot, draw, fence wait and readback | Video's pipe write | All |"
        );
        report.AppendLine("|---|---|---|---|---|---|---|---|");
        string camera = "";
        foreach (ScriptStep step in script.Steps)
        {
            if (step.Value is CameraStep cameraStep)
                camera = cameraStep.Entity;
            if (step.Value is not FilmStep film)
                continue;
            List<CaptureTime> own = [.. frames.Where(frame => film.HasFrameNamed(frame.Name))];
            double ticks = own.Average(frame => frame.Ticks),
                draw = own.Average(frame => frame.Capture - frame.Sink),
                sink = own.Average(frame => frame.Sink);
            report.AppendLine(
                $"| {film.Name} | {camera} | {own.Count} | {film.Every} | {Ms(ticks)} | {Ms(draw)} | {Ms(sink)} | {Ms(ticks + draw + sink)} |"
            );
        }

        report.AppendLine();
        report.AppendLine(
            $"- The whole stretch: {Ms(run / frames.Count)} ms per frame, and the end of the video (`Finish`) {Ms(finish)} ms once."
        );
        return report.ToString();
    }

    private static bool IsOptimized() =>
        typeof(ScriptCommand)
            .Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false)
            .OfType<DebuggableAttribute>()
            .All(a => !a.IsJITOptimizerDisabled);

    /// <summary>The processor's name, from sysctl on macOS, or the architecture elsewhere.</summary>
    private static string Machine()
    {
        try
        {
            var start = new ProcessStartInfo("sysctl", "-n machdep.cpu.brand_string")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            using Process sysctl = Process.Start(start)!;
            string name = sysctl.StandardOutput.ReadToEnd().Trim();
            sysctl.WaitForExit();
            return sysctl.ExitCode == 0 && name.Length > 0 ? name : RuntimeInformation.ProcessArchitecture.ToString();
        }
        catch (Win32Exception)
        {
            return RuntimeInformation.ProcessArchitecture.ToString();
        }
    }
}
