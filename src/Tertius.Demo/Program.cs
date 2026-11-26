using Tertius.Assets;
using Tertius.Host;
using Tertius.Host.Scripting;

namespace Tertius.Demo;

public static class Program
{
    private const string ReadoutsUsage = "Usage: --readouts <script> <log.csv>";

    private const string GenerateUsage = "Usage: --generate <file> [<scene>]";

    /// <summary>The option that runs a Blue Danube scene without its converted art, as the gray box that the tests build.</summary>
    private const string GrayBox = "--gray-box";

    /// <summary>
    /// Runs one of the demo's scenes. See <see cref="ScriptCommand"/> and https://github.com/alexispurslane/tertius/wiki/host-and-scripted-runs.
    /// <list type="bullet">
    /// <item><c>--script scripts/drop-box.json --headless --out captures/drop-box</c> runs a script with no window.</item>
    /// <item><c>--script scripts/drop-box.json --headless --out captures/drop-box --video captures/drop-box.mkv --fps 30</c>
    /// does the same and makes its captures the frames of one video, which ffmpeg from the dev shell encodes.</item>
    /// <item><c>--script scripts/drop-box.json --out captures/drop-box --speed 0.5</c> plays it in a window.</item>
    /// <item><c>--scene drop-box</c> runs the scene live in a window until you close it.</item>
    /// <item><c>--scene orbit --record sessions/today.json</c> does the same and writes your session as a script that replays it.</item>
    /// <item><c>--generate scripts/blue-danube.json</c> writes the Blue Danube demo's long script, and its cuts beside it,
    /// such as <c>scripts/demo-docking.json</c> (see <see cref="Generate"/>).</item>
    /// <item><c>--generate scripts/demo-exit.json demo-exit</c> writes the exit's short script.</item>
    /// <item><c>--readouts scripts/blue-danube.json runs/danube/readouts.csv</c> writes the readout log of a script (see
    /// <see cref="Readouts"/>).</item>
    /// <item>
    /// <c>--edit runs/danube/blue-danube.mp4 --script scripts/blue-danube.json --master runs/danube/blue-danube.mkv --readouts runs/danube/readouts.csv</c>
    /// cuts the master video of the script into the final video (see <see cref="EditCommand"/>).</item>
    /// </list>
    /// </summary>
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>
    /// Runs the demo's command line <paramref name="args"/>, with its output and its errors in the writers: a command line
    /// that starts with <c>--generate</c> goes to <see cref="Generate"/>, one that starts with <c>--readouts</c> to
    /// <see cref="Readouts"/>, one that starts with <c>--edit</c> to <see cref="EditCommand"/> with the cut list of
    /// <see cref="BlueDanubeEdit"/>, and every other one to <see cref="RunScene"/> with this checkout's content root.
    /// Returns the exit code.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error) =>
        args switch
        {
            ["--generate", ..] => Generate(args, output, error),
            ["--readouts", ..] => Readouts(args: args, scenes: DemoScenes.All, output: output, error: error),
            ["--edit", ..] => EditCommand.Run(args: args, cuts: BlueDanubeEdit.Cuts, output: output, error: error),
            _ => RunScene(args: args, output: output, error: error, content: ContentFolders.Find()),
        };

    /// <summary>
    /// Runs a script or a live scene through <see cref="ScriptCommand"/>, after a check that a scene of the art (<see cref="BlueDanubeArt.NeededBy"/>) has its
    /// converted art under <paramref name="content"/>. Without the art, it names <see cref="BlueDanubeArt.SetupCommand"/>
    /// and returns <see cref="ScriptCommand.FileError"/>, so a run never looks like a finished one in gray boxes.
    /// The scenes draw the art under <paramref name="content"/>. <c>--gray-box</c> skips the check and builds every scene as
    /// its gray box, and the script command never sees it. A script that does not load passes on, so that the script
    /// command reports its error.
    /// </summary>
    public static int RunScene(string[] args, TextWriter output, TextWriter error, ContentFolders? content)
    {
        bool grayBox = args.Contains(GrayBox);
        string[] rest = [.. args.Where(arg => arg != GrayBox)];
        string? scene = grayBox ? null : SceneOf(rest);
        bool needsArt = scene is not null && BlueDanubeArt.NeededBy(scene);
        IReadOnlyList<ModelProblem> problems = needsArt ? BlueDanubeArt.Problems(content) : [];
        if (problems.Count > 0)
        {
            error.WriteLine(
                $"The Blue Danube art does not load: {string.Join("; ", problems.Select(Describe))}. Run "
                    + $"`{BlueDanubeArt.SetupCommand}` once from the repository's root, or pass {GrayBox} to run the "
                    + "gray-box scene."
            );
            return ScriptCommand.FileError;
        }

        if (
            scene is not null
            && BlueDanube.Scenes.Contains(scene)
            && content is not null
            && !File.Exists(EarthMaterial.PathIn(content))
        )
        {
            error.WriteLine($"The Earth material is missing. Run `{BlueDanubeArt.SetupCommand}`.");
            return ScriptCommand.FileError;
        }

        return ScriptCommand.Run(
            args: rest,
            scenes: DemoScenes.For(grayBox ? null : content),
            output: output,
            error: error
        );
    }

    /// <summary>One line for a model that does not load: its file and why.</summary>
    private static string Describe(ModelProblem problem) =>
        problem switch
        {
            MissingModel => $"{problem.Path} is missing",
            MalformedModel malformed => $"{problem.Path} is malformed ({malformed.Reason})",
            MissingPiece piece => $"{problem.Path} has no piece {piece.Piece}",
            _ => problem.Path,
        };

    /// <summary>The scene that a command line runs: the one after --scene, or the one that the --script file names.</summary>
    private static string? SceneOf(string[] args)
    {
        int at = Array.IndexOf(args, "--scene");
        if (at >= 0)
            return at + 1 < args.Length ? args[at + 1] : null;
        at = Array.IndexOf(args, "--script");
        if (at < 0 || at + 1 >= args.Length)
            return null;
        try
        {
            return Script.Load(args[at + 1]).Scene;
        }
        catch (Exception ex) when (ex is ScriptException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>--readouts &lt;script&gt; &lt;log.csv&gt;</c> runs the script with no renderer, with its scene from
    /// <paramref name="scenes"/>, and writes its readout log (see <see cref="ReadoutLog"/>) to the CSV file: one row for
    /// each capture. It runs every tick of the script, which takes about as long as the generator's rehearsal. Returns
    /// the exit code of <see cref="ScriptCommand"/>: 0, 1 for a wrong command line, 2 for a file that cannot be read or
    /// written, and 3 for a script that cannot run. Writes the log's path and its count of rows to <paramref name="output"/>.
    /// </summary>
    public static int Readouts(string[] args, IReadOnlyList<Scene> scenes, TextWriter output, TextWriter error)
    {
        if (args is not ["--readouts", string scriptPath, string logPath])
        {
            error.WriteLine("--readouts needs exactly a script and the log to write.");
            error.WriteLine(ReadoutsUsage);
            return ScriptCommand.CommandLineError;
        }

        IReadOnlyList<ReadoutRow> rows;
        try
        {
            rows = ReadoutLog.Record(Script.Load(scriptPath), scenes);
        }
        catch (ScriptException ex)
        {
            error.WriteLine($"The script {scriptPath} cannot run: {ex.Message}");
            return ScriptCommand.ScriptError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot read the script {scriptPath}: {ex.Message}");
            return ScriptCommand.FileError;
        }

        try
        {
            File.WriteAllText(logPath, ReadoutLog.ToCsv(rows));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot write the log {logPath}: {ex.Message}");
            return ScriptCommand.FileError;
        }

        output.WriteLine($"{logPath}: {rows.Count} rows");
        return ScriptCommand.Ok;
    }

    /// <summary>
    /// <c>--generate &lt;file&gt;</c> writes the Blue Danube demo's long script (see <see cref="BlueDanube"/>) to the file,
    /// as the checked-in <c>scripts/blue-danube.json</c>, and each of its <see cref="BlueDanube.Cuts"/> into the same
    /// folder, as the checked-in <c>scripts/demo-docking.json</c>. It rehearses the whole run first, which takes a while. A
    /// scene after the file names another of <see cref="BlueDanube.Scenes"/>: <c>--generate &lt;file&gt; demo-exit</c>
    /// writes the exit's short script, as the checked-in <c>scripts/demo-exit.json</c>, and no cuts. Returns the exit code
    /// of <see cref="ScriptCommand"/>: 0, 1 for a wrong command line, 2 for a file that cannot be written, 3 for a section
    /// that a run cannot take, and 4 for a cue that never holds. Writes the file's path to
    /// <paramref name="output"/>, then one line for each film: its name, its frames, the ticks before each frame and the
    /// second of the run at which it starts. Then it writes the path of each cut.
    /// </summary>
    public static int Generate(string[] args, TextWriter output, TextWriter error)
    {
        if (args is not ["--generate", string path, .. string[] rest] || rest.Length > 1)
        {
            error.WriteLine("--generate needs exactly one file, the script to write, and at most one scene.");
            error.WriteLine(GenerateUsage);
            return ScriptCommand.CommandLineError;
        }

        string scene = rest is [string named] ? named : BlueDanube.SceneName;
        if (!BlueDanube.Scenes.Contains(scene))
        {
            error.WriteLine(
                $"--generate knows the sections of the scenes {string.Join(" and ", BlueDanube.Scenes)}, not of {scene}."
            );
            error.WriteLine(GenerateUsage);
            return ScriptCommand.CommandLineError;
        }

        // The rehearsal of the script takes minutes, so a folder that does not exist fails before it, not after.
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null && !Directory.Exists(folder))
        {
            error.WriteLine($"Cannot write the script {path}: the folder {folder} does not exist.");
            return ScriptCommand.FileError;
        }

        Script script;
        try
        {
            script = BlueDanube.Generate(scene);
        }
        catch (ScriptException ex)
        {
            error.WriteLine($"A section of the Blue Danube script cannot run: {ex.Message}");
            return ScriptCommand.ScriptError;
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine($"The Blue Danube script cannot be generated: {ex.Message}");
            return ScriptCommand.InternalError;
        }

        if (!Write(path, script, error))
            return ScriptCommand.FileError;
        output.WriteLine(path);
        long tick = 0;
        foreach (ScriptStep step in script.Steps)
        {
            if (step.Value is not FilmStep film)
                continue;
            output.WriteLine(
                FormattableString.Invariant(
                    $"{film.Name}: {film.Frames} frames, every {film.Every} ticks, from {tick / 60.0:F1} s"
                )
            );
            tick += (long)film.Frames * film.Every;
        }

        // The cuts film a part of the long script, so only the long script has them.
        foreach (
            (string file, string firstSection, string lastSection) in scene == BlueDanube.SceneName
                ? BlueDanube.Cuts
                : []
        )
        {
            // The folder of the script, which the check before the rehearsal found, or the root.
            string cut = Path.Combine(folder ?? Path.GetPathRoot(Path.GetFullPath(path)) ?? "", file);
            if (!Write(cut, BlueDanube.FilmedBetween(script, firstSection, lastSection), error))
                return ScriptCommand.FileError;
            output.WriteLine(cut);
        }

        return ScriptCommand.Ok;
    }

    /// <summary>Writes <paramref name="script"/> to <paramref name="path"/>, or the reason that it cannot to
    /// <paramref name="error"/>. Returns true when it wrote it.</summary>
    private static bool Write(string path, Script script, TextWriter error)
    {
        try
        {
            File.WriteAllText(path, script.ToJson());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot write the script {path}: {ex.Message}");
            return false;
        }
    }
}
