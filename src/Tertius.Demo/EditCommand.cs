using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Tertius.Host;
using Tertius.Host.Scripting;

namespace Tertius.Demo;

/// <summary>
/// <c>--edit</c>: turns the master video of a script into the final video (docs/plans/blue-danube-demo.md, "The edit"). It
/// keeps the stretches of the cut list, draws the readouts of the readout log on each frame, puts the title cards before and
/// after, adds the music, and encodes one MP4 with ffmpeg from the dev shell.
/// </summary>
public static class EditCommand
{
    public const string Usage =
        "Usage: --edit <out.mp4> --script <script> --master <master.mkv> --readouts <log.csv> [--music <file>] [--music-start <seconds>] [--music-credit <text>]";

    /// <summary>How long the music fades in at the start, in seconds.</summary>
    public const double MusicFadeIn = 2;

    /// <summary>How long the music fades out at the end of the video, in seconds.</summary>
    public const double MusicFadeOut = 5;

    /// <summary>How long a card fades in from black and out to black, and the film fades in, in seconds.</summary>
    public const double Fade = 0.5;

    /// <summary>How long the film fades out to black at its end, in seconds.</summary>
    public const double FilmFadeOut = 1;

    /// <summary>The fonts of the subtitles, from macOS. The edit links them into a folder of its own for libass.</summary>
    public static IReadOnlyList<string> Fonts { get; } =
    ["/System/Library/Fonts/Avenir Next.ttc", "/System/Library/Fonts/Menlo.ttc"];

    private sealed record Options(
        string Output,
        string Script,
        string Master,
        string Readouts,
        string? Music,
        double MusicStart,
        string? MusicCredit
    );

    /// <summary>
    /// Runs <c>--edit</c> with the cut list <paramref name="cuts"/>. Returns the exit code of <see cref="ScriptCommand"/>: 0;
    /// 1 for a wrong command line; 2 for a file that cannot be read or written, and for ffmpeg failing; 3 for a script, a
    /// log or a master that do not match each other or the cut list; and 5 when ffmpeg or ffprobe cannot start. Writes the
    /// path of the video and its length to <paramref name="output"/>.
    /// </summary>
    public static int Run(string[] args, CutList cuts, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(cuts);
        if (ReadOptions(args, error) is not { } options)
        {
            error.WriteLine(Usage);
            return ScriptCommand.CommandLineError;
        }

        IReadOnlyList<Stretch> stretches;
        IReadOnlyList<ReadoutRow> rows;
        try
        {
            IReadOnlyList<ScriptFilm> films = ScriptFilm.Of(Script.Load(options.Script));
            stretches = Stretch.Of(films, cuts.Trims);
            rows = ReadoutLog.FromCsv(File.ReadAllText(options.Readouts));
            ScriptFilm.CheckRows(films, rows);
        }
        catch (Exception ex) when (ex is ScriptException or ArgumentException or FormatException)
        {
            error.WriteLine($"The edit cannot cut this script: {ex.Message}");
            return ScriptCommand.ScriptError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"The edit cannot read its files: {ex.Message}");
            return ScriptCommand.FileError;
        }

        string parts = options.Output + ".parts";
        int width,
            height;
        try
        {
            if (!File.Exists(options.Master))
            {
                error.WriteLine($"Cannot read the master {options.Master}: the file does not exist.");
                return ScriptCommand.FileError;
            }

            (int frames, width, height) = ProbeMaster(options.Master);
            if (frames != rows.Count)
            {
                error.WriteLine($"The master {options.Master} has {frames} frames, and the script has {rows.Count}.");
                return ScriptCommand.ScriptError;
            }

            string film;
            try
            {
                film = EditSubtitles.Film(stretches, rows, cuts);
            }
            catch (ArgumentException ex)
            {
                error.WriteLine($"The edit cannot cut this script: {ex.Message}");
                return ScriptCommand.ScriptError;
            }

            WriteParts(parts: parts, film: film, cuts: cuts, options: options);
        }
        catch (Win32Exception ex)
        {
            error.WriteLine($"ffprobe cannot start: {ex.Message}");
            return ScriptCommand.ToolError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"The edit cannot write its parts in {parts}: {ex.Message}");
            return ScriptCommand.FileError;
        }

        List<string> arguments = Arguments(
            stretches: stretches,
            cuts: cuts,
            master: options.Master,
            music: options.Music,
            musicStart: options.MusicStart,
            output: options.Output,
            width: width,
            height: height
        );
        try
        {
            (int exit, string errors) = RunTool(tool: "ffmpeg", arguments: arguments, folder: parts);
            if (exit != 0)
            {
                error.WriteLine($"ffmpeg exited with code {exit}: {errors}");
                return ScriptCommand.FileError;
            }
        }
        catch (Win32Exception ex)
        {
            error.WriteLine($"ffmpeg cannot start: {ex.Message}");
            return ScriptCommand.ToolError;
        }

        int videoFrames = cuts.Opening.Frames + stretches.Sum(s => s.Count) + cuts.Closing.Frames;
        output.WriteLine(
            string.Format(
                provider: CultureInfo.InvariantCulture,
                format: "{0}: {1:F1} s",
                arg0: options.Output,
                arg1: videoFrames / (double)Stretch.FramesPerSecond
            )
        );
        return ScriptCommand.Ok;
    }

    /// <summary>The options of the command line, or null after a message for a wrong one.</summary>
    private static Options? ReadOptions(string[] args, TextWriter error)
    {
        if (args is not ["--edit", string output, .. var rest] || output.StartsWith("--", StringComparison.Ordinal))
        {
            error.WriteLine("--edit needs the video to write first.");
            return null;
        }

        var values = new Dictionary<string, string>();
        for (int i = 0; i < rest.Length; i += 2)
        {
            string key = rest[i];
            if (
                key
                    is not (
                        "--script"
                        or "--master"
                        or "--readouts"
                        or "--music"
                        or "--music-start"
                        or "--music-credit"
                    )
                || i + 1 >= rest.Length
                || values.ContainsKey(key)
            )
            {
                error.WriteLine($"Unknown, repeated or empty option '{key}'.");
                return null;
            }

            values[key] = rest[i + 1];
        }

        foreach (string required in (string[])["--script", "--master", "--readouts"])
            if (!values.ContainsKey(required))
            {
                error.WriteLine($"--edit needs {required}.");
                return null;
            }

        if (!output.EndsWith(".mp4", StringComparison.Ordinal))
        {
            error.WriteLine("The video of --edit must end in .mp4.");
            return null;
        }

        double start = 0;
        if (
            values.TryGetValue("--music-start", out string? startText)
            && !(
                double.TryParse(
                    s: startText,
                    style: NumberStyles.Float,
                    provider: CultureInfo.InvariantCulture,
                    result: out start
                )
                && start >= 0
            )
        )
        {
            error.WriteLine("--music-start must be a number of seconds, 0 or more.");
            return null;
        }

        string? music = values.GetValueOrDefault("--music");
        if (music == null && (values.ContainsKey("--music-start") || values.ContainsKey("--music-credit")))
        {
            error.WriteLine("--music-start and --music-credit need --music.");
            return null;
        }

        return new(
            Output: Path.GetFullPath(output),
            Script: Path.GetFullPath(values["--script"]),
            Master: Path.GetFullPath(values["--master"]),
            Readouts: Path.GetFullPath(values["--readouts"]),
            Music: music == null ? null : Path.GetFullPath(music),
            MusicStart: start,
            MusicCredit: values.GetValueOrDefault("--music-credit")
        );
    }

    /// <summary>Writes the subtitle files and the folder of fonts into <paramref name="parts"/>.</summary>
    private static void WriteParts(string parts, string film, CutList cuts, Options options)
    {
        Directory.CreateDirectory(parts);
        File.WriteAllText(Path.Combine(parts, "film.ass"), film);
        File.WriteAllText(Path.Combine(parts, "opening.ass"), EditSubtitles.Card(cuts.Opening));
        TitleCard closing =
            options.Music == null
                ? cuts.Closing
                : cuts.Closing with
                {
                    Lines =
                    [
                        .. cuts.Closing.Lines,
                        new("Music: " + (options.MusicCredit ?? Path.GetFileName(options.Music)), 24, Gray: true),
                    ],
                };
        File.WriteAllText(Path.Combine(parts, "closing.ass"), EditSubtitles.Card(closing));
        string fonts = Path.Combine(parts, "fonts");
        Directory.CreateDirectory(fonts);
        foreach (string font in Fonts.Where(File.Exists))
        {
            string link = Path.Combine(fonts, Path.GetFileName(font));
            if (!File.Exists(link))
                File.CreateSymbolicLink(link, font);
        }
    }

    /// <summary>
    /// The arguments of the ffmpeg command, which runs in the folder of the parts. The master's frame k gets the time k /
    /// 60 s, the subtitles, and then the selection of the kept frames. The cards are black frames with their subtitles.
    /// The cards and the film fade from and to black, and the three join in order.
    /// </summary>
    public static List<string> Arguments(
        IReadOnlyList<Stretch> stretches,
        CutList cuts,
        string master,
        string? music,
        double musicStart,
        string output,
        int width,
        int height
    )
    {
        double film = stretches.Sum(s => s.Count) / (double)Stretch.FramesPerSecond;
        // Each card lasts its whole frames, as its subtitles count them. For a time between two frames, ffmpeg would make
        // one frame more than the rounding.
        double opening = cuts.Opening.Frames / (double)Stretch.FramesPerSecond,
            closing = cuts.Closing.Frames / (double)Stretch.FramesPerSecond;
        double length = opening + film + closing;
        string select = string.Join("+", stretches.Select(s => $"between(n,{s.From},{s.From + s.Count - 1})"));
        CultureInfo invariant = CultureInfo.InvariantCulture;

        string Card(string name, double seconds) =>
            string.Format(
                invariant,
                "color=c=black:s={0}x{1}:r=60:d={2},settb=1/60,setpts=N,ass={3}.ass:fontsdir=fonts,fade=t=in:st=0:d={4},fade=t=out:st={5}:d={4},format=yuv420p,setsar=1[{3}]",
                width,
                height,
                seconds,
                name,
                Fade,
                seconds - Fade
            );

        string graph = string.Join(
            ";",
            string.Format(
                invariant,
                "[0:v]settb=1/60,setpts=N,ass=film.ass:fontsdir=fonts,select='{0}',settb=1/60,setpts=N,fade=t=in:st=0:d={1},fade=t=out:st={2}:d={3},format=yuv420p,setsar=1[film]",
                select,
                Fade,
                film - FilmFadeOut,
                FilmFadeOut
            ),
            Card("opening", opening),
            Card("closing", closing),
            "[opening][film][closing]concat=n=3:v=1:a=0[v]"
        );
        List<string> arguments = ["-hide_banner", "-loglevel", "error", "-nostats", "-i", master];
        if (music != null)
        {
            arguments.AddRange(["-i", music]);
            graph += string.Format(
                invariant,
                ";[1:a]atrim=start={0},asetpts=PTS-STARTPTS,afade=t=in:st=0:d={1},afade=t=out:st={2}:d={3},apad,atrim=end={4}[a]",
                musicStart,
                MusicFadeIn,
                length - MusicFadeOut,
                MusicFadeOut,
                length
            );
        }

        arguments.AddRange(["-filter_complex", graph, "-map", "[v]"]);
        if (music != null)
            arguments.AddRange(["-map", "[a]", "-c:a", "aac", "-b:a", "192k"]);
        arguments.AddRange([
            "-c:v",
            "libx264",
            "-preset",
            "medium",
            "-crf",
            "18",
            "-pix_fmt",
            "yuv420p",
            "-r",
            "60",
            "-movflags",
            "+faststart",
            "-y",
            output,
        ]);
        return arguments;
    }

    /// <summary>The master's count of frames, its width and its height, from ffprobe.</summary>
    private static (int Frames, int Width, int Height) ProbeMaster(string master)
    {
        (int exit, string text) = RunTool(
            tool: "ffprobe",
            arguments:
            [
                "-v",
                "error",
                "-select_streams",
                "v:0",
                "-count_packets",
                "-show_entries",
                "stream=nb_read_packets,width,height",
                "-of",
                "csv=p=0",
                master,
            ],
            folder: null,
            standardOutput: true
        );
        if (exit != 0)
            throw new IOException($"ffprobe cannot read the master {master}: {text}");
        string[] cells = text.Trim().Split(',');
        return (
            int.Parse(cells[2], CultureInfo.InvariantCulture),
            int.Parse(cells[0], CultureInfo.InvariantCulture),
            int.Parse(cells[1], CultureInfo.InvariantCulture)
        );
    }

    /// <summary>Runs a tool of the dev shell in <paramref name="folder"/>, and returns its exit code with its standard output, or the last lines of its errors.</summary>
    private static (int Exit, string Text) RunTool(
        string tool,
        IEnumerable<string> arguments,
        string? folder,
        bool standardOutput = false
    )
    {
        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (folder != null)
            start.WorkingDirectory = folder;
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        // Without the shell, Process.Start gives a process or throws.
        using Process process = Process.Start(start) ?? throw new IOException($"{tool} did not start.");
        Task<string> errors = process.StandardError.ReadToEndAsync();
        Task<string> text = process.StandardOutput.ReadToEndAsync();
        process.WaitForExit();
        string lastErrors = string.Join(
            '\n',
            errors.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(FfmpegVideoSink.ErrorLinesKept)
        );
        return (process.ExitCode, standardOutput && process.ExitCode == 0 ? text.Result : lastErrors);
    }
}
