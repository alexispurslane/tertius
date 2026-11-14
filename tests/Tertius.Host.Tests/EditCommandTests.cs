using System.Diagnostics;
using System.Globalization;
using Tertius.Demo;
using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo's <c>--edit</c> on the real ffmpeg of the dev shell (docs/plans/blue-danube-demo.md, "The final render"). The
/// master of these tests is 220 frames of 128 by 72 pixels, where every pixel of frame k has the gray level k. The script
/// films <c>a</c>, 100 frames of every tick, and <c>b</c>, 120 frames of every 4th tick. The cut list trims 30 frames from
/// the head of <c>a</c> and 10 from its tail, and its cards last 1 s each.
/// </summary>
public sealed class EditCommandTests : IDisposable
{
    private const int Width = 128,
        Height = 72,
        MasterFrames = 220;

    private static readonly Script Films = new(
        "scene",
        [new(new CameraStep("one")), new(new FilmStep("a", 100, 1)), new(new FilmStep("b", 120, 4))],
        new(Width, Height)
    );

    private static readonly TitleCard Card = new(1, [new("Tertius", 96)]);

    private static readonly CutList Cuts = new(
        Parts: [new("a", "Earth", "The walk.")],
        Trims: [new("a", Head: 30, Tail: 10)],
        Panels: [],
        Opening: Card,
        Closing: Card
    );

    private readonly string folder = HostTesting.NewFolder();

    public EditCommandTests()
    {
        File.WriteAllText(ScriptPath, Films.ToJson());
        File.WriteAllText(LogPath, ReadoutLog.ToCsv(RowsOf(Films)));
        MakeMaster(MasterPath, MasterFrames);
    }

    private string ScriptPath => Path.Combine(folder, "films.json");
    private string LogPath => Path.Combine(folder, "log.csv");
    private string MasterPath => Path.Combine(folder, "master.mkv");
    private string VideoPath => Path.Combine(folder, "edit.mp4");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static List<ReadoutRow> RowsOf(Script script) =>
        [
            .. ScriptFilm
                .Of(script)
                .SelectMany(film =>
                    Enumerable
                        .Range(0, film.Frames)
                        .Select(k => new ReadoutRow(
                            Frame: $"{film.Name}-{k:000000}",
                            Tick: film.FirstTick + (k + 1L) * film.Every,
                            Position: new(DemoScenes.EarthRadius, 0, 0),
                            Speed: 0,
                            ToStation: 1e6,
                            Axis: 0,
                            Felt: 9.8
                        ))
                ),
        ];

    /// <summary>A lossless video of <paramref name="frames"/> frames at 60 frames per second, whose frame k is gray level k.</summary>
    private static void MakeMaster(string path, int frames) =>
        Tool(
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-f",
            "lavfi",
            "-i",
            $"color=c=black:s={Width}x{Height}:r=60,format=rgb24,geq=r=N:g=N:b=N",
            "-frames:v",
            frames.ToString(CultureInfo.InvariantCulture),
            "-c:v",
            "libx264rgb",
            "-qp",
            "0",
            "-y",
            path
        );

    private static string Tool(string tool, params string[] arguments)
    {
        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, "{0} must run: {1}", tool, errors.Result);
        return output;
    }

    /// <summary>The kinds of the streams of the video, such as <c>video</c> and <c>audio</c>, with their durations in seconds.</summary>
    private static List<(string Kind, double Seconds)> Streams(string path) =>
        [
            .. Tool("ffprobe", "-v", "error", "-show_entries", "stream=codec_type,duration", "-of", "csv=p=0", path)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().Split(','))
                .Select(cells => (cells[0], double.Parse(cells[1], CultureInfo.InvariantCulture))),
        ];

    private int Edit(params string[] extra) => Edit(new(), new(), extra);

    private int Edit(StringWriter output, StringWriter error, params string[] extra) =>
        EditCommand.Run(
            args:
            [
                "--edit",
                VideoPath,
                "--script",
                ScriptPath,
                "--master",
                MasterPath,
                "--readouts",
                LogPath,
                .. extra,
            ],
            cuts: Cuts,
            output: output,
            error: error
        );

    [Fact]
    public void TheEditWritesTheOpeningCardTheKeptFramesAndTheClosingCardAt60FramesPerSecond()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exit = Edit(output, error);

        exit.Should().Be(ScriptCommand.Ok, error.ToString());
        // 60 frames of each card, and 60 of a and 120 of b.
        VideoTesting.CountFrames(VideoPath).Should().Be(60 + 60 + 120 + 60);
        VideoTesting.FrameRate(VideoPath).Should().Be("60/1");
        output.ToString().Trim().Should().Be($"{VideoPath}: 5.0 s");
    }

    [Fact]
    public void TheEditKeepsTheMasterFramesOfEachStretchInOrder()
    {
        Edit().Should().Be(ScriptCommand.Ok);

        List<byte[]> frames = VideoTesting.DecodeFrames(VideoPath, Width, Height);

        // Kept frame j comes after the 60 frames of the opening card. The film fades in over its first 30 frames and out
        // over its last 60, so the test reads frames between. Frame 40 of the film is frame 70 of the master, after the
        // trim of 30. Frames 80 and 110 are frames 20 and 50 of b, which starts at frame 100 of the master.
        int Center(int film) => frames[60 + film][(Height / 2 * Width + Width / 2) * 3];
        Center(40).Should().BeCloseTo(70, 3);
        Center(80).Should().BeCloseTo(120, 3);
        Center(110).Should().BeCloseTo(150, 3);
    }

    [Fact]
    public void TheEditHasNoSoundWithoutMusic()
    {
        Edit().Should().Be(ScriptCommand.Ok);

        Streams(VideoPath).Select(stream => stream.Kind).Should().Equal("video");
    }

    [Fact]
    public void TheEditAddsTheMusicForTheLengthOfTheVideo()
    {
        string music = Path.Combine(folder, "music.wav");
        Tool(
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-f",
            "lavfi",
            "-i",
            "sine=frequency=440:duration=20",
            "-y",
            music
        );

        Edit("--music", music, "--music-start", "3", "--music-credit", "A sine, public domain")
            .Should()
            .Be(ScriptCommand.Ok);

        List<(string Kind, double Seconds)> streams = Streams(VideoPath);
        streams.Select(stream => stream.Kind).Should().Equal("video", "audio");
        streams[1].Seconds.Should().BeApproximately(5.0, 0.05);
    }

    [Fact]
    public void TheClosingCardCreditsTheMusic()
    {
        string music = Path.Combine(folder, "music.wav");
        Tool(
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-f",
            "lavfi",
            "-i",
            "sine=frequency=440:duration=20",
            "-y",
            music
        );

        Edit("--music", music, "--music-credit", "A sine, public domain").Should().Be(ScriptCommand.Ok);

        File.ReadAllText(Path.Combine(VideoPath + ".parts", "closing.ass"))
            .Should()
            .Contain("Music: A sine, public domain");
    }

    /// <summary>The credit of the demo's recording, the longest line of any card.</summary>
    private const string RecordingCredit =
        "The Blue Danube (Johann Strauss II). United States Marine Band, Albert F. Schoepper, conductor. Public domain.";

    /// <summary>
    /// The card keeps its margins with the credit of the demo's recording, and with a credit twice as long, which is
    /// wider than the card in any font. The card's fonts come from macOS, so on Linux libass draws another font, whose
    /// line can be wider than the Mac's (ticket ter-j4h9). A line wider than the card wraps inside its margins.
    /// </summary>
    [Theory]
    [InlineData(RecordingCredit)]
    [InlineData(RecordingCredit + " " + RecordingCredit)]
    public void TheClosingCardShowsTheCreditOfTheRecordingInsideItsMargins(string credit)
    {
        string music = Path.Combine(folder, "music.wav");
        Tool(
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-f",
            "lavfi",
            "-i",
            "sine=frequency=440:duration=20",
            "-y",
            music
        );
        Edit("--music", music, "--music-credit", credit).Should().Be(ScriptCommand.Ok);
        string parts = VideoPath + ".parts",
            picture = Path.Combine(folder, "closing.rgb");

        // The closing card drawn by libass at 640 by 360, half the size of its drawing, at the middle of its 1 s.
        Tool(
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-f",
            "lavfi",
            "-i",
            "color=c=black:s=640x360:r=60:d=1",
            "-vf",
            $"ass=filename={Path.Combine(parts, "closing.ass")}:fontsdir={Path.Combine(parts, "fonts")},select=eq(n\\,30)",
            "-frames:v",
            "1",
            "-f",
            "rawvideo",
            "-pix_fmt",
            "rgb24",
            "-y",
            picture
        );
        byte[] rgb = File.ReadAllBytes(picture);

        // A card keeps 80 of the 1,280 pixels of its drawing on each side free: 40 of 640 here.
        int Brightest(int fromColumn, int toColumn) =>
            Enumerable
                .Range(0, 360)
                .SelectMany(y =>
                    Enumerable.Range(fromColumn, toColumn - fromColumn).Select(x => (int)rgb[(y * 640 + x) * 3])
                )
                .Max();

        Brightest(0, 40).Should().BeLessThan(16);
        Brightest(600, 640).Should().BeLessThan(16);
        Brightest(40, 600).Should().BeGreaterThan(128);
    }

    [Fact]
    public void TheEditWritesTheSubtitlesOfTheKeptFramesIntoAFolderBesideTheVideo()
    {
        Edit().Should().Be(ScriptCommand.Ok);

        string film = File.ReadAllText(Path.Combine(VideoPath + ".parts", "film.ass"));
        film.Split('\n')
            .Count(line =>
                line.StartsWith("Dialogue: 0,", StringComparison.Ordinal)
                && line.Contains(",Readout,", StringComparison.Ordinal)
            )
            .Should()
            .Be(180);
        File.Exists(Path.Combine(VideoPath + ".parts", "opening.ass")).Should().BeTrue();
    }

    [Fact]
    public void ACardWhoseSecondsFallBetweenTwoFramesLastsItsWholeFramesInTheVideo()
    {
        // 0.505 s is 30.3 frames. The card rounds it to 30 frames, so the video must hold 30 frames of the card, not 31.
        CutList cuts = Cuts with
        {
            Opening = Card with { Seconds = 0.505 },
        };

        EditCommand
            .Run(
                args: ["--edit", VideoPath, "--script", ScriptPath, "--master", MasterPath, "--readouts", LogPath],
                cuts: cuts,
                output: new StringWriter(),
                error: new StringWriter()
            )
            .Should()
            .Be(ScriptCommand.Ok);

        cuts.Opening.Frames.Should().Be(30);
        VideoTesting.CountFrames(VideoPath).Should().Be(cuts.Opening.Frames + 60 + 120 + cuts.Closing.Frames);
    }

    [Fact]
    public void TheEditRefusesALogOfAnotherScript()
    {
        File.WriteAllText(LogPath, ReadoutLog.ToCsv(RowsOf(Films)[..^1]));
        var error = new StringWriter();

        int exit = Edit(new(), error);

        exit.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain("219 rows");
        File.Exists(VideoPath).Should().BeFalse();
    }

    [Fact]
    public void TheEditRefusesAMasterWithAnotherCountOfFrames()
    {
        MakeMaster(MasterPath, MasterFrames - 1);
        var error = new StringWriter();

        int exit = Edit(new(), error);

        exit.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain("219 frames");
        File.Exists(VideoPath).Should().BeFalse();
    }

    [Fact]
    public void TheEditReportsAMissingMasterAsAFileError()
    {
        File.Delete(MasterPath);

        Edit().Should().Be(ScriptCommand.FileError);
        File.Exists(VideoPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("--edit")]
    [InlineData("--edit", "out.mp4", "--script", "s.json", "--master", "m.mkv")]
    [InlineData("--edit", "out.mkv", "--script", "s.json", "--master", "m.mkv", "--readouts", "l.csv")]
    [InlineData("--edit", "out.mp4", "--script", "s.json", "--master", "m.mkv", "--readouts", "l.csv", "--speed", "2")]
    [InlineData("--edit", "out.mp4", "--script", "s.json", "--master", "m.mkv", "--readouts", "l.csv", "--music")]
    [InlineData(
        "--edit",
        "out.mp4",
        "--script",
        "s.json",
        "--master",
        "m.mkv",
        "--readouts",
        "l.csv",
        "--music-start",
        "3"
    )]
    [InlineData(
        "--edit",
        "out.mp4",
        "--script",
        "s.json",
        "--master",
        "m.mkv",
        "--readouts",
        "l.csv",
        "--music",
        "a.wav",
        "--music-start",
        "-1"
    )]
    [InlineData(
        "--edit",
        "out.mp4",
        "--script",
        "s.json",
        "--script",
        "t.json",
        "--master",
        "m.mkv",
        "--readouts",
        "l.csv"
    )]
    public void TheCommandLineRefusesAWrongEdit(params string[] args)
    {
        var error = new StringWriter();

        int exit = EditCommand.Run(args: args, cuts: Cuts, output: new StringWriter(), error: error);

        exit.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain(EditCommand.Usage);
    }

    [Fact]
    public void TheDemosCommandLineSendsEditToTheEdit()
    {
        var error = new StringWriter();

        int exit = Program.Run(["--edit"], new StringWriter(), error);

        exit.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain(EditCommand.Usage);
    }
}
