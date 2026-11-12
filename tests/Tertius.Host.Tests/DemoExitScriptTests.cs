using Tertius.Demo;
using Tertius.Host.Scripting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The exit's short script (docs/plans/blue-danube-demo.md, "The exit", "Sections of the long script"): the checked-in
/// scripts/demo-exit.json is what the generator writes for the scene <c>demo-exit</c> from the exit's sections and the coda's, and it
/// films them in their order. <see cref="CheckedInScriptsTests"/> runs the checks of a run over every checked-in script.
/// </summary>
public sealed class DemoExitScriptTests
{
    private static string CheckedIn => Path.Combine(RepositoryRoot(), "scripts", "demo-exit.json");

    /// <summary>
    /// The demo's command line writes the script with <c>--generate &lt;file&gt; demo-exit</c>, exits with 0 and prints the
    /// file's path first. Two runs on this machine write the same file, and on arm64, which recorded it, the file equals the
    /// checked-in script (<see cref="RecordedReference"/>).
    /// </summary>
    [Fact]
    public void TheCheckedInScriptIsWhatTheDemosCommandLineGeneratesForTheExitsScene()
    {
        string first = NewFolder();
        string second = NewFolder();
        try
        {
            string written = Generate(first);
            File.ReadAllText(Generate(second))
                .Should()
                .Be(File.ReadAllText(written), "two runs on one machine write the same script");
            Assert.SkipUnless(RecordedReference.Applies, RecordedReference.SkipReason);
            File.ReadAllText(written)
                .Should()
                .Be(
                    File.ReadAllText(CheckedIn),
                    "scripts/demo-exit.json must be what the generator writes: run `dotnet run --project src/Tertius.Demo -- --generate scripts/demo-exit.json demo-exit`"
                );
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    /// <summary>Runs the demo's command line that writes the exit's script into <paramref name="folder"/>; the file's path.</summary>
    private static string Generate(string folder)
    {
        string written = Path.Combine(folder, "demo-exit.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int exit = Program.Generate(["--generate", written, BlueDanube.ExitSceneName], output, error);

        exit.Should().Be(ScriptCommand.Ok, error.ToString());
        output.ToString().Split('\n')[0].Trim().Should().Be(written);
        return written;
    }

    /// <summary>The script names the scene <c>demo-exit</c> and films the exit's sections and then the coda's, in their order.</summary>
    [Fact]
    public void TheCheckedInScriptFilmsTheExitsSectionsAndThenTheCodas()
    {
        var script = Script.Load(CheckedIn);
        script.Scene.Should().Be(BlueDanube.ExitSceneName);
        script
            .Steps.Select(step => step.Value)
            .OfType<FilmStep>()
            .Select(film => film.Name)
            .Should()
            .Equal(BlueDanube.ExitSections.Concat(BlueDanube.CodaSections).Select(section => section.Name));
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
}
