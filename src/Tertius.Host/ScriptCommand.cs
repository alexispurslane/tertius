using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Rendering;
using Tertius.Simulation;

namespace Tertius.Host;

/// <summary>
/// The command line of a scripted run: <c>--script &lt;file&gt; --headless --out &lt;folder&gt;</c>. The Demo's Main calls it
/// with its table of scenes. It builds the scene that the script names in a new world on the Jolt backend, runs the
/// script, and writes each dump into the output folder.
/// </summary>
public static class ScriptCommand
{
    /// <summary>The run finished.</summary>
    public const int Ok = 0;

    /// <summary>The command line is wrong.</summary>
    public const int CommandLineError = 1;

    /// <summary>A file could not be read or written.</summary>
    public const int FileError = 2;

    /// <summary>The script cannot run, or names a scene that the table does not have.</summary>
    public const int ScriptError = 3;

    /// <summary>The headless renderer cannot start, for example on a machine with no Vulkan driver: a missing tool.</summary>
    public const int RendererError = 5;

    private const string Usage = "Usage: --script <file> --headless --out <folder>";

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
        if (!options.Headless)
        {
            error.WriteLine("The window does not exist yet (ticket ter-mu7k), so every run is headless. Pass --headless.");
            return CommandLineError;
        }

        Script script;
        try
        {
            script = Script.Load(options.Script);
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
            foreach (StateDump dump in result.Dumps)
                output.WriteLine(Path.Combine(options.Out, dump.Name + ".json"));
            foreach (string capture in result.Captures)
                output.WriteLine(capture);
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
    }

    private sealed record Options(string Script, string Out, bool Headless);

    /// <summary>The options, or null and the problem for an unknown option, a missing value or a missing option.</summary>
    private static Options? ReadOptions(string[] args, out string? problem)
    {
        string? script = null, output = null;
        bool headless = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless":
                    headless = true;
                    break;
                case "--script" or "--out" when i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]):
                    problem = $"{args[i]} needs a value.";
                    return null;
                case "--script":
                    script = args[++i];
                    break;
                case "--out":
                    output = args[++i];
                    break;
                default:
                    problem = $"Unknown option '{args[i]}'.";
                    return null;
            }
        }
        problem = script is null ? "The option --script is missing." : output is null ? "The option --out is missing." : null;
        return problem is null ? new Options(script!, output!, headless) : null;
    }
}
