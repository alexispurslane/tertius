using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// One headless run of a checked-in script from the folder scripts, named without its .json, in a world on the real Jolt
/// backend, with the headless renderer for its captures. A test class takes a subclass as its fixture, so that all of its
/// tests read the one run.
/// </summary>
public abstract class CheckedInRun : IDisposable
{
    protected CheckedInRun(string name) : this(name, beforeEachTick: null)
    {
    }

    /// <summary>
    /// With <paramref name="beforeEachTick"/>, the run calls it with the world and the scene at every tick from tick 0 to
    /// the end of the run, exactly once for each tick, so that a test can watch what no dump shows, such as the physics
    /// homes of numeric frames. The playback asks before each tick, and after the last one only when the script ends with
    /// a step that renders, so the run adds the last tick itself when the playback did not.
    /// </summary>
    protected CheckedInRun(string name, Action<World, SceneInstance>? beforeEachTick)
    {
        Script = Script.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", name + ".json")));
        World = NewJoltWorld();
        VulkanRenderer renderer = GpuTesting.Headless(16, 16);
        try
        {
            Scene = DemoScenes.All.Single(s => s.Name == Script.Scene).Build(World, renderer);
            IPlayback playback = new HeadlessPlayback(renderer, Script.Window, new PngFolder(Folder));
            long lastWatched = -1;

            void Watch()
            {
                if (World.TickNumber == lastWatched)
                    return;
                lastWatched = World.TickNumber;
                beforeEachTick!(World, Scene);
            }

            if (beforeEachTick != null)
                playback = new WatchedPlayback(playback, Watch);
            Result = ScriptRunner.Run(Script, World, Scene, Folder, playback);
            if (beforeEachTick != null)
                Watch();
        }
        finally
        {
            renderer.Dispose();
            ValidationMessages = renderer.ValidationMessages;
        }
    }

    public Script Script { get; }
    public World World { get; }
    public SceneInstance Scene { get; }
    public RunResult Result { get; }
    public string Folder { get; } = NewFolder();
    public IReadOnlyList<string> ValidationMessages { get; }

    public Png Capture(string name) => Png.Load(Path.Combine(Folder, name + ".png"));

    public void Dispose()
    {
        World.Dispose();
        Directory.Delete(Folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>A playback that calls <paramref name="beforeTick"/> before each tick, and otherwise is
    /// <paramref name="inner"/>.</summary>
    private sealed class WatchedPlayback(IPlayback inner, Action beforeTick) : IPlayback
    {
        public bool WaitForTick()
        {
            beforeTick();
            return inner.WaitForTick();
        }

        public bool Capture(World world, string name) => inner.Capture(world, name);
    }
}
