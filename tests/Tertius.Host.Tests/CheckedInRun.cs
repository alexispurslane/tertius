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
    /// <summary>
    /// Runs the script <paramref name="name"/>. If <paramref name="beforeEachTick"/> is given, the run calls it with the
    /// world and the scene before each tick, so it sees the state after every tick but the last.
    /// </summary>
    protected CheckedInRun(string name, Action<World, SceneInstance>? beforeEachTick = null)
    {
        Script script = Script.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", name + ".json")));
        World = NewJoltWorld();
        VulkanRenderer renderer = GpuTesting.Headless(16, 16);
        try
        {
            Scene = DemoScenes.All.Single(s => s.Name == script.Scene).Build(World, renderer);
            IPlayback playback = new HeadlessPlayback(renderer, script.Window);
            Result = ScriptRunner.Run(script, World, Scene, Folder,
                beforeEachTick == null ? playback : new Watched(playback, () => beforeEachTick(World, Scene)));
        }
        finally
        {
            renderer.Dispose();
            ValidationMessages = renderer.ValidationMessages;
        }
    }

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

    /// <summary>A playback that calls <paramref name="watch"/> each time before it lets a tick run.</summary>
    private sealed class Watched(IPlayback playback, Action watch) : IPlayback
    {
        public bool WaitForTick()
        {
            watch();
            return playback.WaitForTick();
        }

        public bool Capture(World world, string path) => playback.Capture(world, path);
    }
}
