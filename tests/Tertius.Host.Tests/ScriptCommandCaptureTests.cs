using System.Text.Json;
using Tertius.Demo;
using Tertius.Rendering;
using static Tertius.Host.Tests.GpuTesting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>The command line with scripts that capture images, which makes it create a headless renderer.</summary>
public sealed class ScriptCommandCaptureTests : IDisposable
{
    private readonly string folder = NewFolder();
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    private string Out => Path.Combine(folder, "out");

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string ScriptFile(string json)
    {
        string path = Path.Combine(folder, "script.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void TheCommandCapturesWithAHeadlessRendererAndWritesThePathOfEveryFile()
    {
        string script = ScriptFile("""
                                   { "scene": "lit-box", "window": { "width": 160, "height": 90 },
                                     "steps": [ { "do": "capture", "name": "front" }, { "do": "dump", "name": "state" } ] }
                                   """);

        int code = ScriptCommand.Run(["--script", script, "--headless", "--out", Out], [LitBoxScene], output, error);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        Png.Load(Path.Combine(Out, "front.png")).IsRed(80, 45).Should()
            .BeTrue("the scene gets the renderer's resources, so its box has a mesh");
        output.ToString().Should().Contain(Path.Combine(Out, "front.png")).And.Contain(Path.Combine(Out, "state.json"));
    }

    [Fact]
    public void AScriptThatOnlyCapturesGetsOneHeadlessRendererWithoutImpostors()
    {
        string script =
            ScriptFile(
                """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "capture", "name": "front" } ] }""");
        var made = new List<VulkanRenderer>();

        int code = ScriptCommand.Run(["--script", script, "--headless", "--out", Out], [LitBoxScene], output, error,
            options =>
            {
                made.Add(VulkanRenderer.CreateHeadless(options));
                return made[^1];
            });

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        made.Should().ContainSingle();
        File.Exists(Path.Combine(Out, "front.png")).Should().BeTrue();
        // The box is about 40 px tall, below the impostor threshold, so a renderer with impostors on would capture it and
        // draw it as an impostor. A scripted run draws it as geometry, so its images do not depend on impostor state.
        made[0].LastFrameStats.Impostors.Should().Be(0);
        made[0].LastFrameStats.ImpostorCaptures.Should().Be(0);
    }

    [Fact]
    public void AScriptThatOnlyFilmsGetsAHeadlessRendererAndWritesThePathOfEveryFrame()
    {
        string script =
            ScriptFile(
                """{ "scene": "lit-box", "window": { "width": 160, "height": 90 }, "steps": [ { "do": "film", "name": "box", "frames": 2, "every": 1 } ] }""");

        int code = ScriptCommand.Run(["--script", script, "--headless", "--out", Out], [LitBoxScene], output, error);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        Png.Load(Path.Combine(Out, "box-000000.png")).IsRed(80, 45).Should()
            .BeTrue("the scene gets the renderer's resources, so its box has a mesh");
        Png.Load(Path.Combine(Out, "box-000001.png")).IsRed(80, 45).Should().BeTrue();
        output.ToString().Should().Contain(Path.Combine(Out, "box-000000.png")).And
            .Contain(Path.Combine(Out, "box-000001.png"));
    }

    [Fact]
    public void ARendererThatCannotStartExitsWithTheToolErrorCodeAndWritesNoImage()
    {
        string script = ScriptFile("""{ "scene": "lit-box", "steps": [ { "do": "capture", "name": "front" } ] }""");

        int code = ScriptCommand.Run(["--script", script, "--headless", "--out", Out], [LitBoxScene], output, error,
            _ => throw new InvalidOperationException("no Vulkan driver"));

        code.Should().Be(ScriptCommand.ToolError);
        error.ToString().Should().Contain(script).And.Contain("no Vulkan driver");
        File.Exists(Path.Combine(Out, "front.png")).Should().BeFalse();
    }

    [Fact]
    public void TheDemoRunsItsDropBoxScript()
    {
        string root = RepositoryRoot();

        int code = ScriptCommand.Run(
            ["--script", Path.Combine(root, "scripts", "drop-box.json"), "--headless", "--out", Out], DemoScenes.All,
            output, error);

        code.Should().Be(ScriptCommand.Ok, error.ToString());
        using JsonDocument landed = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, "landed.json")));
        landed.RootElement.GetProperty("camera").GetString().Should().Be("camera");
        JsonElement box = landed.RootElement.GetProperty("entities").EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == "box");
        box.GetProperty("pose").GetProperty("position")[1].GetDouble().Should()
            .BeApproximately(0.5, 0.02, "the 1 m box rests on the floor whose top is at y = 0");
        // The camera at (0, 2.5, 9), tilted down 6.3 degrees, sees the box's center at y = 3 on row 256 and at y = 0.5 on
        // row 428 of the 720 rows.
        Png start = Png.Load(Path.Combine(Out, "start.png"));
        start.IsOrange(640, 256).Should().BeTrue("the box starts at y = 3");
        Png end = Png.Load(Path.Combine(Out, "landed.png"));
        end.IsOrange(640, 428).Should().BeTrue("the box rests on the floor");
        end.IsOrange(640, 256).Should().BeFalse("the box has left its start");
    }
}
