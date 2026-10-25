using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Tertius.Host.Scripting;
using Tertius.Host.Windowing;
using Tertius.Physics;
using Tertius.Rendering;
using Tertius.Simulation;

namespace Tertius.Host;

/// <summary>
/// The command line of the host. The Demo's Main calls it with its table of scenes.
/// <list type="bullet">
/// <item><c>--script &lt;file&gt; --headless --out &lt;folder&gt; [--video &lt;file.mkv&gt; [--fps &lt;rate&gt;]]</c> runs a script with
/// no window, as fast as it can. With <c>--video</c>, its captures become the frames of one video, which ffmpeg encodes,
/// at <c>--fps</c> frames per second, 30 without the option. Its dumps still go into the output folder.</item>
/// <item><c>--script &lt;file&gt; --out &lt;folder&gt; [--speed &lt;factor&gt;]</c> plays a script in a window.</item>
/// <item><c>--size &lt;width&gt;x&lt;height&gt;</c>, such as <c>--size 1920x1080</c>, replaces the size of a script's window
/// in either run.</item>
/// <item><c>--tone-curve aces|hable</c> chooses the headless renderer's <see cref="ToneCurve"/>, ACES fitted without the
/// option.</item>
/// <item><c>--ambient-occlusion off|&lt;meters&gt;</c> turns the headless renderer's ambient occlusion off, or sets its
/// radius in m. Without the option, the renderer keeps its default.</item>
/// <item><c>--bloom on|off</c> keeps the headless renderer's default <see cref="Bloom"/>, or turns it off.</item>
/// <item><c>--reflections on|off</c> keeps the headless renderer's default <see cref="ConeTracedReflections"/>, or turns
/// them off.</item>
/// <item><c>--anti-aliasing off|smaa</c> chooses the headless renderer's <see cref="AntiAliasing"/>, SMAA without the
/// option.</item>
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

    /// <summary>
    /// A tool that the run needs cannot start: the renderer, the window or ffmpeg. Examples are a machine with no Vulkan
    /// driver, and a shell without ffmpeg on its search path.
    /// </summary>
    public const int ToolError = 5;

    /// <summary>
    /// The file in the output folder of a headless run that holds the summary of the GPU time of each pass, when the
    /// renderer times its passes.
    /// </summary>
    public const string GpuTimingsFile = "gpu-timings.json";

    /// <summary>
    /// The file in the output folder of a headless run with a renderer that holds the exposure of each shown frame, in the
    /// order shown.
    /// </summary>
    public const string ExposureFile = "exposure.json";

    private const string Usage =
        "Usage: --script <file> --headless --out <folder> [--video <file.mkv> [--fps <rate>]] [--size <width>x<height>] [--tone-curve aces|hable] [--ambient-occlusion off|<meters>] [--bloom on|off] [--reflections on|off] [--anti-aliasing off|smaa]  |  --script <file> --out <folder> [--speed <factor>] [--size <width>x<height>]  |  --scene <name> [--speed <factor>] [--record <file>]";

    private static readonly JsonSerializerOptions ReportJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // The options that take a value. --headless is the one option without one.
    private static readonly string[] ValueOptions =
    [
        "--script",
        "--scene",
        "--out",
        "--speed",
        "--record",
        "--video",
        "--fps",
        "--size",
        "--tone-curve",
        "--ambient-occlusion",
        "--bloom",
        "--reflections",
        "--anti-aliasing",
    ];

    /// <summary>
    /// Runs the command. Returns the exit code. Writes the path of each dump and each captured image, or of the video, to
    /// <paramref name="output"/>, and every problem and the world's log to <paramref name="error"/>. Makes a headless
    /// renderer only for a script with a capture or film step, so a script with dumps only runs on a machine with no GPU.
    /// A headless renderer that times its passes adds <see cref="GpuTimingsFile"/>, its path, one line for each pass and one
    /// for the frame's GPU span.
    /// Every headless renderer adds <see cref="ExposureFile"/> and its path.
    /// </summary>
    public static int Run(string[] args, IReadOnlyList<Scene> scenes, TextWriter output, TextWriter error) =>
        Run(args: args, scenes: scenes, output: output, error: error, createRenderer: VulkanRenderer.CreateHeadless);

    /// <summary>
    /// <see cref="Run(string[], IReadOnlyList{Scene}, TextWriter, TextWriter)"/> with the function that makes the headless
    /// renderer, and the function that starts the video's ffmpeg (<see cref="FfmpegVideoSink.Start(VideoFile, ScriptWindow)"/>
    /// when null), so that a
    /// test can wrap the real ones or make them fail.
    /// </summary>
    internal static int Run(
        string[] args,
        IReadOnlyList<Scene> scenes,
        TextWriter output,
        TextWriter error,
        Func<RendererOptions, VulkanRenderer> createRenderer,
        Func<VideoFile, ScriptWindow, FfmpegVideoSink>? startVideo = null
    )
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
            return RunFreePlay(options: options, sceneName: sceneName, scenes: scenes, output: output, error: error);

        // ReadOptions gives a run that is not free play its script and its output folder.
        if (options is not { Script: { } scriptPath, Out: { } outFolder })
            throw new UnreachableException("A script run has its script and its output folder.");
        Script script;
        try
        {
            script = Script.Load(scriptPath);
            if (options.Size is { } size)
                script = script with { Window = size };
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

        bool captures = script.Steps.Any(step => step is CaptureStep or FilmStep);
        if (options.Video is not null && !captures)
        {
            error.WriteLine(
                $"--video makes a video of the captures, and {options.Script} has no capture or film step."
            );
            error.WriteLine(Usage);
            return CommandLineError;
        }

        Scene? scene = scenes.FirstOrDefault(s => s.Name == script.Scene);
        if (scene is null)
        {
            error.WriteLine(
                $"{options.Script}: no scene is named '{script.Scene}'. The scenes are: {string.Join(", ", scenes.Select(s => s.Name))}."
            );
            return ScriptError;
        }

        if (!options.Headless)
            return WindowHost.Run(
                sceneToBuild: scene,
                script: script,
                scriptPath: options.Script,
                outputFolder: options.Out,
                speed: options.Speed,
                recordPath: null,
                output: output,
                error: error
            );
        return RunHeadless(
            options: options,
            script: script,
            scene: scene,
            outFolder: outFolder,
            createRenderer: captures ? createRenderer : null,
            startVideo: startVideo ?? FfmpegVideoSink.Start,
            output: output,
            error: error
        );
    }

    /// <summary>
    /// Runs a script with no window, as fast as it can. Makes a renderer with <paramref name="createRenderer"/>, which is
    /// null for a script with no capture or film step, and a playback that draws the captures into the run's target.
    /// </summary>
    private static int RunHeadless(
        Options options,
        Script script,
        Scene scene,
        string outFolder,
        Func<RendererOptions, VulkanRenderer>? createRenderer,
        Func<VideoFile, ScriptWindow, FfmpegVideoSink> startVideo,
        TextWriter output,
        TextWriter error
    )
    {
        using var world = new World(
            new() { DisposePhysics = true },
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced)
        );
        VulkanRenderer? renderer = null;
        if (createRenderer != null)
        {
            renderer = StartRenderer(
                createRenderer: createRenderer,
                size: script.Window,
                options: options,
                error: error
            );
            if (renderer is null)
                return ToolError;
        }

        using VulkanRenderer? rendererToDispose = renderer;
        return Play(
            options: options,
            script: script,
            scene: scene,
            outFolder: outFolder,
            world: world,
            renderer: renderer,
            startVideo: startVideo,
            output: output,
            error: error
        );
    }

    /// <summary>
    /// Plays the script in <paramref name="world"/> with a playback on <paramref name="renderer"/>, or with none when it is
    /// null, then writes the files of the run and the readings of the renderer. Returns the exit code.
    /// </summary>
    private static int Play(
        Options options,
        Script script,
        Scene scene,
        string outFolder,
        World world,
        VulkanRenderer? renderer,
        Func<VideoFile, ScriptWindow, FfmpegVideoSink> startVideo,
        TextWriter output,
        TextWriter error
    )
    {
        CaptureTarget target = options.Video is { } video ? video : new PngFolder(outFolder);
        var readings = new FrameReadings();
        HeadlessPlayback? playback = null;
        if (renderer != null)
        {
            Result<HeadlessPlayback, PlaybackFailure> started = StartPlayback(
                renderer: renderer,
                size: script.Window,
                target: target,
                startVideo: startVideo,
                readings: readings,
                options: options
            );
            if (started.Problem is { Code: var code, Message: var message })
            {
                error.WriteLine(message);
                return code;
            }

            playback = started.Ok;
        }

        // Disposed first, before RunHeadless disposes the renderer, then the world and its physics: the playback ends the
        // video on every way out.
        using HeadlessPlayback? playbackToDispose = playback;
        // The log holds the errors, which the dumps also hold, and the engine's notes, such as a run that is no longer
        // deterministic, which only the log holds.
        world.Log += error.WriteLine;
        try
        {
            RunResult result = ScriptRunner.Run(
                script: script,
                world: world,
                scene: scene.Build(world, renderer ?? RenderResources.None),
                outputFolder: options.Out,
                playback: playback
            );
            playback?.Finish();
            WriteFiles(result: result, outputFolder: outFolder, target: target, output: output);
            if (renderer != null)
                WriteReadings(
                    readings: readings,
                    renderer: renderer,
                    size: script.Window,
                    captures: result.Captures,
                    outputFolder: outFolder,
                    output: output
                );
            return Ok;
        }
        catch (Exception ex)
        {
            (int failure, string message) = Failure(ex, options);
            error.WriteLine(message);
            return failure;
        }
    }

    /// <summary>Writes <see cref="GpuTimingsFile"/> and <see cref="ExposureFile"/> from the readings of a run's shown frames.</summary>
    private static void WriteReadings(
        FrameReadings readings,
        VulkanRenderer renderer,
        ScriptWindow size,
        IReadOnlyList<string> captures,
        string outputFolder,
        TextWriter output
    )
    {
        WriteGpuTimings(
            timings: readings.Timings,
            renderer: renderer,
            size: size,
            outputFolder: outputFolder,
            output: output
        );
        WriteExposures(stops: readings.Exposures, captures: captures, outputFolder: outputFolder, output: output);
    }

    /// <summary>
    /// The playback that draws the captures into <paramref name="target"/> and adds each shown frame to
    /// <paramref name="readings"/>, or why it cannot start: the video's ffmpeg or folder.
    /// </summary>
    private static Result<HeadlessPlayback, PlaybackFailure> StartPlayback(
        VulkanRenderer renderer,
        ScriptWindow size,
        CaptureTarget target,
        Func<VideoFile, ScriptWindow, FfmpegVideoSink> startVideo,
        FrameReadings readings,
        Options options
    )
    {
        try
        {
            return new HeadlessPlayback(
                renderer: renderer,
                size: size,
                target: target,
                startVideo: startVideo,
                rendered: () => readings.Add(renderer.LastFrameStats)
            );
        }
        // Only a video target starts a tool, ffmpeg. Process.Start throws a Win32Exception when ffmpeg is not on the
        // search path or cannot start.
        catch (Win32Exception ex) when (options.Video is { } file)
        {
            return new PlaybackFailure(
                ToolError,
                $"Cannot start ffmpeg for the video {file.Path} of {options.Script}: {ex.Message}"
            );
        }
        // The folder of the video cannot be made: a file problem, as for a dump or an image.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && options.Video is { } file)
        {
            return new PlaybackFailure(
                FileError,
                $"Cannot write the video {file.Path} of {options.Script}: {ex.Message}"
            );
        }
    }

    /// <summary>Why the playback of a headless run cannot start: the exit code and the message.</summary>
    private sealed record PlaybackFailure(int Code, string Message);

    /// <summary>The headless renderer, or null after writing why it cannot start to <paramref name="error"/>.</summary>
    private static VulkanRenderer? StartRenderer(
        Func<RendererOptions, VulkanRenderer> createRenderer,
        ScriptWindow size,
        Options options,
        TextWriter error
    )
    {
        var rendererOptions = new RendererOptions
        {
            Width = size.Width,
            Height = size.Height,
            Impostors = false,
        };
        if (options.ToneCurve is { } curve)
            rendererOptions.ToneCurve = curve;
        if (options.AmbientOcclusion is { } ambientOcclusion)
            rendererOptions.AmbientOcclusion = ambientOcclusion;
        if (options.BloomOff)
            rendererOptions.Bloom = new NoBloom();
        if (options.ReflectionsOff)
            rendererOptions.Reflections = new NoReflections();
        if (options.AntiAliasing is { } antiAliasing)
            rendererOptions.AntiAliasing = antiAliasing;
        try
        {
            return createRenderer(rendererOptions);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Cannot start the headless renderer for the captures of {options.Script}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The exit code and the message of an exception that stopped a headless run after it started.</summary>
    private static (int Code, string Message) Failure(Exception ex, Options options) =>
        ex switch
        {
            ScriptException => (ScriptError, $"{options.Script}: {ex.Message}"),
            // ffmpeg failed, and the message gives the video, ffmpeg's exit code and its last error lines.
            VideoStreamException => (FileError, $"Cannot write the video of {options.Script}: {ex.Message}"),
            // A dump or an image that cannot be written: the same words as in the window.
            IOException or UnauthorizedAccessException => (FileError, $"Cannot write into {options.Out}: {ex.Message}"),
            // A bug, such as a broken contract that escapes World.Tick: the same exit code as in the window.
            _ => (InternalError, $"The run failed: {ex}"),
        };

    /// <summary>Runs the scene named <paramref name="sceneName"/> live in a window, after it finds the scene before a window opens.</summary>
    private static int RunFreePlay(
        Options options,
        string sceneName,
        IReadOnlyList<Scene> scenes,
        TextWriter output,
        TextWriter error
    )
    {
        Scene? live = scenes.FirstOrDefault(s => s.Name == sceneName);
        if (live is null)
        {
            error.WriteLine(
                $"No scene is named '{sceneName}'. The scenes are: {string.Join(", ", scenes.Select(s => s.Name))}."
            );
            return ScriptError;
        }

        return WindowHost.Run(
            sceneToBuild: live,
            script: null,
            scriptPath: null,
            outputFolder: null,
            speed: options.Speed,
            recordPath: options.Record,
            output: output,
            error: error
        );
    }

    /// <summary>
    /// Writes the path of each dump, then the path of each captured image of a <see cref="PngFolder"/>, or the path of the
    /// <see cref="VideoFile"/> that holds the captures.
    /// </summary>
    internal static void WriteFiles(RunResult result, string outputFolder, CaptureTarget target, TextWriter output)
    {
        foreach (StateDump dump in result.Dumps)
            output.WriteLine(Path.Combine(outputFolder, dump.Name + ".json"));
        switch (target)
        {
            case PngFolder folder:
                foreach (string capture in result.Captures)
                    output.WriteLine(folder.PathOf(capture));
                break;
            case VideoFile video when result.Captures.Count > 0:
                output.WriteLine(video.Path);
                break;
            case VideoFile or null:
                // A video without captures holds no frames, and no target writes no files.
                break;
            default:
                throw new UnreachableException(
                    $"The capture target {target} has a case that the command does not list."
                );
        }
    }

    /// <summary>
    /// Writes <see cref="GpuTimingsFile"/> into the output folder with the summary of the frames' GPU span and of each pass,
    /// then its path, one line for each pass and one for the span to <paramref name="output"/>. Writes nothing when no
    /// frame was timed.
    /// </summary>
    internal static void WriteGpuTimings(
        GpuTimingSummary timings,
        VulkanRenderer renderer,
        ScriptWindow size,
        string outputFolder,
        TextWriter output
    )
    {
        if (timings.Frame() is not { } frame)
            return;
        IReadOnlyList<PassSummary> passes = timings.Passes();
        string path = Path.Combine(outputFolder, GpuTimingsFile);
        Directory.CreateDirectory(outputFolder);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new GpuTimingReport(
                    Device: renderer.DeviceName,
                    Width: size.Width,
                    Height: size.Height,
                    Frame: frame,
                    Passes: passes
                ),
                ReportJson
            )
        );
        output.WriteLine(path);
        foreach (PassSummary pass in passes)
            output.WriteLine(
                FormattableString.Invariant(
                    $"GPU {pass.Pass}: mean {pass.MeanMs:F3} ms, median {pass.MedianMs:F3} ms, p95 {pass.P95Ms:F3} ms over {pass.Frames} frames"
                )
            );
        output.WriteLine(
            FormattableString.Invariant(
                $"GPU frame span: mean {frame.MeanMs:F3} ms, median {frame.MedianMs:F3} ms, p95 {frame.P95Ms:F3} ms over {frame.Frames} frames"
            )
        );
    }

    /// <summary>
    /// Writes <see cref="ExposureFile"/> into the output folder with one <see cref="ShownExposure"/> per shown frame, in the
    /// order shown, then its path to <paramref name="output"/>.
    /// </summary>
    /// <param name="stops">The exposure in stops of each shown frame, in the order shown.</param>
    /// <param name="captures">The name of each shown frame's capture, in the order shown.</param>
    internal static void WriteExposures(
        IReadOnlyList<float> stops,
        IReadOnlyList<string> captures,
        string outputFolder,
        TextWriter output
    )
    {
        if (stops.Count != captures.Count)
            throw new UnreachableException(
                $"Each of the {captures.Count} captures renders one shown frame, and {stops.Count} frames have an exposure."
            );
        string path = Path.Combine(outputFolder, ExposureFile);
        Directory.CreateDirectory(outputFolder);
        ShownExposure[] frames =
        [
            .. captures.Select(
                (capture, frame) => new ShownExposure(Frame: frame, Capture: capture, Stops: stops[frame])
            ),
        ];
        File.WriteAllText(path, JsonSerializer.Serialize(frames, ReportJson));
        output.WriteLine(path);
    }

    /// <summary>One entry of <see cref="ExposureFile"/>.</summary>
    /// <param name="Frame">The shown frame's number, counted from 0 over the run's captures: its frame number in a video.</param>
    /// <param name="Capture">The capture's name, which is the name of its PNG file.</param>
    /// <param name="Stops">The exposure in stops that the frame's passes used (<see cref="ExposureReading.Stops"/>).</param>
    internal readonly record struct ShownExposure(int Frame, string Capture, float Stops);

    /// <summary>The content of <see cref="GpuTimingsFile"/>: the device, the size of the frames, their GPU span and each pass.</summary>
    internal sealed record GpuTimingReport(
        string Device,
        int Width,
        int Height,
        SpanSummary Frame,
        IReadOnlyList<PassSummary> Passes
    );

    /// <param name="Script">The script file, or null for free play.</param>
    /// <param name="Scene">The scene of free play, or null for a script.</param>
    /// <param name="Out">The output folder of a script.</param>
    /// <param name="Headless">True to run a script with no window.</param>
    /// <param name="Speed">The wall-clock speed of the window, 1 for real time.</param>
    /// <param name="Record">The file that free play writes its session to as a script, or null to record nothing.</param>
    /// <param name="Video">The video that a headless run writes its captures into, or null to write PNG files into the output folder.</param>
    /// <param name="Size">The size that replaces the size of the script's window, or null to keep it.</param>
    /// <param name="ToneCurve">The curve that replaces the headless renderer's default, or null to keep it.</param>
    /// <param name="AmbientOcclusion">The setting that replaces the headless renderer's default, or null to keep it.</param>
    /// <param name="BloomOff">True to turn the headless renderer's bloom off, false to keep its default.</param>
    /// <param name="ReflectionsOff">True to turn the headless renderer's reflections off, false to keep its default.</param>
    /// <param name="AntiAliasing">The anti-aliasing that replaces the headless renderer's default, or null to keep it.</param>
    internal sealed record Options(
        string? Script,
        string? Scene,
        string? Out,
        bool Headless,
        double Speed,
        string? Record = null,
        VideoFile? Video = null,
        ScriptWindow? Size = null,
        ToneCurve? ToneCurve = null,
        AmbientOcclusionSetting? AmbientOcclusion = null,
        bool BloomOff = false,
        bool ReflectionsOff = false,
        AntiAliasing? AntiAliasing = null
    );

    /// <summary>The options, or null and the problem for an unknown option, a missing value or a missing option.</summary>
    internal static Options? ReadOptions(string[] args, out string? problem)
    {
        if (Gather(args, out problem) is not { } given)
            return null;
        problem = PlayProblem(given) ?? ValueProblem(given);
        if (problem is not null)
            return null;
        string? video = given.Value("--video");
        return new Options(
            Script: given.Value("--script"),
            Scene: given.Value("--scene"),
            Out: given.Value("--out"),
            Headless: given.Headless,
            Speed: SpeedOf(given.Value("--speed")) ?? 1,
            Record: given.Value("--record"),
            Video: video is null
                ? null
                : new VideoFile(video, FpsOf(given.Value("--fps")) ?? VideoFile.DefaultFramesPerSecond),
            Size: SizeOf(given.Value("--size")),
            ToneCurve: ToneCurveOf(given.Value("--tone-curve")),
            AmbientOcclusion: AmbientOcclusionOf(given.Value("--ambient-occlusion")),
            BloomOff: given.Value("--bloom") == "off",
            ReflectionsOff: given.Value("--reflections") == "off",
            AntiAliasing: AntiAliasingOf(given.Value("--anti-aliasing"))
        );
    }

    /// <summary>The options of a command line as given, before the rules between them: the value of each option, and <c>--headless</c>.</summary>
    private sealed record GivenOptions(IReadOnlyDictionary<string, string> Values, bool Headless)
    {
        public string? Value(string option) => Values.GetValueOrDefault(option);
    }

    /// <summary>The options as given, or null and the problem for an unknown option or a missing value.</summary>
    private static GivenOptions? Gather(string[] args, out string? problem)
    {
        var values = new Dictionary<string, string>();
        bool headless = false;
        problem = null;
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (option == "--headless")
            {
                headless = true;
                continue;
            }

            if (!ValueOptions.Contains(option))
            {
                problem = $"Unknown option '{option}'.";
                return null;
            }

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                problem = $"{option} needs a value.";
                return null;
            }

            values[option] = args[++i];
        }

        return new(values, headless);
    }

    /// <summary>The problem with the choice between a script and free play, and with the options that belong to one of them, or null.</summary>
    private static string? PlayProblem(GivenOptions given) =>
        (given.Value("--script"), given.Value("--scene")) switch
        {
            (null, null) => "Give --script or --scene.",
            (not null, not null) => "Give --script or --scene, not both.",
            (null, not null) when given.Headless =>
                "--scene is free play, and free play needs the window. Leave out --headless.",
            (null, not null) when given.Value("--out") != null =>
                "--out applies to a script. Free play writes only the file that --record names.",
            (not null, null) when given.Value("--record") != null =>
                "--record applies to free play, so give --scene. A script is a record already.",
            (not null, null) when given.Value("--out") is null => "The option --out is missing.",
            _ => null,
        };

    /// <summary>The problem with the value of an option, or with an option that another option needs, or null.</summary>
    private static string? ValueProblem(GivenOptions given) =>
        SpeedProblem(given)
        ?? VideoProblem(given)
        ?? SizeProblem(given)
        ?? ToneCurveProblem(given)
        ?? AmbientOcclusionProblem(given)
        ?? OnOffProblem(given, "--bloom")
        ?? OnOffProblem(given, "--reflections")
        ?? AntiAliasingProblem(given);

    private static string? SpeedProblem(GivenOptions given)
    {
        if (given.Value("--speed") is not { } speed)
            return null;
        if (given.Headless)
            return "--speed applies to the window. A headless run runs its ticks back to back.";
        if (SpeedOf(speed) is null)
            return $"--speed needs a positive number, such as 0.5 or 2, not '{speed}'.";
        return null;
    }

    private static string? VideoProblem(GivenOptions given)
    {
        string? video = given.Value("--video"),
            fps = given.Value("--fps");
        if (video != null && !given.Headless)
            return "--video applies to a headless run. The window writes PNG files, so add --headless or leave out --video.";
        if (video != null && !VideoFile.HasVideoExtension(video))
            return $"The --video file's name ends in {VideoFile.Extension}, and '{video}' does not.";
        if (fps != null && video is null)
            return "--fps applies to a video, so give --video too.";
        if (fps != null && FpsOf(fps) is null)
            return $"--fps needs a whole number from {VideoFile.MinFramesPerSecond} to {VideoFile.MaxFramesPerSecond}, not '{fps}'.";
        return null;
    }

    private static string? SizeProblem(GivenOptions given)
    {
        if (given.Value("--size") is not { } size)
            return null;
        if (given.Value("--script") is null)
            return "--size applies to a script, so give --script.";
        if (SizeOf(size) is null)
            return $"--size needs a width and a height of 1 to {ScriptWindow.MaxSide} pixels, such as 1920x1080, not '{size}'.";
        return null;
    }

    private static string? ToneCurveProblem(GivenOptions given)
    {
        if (given.Value("--tone-curve") is not { } curve)
            return null;
        if (!given.Headless)
            return "--tone-curve applies to a headless run, so add --headless.";
        if (ToneCurveOf(curve) is null)
            return $"--tone-curve needs aces or hable, not '{curve}'.";
        return null;
    }

    private static string? AmbientOcclusionProblem(GivenOptions given)
    {
        if (given.Value("--ambient-occlusion") is not { } occlusion)
            return null;
        if (!given.Headless)
            return "--ambient-occlusion applies to a headless run, so add --headless.";
        if (AmbientOcclusionOf(occlusion) is null)
            return $"--ambient-occlusion needs off or a radius in m, a positive number such as 0.5, not '{occlusion}'.";
        return null;
    }

    /// <summary>The problem with a switch of the headless renderer, such as <c>--bloom</c>, which takes on or off, or null.</summary>
    private static string? OnOffProblem(GivenOptions given, string option)
    {
        if (given.Value(option) is not { } value)
            return null;
        if (!given.Headless)
            return $"{option} applies to a headless run, so add --headless.";
        if (value is not ("on" or "off"))
            return $"{option} needs on or off, not '{value}'.";
        return null;
    }

    private static string? AntiAliasingProblem(GivenOptions given)
    {
        if (given.Value("--anti-aliasing") is not { } antiAliasing)
            return null;
        if (!given.Headless)
            return "--anti-aliasing applies to a headless run, so add --headless.";
        if (AntiAliasingOf(antiAliasing) is null)
            return $"--anti-aliasing needs off or smaa, not '{antiAliasing}'.";
        return null;
    }

    /// <summary>The wall-clock speed that the text gives, or null for no text or one that is not a positive finite number.</summary>
    private static double? SpeedOf(string? text)
    {
        bool read = double.TryParse(
            s: text,
            style: NumberStyles.Float,
            provider: CultureInfo.InvariantCulture,
            result: out double speed
        );
        return read && double.IsFinite(speed) && speed > 0 ? speed : null;
    }

    /// <summary>The frame rate that the text gives, or null for no text or one that is not a whole number in the range of a video.</summary>
    private static int? FpsOf(string? text)
    {
        bool read = int.TryParse(
            s: text,
            style: NumberStyles.None,
            provider: CultureInfo.InvariantCulture,
            result: out int fps
        );
        return read && fps is >= VideoFile.MinFramesPerSecond and <= VideoFile.MaxFramesPerSecond ? fps : null;
    }

    /// <summary>The tone curve that the text names, or null for no text or a name that is not aces or hable.</summary>
    private static ToneCurve? ToneCurveOf(string? text) =>
        text switch
        {
            "aces" => ToneCurve.AcesFitted,
            "hable" => ToneCurve.Hable,
            _ => null,
        };

    /// <summary>
    /// The ambient occlusion that the text gives: none for <c>off</c>, the renderer's default with the radius for a
    /// positive finite number of m, and null for no text or any other text. The renderer refuses a radius above its limit.
    /// </summary>
    private static AmbientOcclusionSetting? AmbientOcclusionOf(string? text)
    {
        if (text == "off")
            return new NoAmbientOcclusion();
        bool read = float.TryParse(
            s: text,
            style: NumberStyles.Float,
            provider: CultureInfo.InvariantCulture,
            result: out float radius
        );
        bool positive = read && float.IsFinite(radius) && radius > 0;
        return positive ? GroundTruthOcclusion.Default with { RadiusMeters = radius } : null;
    }

    /// <summary>The anti-aliasing that the text names, or null for no text or a name that is not off or smaa.</summary>
    private static AntiAliasing? AntiAliasingOf(string? text) =>
        text switch
        {
            "off" => AntiAliasing.None,
            "smaa" => AntiAliasing.Smaa,
            _ => null,
        };

    /// <summary>The size that the text <c>&lt;width&gt;x&lt;height&gt;</c> gives, or null for no text or when a side is not a whole number of 1 to <see cref="ScriptWindow.MaxSide"/>.</summary>
    private static ScriptWindow? SizeOf(string? text)
    {
        if (text?.Split('x') is not [string widthText, string heightText])
            return null;
        if (
            !int.TryParse(
                s: widthText,
                style: NumberStyles.None,
                provider: CultureInfo.InvariantCulture,
                result: out int width
            )
        )
            return null;
        if (
            !int.TryParse(
                s: heightText,
                style: NumberStyles.None,
                provider: CultureInfo.InvariantCulture,
                result: out int height
            )
        )
            return null;
        bool fits = width is >= 1 and <= ScriptWindow.MaxSide && height is >= 1 and <= ScriptWindow.MaxSide;
        return fits ? new ScriptWindow(width, height) : null;
    }
}
