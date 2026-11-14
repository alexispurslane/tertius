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
/// <item><c>--gpu-run FIRST..LAST</c> adds the inclusive sum of consecutive passes to a headless timing report.
/// Repeat it for several runs. Pass names match the recorded names, including spaces and case.</item>
/// <item><c>--tone-curve aces|hable</c> chooses the headless renderer's <see cref="ToneCurve"/>, ACES fitted without the
/// option.</item>
/// <item><c>--screen-trace on|off</c> keeps the headless renderer's default <see cref="ScreenTrace"/>, or turns the
/// screen tracer off, so that every surface takes the environment's closed forms.</item>
/// <item><c>--bloom on|off</c> keeps the headless renderer's default <see cref="Bloom"/>, or turns it off.</item>
/// <item><c>--upscaling off|fsr3</c> chooses native rendering or FSR 3.1. FSR is the default.</item>
/// <item><c>--render-scale SCALE</c> sets FSR's fixed headless render scale, from 0.5 to 1. The default is 2/3.</item>
/// <item><c>--local-shadows off|on</c> turns the headless renderer's <see cref="RendererOptions.LocalShadows"/> off, or
/// keeps the default atlas. <c>--local-shadow-tiles N</c> gives that atlas N tiles.</item>
/// <item><c>--cascaded-lights N</c> sets the headless renderer's <see cref="RendererOptions.CascadedLights"/>, 0 to 4.</item>
/// <item><c>--environment-map on|off</c> keeps the headless renderer's default <see cref="SceneEnvironmentMap"/>, or turns
/// it off.</item>
/// <item><c>--subsurface on|off</c> keeps the headless renderer's default <see cref="SeparableSubsurface"/>, or turns the
/// skin scatter off.</item>
/// <item><c>--in-flight 1|2</c> sets the headless renderer's frame count. One waits for each capture, and two keep both
/// frames in flight, as a window does, so that a timed film keeps the GPU at its top clock
/// (<see cref="RendererOptions.CapturesInFlight"/>).</item>
/// <item><c>--pass-timestamps on|off</c> keeps a GPU timestamp after every pass, the headless default, or stamps only the
/// render scale's cost groups, as the window does, so that a timed run measures the window's frame span.</item>
/// <item><c>--exposure STOPS</c> gives the headless renderer a <see cref="FixedExposure"/> of that many stops, from −64 to
/// 64, so that two timed runs whose images differ light their pixels alike (ticket t2-bne2). Without it the exposure
/// follows the view.</item>
/// <item><c>--key-lights off|N</c> chooses <see cref="EveryLightPair"/>, or <see cref="KeyAndFill"/> with N key
/// lights, 1 to 4. Without it the renderer keeps its default (<see cref="RendererOptions.LightPairs"/>).</item>
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

    /// <summary>
    /// The line that a headless run with a renderer prints when its first shown frame has finished, the start of its timed
    /// span. tools/gpu_clock.py takes the GPU clock of gpu-clock's samples between this line and <see cref="TimedEnd"/>
    /// (tools/README.md, "The GPU clock of a timed run").
    /// </summary>
    public const string TimedStart = "timed-start";

    /// <summary>The line that a headless run prints when the script's steps end, after <see cref="TimedStart"/>.</summary>
    public const string TimedEnd = "timed-end";

    /// <summary>Why individual pass times are omitted when the renderer's stamps latch late (ARCHITECTURE.md, "GPU timings").</summary>
    public const string LateStampsNote =
        "On MoltenVK a pass's GPU time can hold part of the next pass's work: only the span and the sums of neighbouring passes"
        + " hold. Individual pass times are omitted; raw per-pass JSON values are not valid alone."
        + " Read each pass's own time from a trace (tools/README.md, \"GPU trace\").";

    /// <summary>
    /// The file in the output folder of a headless run with a renderer that holds the exposure of each shown frame, in the
    /// order shown.
    /// </summary>
    public const string ExposureFile = "exposure.json";

    private const string Usage =
        "Usage: --script <file> --headless --out <folder> [--video none|<file.mkv> [--fps <rate>]] [--size <width>x<height>] [--gpu-run FIRST..LAST]... [--tone-curve aces|hable] [--screen-trace on|off] [--bloom on|off] [--upscaling off|fsr3] [--render-scale <scale>] [--local-shadows off|on] [--local-shadow-tiles <count>] [--cascaded-lights <count>] [--environment-map on|off] [--subsurface on|off] [--in-flight 1|2] [--pass-timestamps on|off] [--exposure <stops>] [--key-lights off|<count>]  |  --script <file> --out <folder> [--speed <factor>] [--size <width>x<height>]  |  --scene <name> [--speed <factor>] [--record <file>]";

    // The value of --video that draws the captures and drops them (Discard), for a run that only times its frames.
    private const string NoVideo = "none";

    private static readonly JsonSerializerOptions ReportJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // The options of the headless renderer, in the order of the usage line, each with the function that gives the problem
    // with its value, or null for a value that it takes. One of them also reads another option's value:
    // --local-shadow-tiles.
    private static readonly (
        string Option,
        Func<GivenOptions, string, string?> ValueProblem
    )[] HeadlessRendererOptions =
    [
        ("--tone-curve", (_, curve) => ToneCurveProblem(curve)),
        ("--screen-trace", (_, value) => OnOffProblem("--screen-trace", value)),
        ("--bloom", (_, value) => OnOffProblem("--bloom", value)),
        ("--upscaling", (_, upscaling) => UpscalingProblem(upscaling)),
        (
            "--render-scale",
            (_, scale) => RenderScaleOf(scale) is null ? "--render-scale needs a finite number from 0.5 to 1." : null
        ),
        ("--local-shadows", (_, localShadows) => LocalShadowsProblem(localShadows)),
        ("--local-shadow-tiles", LocalShadowTilesProblem),
        ("--cascaded-lights", (_, count) => CascadedLightsProblem(count)),
        ("--environment-map", (_, value) => OnOffProblem("--environment-map", value)),
        ("--subsurface", (_, value) => OnOffProblem("--subsurface", value)),
        ("--in-flight", (_, count) => InFlightProblem(count)),
        ("--pass-timestamps", (_, value) => OnOffProblem("--pass-timestamps", value)),
        ("--exposure", (_, stops) => ExposureProblem(stops)),
        ("--key-lights", (_, count) => KeyLightsProblem(count)),
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
        new(
            Option: "--gpu-run",
            Stray: "--gpu-run applies to a headless run, so add --headless.",
            ValueProblem: (_, run) => GpuTimingSummary.ReadRun(run).Problem
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
        "--gpu-run",
        .. HeadlessRendererOptions.Select(renderer => renderer.Option),
    ];

    /// <summary>
    /// Runs the command. Returns the exit code. Writes the path of each dump and each captured image, or of the video, to
    /// <paramref name="output"/>, and every problem and the world's log to <paramref name="error"/>. Makes a headless
    /// renderer only for a script with a capture or film step, so a script with dumps only runs on a machine with no GPU.
    /// A headless renderer that times its passes adds <see cref="GpuTimingsFile"/>, its path, the frame's GPU span and
    /// requested pass-run sums. Individual pass lines are omitted when the renderer's stamps latch late.
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
            FreePlayRun free => RunFreePlay(free: free, scenes: scenes, output: output, error: error),
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
            headless: headless,
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
    /// Plays the script in <paramref name="world"/> with the headless capture and timing choices, then writes the files
    /// and renderer readings. Returns the exit code. A null renderer plays only steps that need no rendering.
    /// </summary>
    private static int Play(
        ScriptRun run,
        Headless headless,
        Script script,
        Scene scene,
        World world,
        VulkanRenderer? renderer,
        Func<VideoFile, ScriptWindow, FfmpegVideoSink> startVideo,
        TextWriter output,
        TextWriter error
    )
    {
        CaptureTarget target = headless.TargetIn(run.Out);
        var readings = new FrameReadings();
        if (renderer is null && readings.Timings.Runs(headless.GpuRuns).Problem is { } timingProblem)
        {
            error.WriteLine(timingProblem);
            return CommandLineError;
        }
        HeadlessPlayback? playback = null;
        if (renderer != null)
        {
            Result<HeadlessPlayback, RunFailure> started = StartPlayback(
                renderer: renderer,
                size: script.Window,
                target: target,
                startVideo: startVideo,
                readings: readings,
                scriptPath: run.Script,
                output: output
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
            // The timed span ends when the GPU finished the frames in flight, which their hand-over waits for.
            playback?.FinishCaptures();
            if (readings.Exposures.Count > 0)
                output.WriteLine(TimedEnd);
            playback?.Finish();
            WriteFiles(result: result, outputFolder: run.Out, target: target, output: output);
            if (renderer != null)
            {
                string? problem = WriteReadings(
                    readings: readings,
                    renderer: renderer,
                    size: script.Window,
                    captures: result.Captures,
                    gpuRuns: headless.GpuRuns,
                    outputFolder: run.Out,
                    output: output
                );
                if (problem is not null)
                {
                    error.WriteLine(problem);
                    return CommandLineError;
                }
            }
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
    private static string? WriteReadings(
        FrameReadings readings,
        VulkanRenderer renderer,
        ScriptWindow size,
        IReadOnlyList<string> captures,
        IReadOnlyList<string> gpuRuns,
        string outputFolder,
        TextWriter output
    )
    {
        string? problem = WriteGpuTimings(
            timings: readings.Timings,
            device: renderer.DeviceName,
            stampsLatchLate: renderer.StampsLatchLate,
            size: size,
            runs: gpuRuns,
            outputFolder: outputFolder,
            output: output
        );
        if (problem is not null)
            return problem;
        WriteExposures(stops: readings.Exposures, captures: captures, outputFolder: outputFolder, output: output);
        return null;
    }

    /// <summary>
    /// The playback that draws the captures into <paramref name="target"/> and adds each shown frame to
    /// <paramref name="readings"/> when its capture is handed over, or why it cannot start: the video's ffmpeg or folder.
    /// It writes <see cref="TimedStart"/> to <paramref name="output"/> when the first shown frame has finished.
    /// </summary>
    private static Result<HeadlessPlayback, RunFailure> StartPlayback(
        VulkanRenderer renderer,
        ScriptWindow size,
        CaptureTarget target,
        Func<VideoFile, ScriptWindow, FfmpegVideoSink> startVideo,
        FrameReadings readings,
        string scriptPath,
        TextWriter output
    )
    {
        try
        {
            return new HeadlessPlayback(
                renderer: renderer,
                size: size,
                target: target,
                startVideo: startVideo,
                handedOver: () =>
                {
                    readings.Add(renderer.LastFrameStats);
                    // The timed span starts after the first shown frame, which holds the creation of the pipelines and
                    // the warm-up draws of a still at the GPU's low clock.
                    if (readings.Exposures.Count == 1)
                        output.WriteLine(TimedStart);
                }
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
            Upscaling = new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
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
    private static int RunFreePlay(FreePlayRun free, IReadOnlyList<Scene> scenes, TextWriter output, TextWriter error)
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
    /// then its path, valid console timings and requested run sums. Returns a range error without writing a report.
    /// Writes nothing when no frame was timed and no runs were requested.
    /// </summary>
    internal static string? WriteGpuTimings(
        GpuTimingSummary timings,
        string device,
        bool stampsLatchLate,
        ScriptWindow size,
        IReadOnlyList<string> runs,
        string outputFolder,
        TextWriter output
    )
    {
        Result<IReadOnlyList<RunSummary>, string> summarized = timings.Runs(runs);
        if (summarized.Problem is { } problem)
            return problem;
        if (timings.Frame() is not { } frame)
            return null;
        IReadOnlyList<PassSummary> passes = timings.Passes();
        IReadOnlyList<RunSummary> runSummaries = summarized.Ok ?? throw new UnreachableException();
        string path = Path.Combine(outputFolder, GpuTimingsFile);
        Directory.CreateDirectory(outputFolder);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new GpuTimingReport(
                    Device: device,
                    Width: size.Width,
                    Height: size.Height,
                    Frame: frame,
                    Passes: passes
                )
                {
                    StampsLatchLate = stampsLatchLate,
                    Runs = runSummaries,
                },
                ReportJson
            )
        );
        output.WriteLine(path);
        if (!stampsLatchLate)
            foreach (PassSummary pass in passes)
                WriteTiming(
                    pass.Pass,
                    new SpanSummary(
                        Frames: pass.Frames,
                        MeanMs: pass.MeanMs,
                        MedianMs: pass.MedianMs,
                        P95Ms: pass.P95Ms
                    ),
                    output
                );
        if (stampsLatchLate)
            output.WriteLine(LateStampsNote);
        WriteTiming("frame span", frame, output);
        foreach (RunSummary run in runSummaries)
            WriteTiming($"run {run.First}..{run.Last}", run.Timing, output);
        return null;
    }

    private static void WriteTiming(string name, SpanSummary timing, TextWriter output) =>
        output.WriteLine(
            FormattableString.Invariant(
                $"GPU {name}: mean {timing.MeanMs:F3} ms, median {timing.MedianMs:F3} ms, p95 {timing.P95Ms:F3} ms over {timing.Frames} frames"
            )
        );

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
    )
    {
        /// <summary>Whether individual pass timings can contain neighbouring work.</summary>
        public bool StampsLatchLate { get; init; }

        /// <summary>The requested inclusive pass runs, with statistics of their per-frame sums.</summary>
        public IReadOnlyList<RunSummary> Runs { get; init; } = [];

        /// <summary>Why raw individual pass values cannot be used alone, or null on an ordinary timestamp driver.</summary>
        public string? Warning => StampsLatchLate ? LateStampsNote : null;
    }

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
    private sealed record GivenOptions(IReadOnlyDictionary<string, List<string>> Values, bool Headless)
    {
        public string? Value(string option) => Values.GetValueOrDefault(option)?.LastOrDefault();

        public IReadOnlyList<string> AllValues(string option) =>
            Values.TryGetValue(option, out List<string>? values) ? values : Array.Empty<string>();

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
                if (!Values.ContainsKey(rule.Option))
                    continue;
                if (!taken.Contains(rule.Option))
                    return rule.Stray;
                IEnumerable<string> checkedValues =
                    rule.Option == "--gpu-run"
                        ? AllValues(rule.Option)
                        : [Value(rule.Option) ?? throw new UnreachableException()];
                foreach (string value in checkedValues)
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
        var values = new Dictionary<string, List<string>>();
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

            if (!values.TryGetValue(option, out List<string>? optionValues))
            {
                optionValues = [];
                values.Add(option, optionValues);
            }
            optionValues.Add(args[++i]);
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
            new FreePlayRun(Scene: scene, Speed: SpeedOf(given.Value("--speed")) ?? 1, Record: given.Value("--record"))
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
            {
                GpuRuns = given.AllValues("--gpu-run"),
            }
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
            ScreenTraceOff = given.Value("--screen-trace") == "off",
            BloomOff = given.Value("--bloom") == "off",
            Upscaling = UpscalingOf(given.Value("--upscaling")),
            RenderScale = RenderScaleOf(given.Value("--render-scale")),
            LocalShadows = LocalShadowsOf(given.Value("--local-shadows"), given.Value("--local-shadow-tiles")),
            CascadedLights = CascadedLightsOf(given.Value("--cascaded-lights")),
            EnvironmentMapOff = given.Value("--environment-map") == "off",
            SubsurfaceOff = given.Value("--subsurface") == "off",
            PassTimestampsOff = given.Value("--pass-timestamps") == "off",
            FramesInFlight = given.Value("--in-flight") is { } count
                ? int.Parse(count, CultureInfo.InvariantCulture)
                : null,
            ExposureStops = ExposureOf(given.Value("--exposure")),
            KeyLights = KeyLightsOf(given.Value("--key-lights")),
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

    /// <summary>The problem with the count of <c>--in-flight</c>, which takes 1 or 2, or null.</summary>
    private static string? InFlightProblem(string count) =>
        count is "1" or "2" ? null : $"--in-flight needs 1 or 2, not '{count}'.";

    /// <summary>The problem with a switch of the headless renderer, such as <c>--bloom</c>, which takes on or off, or null.</summary>
    private static string? OnOffProblem(string option, string value) =>
        value is "on" or "off" ? null : $"{option} needs on or off, not '{value}'.";

    private static string? UpscalingProblem(string upscaling) =>
        UpscalingOf(upscaling) is null ? $"--upscaling needs off or fsr3, not '{upscaling}'." : null;

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

    private static string? ExposureProblem(string stops) =>
        ExposureOf(stops) is null
            ? $"--exposure needs a number of stops from -{ExposureSetting.StopsLimit} to {ExposureSetting.StopsLimit}, not '{stops}'."
            : null;

    private static string? KeyLightsProblem(string count) =>
        KeyLightsOf(count) is null
            ? $"--key-lights needs off or a whole number from 1 to {KeyAndFill.MaxKeyLights}, not '{count}'."
            : null;

    /// <summary>The fixed exposure in stops that the text gives, or null for no text or one outside −64 to 64.</summary>
    private static float? ExposureOf(string? text)
    {
        bool read = float.TryParse(
            s: text,
            style: NumberStyles.Float,
            provider: CultureInfo.InvariantCulture,
            result: out float stops
        );
        return read && MathF.Abs(stops) <= ExposureSetting.StopsLimit ? stops : null;
    }

    /// <summary>The key lights that the text gives, 0 for <c>off</c>, or null for no text or one that is neither off nor 1 to 4.</summary>
    private static int? KeyLightsOf(string? text) =>
        text == "off" ? 0 : WholeNumberOf(text: text, least: 1, most: KeyAndFill.MaxKeyLights);

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

    /// <summary>The fixed render scale, or null for no text or a value outside the finite range 0.5 to 1.</summary>
    private static float? RenderScaleOf(string? text) =>
        float.TryParse(
            s: text,
            style: NumberStyles.Float,
            provider: CultureInfo.InvariantCulture,
            result: out float scale
        ) && scale is >= 0.5f and <= 1
            ? scale
            : null;

    private static UpscalingSetting? UpscalingOf(string? text) =>
        text switch
        {
            "off" => new NoUpscaling(),
            "fsr3" => Fsr3Upscaling.Default,
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
