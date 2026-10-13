using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// One headless run of a checked-in script from the folder scripts, named without its .json, in a world on the real Jolt
/// backend, with the headless renderer for its captures. A test class takes a subclass as its fixture, so that all of its
/// tests read the one run. A subclass that passes <c>sample</c> sees the world and the scene after every tick: the run
/// calls it before each tick and once after the last, so it sees the state after each tick from 0, the scene as built,
/// to the last. <see cref="World.TickNumber"/> says which tick.
/// </summary>
public abstract class CheckedInRun : IDisposable
{
    protected CheckedInRun(string name, Action<World, SceneInstance>? sample = null)
    {
        Script script = Script.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", name + ".json")));
        World = NewJoltWorld();
        VulkanRenderer renderer = GpuTesting.Headless(16, 16);
        try
        {
            Scene = DemoScenes.All.Single(s => s.Name == script.Scene).Build(World, renderer);
            IPlayback playback = new HeadlessPlayback(renderer, script.Window);
            Result = ScriptRunner.Run(script, World, Scene, Folder, sample is null ? playback : new Sampled(playback, () => sample(World, Scene)));
            sample?.Invoke(World, Scene);
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

    /// <summary>A playback that calls <c>sample</c> before each tick, then lets <c>inner</c> pace it.</summary>
    private sealed class Sampled(IPlayback inner, Action sample) : IPlayback
    {
        public bool WaitForTick()
        {
            sample();
            return inner.WaitForTick();
        }

        public bool Capture(World world, string path) => inner.Capture(world, path);
    }
}
