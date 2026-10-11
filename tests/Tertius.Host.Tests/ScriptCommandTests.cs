using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// ScriptCommand: the command line that the Demo runs, <c>--script &lt;file&gt; --headless --out &lt;folder&gt;</c>, and its exit
/// codes.
/// </summary>
public sealed class ScriptCommandTests : IDisposable
{
    private readonly string folder = NewFolder();
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();
    private static readonly Scene[] Scenes = [ProbesScene, BoxOnFloorScene, LoggerScene];

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string ScriptFile(string json)
    {
        string path = Path.Combine(folder, "script.json");
        File.WriteAllText(path, json);
        return path;
    }

    private string Out => Path.Combine(folder, "out");

    private int Run(params string[] args) => ScriptCommand.Run(args, Scenes, output, error);

    [Fact]
    public void TheCommandRunsTheScriptOnTheSceneThatItNamesAndWritesTheDumps()
    {
        string script = ScriptFile("""{ "scene": "box-on-floor", "steps": [ { "do": "dump", "name": "start" }, { "do": "run", "ticks": 60 }, { "do": "dump", "name": "landed" } ] }""");

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        File.ReadAllText(Path.Combine(Out, "landed.json")).Should().Contain("\"box\"");
        output.ToString().Should().Be(Path.Combine(Out, "start.json") + Environment.NewLine + Path.Combine(Out, "landed.json") + Environment.NewLine);
    }

    [Fact]
    public void AScriptThatIsNotValidExitsWithTheScriptErrorCodeAndNamesTheFile()
    {
        string script = ScriptFile("""{ "scene": "probes", "steps": [] }""");

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain(script);
        Directory.Exists(Out).Should().BeFalse("nothing ran");
    }

    [Fact]
    public void AnOutputFolderThatCannotBeMadeExitsWithTheFileErrorCode()
    {
        string script = ScriptFile("""{ "scene": "probes", "steps": [ { "do": "dump", "name": "now" } ] }""");
        string taken = Path.Combine(folder, "taken");
        File.WriteAllText(taken, "a file, not a folder");

        int code = Run("--script", script, "--headless", "--out", taken);

        code.Should().Be(ScriptCommand.FileError);
        error.ToString().Should().Contain("taken");
    }

    [Fact]
    public void AScriptThatNamesAnUnknownSceneExitsWithTheScriptErrorCode()
    {
        string script = ScriptFile("""{ "scene": "moon-base", "steps": [ { "do": "run", "ticks": 1 } ] }""");

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain("moon-base");
    }

    [Fact]
    public void AScriptThatCannotRunExitsWithTheScriptErrorCode()
    {
        string script = ScriptFile("""{ "scene": "probes", "steps": [ { "do": "post", "to": { "kind": "entity", "id": "ghost" }, "event": "Nudge" } ] }""");

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.ScriptError);
        error.ToString().Should().Contain("ghost");
    }

    [Fact]
    public void AScriptFileThatDoesNotExistExitsWithTheFileErrorCode()
    {
        int code = Run("--script", Path.Combine(folder, "missing.json"), "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.FileError);
        error.ToString().Should().Contain("missing.json");
    }

    [Theory]
    [InlineData("--headless", "--out", "out")]
    [InlineData("--script", "script.json", "--headless")]
    [InlineData("--script", "script.json", "--headless", "--out")]
    [InlineData("--script", "script.json", "--headless", "--out", "out", "--fast")]
    [InlineData("--script", "", "--headless", "--out", "out")]
    [InlineData("--script", "script.json", "--headless", "--out", " ")]
    public void AMissingOrUnknownOptionExitsWithTheCommandLineErrorCode(params string[] args)
    {
        int code = Run(args);

        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().NotBeEmpty();
    }

    [Fact]
    public void ARunWithoutHeadlessExitsWithTheCommandLineErrorCodeUntilTheWindowExists()
    {
        string script = ScriptFile("""{ "scene": "probes", "steps": [ { "do": "run", "ticks": 1 } ] }""");

        int code = Run("--script", script, "--out", Out);

        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().Contain("--headless");
    }

    [Fact]
    public void TheCommandWritesTheWorldsLogToStandardError()
    {
        string script = ScriptFile("""{ "scene": "logger", "steps": [ { "do": "run", "ticks": 2 } ] }""");

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        error.ToString().Should().Contain("hello from the logger");
    }

    [Fact]
    public void AScriptWithoutCapturesRunsWithNoRendererAndTheDemoSceneStillMakesItsEntities()
    {
        string script = ScriptFile("""{ "scene": "drop-box", "steps": [ { "do": "run", "ticks": 120 }, { "do": "dump", "name": "landed" } ] }""");

        int code = ScriptCommand.Run(["--script", script, "--headless", "--out", Out], Tertius.Demo.DemoScenes.All, output, error,
            _ => throw new InvalidOperationException("a script without captures makes no renderer"));

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        using var landed = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, "landed.json")));
        System.Text.Json.JsonElement box = landed.RootElement.GetProperty("entities").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "box");
        box.GetProperty("pose").GetProperty("position")[1].GetDouble().Should().BeApproximately(0.5, 0.02);
    }
}
