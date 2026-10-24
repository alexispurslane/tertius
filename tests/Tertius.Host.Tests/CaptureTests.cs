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
public sealed class CaptureTests : IDisposable
{
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>
    /// Runs the script on a fresh world and a fresh validating renderer, with its dumps and its images in
    /// <paramref name="output"/>, and checks that validation stayed silent.
    /// </summary>
    private static RunResult Run(string json, Func<World> newWorld, Func<World, IRenderResources, SceneInstance> build,
        string output)
    {
        using World world = newWorld();
        return Run(json: json, world: world, build: build, output: output, images: new(output));
    }

    /// <summary>
    /// Runs the script on the given world and a fresh validating renderer, with its dumps in <paramref name="output"/> and
    /// its images in <paramref name="images"/>, and checks that validation stayed silent.
    /// </summary>
    private static RunResult Run(string json, World world, Func<World, IRenderResources, SceneInstance> build,
        string? output, PngFolder images)
    {
        Script script = Script.Parse(json);
        // A small renderer: the runner resizes it to the script's window.
        VulkanRenderer renderer = Headless(16, 16);
        try
        {
            using var playback = new HeadlessPlayback(renderer, script.Window, images);
            return ScriptRunner.Run(script: script, world: world, scene: build(world, renderer), outputFolder: output, playback: playback);
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
        RunResult result = Run(
            json: """{ "scene": "lit-box", "window": { "width": 320, "height": 180 }, "steps": [ { "do": "capture", "name": "front" } ] }""",
            newWorld: NewWorld, build: LitBox, output: folder);

        result.Captures.Should().Equal("front");
        Png image = Png.Load(Path.Combine(folder, "front.png"));
        image.Width.Should().Be(320);
        image.Height.Should().Be(180);
    }

    [Fact]
    public void AScriptWithoutAWindowCapturesAt1280By720()
    {
        Run(json: """{ "scene": "lit-box", "steps": [ { "do": "capture", "name": "front" } ] }""", newWorld: NewWorld, build: LitBox, output: folder);

        Png image = Png.Load(Path.Combine(folder, "front.png"));
        (image.Width, image.Height).Should().Be((1280, 720));
    }

    [Fact]
    public void ACaptureShowsTheSceneThroughTheCamera()
    {
        Run(
            json: """{ "scene": "lit-box", "window": { "width": 320, "height": 180 }, "steps": [ { "do": "capture", "name": "front" } ] }""",
            newWorld: NewWorld, build: LitBox, output: folder);

        Png image = Png.Load(Path.Combine(folder, "front.png"));
        image.IsRed(160, 90).Should().BeTrue("the red box fills the middle of the front camera's view");
        image.IsRed(5, 5).Should().BeFalse("the corner shows the empty background");
    }

    [Fact]
    public void ACameraStepChangesTheViewOfTheCapturesAfterIt()
    {
        Run(json: """
            { "scene": "lit-box", "window": { "width": 320, "height": 180 },
              "steps": [ { "do": "capture", "name": "before" }, { "do": "camera", "entity": "away" }, { "do": "capture", "name": "after" } ] }
            """, newWorld: NewWorld, build: LitBox, output: folder);

        Png.Load(Path.Combine(folder, "before.png")).IsRed(160, 90).Should().BeTrue();
        Png.Load(Path.Combine(folder, "after.png")).IsRed(160, 90).Should()
            .BeFalse("the away camera looks away from the box");
    }

    [Fact]
    public void ACaptureShowsTheStateAfterTheTicksBeforeIt()
    {
        Run(json: """
            { "scene": "lit-drop", "window": { "width": 320, "height": 180 },
              "steps": [ { "do": "capture", "name": "start" }, { "do": "run", "ticks": 90 }, { "do": "capture", "name": "landed" } ] }
            """, newWorld: NewJoltWorld, build: LitDrop, output: folder);

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

        Run(json: script, newWorld: NewJoltWorld, build: LitDrop, output: folder);
        Run(json: script, newWorld: NewJoltWorld, build: LitDrop, output: second);

        File.ReadAllBytes(Path.Combine(second, "falling.png")).Should()
            .Equal(File.ReadAllBytes(Path.Combine(folder, "falling.png")));
    }

    [Fact]
    public void ACaptureWritesItsPngIntoTheFolderOfThePlaybacksTargetWhetherOrNotTheRunHasAnOutputFolder()
    {
        using World world = NewWorld();
        string images = Path.Combine(folder, "images");

        Run(
            json: """{ "scene": "lit-box", "window": { "width": 32, "height": 32 }, "steps": [ { "do": "capture", "name": "now" } ] }""",
            world: world, build: LitBox, output: null, images: new(images));

        File.Exists(Path.Combine(images, "now.png")).Should()
            .BeTrue("the target names the folder, and the PNG sink makes it");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void APngTargetCannotExistWithoutAFolder(string empty)
    {
        Action make = () => _ = new PngFolder(empty);

        make.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("video.mp4")]
    [InlineData("video")]
    [InlineData("video.mkv.png")]
    public void AVideoTargetCannotExistWithoutAMatroskaFile(string path)
    {
        Action make = () => _ = new VideoFile(path, 30);

        make.Should().Throw<ArgumentException>().Where(ex => ex.ParamName == "path");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    [InlineData(-30)]
    public void AVideoTargetCannotExistWithARateOutside1To240FramesPerSecond(int framesPerSecond)
    {
        Action make = () => _ = new VideoFile("video.mkv", framesPerSecond);

        make.Should().Throw<ArgumentOutOfRangeException>().Where(ex => ex.ParamName == "framesPerSecond");
    }

    [Theory]
    [InlineData("video.mkv", 1)]
    [InlineData("runs/VIDEO.MKV", 240)]
    public void AVideoTargetTakesAMatroskaFileInAnyCaseAndARateFrom1To240FramesPerSecond(string path,
        int framesPerSecond)
    {
        var video = new VideoFile(path, framesPerSecond);

        (video.Path, video.FramesPerSecond).Should().Be((path, framesPerSecond));
    }

    [Fact]
    public void ACaptureAfterTheCameraEntityDiesStopsTheRunAndNamesTheStep()
    {
        using World world = NewWorld();

        Action run = () =>
            Run(
                json: """{ "scene": "lit-box", "window": { "width": 32, "height": 32 }, "steps": [ { "do": "run", "ticks": 2 }, { "do": "capture", "name": "now" } ] }""",
                world: world, build: (w, resources) =>
                {
                    SceneInstance scene = LitBox(w, resources);
                    w.AddComponent(scene["front"], new DestroysItselfOnFirstTick());
                    return scene;
                }, output: folder, images: new(folder));

        run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2").And.Contain("camera").And
            .Contain("not alive");
        File.Exists(Path.Combine(folder, "now.png")).Should().BeFalse("an image of nothing is not written");
    }
}
