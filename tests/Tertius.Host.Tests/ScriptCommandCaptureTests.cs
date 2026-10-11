using System.Text.Json;
using static Tertius.Host.Tests.GpuTesting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>The command line with scripts that capture images, which makes it create a headless renderer.</summary>
[Collection(GpuCollection.Name)]
public sealed class ScriptCommandCaptureTests : IDisposable
{
    private readonly string folder = NewFolder();
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string Out => Path.Combine(folder, "out");

    [Fact]
    public void TheCommandCapturesWithAHeadlessRendererAndWritesThePathOfEveryFile()
    {
        string script = Path.Combine(folder, "script.json");
        File.WriteAllText(script, """
            { "scene": "lit-box", "window": { "width": 160, "height": 90 },
              "steps": [ { "do": "capture", "name": "front" }, { "do": "dump", "name": "state" } ] }
            """);

        int code = ScriptCommand.Run(["--script", script, "--headless", "--out", Out], [LitBoxScene], output, error);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        Png.Load(Path.Combine(Out, "front.png")).IsRed(80, 45).Should().BeTrue("the scene gets the renderer's resources, so its box has a mesh");
        output.ToString().Should().Contain(Path.Combine(Out, "front.png")).And.Contain(Path.Combine(Out, "state.json"));
    }

    [Fact]
    public void TheDemoRunsItsDropBoxScript()
    {
        string root = RepositoryRoot();

        int code = ScriptCommand.Run(["--script", Path.Combine(root, "scripts", "drop-box.json"), "--headless", "--out", Out], Tertius.Demo.DemoScenes.All, output, error);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        using JsonDocument landed = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, "landed.json")));
        JsonElement box = landed.RootElement.GetProperty("entities").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "box");
        box.GetProperty("pose").GetProperty("position")[1].GetDouble().Should().BeApproximately(0.5, 0.02, "the 1 m box rests on the floor whose top is at y = 0");
        File.Exists(Path.Combine(Out, "landed.png")).Should().BeTrue();
    }
}
