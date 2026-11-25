using Tertius.Assets;
using Tertius.Demo;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The Blue Danube art (<see cref="BlueDanubeArt"/>): its references match the sidecars that the setup command converts,
/// and a Blue Danube run without the converted art stops with the setup command, unless it asks for the gray box. No test
/// needs the art itself.
/// </summary>
public sealed class BlueDanubeArtTests
{
    private static ContentFolders EmptyContent() => new(NewFolder());

    private static string Script(string scene)
    {
        string path = Path.Combine(NewFolder(), "run.json");
        File.WriteAllText(path, $$"""{"scene": "{{scene}}", "steps": [{"do": "run", "ticks": 1}]}""");
        return path;
    }

    [Fact]
    public void EachReferenceHasTheSidecarThatTheSetupCommandConverts()
    {
        string assets = Path.Combine(path1: RepositoryRoot(), path2: "content", path3: "tertius.demo", path4: "assets");
        string[] sidecars =
        [
            .. Directory
                .EnumerateFiles(Path.Combine(assets, "blue-danube"), "*.import.json", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(assets, file).Replace('\\', '/')[..^".import.json".Length])
                .Order(StringComparer.Ordinal),
        ];

        BlueDanubeArt.All.Select(r => r.Path).Order(StringComparer.Ordinal).Should().Equal(sidecars);
        BlueDanubeArt.All.Should().AllSatisfy(r => r.Package.Should().Be("tertius.demo"));
    }

    [Fact]
    public void WithoutConvertedArtEveryModelIsMissing()
    {
        ContentFolders content = EmptyContent();

        BlueDanubeArt.Missing(content).Should().HaveCount(BlueDanubeArt.All.Count);
    }

    [Fact]
    public void ABlueDanubeScriptWithoutItsArtStopsAndNamesTheSetupCommand()
    {
        var error = new StringWriter();

        int code = Program.RunScene(
            args: ["--script", Script("demo-exit"), "--headless", "--out", NewFolder()],
            output: new StringWriter(),
            error: error,
            content: EmptyContent()
        );

        code.Should().Be(ScriptCommand.FileError);
        error.ToString().Should().Contain(BlueDanubeArt.SetupCommand).And.Contain("--gray-box");
    }

    [Fact]
    public void ABlueDanubeSceneWithoutItsArtStopsToo()
    {
        var error = new StringWriter();

        int code = Program.RunScene(
            args: ["--scene", BlueDanube.SceneName],
            output: new StringWriter(),
            error: error,
            content: null
        );

        code.Should().Be(ScriptCommand.FileError);
        error.ToString().Should().Contain(BlueDanubeArt.SetupCommand);
    }

    [Fact]
    public void TheArtPreviewWithoutItsArtStopsToo()
    {
        var error = new StringWriter();

        int code = Program.RunScene(
            args: ["--script", Script(DemoScenes.ArtPreviewSceneName), "--headless", "--out", NewFolder()],
            output: new StringWriter(),
            error: error,
            content: EmptyContent()
        );

        code.Should().Be(ScriptCommand.FileError);
        error.ToString().Should().Contain(BlueDanubeArt.SetupCommand);
    }

    [Fact]
    public void GrayBoxSkipsTheArtCheckAndIsNotPassedOn()
    {
        var error = new StringWriter();

        // Without --out the script command refuses the command line, after the art check would have stopped it.
        int code = Program.RunScene(
            args: ["--script", Script("demo-exit"), "--headless", "--gray-box"],
            output: new StringWriter(),
            error: error,
            content: EmptyContent()
        );

        code.Should().Be(ScriptCommand.CommandLineError);
        error.ToString().Should().NotContain(BlueDanubeArt.SetupCommand).And.NotContain("--gray-box");
    }

    [Fact]
    public void AnotherScenesScriptNeedsNoArt()
    {
        var error = new StringWriter();

        int code = Program.RunScene(
            args: ["--script", Script("drop-box"), "--headless"],
            output: new StringWriter(),
            error: error,
            content: EmptyContent()
        );

        code.Should().Be(ScriptCommand.CommandLineError, "the script command refuses the missing --out");
        error.ToString().Should().NotContain(BlueDanubeArt.SetupCommand);
    }
}
