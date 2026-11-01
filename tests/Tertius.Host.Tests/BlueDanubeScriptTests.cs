using Tertius.Demo;
using Tertius.Host.Scripting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The Blue Danube demo's long script: the checked-in scripts/blue-danube.json is what the generator writes from the demo's
/// command line, and it films its sections. <see cref="CheckedInScriptsTests"/> runs the checks of a run over every
/// checked-in script.
/// </summary>
public sealed class BlueDanubeScriptTests
{
    private static string CheckedIn => Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json");

    /// <summary>
    /// The demo's command line writes the script with <c>--generate &lt;file&gt;</c>, exits with 0 and prints the file's
    /// path first, and the file equals the checked-in script, so the two cannot drift: a change of the scene or of the
    /// sections fails this test until the checked-in script is written again. The same holds for each cut that it writes
    /// beside the script, such as scripts/demo-docking.json.
    /// </summary>
    [Fact]
    public void TheCheckedInScriptIsWhatTheDemosCommandLineGenerates()
    {
        string folder = NewFolder();
        try
        {
            string written = Path.Combine(folder, "blue-danube.json");
            var output = new StringWriter();
            var error = new StringWriter();

            int exit = Program.Generate(["--generate", written], output, error);

            exit.Should().Be(ScriptCommand.Ok, error.ToString());
            output.ToString().Split('\n')[0].Trim().Should().Be(written);
            File.ReadAllText(written)
                .Should()
                .Be(
                    File.ReadAllText(CheckedIn),
                    "scripts/blue-danube.json must be what the generator writes: run `dotnet run --project src/Tertius.Demo -- --generate scripts/blue-danube.json`"
                );
            BlueDanube.Cuts.Should().NotBeEmpty();
            foreach ((string file, _, _) in BlueDanube.Cuts)
            {
                output.ToString().Should().Contain(Path.Combine(folder, file));
                File.ReadAllText(Path.Combine(folder, file))
                    .Should()
                    .Be(
                        File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", file)),
                        $"scripts/{file} must be what the generator writes beside the long script"
                    );
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheCheckedInScriptFilms()
    {
        var script = Script.Load(CheckedIn);

        script.Steps.Should().Contain(step => step.Value is FilmStep);
    }

    [Theory]
    [InlineData("--generate")]
    [InlineData("--generate", "a.json", "b.json")]
    public void TheCommandLineRefusesAGenerateWithoutExactlyOneFile(params string[] args)
    {
        var error = new StringWriter();

        int exit = Program.Generate(args, new StringWriter(), error);

        exit.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("--generate <file>");
    }

    /// <summary>
    /// The demo's command line sends a command line that starts with <c>--generate</c> to the generator, which refuses one
    /// with no file with its own usage, and every other command line to the script command, which refuses an empty one
    /// with the usage of its runs.
    /// </summary>
    [Fact]
    public void TheCommandLineSendsGenerateToTheGeneratorAndEverythingElseToTheScriptCommand()
    {
        var generateError = new StringWriter();
        var otherError = new StringWriter();

        int generate = Program.Run(["--generate"], new StringWriter(), generateError);
        int other = Program.Run([], new StringWriter(), otherError);

        generate.Should().Be(ScriptCommand.CommandLineError);
        generateError.ToString().Should().Contain("--generate <file>").And.NotContain("--script <file>");
        other.Should().Be(ScriptCommand.CommandLineError);
        otherError.ToString().Should().Contain("--script <file>").And.NotContain("--generate <file>");
    }

    [Fact]
    public void TheGeneratorReportsAFileErrorWhenItCannotWriteTheScript()
    {
        string path = Path.Combine(
            path1: Path.GetTempPath(),
            path2: Guid.NewGuid().ToString("N"),
            path3: "no-such-folder",
            path4: "blue-danube.json"
        );
        var output = new StringWriter();
        var error = new StringWriter();

        int code = Program.Run(["--generate", path], output, error);

        code.Should().Be(ScriptCommand.FileError);
        error.ToString().Should().Contain($"Cannot write the script {path}");
        output.ToString().Should().BeEmpty();
        File.Exists(path).Should().BeFalse();
    }
}
