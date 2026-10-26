using Tertius.Host.Scripting;
using Tertius.Rendering;
using static Tertius.Host.Tests.GpuTesting;
using static Tertius.Host.Tests.HostTesting;
using static Tertius.Host.Tests.VideoTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The command line with <c>--video</c>: a headless run whose captures go into one video, which the real ffmpeg of the
/// dev shell encodes, and the failures of ffmpeg. See docs/plans/blue-danube-demo.md, change 1.
/// </summary>
public sealed class ScriptCommandVideoTests : IDisposable
{
    private const int Width = 160,
        Height = 90;

    // A capture, a run, a capture and a film of three frames: five images of a box that falls.
    private const string FiveCaptures = """
        { "do": "capture", "name": "start" }, { "do": "run", "ticks": 20 }, { "do": "capture", "name": "falling" },
        { "do": "film", "name": "drop", "frames": 3, "every": 15 }
        """;

    private static readonly string[] FiveCaptureNames =
    [
        "start",
        "falling",
        "drop-000000",
        "drop-000001",
        "drop-000002",
    ];

    private readonly string folder = NewFolder();
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    private string Out => Path.Combine(folder, "out");

    private string Video => Path.Combine(folder, "video.mkv");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string ScriptFile(string steps, string scene = "lit-drop")
    {
        string path = Path.Combine(folder, "script.json");
        File.WriteAllText(
            path,
            $$"""{ "scene": "{{scene}}", "window": { "width": {{Width}}, "height": {{Height}} }, "steps": [ {{steps}} ] }"""
        );
        return path;
    }

    private int Run(string script, params string[] more) =>
        ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, .. more],
            scenes: [LitBoxScene, LitDropScene],
            output: output,
            error: error
        );

    [Fact]
    public void AVideoRunHoldsOneFrameForEachCaptureInTheOrderOfTheStepsPixelForPixelAsThePngCaptures()
    {
        string script = ScriptFile(FiveCaptures);
        string pngs = Path.Combine(folder, "pngs");
        ScriptCommand
            .Run(
                args: ["--script", script, "--headless", "--out", pngs],
                scenes: [LitDropScene],
                output: output,
                error: error
            )
            .Should()
            .Be(ScriptCommand.Ok, error.ToString());

        int code = Run(script, "--video", Video);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        List<byte[]> frames = DecodeFrames(Video, Width, Height);
        frames.Should().HaveCount(5);
        for (int i = 0; i < FiveCaptureNames.Length; i++)
            frames[i]
                .Should()
                .Equal(
                    Rgb(Path.Combine(pngs, FiveCaptureNames[i] + ".png")),
                    "frame {0} is the capture '{1}'",
                    i,
                    FiveCaptureNames[i]
                );
        frames[0].Should().NotEqual(frames[4], "the box falls, so the order of the frames shows");
        Directory.EnumerateFiles(Out, "*.png").Should().BeEmpty("the captures of a video run go into the video only");
    }

    [Fact]
    public void TwoVideoRunsOfOneScriptGiveTheSameDecodedFrames()
    {
        string script = ScriptFile(FiveCaptures);
        string second = Path.Combine(folder, "second.mkv");

        Run(script, "--video", Video).Should().Be(ScriptCommand.Ok, error.ToString());
        Run(script, "--video", second).Should().Be(ScriptCommand.Ok, error.ToString());

        List<byte[]> first = DecodeFrames(Video, Width, Height),
            again = DecodeFrames(second, Width, Height);
        again.Should().HaveCount(first.Count);
        for (int i = 0; i < first.Count; i++)
            again[i].Should().Equal(first[i], "frame {0}", i);
    }

    [Theory]
    [InlineData(null, "30/1")]
    [InlineData("24", "24/1")]
    public void TheVideoPlaysAtTheFrameRateOfTheCommandLine(string? fps, string rate)
    {
        string script = ScriptFile("""{ "do": "capture", "name": "only" }""");

        int code = Run(script, fps is null ? ["--video", Video] : ["--video", Video, "--fps", fps]);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        FrameRate(Video).Should().Be(rate);
    }

    [Fact]
    public void AVideoRunWritesThePathOfEachDumpAndThenThePathOfTheVideo()
    {
        string script = ScriptFile(
            """{ "do": "dump", "name": "start" }, { "do": "capture", "name": "only" }, { "do": "dump", "name": "end" }"""
        );

        int code = Run(script, "--video", Video);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        string[] lines = output.ToString().Split(Environment.NewLine);
        lines[..3].Should().Equal(Path.Combine(Out, "start.json"), Path.Combine(Out, "end.json"), Video);
        lines[3..]
            .Should()
            .Contain(
                Path.Combine(Out, ScriptCommand.ExposureFile),
                "the reports of the renderer follow the files of the run"
            );
        File.Exists(Path.Combine(Out, "end.json")).Should().BeTrue("the dumps still go into the output folder");
    }

    [Fact]
    public void WhenFfmpegCannotStartTheCommandExitsWithTheToolErrorCodeBeforeTheSceneBuildsAndWritesNothing()
    {
        string script = ScriptFile(
            """{ "do": "dump", "name": "start" }, { "do": "capture", "name": "only" }""",
            scene: "counted"
        );
        string video = Path.Combine(path1: folder, path2: "runs", path3: "new", path4: "video.mkv");
        int builds = 0;
        var counted = new Scene(
            "counted",
            (world, resources) =>
            {
                builds++;
                return LitDrop(world, resources);
            }
        );

        // The real start of the sink, with a program that is not on the search path, as in a shell without ffmpeg.
        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, "--video", video],
            scenes: [counted],
            output: output,
            error: error,
            createRenderer: VulkanRenderer.CreateHeadless,
            startVideo: (file, size) => FfmpegVideoSink.Start(file, size, "no-such-ffmpeg")
        );

        code.Should().Be(ScriptCommand.ToolError);
        error
            .ToString()
            .Should()
            .Contain("Cannot start ffmpeg")
            .And.Contain(video)
            .And.Contain("No such file or directory");
        builds.Should().Be(0, "the command starts ffmpeg before the scene builds");
        Directory
            .EnumerateFileSystemEntries(folder)
            .Select(Path.GetFileName)
            .Should()
            .Equal(
                ["script.json"],
                "no tick runs, so nothing is written: no folder of the video, no video, no output folder and no dump"
            );
    }

    [Fact]
    public void AVideoFolderThatCannotBeMadeExitsWithTheFileErrorCode()
    {
        string script = ScriptFile("""{ "do": "capture", "name": "only" }""");
        string taken = Path.Combine(folder, "taken");
        File.WriteAllText(taken, "a file, not a folder");

        int code = Run(script, "--video", Path.Combine(taken, "video.mkv"));

        code.Should().Be(ScriptCommand.FileError, error.ToString());
        error
            .ToString()
            .Should()
            .StartWith($"Cannot write the video {Path.Combine(taken, "video.mkv")} of {script}: ")
            .And.Contain("taken");
    }

    [Theory]
    // One small frame fits in the pipe, so ffmpeg's failure shows when the run finishes the video.
    [InlineData(1, 16, 16)]
    // Twenty frames of 57,600 bytes do not, so a write in the middle of the run meets the closed pipe.
    [InlineData(20, Width, Height)]
    public void WhenFfmpegFailsTheCommandExitsWithTheFileErrorCodeAndPrintsFfmpegsExitCodeAndItsLastErrorLines(
        int frames,
        int width,
        int height
    )
    {
        string path = Path.Combine(folder, "script.json");
        File.WriteAllText(
            path,
            $$"""{ "scene": "lit-drop", "window": { "width": {{width}}, "height": {{height}} }, "steps": [ { "do": "film", "name": "drop", "frames": {{frames}}, "every": 1 } ] }"""
        );
        // A folder at the path of the video: ffmpeg reads the first frame, cannot open its output, and exits.
        Directory.CreateDirectory(Video);

        int code = Run(path, "--video", Video);

        code.Should().Be(ScriptCommand.FileError);
        error
            .ToString()
            .Should()
            .StartWith(
                $"Cannot write the video of {path}: ffmpeg stopped with exit code",
                "the video is not in the output folder"
            )
            .And.Contain("Is a directory")
            .And.Contain(Video);
    }

    [Fact]
    public void ARunThatFailsAtAStepExitsWithTheCodeOfThatFailureAndLeavesAVideoWithTheFramesBeforeIt()
    {
        // The camera eye destroys itself during tick 3. Frame 0 comes after ticks 0 and 1, frame 1 after ticks 2 and 3.
        string script = ScriptFile("""{ "do": "film", "name": "drop", "frames": 4, "every": 2 }""", scene: "dying-eye");
        var dyingEye = new Scene(
            "dying-eye",
            (world, resources) =>
            {
                SceneInstance scene = LitDrop(world, resources);
                world.AddComponent(scene["eye"], new DestroysItselfOnTick(3));
                return scene;
            }
        );

        int code = ScriptCommand.Run(
            args: ["--script", script, "--headless", "--out", Out, "--video", Video],
            scenes: [dyingEye],
            output: output,
            error: error
        );

        code.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain("frame 1").And.Contain("not alive");
        CountFrames(Video).Should().Be(1, "the video ends after the frame before the failure, and ffprobe reads it");
    }

    [Fact]
    public void AScriptThatFailsBeforeTheFirstTickExitsWithTheScriptErrorCodeAndLeavesNoVideo()
    {
        string script = ScriptFile(
            """{ "do": "capture", "name": "only" }, { "do": "camera", "entity": "no-such-camera" }"""
        );

        int code = Run(script, "--video", Video);

        code.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain("no-such-camera");
        File.Exists(Video).Should().BeFalse("a video that holds no frame is deleted");
    }
}
