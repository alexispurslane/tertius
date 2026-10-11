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
    private static readonly Scene[] Scenes = [ProbesScene, BoxOnFloorScene];

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
        string script = ScriptFile("""{ "scene": "box-on-floor", "steps": [ { "do": "run", "ticks": 60 }, { "do": "dump", "name": "landed" } ] }""");

        int code = Run("--script", script, "--headless", "--out", Out);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        File.ReadAllText(Path.Combine(Out, "landed.json")).Should().Contain("\"box\"");
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
    }

    [Theory]
    [InlineData("--headless", "--out", "out")]
    [InlineData("--script", "script.json", "--headless")]
    [InlineData("--script", "script.json", "--headless", "--out")]
    [InlineData("--script", "script.json", "--headless", "--out", "out", "--fast")]
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
}
