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
internal sealed class WindowHost
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
    private readonly MouseTracker mouse = new();
    private IInputContext? input;

    // The fly camera: the main thread sets its keys and turns it, the render thread starts, moves and applies it.
    private readonly Lock flyLock = new();
    private readonly FlyCamera fly = new();
    private bool flyActive;
    private bool flyStartWanted;

    // How the simulation thread ended. Read on the main thread after it joins.
    private RunResult? result;
    private string? scriptProblem;
    private bool stoppedInCapture;
    private Exception? failure;
    private int closeWanted;

    private WindowHost(IWindow window, VulkanRenderer renderer, World world, SceneInstance scene, Script? script, string? outputFolder, double speed)
    {
        this.window = window;
        this.renderer = renderer;
        this.world = world;
        this.scene = scene;
        this.script = script;
        this.outputFolder = outputFolder;
        pacer = new TickPacer(world.DeltaTime, speed);
    }

    /// <summary>
    /// Opens the window, builds <paramref name="sceneToBuild"/>, and plays <paramref name="script"/>, or runs the scene
    /// live if it is null, until the script ends or the window closes. Returns the exit code of <see cref="ScriptCommand"/>.
    /// Call it on the main thread, which macOS needs for a window.
    /// </summary>
    public static int Run(Scene sceneToBuild, Script? script, string? scriptPath, string? outputFolder, double speed, TextWriter output, TextWriter error)
    {
        ScriptWindow size = script?.Window ?? ScriptWindow.Default;
        IWindow window = Window.Create(WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(size.Width, size.Height),
            Title = $"Tertius: {sceneToBuild.Name}",
            IsEventDriven = true,
        });
        window.Initialize();
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
            keyboard.KeyDown += (_, key, _) => OnKey(key, down: true);
            keyboard.KeyUp += (_, key, _) => OnKey(key, down: false);
        }
        foreach (IMouse device in input.Mice)
        {
            device.MouseMove += (_, position) => OnMouseMove(position);
            device.MouseDown += (_, button) => PostInput(InputKind.MouseDown, (int)button, Vector2.Zero);
            device.MouseUp += (_, button) => PostInput(InputKind.MouseUp, (int)button, Vector2.Zero);
            device.Scroll += (_, wheel) => PostInput(InputKind.Scroll, 0, new Vector2(wheel.X, wheel.Y));
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
                    world.Tick();
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
        catch (OperationCanceledException)
        {
            // The window closed while a capture waited for the render thread.
            stoppedInCapture = true;
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
                    renderer.CaptureNextFrame(path);
                    renderer.Render(shot, alpha: 1f);
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
                        fly.Start(worldCamera.Rotation);
                        flyStartWanted = false;
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

    // ---- Input, on the main thread -------------------------------------------------------------------------------------

    private void OnKey(Key key, bool down)
    {
        if (key == Key.Escape && down)
        {
            window.Close();
            return;
        }
        if (key == Key.F1 && down)
        {
            ToggleFlyCamera();
            return;
        }
        if (FlyKeyOf(key) is { } flyKey && IsFlying())
        {
            lock (flyLock)
                fly.SetKey(flyKey, down);
            return;
        }
        PostInput(down ? InputKind.KeyDown : InputKind.KeyUp, (int)key, Vector2.Zero);
    }

    private void OnMouseMove(Vector2 position)
    {
        Vector2 movement = mouse.MoveTo(position);
        if (IsFlying())
        {
            lock (flyLock)
                fly.Turn(movement);
            return;
        }
        PostInput(InputKind.MouseMove, 0, movement);
    }

    /// <summary>
    /// Sends input to the <c>input</c> topic in free play. During a script, live input reaches no entity, so the script
    /// runs the same with or without a person at the keyboard.
    /// </summary>
    private void PostInput(InputKind kind, int code, Vector2 delta)
    {
        if (script is null && !IsFlying())
            world.Post(Event.Create(Topics.Input, Entity.Null, new InputEvent { Kind = kind, Code = code, Delta = delta }));
    }

    private bool IsFlying()
    {
        lock (flyLock)
            return flyActive;
    }

    private void ToggleFlyCamera()
    {
        bool active;
        lock (flyLock)
        {
            flyActive = !flyActive;
            // The render thread starts it at the world's camera, which only it can read.
            flyStartWanted = flyActive;
            active = flyActive;
        }
        foreach (IMouse device in input!.Mice)
            device.Cursor.CursorMode = !active ? CursorMode.Normal : device.Cursor.IsSupported(CursorMode.Raw) ? CursorMode.Raw : CursorMode.Disabled;
    }

    private static FlyKey? FlyKeyOf(Key key) => key switch
    {
        Key.W => FlyKey.Forward,
        Key.S => FlyKey.Back,
        Key.A => FlyKey.Left,
        Key.D => FlyKey.Right,
        Key.Q => FlyKey.Down,
        Key.E => FlyKey.Up,
        Key.ShiftLeft or Key.ShiftRight => FlyKey.Fast,
        _ => null,
    };

    // ---- The outcome ---------------------------------------------------------------------------------------------------

    private int Report(string? scriptPath, TextWriter output, TextWriter error)
    {
        if (failure is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot write into {outputFolder}: {failure.Message}");
            return ScriptCommand.FileError;
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
        if (result is { Stopped: true } || stoppedInCapture)
            error.WriteLine($"The window closed at tick {world.TickNumber}, so the script stopped before its last step.");
        return ScriptCommand.Ok;
    }
}
