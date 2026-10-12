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
    protected CheckedInRun(string name)
    {
        Script script = Script.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", name + ".json")));
        World = NewJoltWorld();
        VulkanRenderer renderer = GpuTesting.Headless(16, 16);
        try
        {
            Scene = DemoScenes.All.Single(s => s.Name == script.Scene).Build(World, renderer);
            Result = ScriptRunner.Run(script, World, Scene, Folder, new HeadlessPlayback(renderer, script.Window));
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
}
