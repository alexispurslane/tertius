using System.Numerics;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.GpuTesting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The <c>outlines</c> step on the real headless renderer: it shows or hides the frame outlines from the next capture on,
/// and captures with outlines stay the same from run to run. See docs/plans/blue-danube-demo.md, change 3, "Who turns
/// outlines on".
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class OutlinesStepTests : IDisposable
{
    /// <summary>The bubble of the ship, as the camera <c>front</c> sees it: 4 m ahead, in the axes of space, with radius 2 m.</summary>
    private static readonly FrameOutline ShipBubble = new SemanticOutline(new(0, 0, -4), Quaternion.Identity, new(2));

    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>
    /// The scene "lit-box" with one frame more: <c>ship</c>, a child of the root frame <c>space</c> at its origin, with a
    /// bubble that is a sphere of radius 2 m round the box. The camera <c>front</c> stands at (0, 0, 4) in space and looks
    /// along -Z, so the origin of the ship is 4 m in front of it.
    /// </summary>
    private static SceneInstance LitBoxInShip(World world, IRenderResources resources)
    {
        SceneInstance scene = LitBox(world, resources);
        scene.Add("ship",
            world.Spawn(new() { Frame = world.GetFrame(scene["box"]), AsFrame = new(FrameBounds.Sphere(2)) }));
        return scene;
    }

    /// <summary>
    /// The real headless renderer, which also notes the outlines of each snapshot that it draws. A headless capture draws
    /// exactly one snapshot, so the notes have one entry for each capture.
    /// </summary>
    private sealed class NotesTheOutlines(VulkanRenderer inner) : IRenderer
    {
        public List<FrameOutline[]> Drawn { get; } = [];

        public void Render(RenderSnapshot snapshot, float alpha)
        {
            Drawn.Add([.. snapshot.Outlines]);
            inner.Render(snapshot, alpha);
        }

        public void Resize(int width, int height) => inner.Resize(width, height);

        public void CaptureNextFrame(ICaptureSink sink) => inner.CaptureNextFrame(sink);

        public MeshId CreateMesh(MeshData mesh) => inner.CreateMesh(mesh);

        public TextureId CreateTexture(TextureData texture) => inner.CreateTexture(texture);

        public MaterialId CreateMaterial(MaterialDesc material) => inner.CreateMaterial(material);

        public void Dispose() => inner.Dispose();
    }

    /// <summary>
    /// Runs the script on a fresh world with the scene "lit-box" in a ship, and a fresh validating renderer. Returns the
    /// outlines of each capture, in the order taken, and checks that validation stayed silent.
    /// </summary>
    private static List<FrameOutline[]> Run(string steps, string output)
    {
        Script script =
            Script.Parse(
                $$"""{ "scene": "lit-box-in-ship", "window": { "width": 320, "height": 180 }, "steps": [ {{steps}} ] }""");
        using World world = NewWorld();
        // A small renderer: the runner resizes it to the script's window.
        VulkanRenderer vulkan = Headless(16, 16);
        var renderer = new NotesTheOutlines(vulkan);
        try
        {
            ScriptRunner.Run(script, world, LitBoxInShip(world, renderer), output,
                new HeadlessPlayback(renderer, script.Window, new PngFolder(output)));
            return renderer.Drawn;
        }
        finally
        {
            renderer.Dispose();
            vulkan.ValidationMessages.Should().BeEmpty("the Vulkan validation layers must stay silent");
        }
    }

    [Fact]
    public void AnOutlinesStepShowsTheFrameOutlinesFromTheNextCaptureOn()
    {
        List<FrameOutline[]> captures = Run("""
                                            { "do": "capture", "name": "before" }, { "do": "outlines", "show": true }, { "do": "capture", "name": "after" }, { "do": "capture", "name": "later" }
                                            """, folder);

        captures.Should().HaveCount(3);
        captures[0].Should().BeEmpty("the outlines are off by default");
        captures[1].Should().Equal(ShipBubble);
        captures[2].Should().Equal(ShipBubble);
    }

    [Fact]
    public void AnOutlinesStepThatDoesNotShowThemHidesTheFrameOutlinesFromTheNextCaptureOn()
    {
        List<FrameOutline[]> captures = Run("""
                                            { "do": "outlines", "show": true }, { "do": "capture", "name": "shown" }, { "do": "outlines", "show": false }, { "do": "capture", "name": "hidden" }
                                            """, folder);

        captures.Should().HaveCount(2);
        captures[0].Should().Equal(ShipBubble);
        captures[1].Should().BeEmpty();
    }

    /// <summary>
    /// Both runs must draw their capture with the ship's bubble, so that the bytes compared are those of an image with
    /// outlines. Until the renderer draws the outlines (ticket ter-p51r), the images hold none, so only the two checks of
    /// the drawn outlines are about outlines.
    /// </summary>
    [Fact]
    public void TwoRunsWithOutlinesCaptureByteIdenticalImages()
    {
        const string steps = """{ "do": "outlines", "show": true }, { "do": "capture", "name": "outlined" }""";
        string second = Path.Combine(folder, "second");

        List<FrameOutline[]> firstCaptures = Run(steps, folder);
        List<FrameOutline[]> secondCaptures = Run(steps, second);

        firstCaptures.Should().ContainSingle().Which.Should().Equal(ShipBubble);
        secondCaptures.Should().ContainSingle().Which.Should().Equal(ShipBubble);
        File.ReadAllBytes(Path.Combine(second, "outlined.png")).Should()
            .Equal(File.ReadAllBytes(Path.Combine(folder, "outlined.png")));
    }
}
