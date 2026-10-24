using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Rendering;
using Tertius.Simulation;

namespace Tertius.Host.Windowing;

/// <summary>
/// The window: plays a script, or runs a scene live until it closes. See docs/plans/host-and-scripted-runs.md, "The
/// window". Three threads share the work, as ARCHITECTURE.md says: the main thread runs the window and its input, the
/// simulation thread runs the ticks and the script, and the render thread draws the latest render snapshot.
/// </summary>
/// <remarks>
/// The threads share only thread-safe things: the input queue and the view presses of <see cref="FreePlay"/> (the main
/// thread writes, the simulation thread reads), the render snapshots' triple buffer (the simulation thread writes, the
/// render thread reads), the <see cref="CaptureMailbox"/>, the fly camera under its lock, and the closing token. The
/// renderer belongs to the render thread, which disposes it before the window is destroyed, as MoltenVK needs.
/// </remarks>
[SuppressMessage("Design", "CA1001",
    Justification =
        "A CancellationTokenSource without a timer holds an OS handle only after a read of its WaitHandle, which this class never does.")]
internal sealed class WindowHost : IInputTarget
{
    private readonly IWindow window;
    private readonly VulkanRenderer renderer;
    private readonly World world;
    private readonly SceneInstance scene;
    private readonly FreePlay? freePlay;
    private readonly RecordTo? record;
    private readonly Script? script;

    private readonly string? outputFolder;

    // Where the captures of a script go: PNG files in its output folder. Free play has none, because it captures nothing.
    private readonly PngFolder? images;
    private readonly TickPacer pacer;
    private readonly CaptureMailbox captures = new();
    private readonly CancellationTokenSource closing = new();
    private readonly InputRouter router;
    private IInputContext? input;

    // The fly camera: the main thread sets its keys and turns it, the render thread starts, moves and applies it.
    private readonly Lock flyLock = new();
    private readonly FlyCamera fly = new();
    private bool flyActive;
    private bool flyStartWanted;

    // How the simulation thread ended. Read on the main thread after it joins.
    private RunResult? result;
    private string? scriptProblem;
    private Exception? failure;
    private int closeWanted;
    private int signalled;


    private WindowHost(IWindow window, VulkanRenderer renderer, World world, SceneInstance scene, Script? script,
        string? outputFolder, double speed, RecordTo? record)
    {
        this.window = window;
        this.renderer = renderer;
        this.world = world;
        this.scene = scene;
        // In free play, the window's input reaches the world through the free play. A script takes no live input.
        freePlay = script is null ? new FreePlay(world, scene, record) : null;
        this.record = record;
        this.script = script;
        this.outputFolder = outputFolder;
        images = outputFolder is null ? null : new PngFolder(outputFolder);
        pacer = new(world.DeltaTime, speed);
        router = new(freePlay: script is null, this);
    }

    /// <summary>
    /// Opens the window, builds <paramref name="sceneToBuild"/>, and plays <paramref name="script"/>, or runs the scene
    /// live if it is null, until the script ends or the window closes. With <paramref name="recordPath"/>, free play writes
    /// the session there as a script when it ends. Returns the exit code of <see cref="ScriptCommand"/>.
    /// Call it on the main thread, which macOS needs for a window.
    /// </summary>
    public static int Run(Scene sceneToBuild, Script? script, string? scriptPath, string? outputFolder, double speed,
        string? recordPath, TextWriter output, TextWriter error)
    {
        ScriptWindow size = script?.Window ?? ScriptWindow.Default;
        RecordTo? record = recordPath is null ? null : new RecordTo(recordPath, sceneToBuild.Name, size);
        IWindow? window = null;
        try
        {
            window = Window.Create(WindowOptions.DefaultVulkan with
            {
                Size = new(size.Width, size.Height),
                Title = $"Tertius: {sceneToBuild.Name}",
                IsEventDriven = true
            });
            window.Initialize();
        }
        // No display, or no Vulkan driver for the window: as for a renderer that cannot start.
        catch (Exception ex)
        {
            error.WriteLine($"Cannot open the window: {ex.Message}");
            window?.Dispose();
            return ScriptCommand.ToolError;
        }

        try
        {
            VulkanRenderer renderer;
            try
            {
                // Free play may use impostors. A script draws every object as geometry, as a headless run does.
                renderer = VulkanRenderer.CreateForWindow(window, new() { Impostors = script is null });
            }
            catch (Exception ex)
            {
                error.WriteLine($"Cannot start the renderer for the window: {ex.Message}");
                return ScriptCommand.ToolError;
            }

            var host = default(WindowHost);
            try
            {
                using var world = new World(new() { DisposePhysics = true }, new JoltPhysicsBackend());
                world.Log += error.WriteLine;
                SceneInstance scene = sceneToBuild.Build(world, renderer);
                world.PublishRenderSnapshot();
                host = new(window: window, renderer: renderer, world: world, scene: scene, script: script, outputFolder: outputFolder, speed: speed, record: record);
                host.Play();
                return host.Report(scriptPath, output, error);
            }
            finally
            {
                // Play hands the renderer to the render thread, which disposes it. Otherwise it is disposed here.
                if (host is null)
                    renderer.Dispose();
            }
        }
        finally
        {
            // A last pump that does not wait: event-driven, it would wait for an input event that may never come.
            window.IsEventDriven = false;
            window.DoEvents();
            window.Reset();
        }
    }

    /// <summary>Plays the window. While a record is being written, a stop signal such as Ctrl-C in the terminal closes the window instead of ending the process, so that the record is written.</summary>
    private void Play()
    {
        var signals = new List<PosixSignalRegistration>();
        if (record != null)
            foreach (PosixSignal signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP })
                try
                {
                    signals.Add(PosixSignalRegistration.Create(signal, CloseOnFirstSignal));
                }
                // A platform that has no such signal, such as SIGHUP on Windows, only loses that one way to stop.
                catch (PlatformNotSupportedException)
                {
                }

        try
        {
            RunThreads();
        }
        finally
        {
            foreach (PosixSignalRegistration registration in signals)
                registration.Dispose();
        }
    }

    // A second signal is not caught, so the user can still end a window that does not close.
    private void CloseOnFirstSignal(PosixSignalContext context)
    {
        if (Interlocked.Exchange(ref signalled, 1) != 0)
            return;
        context.Cancel = true;
        CloseWindow();
    }

    /// <summary>Starts the simulation and render threads, runs the window's events until a thread or the user closes it, then stops both threads.</summary>
    private void RunThreads()
    {
        input = window.CreateInput();
        foreach (IKeyboard keyboard in input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => router.Key(key, down: true);
            keyboard.KeyUp += (_, key, _) => router.Key(key, down: false);
        }

        foreach (IMouse device in input.Mice)
        {
            device.MouseMove += (_, position) => router.MouseMove(position);
            device.MouseDown += (_, button) => router.MouseButton(button, down: true);
            device.MouseUp += (_, button) => router.MouseButton(button, down: false);
            device.Scroll += (_, wheel) => router.Scroll(new(wheel.X, wheel.Y));
        }

        CaptureCursor();

        var renderThread = new Thread(RenderLoop) { Name = "Render" };
        var simulationThread = new Thread(SimulationLoop) { Name = "Simulation" };
        renderThread.Start();
        simulationThread.Start();
        while (!window.IsClosing && Volatile.Read(ref closeWanted) == 0)
            window.DoEvents();

        closing.Cancel();
        simulationThread.Join();
        renderThread.Join();
        input.Dispose();
    }

    /// <summary>Asks the main thread to close the window. Any thread.</summary>
    private void CloseWindow()
    {
        Volatile.Write(ref closeWanted, 1);
        window.ContinueEvents();
    }

    // ---- The simulation thread -----------------------------------------------------------------------------------------

    private void SimulationLoop()
    {
        var clock = Stopwatch.StartNew();
        var playback = new WindowPlayback(pacer: pacer, captures: captures, folder: images, secondsSinceStart: () => clock.Elapsed.TotalSeconds, stop: closing.Token);
        try
        {
            // The constructor makes free play exactly when there is no script.
            if (script is { } run)
                result = ScriptRunner.Run(script: run, world: world, scene: scene, outputFolder: outputFolder, playback: playback);
            else
                freePlay?.Run(playback);
        }
        catch (ScriptException ex)
        {
            scriptProblem = ex.Message;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            CloseWindow();
        }
    }

    // ---- The render thread ---------------------------------------------------------------------------------------------

    private void RenderLoop()
    {
        try
        {
            var frameClock = Stopwatch.StartNew();
            double lastFrame = 0;
            while (!closing.IsCancellationRequested)
            {
                if (captures.TryTake(out ICaptureSink? sink))
                {
                    // The simulation thread published this tick's snapshot and waits. A capture shows the world's camera.
                    world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot shot);
                    renderer.CaptureNextFrame(sink);
                    // A frame that the swapchain skips, for example while the window is minimized, draws nothing and
                    // hands the sink nothing. The renderer keeps the request, so the frame is drawn again until one is
                    // drawn or the window closes.
                    do
                    {
                        renderer.Render(shot, alpha: 1f);
                    } while (!renderer.LastFrameStats.Rendered && !closing.Token.WaitHandle.WaitOne(10));

                    if (renderer.LastFrameStats.Rendered)
                        captures.Done();
                    continue;
                }

                world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot);
                double now = frameClock.Elapsed.TotalSeconds;
                float frameSeconds = (float)(now - lastFrame);
                lastFrame = now;
                float alpha = pacer.Alpha(Stopwatch.GetElapsedTime(snapshot.PublishedAt).TotalSeconds);
                RenderCamera worldCamera = snapshot.Camera;
                lock (flyLock)
                {
                    if (flyStartWanted)
                    {
                        fly.Start(worldCamera.Rotation, snapshot.OriginFrameId);
                        flyStartWanted = false;
                    }
                    else if (flyActive)
                    {
                        fly.Follow(worldCamera.Rotation, snapshot.OriginFrameId);
                    }

                    if (flyActive)
                    {
                        fly.Advance(frameSeconds);
                        fly.Apply(ref snapshot.Camera);
                    }
                }

                renderer.Render(snapshot, alpha);
                // The snapshot keeps the world's camera, so a later frame of the same snapshot starts from it again.
                snapshot.Camera = worldCamera;
            }
        }
        catch (Exception ex)
        {
            failure ??= ex;
            CloseWindow();
        }
        finally
        {
            renderer.Dispose();
        }
    }

    // ---- Input, on the main thread: the router decides, these carry it out -----------------------------------------------

    // The router sends input to the game only in free play. The main thread queues it, and the simulation thread posts it.
    public void Post(InputEvent e) => freePlay?.Post(e);

    public void Fly(FlyKey key, bool down)
    {
        lock (flyLock)
        {
            fly.SetKey(key, down);
        }
    }

    public void Turn(Vector2 movement)
    {
        lock (flyLock)
        {
            fly.Turn(movement);
        }
    }

    public void FlyChanged(bool flying)
    {
        lock (flyLock)
        {
            flyActive = flying;
            // The render thread starts it at the world's camera, which only it can read.
            flyStartWanted = flying;
        }

        CaptureCursor();
    }

    // Raw mode gives the mouse movement without the pointer's acceleration, where the system offers it.
    private void CaptureCursor()
    {
        // Before the window loads, there is no input and no cursor to capture.
        if (input is null)
            return;
        foreach (IMouse device in input.Mice)
            device.Cursor.CursorMode = !router.CapturesCursor ? CursorMode.Normal :
                device.Cursor.IsSupported(CursorMode.Raw) ? CursorMode.Raw : CursorMode.Disabled;
    }

    public void Close() => window.Close();

    public void NextView() => freePlay?.NextView();

    // ---- The outcome ---------------------------------------------------------------------------------------------------

    private int Report(string? scriptPath, TextWriter output, TextWriter error)
    {
        int code = ReportRun(scriptPath, output, error);
        return record is null || freePlay is null ? code : ReportRecord(record: record, freePlay: freePlay, code: code, output: output, error: error);
    }

    /// <summary>
    /// Reports the record of a session that ended with the exit code <paramref name="code"/>. The record is written
    /// whatever ended the run, so it is reported whatever ended the run. A record that was written is reported by its path
    /// on standard output. A record that could not be written is reported on standard error, and it makes the exit code
    /// the file error code unless the run failed for another reason already.
    /// </summary>
    internal static int ReportRecord(RecordTo record, FreePlay freePlay, int code, TextWriter output, TextWriter error)
    {
        if (freePlay.RecordFailure is { } problem)
        {
            error.WriteLine($"Cannot write the record {record.Path}: {problem.Message}");
            return code == ScriptCommand.Ok ? ScriptCommand.FileError : code;
        }

        if (freePlay.RecordWritten)
            output.WriteLine(record.Path);
        return code;
    }

    private int ReportRun(string? scriptPath, TextWriter output, TextWriter error)
    {
        if (failure is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot write into {outputFolder}: {failure.Message}");
            return ScriptCommand.FileError;
        }

        if (failure is NotSupportedException)
        {
            error.WriteLine($"The window cannot do what the script asks: {failure.Message}");
            return ScriptCommand.ToolError;
        }

        if (failure != null)
        {
            error.WriteLine($"The window failed: {failure}");
            return ScriptCommand.InternalError;
        }

        if (scriptProblem != null)
        {
            error.WriteLine($"{scriptPath}: {scriptProblem}");
            return ScriptCommand.ScriptError;
        }

        // A script run in the window has an output folder, so it has its images.
        if (result != null && images is not null)
            ScriptCommand.WriteFiles(result: result, outputFolder: images.Folder, target: images, output: output);
        if (result is { Stopped: true })
            error.WriteLine(
                $"The window closed at tick {world.TickNumber}, so the script stopped before its last step.");
        return ScriptCommand.Ok;
    }
}
