using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Every checked-in script in the folder scripts passes the checks of a run, so a script that names a missing scene, a
/// missing view or a missing post target fails a test, not only a headless capture. The list of scripts is the folder
/// itself, so a new script needs no test of its own for these checks. The tests of each scene's own facts stay with the
/// scene, such as <see cref="ShadowScenesTests"/>. No test draws, so none needs a GPU.
/// </summary>
public sealed class CheckedInScriptsTests
{
    /// <summary>
    /// A run checks every step and resolves every name before its first tick: the scene, each camera, each post's target
    /// and event, and each film's camera and playback. The run stops before its first tick here, so it changes nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(CheckedInScripts), MemberType = typeof(HostTesting))]
    public void EveryCheckedInScriptPassesTheChecksOfARun(string file)
    {
        var script = Script.Load(Path.Combine(RepositoryRoot(), "scripts", file));
        using World world = NewJoltWorld();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == script.Scene).Build(world, RenderResources.None);

        RunResult result = ScriptRunner.Run(
            script: script,
            world: world,
            scene: scene,
            outputFolder: null,
            playback: new StopsAtOnce()
        );

        result.Stopped.Should().BeTrue();
        world.TickNumber.Should().Be(0);
    }

    /// <summary>A playback that stops the run before its first tick.</summary>
    private sealed class StopsAtOnce : IPlayback
    {
        public bool WaitForTick() => false;

        public bool Capture(World world, string name) => false;
    }
}
