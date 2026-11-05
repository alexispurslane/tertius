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
/// The measurement that docs/plans/blue-danube-demo.md asks of the video ticket, under "Cost to measure" of change 1: 600
/// frames of the orbit scene, 2 ticks apart, at 1280x720 and at 1920x1080, into a video and into PNG files. It is an
/// explicit test, so a normal run reports it as not run. It runs in a Release build, with the variable
/// <see cref="Variable"/> set to the file of its report:
/// <c>TERTIUS_VIDEO_COST=report.md dotnet test -c Release --project tests/Tertius.Host.Tests -- --filter-class "*VideoCostMeasurement" --explicit only</c>
/// .
/// It drives the objects of a headless run as the command makes them, with validation off, and times them through a
/// renderer and a playback that forward every call.
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class VideoCostMeasurement
{
    public const string Variable = "TERTIUS_VIDEO_COST";

    private const int Frames = 600,
        Every = 2;

    [Fact(Explicit = true)]
    public void WritesItsReportWhereTheVariableNamesAFile()
    {
        string reportPath = Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path
            ? path
            : throw new InvalidOperationException($"Set {Variable} to the file of the report.");
        string folder = NewFolder();
        try
        {
            var report = new StringBuilder();
            report.AppendLine("# The cost of video and PNG captures").AppendLine();
            report
                .AppendLine(
                    $"{Frames} frames of the orbit scene, {Every} ticks apart, from the chase camera. Optimized build of Host: {IsOptimized()}. Validation off. Times are wall-clock means per frame."
                )
                .AppendLine();
            foreach ((int width, int height) in new[] { (1280, 720), (1920, 1080) })
            {
                string video = Path.Combine(folder, $"orbit-{width}x{height}.mkv");
                string pngs = Path.Combine(folder, $"orbit-{width}x{height}");
                Cost toVideo = Measure(width, height, new VideoFile(video, VideoFile.DefaultFramesPerSecond));
                Cost toPng = Measure(width, height, new PngFolder(pngs));
                (double encodeSeconds, double decodeSeconds) = EncodeAgain(
                    video: video,
                    width: width,
                    height: height,
                    folder: folder
                );
                long videoBytes = new FileInfo(video).Length;
                long pngBytes = Directory.EnumerateFiles(pngs, "*.png").Sum(file => new FileInfo(file).Length);
                Write(
                    report: report,
                    width: width,
                    height: height,
                    toVideo: toVideo,
                    toPng: toPng,
                    videoBytes: videoBytes,
                    pngBytes: pngBytes,
                    encode: encodeSeconds,
                    decode: decodeSeconds
                );
            }

            File.WriteAllText(reportPath, report.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static bool IsOptimized() =>
        typeof(ScriptCommand)
            .Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false)
            .OfType<DebuggableAttribute>()
            .All(a => !a.IsJITOptimizerDisabled);

    /// <summary>Wall-clock seconds over the whole run, summed over its frames.</summary>
    /// <param name="Total">From the start of the run to the return of the runner, without the scene's build.</param>
    /// <param name="Captures">Inside the playback's captures: the snapshot, the draw, its fence wait, the readback and the sink.</param>
    /// <param name="Sink">Inside the sink: the pipe write of a video, or the encode and the write of a PNG file.</param>
    /// <param name="Finish">The end of the run: ffmpeg encodes the frames still in its queue and writes the end of the file.</param>
    private sealed record Cost(double Total, double Captures, double Sink, double Finish)
    {
        public double Ticks => Total - Captures;
        public double Draw => Captures - Sink;
    }

    private static Cost Measure(int width, int height, CaptureTarget target)
    {
        var script = Script.Parse(
            $$"""
            { "scene": "orbit", "window": { "width": {{width}}, "height": {{height}} },
              "steps": [ { "do": "film", "name": "orbit", "frames": {{Frames}}, "every": {{Every}} } ] }
            """
        );
        // As the command makes them.
        using var world = new World(new() { DisposePhysics = true }, new JoltPhysicsBackend());
        using var vulkan = VulkanRenderer.CreateHeadless(
            new()
            {
                Width = width,
                Height = height,
                Impostors = false,
                Validation = false,
                Upscaling = new NoUpscaling(),
            }
        );
        var timed = new TimedRenderer(vulkan);
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "orbit").Build(world, vulkan);
        using var playback = new HeadlessPlayback(timed, script.Window, target);
        var watched = new TimedPlayback(playback);
        long start = Stopwatch.GetTimestamp();
        ScriptRunner.Run(script: script, world: world, scene: scene, outputFolder: null, playback: watched);
        long ran = Stopwatch.GetTimestamp();
        playback.Finish();
        long finished = Stopwatch.GetTimestamp();
        return new(
            Total: Seconds(ran - start),
            Captures: watched.Seconds,
            Sink: timed.SinkSeconds,
            Finish: Seconds(finished - ran)
        );
    }

    /// <summary>
    /// The seconds that ffmpeg takes to encode the frames of <paramref name="video"/> again, as the run encodes them, with
    /// the decode of the lossless video on its own thread in the same process. Then the seconds of the decode alone, which
    /// shows how much of the first time is the decode.
    /// </summary>
    private static (double Encode, double Decode) EncodeAgain(string video, int width, int height, string folder)
    {
        double encode = TimeFfmpeg(
            "-hide_banner",
            "-loglevel",
            "error",
            "-nostats",
            "-i",
            video,
            "-vf",
            "format=rgb0",
            "-c:v",
            "libx264rgb",
            "-qp",
            "0",
            "-preset",
            "veryfast",
            "-f",
            "matroska",
            "-y",
            Path.Combine(folder, $"again-{width}x{height}.mkv")
        );
        double decode = TimeFfmpeg("-hide_banner", "-loglevel", "error", "-nostats", "-i", video, "-f", "null", "-");
        return (encode, decode);
    }

    private static double TimeFfmpeg(params string[] arguments)
    {
        var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        long begin = Stopwatch.GetTimestamp();
        using Process ffmpeg = Process.Start(start)!;
        string errors = ffmpeg.StandardError.ReadToEnd();
        ffmpeg.WaitForExit();
        long end = Stopwatch.GetTimestamp();
        ffmpeg.ExitCode.Should().Be(0, errors);
        return Seconds(end - begin);
    }

    private static double Seconds(long ticks) => TimedRenderer.Seconds(ticks);

    private static void Write(
        StringBuilder report,
        int width,
        int height,
        Cost toVideo,
        Cost toPng,
        long videoBytes,
        long pngBytes,
        double encode,
        double decode
    )
    {
        static string Ms(double seconds) => (seconds / Frames * 1000).ToString("F2", CultureInfo.InvariantCulture);

        static string Minutes(double secondsPerFrame) =>
            (secondsPerFrame * 18_000 / 60).ToString("F1", CultureInfo.InvariantCulture);

        double videoPerFrame = (toVideo.Total + toVideo.Finish) / Frames,
            pngPerFrame = (toPng.Total + toPng.Finish) / Frames;
        report.AppendLine($"## {width}x{height}").AppendLine();
        report.AppendLine("| Per frame, ms | Video | PNG |");
        report.AppendLine("|---|---|---|");
        report.AppendLine(
            $"| Wall time, with the end of the run | {Ms(toVideo.Total + toVideo.Finish)} | {Ms(toPng.Total + toPng.Finish)} |"
        );
        report.AppendLine($"| Ticks | {Ms(toVideo.Ticks)} | {Ms(toPng.Ticks)} |");
        report.AppendLine($"| Capture's draw, fence wait and readback | {Ms(toVideo.Draw)} | {Ms(toPng.Draw)} |");
        report.AppendLine(
            $"| Sink: blocked in the pipe write, or the PNG encode and write | {Ms(toVideo.Sink)} | {Ms(toPng.Sink)} |"
        );
        report.AppendLine($"| End of the run, shared by all frames | {Ms(toVideo.Finish)} | {Ms(toPng.Finish)} |");
        report.AppendLine();
        report.AppendLine(
            $"- Bytes per frame: video {videoBytes / Frames:N0}, PNG {pngBytes / Frames:N0}. A raw frame is {width * height * 4:N0}."
        );
        report.AppendLine(
            $"- ffmpeg encodes the video again at {(Frames / encode).ToString("F0", CultureInfo.InvariantCulture)} frames per second, with the decode in the same process. The decode alone runs at {(Frames / decode).ToString("F0", CultureInfo.InvariantCulture)} frames per second."
        );
        report.AppendLine(
            $"- 18,000 frames (10 minutes at 30 frames per second) at these rates: video {Minutes(videoPerFrame)} minutes, PNG {Minutes(pngPerFrame)} minutes."
        );
        report.AppendLine();
    }
}
