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
/// <item><c>--script &lt;file&gt; --headless --out &lt;folder&gt; [--video none|&lt;file.mkv&gt; [--fps &lt;rate&gt;]]</c> runs a script
/// with no window, as fast as it can. With <c>--video</c>, its captures become the frames of one video, which ffmpeg
/// encodes, at <c>--fps</c> frames per second, 30 without the option. With <c>--video none</c>, they are drawn and dropped,
/// for a run that only times them. Its dumps still go into the output folder.</item>
/// <item><c>--script &lt;file&gt; --out &lt;folder&gt; [--speed &lt;factor&gt;]</c> plays a script in a window.</item>
/// <item><c>--size &lt;width&gt;x&lt;height&gt;</c>, such as <c>--size 1920x1080</c>, replaces the size of a script's window
/// in either run.</item>
/// <item><c>--tone-curve aces|hable</c> chooses the headless renderer's <see cref="ToneCurve"/>, ACES fitted without the
/// option.</item>
/// <item><c>--screen-space-light off|&lt;meters&gt;</c> turns the headless renderer's screen-space light off, or sets its
/// radius in m. <c>--screen-space-light-steps N</c> gives it N steps on each side of a slice, 1 to 16. Without either
/// option, the renderer keeps its default.</item>
/// <item><c>--bloom on|off</c> keeps the headless renderer's default <see cref="Bloom"/>, or turns it off.</item>
/// <item><c>--reflections on|off</c> keeps the headless renderer's default <see cref="ConeTracedReflections"/>, or turns
/// them off.</item>
/// <item><c>--anti-aliasing off|smaa</c> chooses the headless renderer's <see cref="AntiAliasing"/>, SMAA without the
/// option.</item>
/// <item><c>--local-shadows off|on</c> turns the headless renderer's <see cref="RendererOptions.LocalShadows"/> off, or
/// keeps the default atlas. <c>--local-shadow-tiles N</c> gives that atlas N tiles.</item>
/// <item><c>--cascaded-lights N</c> sets the headless renderer's <see cref="RendererOptions.CascadedLights"/>, 0 to 4.</item>
/// <item><c>--environment-map on|off</c> keeps the headless renderer's default <see cref="SceneEnvironmentMap"/>, or turns
/// it off.</item>
/// <item><c>--scene &lt;name&gt; [--speed &lt;factor&gt;] [--record &lt;file&gt;]</c> runs a scene live in a window until you close it.
/// With <c>--record</c>, the command writes your session to the file as a script that replays it.</item>
/// </list>
/// Each run builds its scene in a new world on the Jolt backend.
/// </summary>
public static partial class ScriptCommand
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

    /// <summary>The line after the pass times of a run whose renderer's stamps latch late (ARCHITECTURE.md, "GPU timings").</summary>
    public const string LateStampsNote =
        "On MoltenVK a pass's GPU time can hold part of the next pass's work: only the span and the sums of neighbouring passes"
        + " hold. Read each pass's own time from a trace (tools/README.md, \"GPU trace\").";

    /// <summary>
    /// The file in the output folder of a headless run with a renderer that holds the exposure of each shown frame, in the
    /// order shown.
    /// </summary>
    public const string ExposureFile = "exposure.json";

    private const string Usage =
        "Usage: --script <file> --headless --out <folder> [--video none|<file.mkv> [--fps <rate>]] [--size <width>x<height>] [--tone-curve aces|hable] [--screen-space-light off|<meters>] [--screen-space-light-steps <count>] [--bloom on|off] [--reflections on|off] [--anti-aliasing off|smaa] [--local-shadows off|on] [--local-shadow-tiles <count>] [--cascaded-lights <count>] [--environment-map on|off]  |  --script <file> --out <folder> [--speed <factor>] [--size <width>x<height>]  |  --scene <name> [--speed <factor>] [--record <file>]";

    // The value of --video that draws the captures and drops them (Discard), for a run that only times its frames.
    private const string NoVideo = "none";

    private static readonly JsonSerializerOptions ReportJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // The options of the headless renderer, in the order of the usage line, each with the function that gives the problem
    // with its value, or null for a value that it takes. Two of them also read another option's value:
    // --screen-space-light-steps and --local-shadow-tiles.
    private static readonly (
        string Option,
        Func<GivenOptions, string, string?> ValueProblem
    )[] HeadlessRendererOptions =
    [
        ("--tone-curve", (_, curve) => ToneCurveProblem(curve)),
        ("--screen-space-light", (_, light) => ScreenSpaceLightProblem(light)),
        ("--screen-space-light-steps", ScreenSpaceLightStepsProblem),
        ("--bloom", (_, value) => OnOffProblem("--bloom", value)),
        ("--reflections", (_, value) => OnOffProblem("--reflections", value)),
        ("--anti-aliasing", (_, antiAliasing) => AntiAliasingProblem(antiAliasing)),
        ("--local-shadows", (_, localShadows) => LocalShadowsProblem(localShadows)),
        ("--local-shadow-tiles", LocalShadowTilesProblem),
        ("--cascaded-lights", (_, count) => CascadedLightsProblem(count)),
        ("--environment-map", (_, value) => OnOffProblem("--environment-map", value)),
    ];

    // The options that choose the run, each with the problem when a run that does not take it is given it. A command line
    // gets the problem of the first of these before the missing --out of a script run, and before every other option.
    private static readonly (string Option, string Stray)[] RunChoiceOptions =
    [
        ("--headless", "--scene is free play, and free play needs the window. Leave out --headless."),
        ("--record", "--record applies to free play, so give --scene. A script is a record already."),
        ("--out", "--out applies to a script. Free play writes only the file that --record names."),
    ];

    // Every other option but --script and --scene, in the order of the usage line. Each option is checked in turn, first
    // for its place in the run and then for its value, so a command line gets the problem of the first option that has
    // one. Declared after HeadlessRendererOptions, which its initializer reads.
    private static readonly OptionRule[] OptionRules =
    [
        new(
            Option: "--speed",
            Stray: "--speed applies to the window. A headless run runs its ticks back to back.",
            ValueProblem: (_, speed) => SpeedProblem(speed)
        ),
        new(
            Option: "--video",
            Stray: "--video applies to a headless run. The window writes PNG files, so add --headless or leave out --video.",
            ValueProblem: (_, video) => VideoProblem(video)
        ),
        new(Option: "--fps", Stray: "--fps applies to a video, so give --video too.", ValueProblem: FpsProblem),
        new(
            Option: "--size",
            Stray: "--size applies to a script, so give --script.",
            ValueProblem: (_, size) => SizeProblem(size)
        ),
        .. HeadlessRendererOptions.Select(renderer => new OptionRule(
            Option: renderer.Option,
            Stray: $"{renderer.Option} applies to a headless run, so add --headless.",
            ValueProblem: renderer.ValueProblem
        )),
    ];

    // The options that take a value: every option of the tables above but --headless, which takes none. Declared after
    // them, which its initializer reads, so that an option that the tables do not check is unknown.
    private static readonly string[] ValueOptions =
    [
        "--script",
        "--scene",
        .. RunChoiceOptions.Select(choice => choice.Option).Where(option => option != "--headless"),
        .. OptionRules.Select(rule => rule.Option),
    ];

    // The options of the tables above that each run takes. Declared after HeadlessRendererOptions, which they read.
    private static readonly string[] FreePlayOptions = ["--speed", "--record"];
    private static readonly string[] WindowOptions = ["--out", "--speed", "--size"];
    private static readonly string[] HeadlessOptions =
    [
        "--headless",
        "--out",
        "--video",
        "--fps",
        "--size",
        .. HeadlessRendererOptions.Select(renderer => renderer.Option),
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

        return options switch
        {
            FreePlay free => RunFreePlay(free: free, scenes: scenes, output: output, error: error),
            ScriptRun run => RunScript(
                run: run,
                scenes: scenes,
                createRenderer: createRenderer,
                startVideo: startVideo ?? FfmpegVideoSink.Start,
                output: output,
                error: error
            ),
            null => throw new UnreachableException("ReadOptions gives free play or a script run."),
        };
    }

    /// <summary>
    /// Runs a script in a window or headless, after it loads the script and finds its scene. Makes a headless renderer
    /// with <paramref name="createRenderer"/> only for a script with a capture or film step.
    /// </summary>
    private static int RunScript(
        ScriptRun run,
        IReadOnlyList<Scene> scenes,
        Func<RendererOptions, VulkanRenderer> createRenderer,
        Func<VideoFile, ScriptWindow, FfmpegVideoSink> startVideo,
        TextWriter output,
        TextWriter error
    )
    {
        Result<Script, RunFailure> loaded = ScriptOf(run);
        if (loaded.Problem is { Code: var code, Message: var message })
        {
            error.WriteLine(message);
            return code;
        }

        Script script = loaded.Ok ?? throw new UnreachableException("A result without a problem holds its script.");
        Scene? scene = scenes.FirstOrDefault(s => s.Name == script.Scene);
        if (scene is null)
        {
            error.WriteLine(
                $"{run.Script}: no scene is named '{script.Scene}'. The scenes are: {string.Join(", ", scenes.Select(s => s.Name))}."
            );
            return ScriptError;
        }

        return run.Display switch
        {
            InWindow(var speed) => WindowHost.Run(
                sceneToBuild: scene,
                script: script,
                scriptPath: run.Script,
                outputFolder: run.Out,
                speed: speed,
                recordPath: null,
                output: output,
                error: error
            ),
            Headless headless => RunHeadless(
                run: run,
                headless: headless,
                script: script,
                scene: scene,
                createRenderer: HasCaptures(script) ? createRenderer : null,
                startVideo: startVideo,
                output: output,
                error: error
            ),
            null => throw new UnreachableException("ReadOptions gives a script run its display."),
        };
    }

    /// <summary>
    /// The script of <paramref name="run"/> with the window of <c>--size</c>, or why the run cannot use it: it cannot be
    /// read, or the run's video takes captures that the script does not have.
    /// </summary>
    private static Result<Script, RunFailure> ScriptOf(ScriptRun run)
    {
        Script script;
        try
        {
            script = Script.Load(run.Script);
        }
        catch (ScriptException ex)
        {
            return new RunFailure(ScriptError, $"{run.Script}: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RunFailure(FileError, $"Cannot read the script {run.Script}: {ex.Message}");
        }

        bool takesCaptures = run.Display is Headless { Video: not null };
        if (takesCaptures && !HasCaptures(script))
            return new RunFailure(
                CommandLineError,
                $"--video takes the captures, into a video or into nothing, and {run.Script} has no capture or film step."
                    + Environment.NewLine
                    + Usage
            );
        return run.Size is { } size ? script with { Window = size } : script;
    }

    /// <summary>True if the script draws a frame: it has a capture or a film step.</summary>
    private static bool HasCaptures(Script script) => script.Steps.Any(step => step is CaptureStep or FilmStep);

    /// <summary>
    /// Runs a script with no window, as fast as it can. Makes a renderer with <paramref name="createRenderer"/>, which is
    /// null for a script with no capture or film step, and a playback that draws the captures into the run's target.
    /// </summary>
    private static int RunHeadless(
        ScriptRun run,
        Headless headless,
        Script script,
        Scene scene,
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
                overrides: headless.Renderer,
                scriptPath: run.Script,
                error: error
            );
            if (renderer is null)
                return ToolError;
        }

        using VulkanRenderer? rendererToDispose = renderer;
        return Play(
            run: run,
            target: headless.TargetIn(run.Out),
            script: script,
            scene: scene,
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
        ScriptRun run,
        CaptureTarget target,
        Script script,
        Scene scene,
        World world,
        VulkanRenderer? renderer,
        Func<VideoFile, ScriptWindow, FfmpegVideoSink> startVideo,
        TextWriter output,
        TextWriter error
    )
    {
        var readings = new FrameReadings();
        HeadlessPlayback? playback = null;
        if (renderer != null)
        {
            Result<HeadlessPlayback, RunFailure> started = StartPlayback(
                renderer: renderer,
                size: script.Window,
                target: target,
                startVideo: startVideo,
                readings: readings,
                scriptPath: run.Script
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
                outputFolder: run.Out,
                playback: playback
            );
            playback?.Finish();
            WriteFiles(result: result, outputFolder: run.Out, target: target, output: output);
            if (renderer != null)
                WriteReadings(
                    readings: readings,
                    renderer: renderer,
                    size: script.Window,
                    captures: result.Captures,
                    outputFolder: run.Out,
                    output: output
                );
            return Ok;
        }
        catch (Exception ex)
        {
            (int failure, string message) = Failure(ex, run);
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
    private static Result<HeadlessPlayback, RunFailure> StartPlayback(
        VulkanRenderer renderer,
        ScriptWindow size,
        CaptureTarget target,
        Func<VideoFile, ScriptWindow, FfmpegVideoSink> startVideo,
        FrameReadings readings,
        string scriptPath
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
        catch (Win32Exception ex) when (target is VideoFile file)
        {
            return new RunFailure(
                ToolError,
                $"Cannot start ffmpeg for the video {file.Path} of {scriptPath}: {ex.Message}"
            );
        }
        // The folder of the video cannot be made: a file problem, as for a dump or an image.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && target is VideoFile file)
        {
            return new RunFailure(FileError, $"Cannot write the video {file.Path} of {scriptPath}: {ex.Message}");
        }
    }

    /// <summary>Why a script run cannot start: the exit code and the message.</summary>
    private sealed record RunFailure(int Code, string Message);

    /// <summary>The headless renderer, or null after writing why it cannot start to <paramref name="error"/>.</summary>
    private static VulkanRenderer? StartRenderer(
        Func<RendererOptions, VulkanRenderer> createRenderer,
        ScriptWindow size,
        RendererOverrides overrides,
        string scriptPath,
        TextWriter error
    )
    {
        var rendererOptions = new RendererOptions
        {
            Width = size.Width,
            Height = size.Height,
            Impostors = false,
        };
        overrides.ApplyTo(rendererOptions);
        try
        {
            return createRenderer(rendererOptions);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Cannot start the headless renderer for the captures of {scriptPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The exit code and the message of an exception that stopped a headless run after it started.</summary>
    private static (int Code, string Message) Failure(Exception ex, ScriptRun run) =>
        ex switch
        {
            ScriptException => (ScriptError, $"{run.Script}: {ex.Message}"),
            // ffmpeg failed, and the message gives the video, ffmpeg's exit code and its last error lines.
            VideoStreamException => (FileError, $"Cannot write the video of {run.Script}: {ex.Message}"),
            // A dump or an image that cannot be written: the same words as in the window.
            IOException or UnauthorizedAccessException => (FileError, $"Cannot write into {run.Out}: {ex.Message}"),
            // A bug, such as a broken contract that escapes World.Tick: the same exit code as in the window.
            _ => (InternalError, $"The run failed: {ex}"),
        };

    /// <summary>Runs the scene of <paramref name="free"/> live in a window, after it finds the scene before a window opens.</summary>
    private static int RunFreePlay(FreePlay free, IReadOnlyList<Scene> scenes, TextWriter output, TextWriter error)
    {
        Scene? live = scenes.FirstOrDefault(s => s.Name == free.Scene);
        if (live is null)
        {
            error.WriteLine(
                $"No scene is named '{free.Scene}'. The scenes are: {string.Join(", ", scenes.Select(s => s.Name))}."
            );
            return ScriptError;
        }

        return WindowHost.Run(
            sceneToBuild: live,
            script: null,
            scriptPath: null,
            outputFolder: null,
            speed: free.Speed,
            recordPath: free.Record,
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
            case VideoFile or Discard or null:
                // A video without captures holds no frames, and the discard target and no target write no files.
                break;
            default:
                throw new UnreachableException(
                    $"The capture target {target} has a case that the command does not list."
                );
        }
    }

    /// <summary>
    /// Writes <see cref="GpuTimingsFile"/> into the output folder with the summary of the frames' GPU span and of each pass,
    /// then its path, one line for each pass, <see cref="LateStampsNote"/> on a renderer whose stamps latch late, and one
    /// line for the span to <paramref name="output"/>. Writes nothing when no frame was timed.
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
        if (renderer.StampsLatchLate)
            output.WriteLine(LateStampsNote);
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

    /// <summary>
    /// The options, or null and the problem for an unknown option, a missing value, an option that the run does not take,
    /// or a value that the option does not take.
    /// </summary>
    internal static RunOptions? ReadOptions(string[] args, out string? problem)
    {
        if (Gather(args, out problem) is not { } given)
            return null;
        Result<RunOptions, string> read = Read(given);
        problem = read.Problem;
        return read.Ok;
    }

    /// <summary>The options of a command line as given, before the rules between them: the value of each option, and <c>--headless</c>.</summary>
    private sealed record GivenOptions(IReadOnlyDictionary<string, string> Values, bool Headless)
    {
        public string? Value(string option) => Values.GetValueOrDefault(option);

        /// <summary>
        /// The problem with the first option of <see cref="RunChoiceOptions"/> that the command line gives and that a run
        /// with the options <paramref name="taken"/> does not take, or null.
        /// </summary>
        public string? RunChoiceProblem(string[] taken) =>
            RunChoiceOptions
                .Where(choice => Gives(choice.Option) && !taken.Contains(choice.Option))
                .Select(choice => choice.Stray)
                .FirstOrDefault();

        /// <summary>
        /// The problem with the first option of <see cref="OptionRules"/> that the command line gives and that a run with
        /// the options <paramref name="taken"/> does not take, or whose value it does not take, or null.
        /// </summary>
        public string? OptionProblem(string[] taken)
        {
            foreach (OptionRule rule in OptionRules)
            {
                if (Value(rule.Option) is not { } value)
                    continue;
                if (!taken.Contains(rule.Option))
                    return rule.Stray;
                if (rule.ValueProblem(this, value) is { } problem)
                    return problem;
            }

            return null;
        }

        private bool Gives(string option) => option == "--headless" ? Headless : Values.ContainsKey(option);
    }

    /// <summary>An option that only some runs take, and its problems.</summary>
    /// <param name="Option">The option, such as <c>--speed</c>.</param>
    /// <param name="Stray">The problem when a run that does not take the option is given it.</param>
    /// <param name="ValueProblem">
    /// The problem with the option's value, given the options as given and the value, or null for a value that the option
    /// takes.
    /// </param>
    private readonly record struct OptionRule(
        string Option,
        string Stray,
        Func<GivenOptions, string, string?> ValueProblem
    );

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

    /// <summary>The run that the options as given choose, free play or a script run, or the problem with them.</summary>
    private static Result<RunOptions, string> Read(GivenOptions given) =>
        (given.Value("--script"), given.Value("--scene")) switch
        {
            (null, null) => "Give --script or --scene.",
            (not null, not null) => "Give --script or --scene, not both.",
            (null, { } scene) => ReadFreePlay(given, scene),
            ({ } script, null) => ReadScriptRun(given, script),
        };

    private static Result<RunOptions, string> ReadFreePlay(GivenOptions given, string scene)
    {
        if ((given.RunChoiceProblem(FreePlayOptions) ?? given.OptionProblem(FreePlayOptions)) is { } problem)
            return problem;
        return new RunOptions(
            new FreePlay(Scene: scene, Speed: SpeedOf(given.Value("--speed")) ?? 1, Record: given.Value("--record"))
        );
    }

    private static Result<RunOptions, string> ReadScriptRun(GivenOptions given, string script)
    {
        string[] taken = given.Headless ? HeadlessOptions : WindowOptions;
        if (given.RunChoiceProblem(taken) is { } choiceProblem)
            return choiceProblem;
        // Every script run needs its output folder, so its absence comes after the options that choose the run and before
        // the other options.
        if (given.Value("--out") is not { } outFolder)
            return "The option --out is missing.";
        if (given.OptionProblem(taken) is { } problem)
            return problem;
        ScriptDisplay display = given.Headless
            ? new Headless(
                Video: VideoOf(video: given.Value("--video"), fps: given.Value("--fps")),
                Renderer: RendererOverridesOf(given)
            )
            : new InWindow(Speed: SpeedOf(given.Value("--speed")) ?? 1);
        return new RunOptions(
            new ScriptRun(Script: script, Out: outFolder, Size: SizeOf(given.Value("--size")), Display: display)
        );
    }

    /// <summary>The overrides of the headless renderer that the options as given choose.</summary>
    private static RendererOverrides RendererOverridesOf(GivenOptions given) =>
        new()
        {
            ToneCurve = ToneCurveOf(given.Value("--tone-curve")),
            ScreenSpaceLight = ScreenSpaceLightOf(
                given.Value("--screen-space-light"),
                given.Value("--screen-space-light-steps")
            ),
            BloomOff = given.Value("--bloom") == "off",
            ReflectionsOff = given.Value("--reflections") == "off",
            AntiAliasing = AntiAliasingOf(given.Value("--anti-aliasing")),
            LocalShadows = LocalShadowsOf(given.Value("--local-shadows"), given.Value("--local-shadow-tiles")),
            CascadedLights = CascadedLightsOf(given.Value("--cascaded-lights")),
            EnvironmentMapOff = given.Value("--environment-map") == "off",
        };

    private static string? SpeedProblem(string speed) =>
        SpeedOf(speed) is null ? $"--speed needs a positive number, such as 0.5 or 2, not '{speed}'." : null;

    private static string? VideoProblem(string video) =>
        video is not NoVideo && !VideoFile.HasVideoExtension(video)
            ? $"--video needs {NoVideo} or a file whose name ends in {VideoFile.Extension}, not '{video}'."
            : null;

    private static string? FpsProblem(GivenOptions given, string fps) =>
        given.Value("--video") switch
        {
            null => "--fps applies to a video, so give --video too.",
            NoVideo => $"--fps sets the frame rate of a video file, and --video {NoVideo} writes no video.",
            _ when FpsOf(fps) is null =>
                $"--fps needs a whole number from {VideoFile.MinFramesPerSecond} to {VideoFile.MaxFramesPerSecond}, not '{fps}'.",
            _ => null,
        };

    private static string? SizeProblem(string size) =>
        SizeOf(size) is null
            ? $"--size needs a width and a height of 1 to {ScriptWindow.MaxSide} pixels, such as 1920x1080, not '{size}'."
            : null;

    private static string? ToneCurveProblem(string curve) =>
        ToneCurveOf(curve) is null ? $"--tone-curve needs aces or hable, not '{curve}'." : null;

    private static string? ScreenSpaceLightProblem(string light) =>
        light is "off" || RadiusOf(light) is not null
            ? null
            : $"--screen-space-light needs off or a radius in m, a positive number such as 0.5, not '{light}'.";

    private static string? ScreenSpaceLightStepsProblem(GivenOptions given, string steps)
    {
        if (StepsOf(steps) is null)
            return $"--screen-space-light-steps needs a whole number from 1 to {ScreenSpaceLightSetting.MaxStepsPerSide}, not '{steps}'.";
        if (given.Value("--screen-space-light") == "off")
            return "--screen-space-light-steps sets the steps of the estimate, so leave out --screen-space-light off.";
        return null;
    }

    /// <summary>The problem with a switch of the headless renderer, such as <c>--bloom</c>, which takes on or off, or null.</summary>
    private static string? OnOffProblem(string option, string value) =>
        value is "on" or "off" ? null : $"{option} needs on or off, not '{value}'.";

    private static string? AntiAliasingProblem(string antiAliasing) =>
        AntiAliasingOf(antiAliasing) is null ? $"--anti-aliasing needs off or smaa, not '{antiAliasing}'." : null;

    private static string? LocalShadowsProblem(string localShadows) =>
        localShadows is "off" or "on" ? null : $"--local-shadows needs off or on, not '{localShadows}'.";

    private static string? LocalShadowTilesProblem(GivenOptions given, string tiles)
    {
        if (TilesOf(tiles) is null)
            return $"--local-shadow-tiles needs a whole number of 1 or more, not '{tiles}'.";
        if (given.Value("--local-shadows") == "off")
            return "--local-shadow-tiles sets the budget of local shadows, so leave out --local-shadows off.";
        return null;
    }

    private static string? CascadedLightsProblem(string count) =>
        CascadedLightsOf(count) is null
            ? $"--cascaded-lights needs a whole number from 0 to {RendererOptions.MaxCascadedLights}, not '{count}'."
            : null;

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
    private static int? FpsOf(string? text) =>
        WholeNumberOf(text: text, least: VideoFile.MinFramesPerSecond, most: VideoFile.MaxFramesPerSecond);

    /// <summary>
    /// The choice of <c>--video</c> of a headless run: <see cref="Discard"/> for <c>--video none</c>, the video that
    /// <c>--video</c> names at its frame rate, or null without <c>--video</c>.
    /// </summary>
    private static HeadlessVideo? VideoOf(string? video, string? fps) =>
        video switch
        {
            null => null,
            NoVideo => new Discard(),
            _ => new VideoFile(video, FpsOf(fps) ?? VideoFile.DefaultFramesPerSecond),
        };

    /// <summary>The whole number that the text gives, or null for no text or one that is not a number from least to most.</summary>
    private static int? WholeNumberOf(string? text, int least, int most)
    {
        bool read = int.TryParse(
            s: text,
            style: NumberStyles.None,
            provider: CultureInfo.InvariantCulture,
            result: out int number
        );
        return read && number >= least && number <= most ? number : null;
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
    /// The screen-space light that the two texts give: none for <c>off</c>, else the renderer's default with the radius and
    /// the steps that are given, and null when neither option is given. The renderer refuses a radius above its limit.
    /// </summary>
    private static ScreenSpaceLightSetting? ScreenSpaceLightOf(string? light, string? steps)
    {
        if (light == "off")
            return new NoScreenSpaceLight();
        if (light is null && steps is null)
            return null;
        VisibilityBitmaskLight estimate = VisibilityBitmaskLight.Default;
        if (RadiusOf(light) is { } radius)
            estimate = estimate with { RadiusMeters = radius };
        if (StepsOf(steps) is { } count)
            estimate = estimate with { StepsPerSide = count };
        return estimate;
    }

    /// <summary>The radius in m that the text gives, or null for no text or one that is not a positive finite number.</summary>
    private static float? RadiusOf(string? text)
    {
        bool read = float.TryParse(
            s: text,
            style: NumberStyles.Float,
            provider: CultureInfo.InvariantCulture,
            result: out float radius
        );
        return read && float.IsFinite(radius) && radius > 0 ? radius : null;
    }

    /// <summary>The steps on each side of a slice that the text gives, or null for no text or one outside 1 to 16.</summary>
    private static int? StepsOf(string? text) =>
        WholeNumberOf(text: text, least: 1, most: ScreenSpaceLightSetting.MaxStepsPerSide);

    /// <summary>
    /// The local shadows that the two texts give: none for <c>off</c>, the default atlas for <c>on</c> or for a count of
    /// tiles alone, with that count, and null when neither option is given.
    /// </summary>
    private static LocalShadowSetting? LocalShadowsOf(string? localShadows, string? tiles)
    {
        if (localShadows == "off")
            return new NoLocalShadows();
        if (TilesOf(tiles) is { } count)
            return LocalShadowAtlas.Default with { Tiles = count };
        return localShadows == "on" ? LocalShadowAtlas.Default : null;
    }

    /// <summary>The count of tiles that the text gives, or null for no text or one that is not a whole number of 1 or more.</summary>
    private static int? TilesOf(string? text) => WholeNumberOf(text: text, least: 1, most: int.MaxValue);

    /// <summary>The count of far lights with cascades that the text gives, or null for no text or one outside 0 to 4.</summary>
    private static int? CascadedLightsOf(string? text) =>
        WholeNumberOf(text: text, least: 0, most: RendererOptions.MaxCascadedLights);

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
