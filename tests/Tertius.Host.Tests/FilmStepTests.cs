using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.GpuTesting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The <c>film</c> step on the real headless renderer: it is the same as its expansion into run and capture steps, in its
/// images and in the state that it leaves, and a camera that dies during a film stops the run at the next frame. See
/// docs/plans/blue-danube-demo.md, change 2, "The film step".
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class FilmStepTests : IDisposable
{
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>
    /// Runs the steps on a fresh Jolt world with the scene that <paramref name="build"/> makes, and a fresh validating
    /// renderer, at 160 by 90 pixels, and checks that validation stayed silent.
    /// </summary>
    private static RunResult Run(string steps, Func<World, IRenderResources, SceneInstance> build, string output)
    {
        Script script = Script.Parse($$"""{ "scene": "film", "window": { "width": 160, "height": 90 }, "steps": [ {{steps}} ] }""");
        using World world = NewJoltWorld();
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
    public void TheImagesOfAFilmAreByteIdenticalToThoseOfItsExpansionIntoRunAndCaptureSteps()
    {
        string filmed = Path.Combine(folder, "filmed");
        string expanded = Path.Combine(folder, "expanded");
        string[] frames = ["drop-000000.png", "drop-000001.png", "drop-000002.png"];

        Run("""{ "do": "film", "name": "drop", "frames": 3, "every": 20 }""", LitDrop, filmed);
        Run("""
            { "do": "run", "ticks": 20 }, { "do": "capture", "name": "drop-000000" },
            { "do": "run", "ticks": 20 }, { "do": "capture", "name": "drop-000001" },
            { "do": "run", "ticks": 20 }, { "do": "capture", "name": "drop-000002" }
            """, LitDrop, expanded);

        foreach (string frame in frames)
            File.ReadAllBytes(Path.Combine(filmed, frame)).Should().Equal(File.ReadAllBytes(Path.Combine(expanded, frame)), frame);
        File.ReadAllBytes(Path.Combine(filmed, frames[0])).Should().NotEqual(File.ReadAllBytes(Path.Combine(filmed, frames[2])),
            "the box falls between the first frame and the last, so the frames are not all one image");
    }

    [Fact]
    public void AFilmLeavesTheWorldInTheStateOfARunStepOfAllItsTicks()
    {
        string filmed = Path.Combine(folder, "filmed");
        string ran = Path.Combine(folder, "ran");

        Run("""{ "do": "film", "name": "drop", "frames": 3, "every": 20 }, { "do": "dump", "name": "end" }""", LitDrop, filmed);
        Run("""{ "do": "run", "ticks": 60 }, { "do": "dump", "name": "end" }""", LitDrop, ran);

        string filmedDump = File.ReadAllText(Path.Combine(filmed, "end.json"));
        filmedDump.Should().Be(File.ReadAllText(Path.Combine(ran, "end.json")));
        filmedDump.Should().Contain("\"tick\": 60");
    }

    [Fact]
    public void WhenTheCameraDiesDuringAFilmTheRunFailsAtTheNextFrameAndKeepsTheFramesBeforeIt()
    {
        // The camera front destroys itself during tick 3. Frame 0 comes after ticks 0 and 1, frame 1 after ticks 2 and 3.
        Action run = () => Run("""{ "do": "film", "name": "box", "frames": 4, "every": 2 }""", (world, resources) =>
        {
            SceneInstance scene = LitBox(world, resources);
            world.AddComponent(scene["front"], new DestroysItselfOnTick(3));
            return scene;
        }, folder);

        run.Should().Throw<ScriptException>().Which.Message.Should()
            .Contain("step 1 (film)").And.Contain("frame 1").And.Contain("tick 4").And.Contain("camera 'front'").And.Contain("not alive");
        File.Exists(Path.Combine(folder, "box-000000.png")).Should().BeTrue("the frame before the camera died stays on disk");
        File.Exists(Path.Combine(folder, "box-000001.png")).Should().BeFalse("an image of nothing is not written");
    }
}
