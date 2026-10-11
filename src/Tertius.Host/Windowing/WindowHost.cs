using System.Diagnostics;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
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
/// The threads share only thread-safe things: <see cref="World.Post"/> from input, the render snapshots' triple buffer
/// (the simulation thread writes, the render thread reads), the <see cref="CaptureMailbox"/>, the fly camera under its
/// lock, and the closing token. The renderer belongs to the render thread, which disposes it before the window is
/// destroyed, as MoltenVK needs.
/// </remarks>
internal sealed class WindowHost : IInputTarget
{
    private readonly IWindow window;
    private readonly VulkanRenderer renderer;
    private readonly World world;
    private readonly SceneInstance scene;
    private readonly Script? script;
    private readonly string? outputFolder;
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

    // Presses of the view key, which the main thread counts and the simulation thread applies between ticks.
    private int viewChanges;

    private WindowHost(IWindow window, VulkanRenderer renderer, World world, SceneInstance scene, Script? script, string? outputFolder, double speed)
    {
        this.window = window;
        this.renderer = renderer;
        this.world = world;
        this.scene = scene;
        this.script = script;
        this.outputFolder = outputFolder;
        pacer = new TickPacer(world.DeltaTime, speed);
        router = new InputRouter(freePlay: script is null, this);
    }

    /// <summary>
    /// Opens the window, builds <paramref name="sceneToBuild"/>, and plays <paramref name="script"/>, or runs the scene
    /// live if it is null, until the script ends or the window closes. Returns the exit code of <see cref="ScriptCommand"/>.
    /// Call it on the main thread, which macOS needs for a window.
    /// </summary>
    public static int Run(Scene sceneToBuild, Script? script, string? scriptPath, string? outputFolder, double speed, TextWriter output, TextWriter error)
    {
        ScriptWindow size = script?.Window ?? ScriptWindow.Default;
        IWindow? window = null;
        try
        {
            window = Window.Create(WindowOptions.DefaultVulkan with
            {
                Size = new Vector2D<int>(size.Width, size.Height),
                Title = $"Tertius: {sceneToBuild.Name}",
                IsEventDriven = true,
            });
            window.Initialize();
        }
        // No display, or no Vulkan driver for the window: as for a renderer that cannot start.
        catch (Exception ex)
        {
            error.WriteLine($"Cannot open the window: {ex.Message}");
            window?.Dispose();
            return ScriptCommand.RendererError;
        }
        try
        {
            VulkanRenderer renderer;
            try
            {
                // Free play may use impostors. A script draws every object as geometry, as a headless run does.
                renderer = VulkanRenderer.CreateForWindow(window, new RendererOptions { Impostors = script is null });
            }
            catch (Exception ex)
            {
                error.WriteLine($"Cannot start the renderer for the window: {ex.Message}");
                return ScriptCommand.RendererError;
            }
            var host = default(WindowHost);
            try
            {
                using var world = new World(new WorldOptions { DisposePhysics = true }, new JoltPhysicsBackend());
                world.Log += error.WriteLine;
                SceneInstance scene = sceneToBuild.Build(world, renderer);
                world.PublishRenderSnapshot();
                host = new WindowHost(window, renderer, world, scene, script, outputFolder, speed);
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

    /// <summary>Starts the simulation and render threads, runs the window's events until a thread or the user closes it, then stops both threads.</summary>
    private void Play()
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
            device.Scroll += (_, wheel) => router.Scroll(new Vector2(wheel.X, wheel.Y));
        }

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
        var playback = new WindowPlayback(pacer, captures, closing.Token, () => clock.Elapsed.TotalSeconds);
        try
        {
            if (script is null)
            {
                while (playback.WaitForTick())
                {
                    // Only the ticking thread may set the world's camera.
                    for (int changes = Interlocked.Exchange(ref viewChanges, 0); changes > 0; changes--)
                        world.Camera = scene.NextView(world.Camera);
                    world.Tick();
                }
            }
            else
            {
                result = ScriptRunner.Run(script, world, scene, outputFolder, playback);
            }
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
                if (captures.TryTake(out string path))
                {
                    // The simulation thread published this tick's snapshot and waits. A capture shows the world's camera.
                    world.RenderSnapshots.TryAcquireLatest(out RenderSnapshot shot);
                    // A frame that the swapchain skips, for example while the window is minimized, draws and saves
                    // nothing, so the capture is asked again until a frame is drawn or the window closes.
                    do
                    {
                        renderer.CaptureNextFrame(path);
                        renderer.Render(shot, alpha: 1f);
                    }
                    while (!renderer.LastFrameStats.Rendered && !closing.Token.WaitHandle.WaitOne(10));
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

    public void Post(InputEvent e) => world.Post(Event.Create(Topics.Input, Entity.Null, e));

    public void Fly(FlyKey key, bool down)
    {
        lock (flyLock)
            fly.SetKey(key, down);
    }

    public void Turn(Vector2 movement)
    {
        lock (flyLock)
            fly.Turn(movement);
    }

    public void FlyChanged(bool flying)
    {
        lock (flyLock)
        {
            flyActive = flying;
            // The render thread starts it at the world's camera, which only it can read.
            flyStartWanted = flying;
        }
        foreach (IMouse device in input!.Mice)
            device.Cursor.CursorMode = !flying ? CursorMode.Normal : device.Cursor.IsSupported(CursorMode.Raw) ? CursorMode.Raw : CursorMode.Disabled;
    }

    public void Close() => window.Close();

    public void NextView() => Interlocked.Increment(ref viewChanges);

    // ---- The outcome ---------------------------------------------------------------------------------------------------

    private int Report(string? scriptPath, TextWriter output, TextWriter error)
    {
        if (failure is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot write into {outputFolder}: {failure.Message}");
            return ScriptCommand.FileError;
        }
        if (failure is NotSupportedException)
        {
            error.WriteLine($"The window cannot do what the script asks: {failure.Message}");
            return ScriptCommand.RendererError;
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
        if (result != null)
            ScriptCommand.WriteFiles(result, outputFolder!, output);
        if (result is { Stopped: true })
            error.WriteLine($"The window closed at tick {world.TickNumber}, so the script stopped before its last step.");
        return ScriptCommand.Ok;
    }
}
