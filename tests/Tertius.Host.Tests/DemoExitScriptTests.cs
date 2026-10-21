using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The exit's short script (docs/plans/blue-danube-demo.md, "The exit", "Sections of the long script"): the checked-in
/// scripts/demo-exit.json is what the generator writes for the scene <c>demo-exit</c> from the exit's sections, and it
/// passes every check that a run makes before its first tick.
/// </summary>
public sealed class DemoExitScriptTests
{
    private static string CheckedIn => Path.Combine(RepositoryRoot(), "scripts", "demo-exit.json");

    /// <summary>
    /// The demo's command line writes the script with <c>--generate &lt;file&gt; demo-exit</c>, exits with 0, prints the
    /// file's path first, and the file equals the checked-in script.
    /// </summary>
    [Fact]
    public void TheCheckedInScriptIsWhatTheDemosCommandLineGeneratesForTheExitsScene()
    {
        string folder = NewFolder();
        try
        {
            string written = Path.Combine(folder, "demo-exit.json");
            var output = new StringWriter();
            var error = new StringWriter();

            int exit = Program.Generate(["--generate", written, BlueDanube.ExitSceneName], output, error);

            exit.Should().Be(ScriptCommand.Ok, error.ToString());
            output.ToString().Split('\n')[0].Trim().Should().Be(written);
            File.ReadAllText(written).Should().Be(File.ReadAllText(CheckedIn),
                "scripts/demo-exit.json must be what the generator writes: run `dotnet run --project src/Tertius.Demo -- --generate scripts/demo-exit.json demo-exit`");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The script names the scene <c>demo-exit</c>, films the exit's sections in their order, and passes the checks of a run.</summary>
    [Fact]
    public void TheCheckedInScriptFilmsTheExitsSectionsAndPassesTheChecksOfARun()
    {
        Script script = Script.Load(CheckedIn);
        script.Scene.Should().Be(BlueDanube.ExitSceneName);
        script.Steps.Select(step => step.Value).OfType<FilmStep>().Select(film => film.Name)
            .Should().Equal(BlueDanube.ExitSections.Select(section => section.Name));
        using World world = NewJoltWorld();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == script.Scene).Build(world, RenderResources.None);

        RunResult result = ScriptRunner.Run(script, world, scene, outputFolder: null, new StopsAtOnce());

        result.Stopped.Should().BeTrue();
        world.TickNumber.Should().Be(0);
    }

    /// <summary>
    /// The generator knows the sections of two scenes, <c>demo-surface</c> and <c>demo-exit</c>. It refuses any other
    /// scene, and more than one, with its usage.
    /// </summary>
    [Theory]
    [InlineData("--generate", "a.json", "drop-box")]
    [InlineData("--generate", "a.json", "demo-exit", "demo-surface")]
    public void TheGeneratorRefusesASceneWithoutSectionsAndMoreThanOneScene(params string[] args)
    {
        var error = new StringWriter();

        int exit = Program.Generate(args, new StringWriter(), error);

        exit.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("--generate <file> [<scene>]");
        File.Exists("a.json").Should().BeFalse();
    }

    /// <summary>A playback that stops the run before its first tick.</summary>
    private sealed class StopsAtOnce : IPlayback
    {
        public bool WaitForTick() => false;

        public bool Capture(World world, string name) => false;
    }
}
