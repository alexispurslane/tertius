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
    /// path first, and it writes each cut beside the script, such as scripts/demo-docking.json. Two runs on this machine
    /// write the same files. On arm64, which recorded them, the files equal the checked-in scripts
    /// (<see cref="RecordedReference"/>), so the two cannot drift: a change of the scene or of the sections fails this
    /// test until the checked-in scripts are written again.
    /// </summary>
    [Fact]
    public void TheCheckedInScriptIsWhatTheDemosCommandLineGenerates()
    {
        string first = NewFolder();
        string second = NewFolder();
        try
        {
            Generate(first);
            Generate(second);
            BlueDanube.Cuts.Should().NotBeEmpty();
            string[] files = [LongScript, .. BlueDanube.Cuts.Select(cut => cut.File)];
            foreach (string file in files)
            {
                File.ReadAllText(Path.Combine(second, file))
                    .Should()
                    .Be(File.ReadAllText(Path.Combine(first, file)), $"two runs on one machine write the same {file}");
            }

            Assert.SkipUnless(RecordedReference.Applies, RecordedReference.SkipReason);
            foreach (string file in files)
            {
                File.ReadAllText(Path.Combine(first, file))
                    .Should()
                    .Be(
                        File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", file)),
                        $"scripts/{file} must be what the generator writes: run `dotnet run --project src/Tertius.Demo -- --generate scripts/blue-danube.json`"
                    );
            }
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    /// <summary>The file name of the long script, which the generator writes with its cuts beside it.</summary>
    private const string LongScript = "blue-danube.json";

    /// <summary>Runs the demo's command line that writes the long script and its cuts into <paramref name="folder"/>.</summary>
    private static void Generate(string folder)
    {
        string written = Path.Combine(folder, LongScript);
        var output = new StringWriter();
        var error = new StringWriter();

        int exit = Program.Generate(["--generate", written], output, error);

        exit.Should().Be(ScriptCommand.Ok, error.ToString());
        output.ToString().Split('\n')[0].Trim().Should().Be(written);
        foreach ((string file, _, _) in BlueDanube.Cuts)
        {
            output.ToString().Should().Contain(Path.Combine(folder, file));
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
