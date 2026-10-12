using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Host.Windowing;
using Tertius.Rendering;
using Tertius.Simulation;

namespace Tertius.Host;

/// <summary>
/// The command line of the host. The Demo's Main calls it with its table of scenes.
/// <list type="bullet">
/// <item><c>--script &lt;file&gt; --headless --out &lt;folder&gt;</c> runs a script with no window, as fast as it can.</item>
/// <item><c>--script &lt;file&gt; --out &lt;folder&gt; [--speed &lt;factor&gt;]</c> plays a script in a window.</item>
/// <item><c>--scene &lt;name&gt; [--speed &lt;factor&gt;] [--record &lt;file&gt;]</c> runs a scene live in a window until you close it.
/// With <c>--record</c>, the command writes your session to the file as a script that replays it.</item>
/// </list>
/// Each run builds its scene in a new world on the Jolt backend.
/// </summary>
public static class ScriptCommand
{
    /// <summary>The run finished, or the user closed its window.</summary>
    public const int Ok = 0;

    /// <summary>The command line is wrong.</summary>
    public const int CommandLineError = 1;

    /// <summary>A file could not be read or written.</summary>
    public const int FileError = 2;

    /// <summary>The script cannot run, or names a scene that the table does not have.</summary>
    public const int ScriptError = 3;

    /// <summary>The run failed with an exception that is a bug.</summary>
    public const int InternalError = 4;

    /// <summary>The renderer or the window cannot start, for example on a machine with no Vulkan driver: a missing tool.</summary>
    public const int RendererError = 5;

    private const string Usage =
        "Usage: --script <file> --headless --out <folder>  |  --script <file> --out <folder> [--speed <factor>]  |  --scene <name> [--speed <factor>] [--record <file>]";

    /// <summary>
    /// Runs the command. Returns the exit code. Writes the path of each dump and each captured image to
    /// <paramref name="output"/>, and every problem and the world's log to <paramref name="error"/>. Makes a headless
    /// renderer only for a script with a capture step, so a script with dumps only runs on a machine with no GPU.
    /// </summary>
    public static int Run(string[] args, IReadOnlyList<Scene> scenes, TextWriter output, TextWriter error) =>
        Run(args, scenes, output, error, VulkanRenderer.CreateHeadless);

    /// <summary>
    /// <see cref="Run(string[], IReadOnlyList{Scene}, TextWriter, TextWriter)"/> with the function that makes the headless
    /// renderer, so that a test can wrap the real one or make it fail.
    /// </summary>
    internal static int Run(string[] args, IReadOnlyList<Scene> scenes, TextWriter output, TextWriter error, Func<RendererOptions, VulkanRenderer> createRenderer)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(scenes);
        if (ReadOptions(args, out string? problem) is not { } options)
        {
            error.WriteLine(problem);
            error.WriteLine(Usage);
            return CommandLineError;
        }
        if (options.Scene is { } sceneName)
        {
            // Free play: the scene is found before a window opens.
            Scene? live = scenes.FirstOrDefault(s => s.Name == sceneName);
            if (live is null)
            {
                error.WriteLine($"No scene is named '{sceneName}'. The scenes are: {string.Join(", ", scenes.Select(s => s.Name))}.");
                return ScriptError;
            }
            return WindowHost.Run(live, script: null, scriptPath: null, outputFolder: null, options.Speed, options.Record, output, error);
        }

        Script script;
        try
        {
            script = Script.Load(options.Script!);
        }
        catch (ScriptException ex)
        {
            error.WriteLine($"{options.Script}: {ex.Message}");
            return ScriptError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot read the script {options.Script}: {ex.Message}");
            return FileError;
        }

        Scene? scene = scenes.FirstOrDefault(s => s.Name == script.Scene);
        if (scene is null)
        {
            error.WriteLine($"{options.Script}: no scene is named '{script.Scene}'. The scenes are: {string.Join(", ", scenes.Select(s => s.Name))}.");
            return ScriptError;
        }
        if (!options.Headless)
            return WindowHost.Run(scene, script, options.Script, options.Out, options.Speed, recordPath: null, output, error);

        using var world = new World(new WorldOptions { DisposePhysics = true }, new JoltPhysicsBackend());
        VulkanRenderer? renderer = null;
        if (script.Steps.Any(step => step is CaptureStep))
        {
            try
            {
                renderer = createRenderer(new RendererOptions { Width = script.Window.Width, Height = script.Window.Height, Impostors = false });
            }
            catch (Exception ex)
            {
                error.WriteLine($"Cannot start the headless renderer for the captures of {options.Script}: {ex.Message}");
                return RendererError;
            }
        }
        // Disposed in reverse order: the renderer, then the world and its physics.
        using VulkanRenderer? rendererToDispose = renderer;
        // The log holds the errors, which the dumps also hold, and the engine's notes, such as a run that is no longer
        // deterministic, which only the log holds.
        world.Log += error.WriteLine;
        try
        {
            RunResult result = ScriptRunner.Run(script, world, scene.Build(world, renderer ?? RenderResources.None), options.Out,
                renderer is null ? null : new HeadlessPlayback(renderer, script.Window));
            WriteFiles(result, options.Out!, output);
            return Ok;
        }
        catch (ScriptException ex)
        {
            error.WriteLine($"{options.Script}: {ex.Message}");
            return ScriptError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot write into {options.Out}: {ex.Message}");
            return FileError;
        }
        // A bug, such as a broken contract that escapes World.Tick: the same exit code as in the window.
        catch (Exception ex)
        {
            error.WriteLine($"The run failed: {ex}");
            return InternalError;
        }
    }

    /// <summary>Writes the path of each dump, then of each captured image.</summary>
    internal static void WriteFiles(RunResult result, string outputFolder, TextWriter output)
    {
        foreach (StateDump dump in result.Dumps)
            output.WriteLine(Path.Combine(outputFolder, dump.Name + ".json"));
        foreach (string capture in result.Captures)
            output.WriteLine(capture);
    }

    /// <param name="Script">The script file, or null for free play.</param>
    /// <param name="Scene">The scene of free play, or null for a script.</param>
    /// <param name="Out">The output folder of a script.</param>
    /// <param name="Headless">True to run a script with no window.</param>
    /// <param name="Speed">The wall-clock speed of the window, 1 for real time.</param>
    /// <param name="Record">The file that free play writes its session to as a script, or null to record nothing.</param>
    internal sealed record Options(string? Script, string? Scene, string? Out, bool Headless, double Speed, string? Record = null);

    /// <summary>The options, or null and the problem for an unknown option, a missing value or a missing option.</summary>
    internal static Options? ReadOptions(string[] args, out string? problem)
    {
        string? script = null, scene = null, output = null, speedText = null, record = null;
        bool headless = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless":
                    headless = true;
                    break;
                case "--script" or "--scene" or "--out" or "--speed" or "--record" when i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]):
                    problem = $"{args[i]} needs a value.";
                    return null;
                case "--script":
                    script = args[++i];
                    break;
                case "--scene":
                    scene = args[++i];
                    break;
                case "--out":
                    output = args[++i];
                    break;
                case "--speed":
                    speedText = args[++i];
                    break;
                case "--record":
                    record = args[++i];
                    break;
                default:
                    problem = $"Unknown option '{args[i]}'.";
                    return null;
            }
        }
        double speed = 1;
        problem = (script, scene) switch
        {
            (null, null) => "Give --script or --scene.",
            ({ }, { }) => "Give --script or --scene, not both.",
            (null, { }) when headless => "--scene is free play, and free play needs the window. Leave out --headless.",
            (null, { }) when output != null => "--out applies to a script. Free play writes only the file that --record names.",
            ({ }, null) when record != null => "--record applies to free play, so give --scene. A script is a record already.",
            ({ }, null) when output is null => "The option --out is missing.",
            _ when speedText != null && headless => "--speed applies to the window. A headless run runs its ticks back to back.",
            _ when speedText != null && !(double.TryParse(speedText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out speed) && double.IsFinite(speed) && speed > 0) =>
                $"--speed needs a positive number, such as 0.5 or 2, not '{speedText}'.",
            _ => null,
        };
        return problem is null ? new Options(script, scene, output, headless, speed, record) : null;
    }
}
