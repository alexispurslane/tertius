using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.GpuTesting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// Capture steps on the real headless renderer: the image files, what they show, the camera step, and their stability
/// from run to run. See docs/plans/host-and-scripted-runs.md, "Captures and the camera".
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class CaptureTests : IDisposable
{
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>Runs the script on a fresh world and a fresh validating renderer, and checks that validation stayed silent.</summary>
    private static RunResult Run(string json, Func<World> newWorld, Func<World, IRenderResources, SceneInstance> build, string? output)
    {
        using World world = newWorld();
        return Run(json, world, build, output);
    }

    /// <summary>Runs the script on the given world and a fresh validating renderer, and checks that validation stayed silent.</summary>
    private static RunResult Run(string json, World world, Func<World, IRenderResources, SceneInstance> build, string? output)
    {
        Script script = Script.Parse(json);
        // A small renderer: the runner resizes it to the script's window.
        VulkanRenderer renderer = Headless(16, 16);
        try
        {
            return ScriptRunner.Run(script, world, build(world, renderer), output, new HeadlessPlayback(renderer, script.Window));
        }
        finally
        {
            renderer.Dispose();
            renderer.ValidationMessages.Should().BeEmpty("the Vulkan validation layers must stay silent");
        }
    }

    [Fact]
    public void ACaptureWritesAPngOfTheScriptsWindowSizeIntoTheOutputFolder()
    {
        RunResult result = Run("""{ "scene": "lit-box", "window": { "width": 320, "height": 180 }, "steps": [ { "do": "capture", "name": "front" } ] }""",
            NewWorld, LitBox, folder);

        string path = Path.Combine(folder, "front.png");
        result.Captures.Should().Equal(path);
        Png image = Png.Load(path);
        image.Width.Should().Be(320);
        image.Height.Should().Be(180);
    }

    [Fact]
    public void AScriptWithoutAWindowCapturesAt1280By720()
    {
        Run("""{ "scene": "lit-box", "steps": [ { "do": "capture", "name": "front" } ] }""", NewWorld, LitBox, folder);

        Png image = Png.Load(Path.Combine(folder, "front.png"));
        (image.Width, image.Height).Should().Be((1280, 720));
    }

    [Fact]
    public void ACaptureShowsTheSceneThroughTheCamera()
    {
        Run("""{ "scene": "lit-box", "window": { "width": 320, "height": 180 }, "steps": [ { "do": "capture", "name": "front" } ] }""",
            NewWorld, LitBox, folder);

        Png image = Png.Load(Path.Combine(folder, "front.png"));
        image.IsRed(160, 90).Should().BeTrue("the red box fills the middle of the front camera's view");
        image.IsRed(5, 5).Should().BeFalse("the corner shows the empty background");
    }

    [Fact]
    public void ACameraStepChangesTheViewOfTheCapturesAfterIt()
    {
        Run("""
            { "scene": "lit-box", "window": { "width": 320, "height": 180 },
              "steps": [ { "do": "capture", "name": "before" }, { "do": "camera", "entity": "away" }, { "do": "capture", "name": "after" } ] }
            """, NewWorld, LitBox, folder);

        Png.Load(Path.Combine(folder, "before.png")).IsRed(160, 90).Should().BeTrue();
        Png.Load(Path.Combine(folder, "after.png")).IsRed(160, 90).Should().BeFalse("the away camera looks away from the box");
    }

    [Fact]
    public void ACaptureShowsTheStateAfterTheTicksBeforeIt()
    {
        Run("""
            { "scene": "lit-drop", "window": { "width": 320, "height": 180 },
              "steps": [ { "do": "capture", "name": "start" }, { "do": "run", "ticks": 90 }, { "do": "capture", "name": "landed" } ] }
            """, NewJoltWorld, LitDrop, folder);

        int high = RowOfHeight(3, 180), low = RowOfHeight(0.5, 180);
        Png start = Png.Load(Path.Combine(folder, "start.png"));
        start.IsRed(160, high).Should().BeTrue("the box starts at y = 3");
        start.IsRed(160, low).Should().BeFalse("the floor shows where the box will land");
        Png landed = Png.Load(Path.Combine(folder, "landed.png"));
        landed.IsRed(160, high).Should().BeFalse("the box has fallen");
        landed.IsRed(160, low).Should().BeTrue("the box rests on the floor at y = 0.5");
    }

    [Fact]
    public void TheSameScriptCapturesByteIdenticalImagesInTwoRuns()
    {
        const string script = """
            { "scene": "lit-drop", "window": { "width": 320, "height": 180 },
              "steps": [ { "do": "run", "ticks": 40 }, { "do": "capture", "name": "falling" } ] }
            """;
        string second = Path.Combine(folder, "second");

        Run(script, NewJoltWorld, LitDrop, folder);
        Run(script, NewJoltWorld, LitDrop, second);

        File.ReadAllBytes(Path.Combine(second, "falling.png")).Should().Equal(File.ReadAllBytes(Path.Combine(folder, "falling.png")));
    }

    [Fact]
    public void ACaptureWithoutAnOutputFolderStopsTheRunBeforeTheFirstTick()
    {
        using World world = NewWorld();

        Action run = () => Run("""{ "scene": "lit-box", "window": { "width": 32, "height": 32 }, "steps": [ { "do": "run", "ticks": 1 }, { "do": "capture", "name": "now" } ] }""",
            world, LitBox, output: null);

        run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2").And.Contain("output folder");
        world.TickNumber.Should().Be(0);
    }

    [Fact]
    public void ACaptureAfterTheCameraEntityDiesStopsTheRunAndNamesTheStep()
    {
        using World world = NewWorld();

        Action run = () => Run("""{ "scene": "lit-box", "window": { "width": 32, "height": 32 }, "steps": [ { "do": "run", "ticks": 2 }, { "do": "capture", "name": "now" } ] }""",
            world, (w, resources) =>
            {
                SceneInstance scene = LitBox(w, resources);
                w.AddComponent(scene["front"], new DestroysItselfOnFirstTick());
                return scene;
            }, folder);

        run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2").And.Contain("camera").And.Contain("not alive");
        File.Exists(Path.Combine(folder, "now.png")).Should().BeFalse("an image of nothing is not written");
    }
}
