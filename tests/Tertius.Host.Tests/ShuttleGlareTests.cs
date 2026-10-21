using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Demo.DemoScenes;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The window films look out through the glass at the station. A cabin lamp's highlight on a pane appears where the
/// lamp's mirror image in the pane's inner face lies. Seen head-on, glass with an anti-reflection coating reflects about
/// 0.5 % of the light, so the highlight stays faint and the view stays the brightest thing. At a grazing angle, glass
/// reflects much more, so the highlights on the side and roof glass in the cabin views stay.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ShuttleGlareTests : IDisposable
{
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>
    /// In <c>demo-surface</c>, the camera of a window film sees the mirror image of a lamp in the inner face of the pane
    /// that the film looks through. The test renders the view twice with the real renderer, once with the cabin's lamps
    /// and once without them. The lamps also light the glass itself, smoothly across the pane. So the test compares what
    /// the lamps add at the pixel of the mirror image with what they add 80 px above and below it, on the same pane. The
    /// highlight stands less than a quarter of full white above the glass beside it. Glass that reflects 16 % head-on, as
    /// the orbit scene's glass does, gives a highlight up to 192 of 255 above it, and the coated glass about 40.
    /// </summary>
    [Theory]
    [InlineData("window-camera", "pane-nose", "lamp-port-fore")]
    [InlineData("window-camera", "pane-nose", "lamp-starboard-fore")]
    [InlineData("port-window-camera", "pane-port-7", "lamp-starboard-aft")]
    public void ALampsHighlightInTheGlassThatAWindowFilmLooksThroughStaysFaint(string camera, string paneId, string lampId)
    {
        HullPane pane = Shuttle.Glass.Single(p => p.Id == paneId);
        var lamp = Shuttle.Lamps.Single(l => l.Id == lampId);
        // The lamp's light lies 0.3 m in front of its panel, toward the cabin's middle. The pane's inner face lies 1 cm
        // inside the middle of its wall.
        Double3 light = (Double3)lamp.Panel.Center + new Double3(-Math.Sign(lamp.Panel.Center.X) * 0.3, 0, 0);
        Double3 normal = PaneChecks.Normal(pane), face = (Double3)pane.Center - normal * 0.01;
        Double3 mirror = light - normal * (2 * Double3.Dot(light - face, normal));

        (Png lit, DPose view, double verticalFov) = Render(camera, withLamps: true);
        (Png dark, _, _) = Render(camera, withLamps: false);

        PaneChecks.CrossesTheHole(pane.Inner, view.Position, mirror).Should().BeTrue($"the mirror image of {lampId} lies behind {paneId} as {camera} sees it");
        Double3 projected = Picture.Project(view, mirror, verticalFov);
        Picture.Contains(projected).Should().BeTrue($"{camera} sees the mirror image of {lampId}");
        int x = (int)Math.Round((1 + projected.X) / 2 * lit.Width), y = (int)Math.Round((1 - projected.Y) / 2 * lit.Height);
        int peak = Added(lit, dark, x, y), beside = (Added(lit, dark, x, y - Beside) + Added(lit, dark, x, y + Beside)) / 2;
        (peak - beside).Should().BeLessThan(64, $"the highlight of {lampId} in {paneId} at ({x}, {y}) is faint: the lamps add {peak} there and {beside} beside it");
    }

    /// <summary>How far above and below the highlight the test samples the glass beside it, in pixels.</summary>
    private const int Beside = 80;

    /// <summary>How much the lamps add to the brightest channel at a pixel: <paramref name="lit"/> with them against <paramref name="dark"/> without them.</summary>
    private static int Added(Png lit, Png dark, int x, int y)
    {
        (int R, int G, int B) a = lit.Average(x, y), b = dark.Average(x, y);
        return Math.Max(a.R - b.R, Math.Max(a.G - b.G, a.B - b.B));
    }

    /// <summary>
    /// Builds <c>demo-surface</c> on a real world, without the cabin's lamps when <paramref name="withLamps"/> is false,
    /// runs one tick and captures the view of <paramref name="camera"/> at the long script's size with the real headless
    /// renderer. Returns the picture, the camera's pose in the shuttle's frame and the world's vertical view angle.
    /// </summary>
    private (Png Picture, DPose View, double VerticalFov) Render(string camera, bool withLamps)
    {
        string name = withLamps ? "lit" : "dark";
        Script script = Script.Parse($$"""
            { "scene": "demo-surface", "window": { "width": {{BlueDanube.Window.Width}}, "height": {{BlueDanube.Window.Height}} },
              "steps": [ { "do": "camera", "entity": "{{camera}}" }, { "do": "run", "ticks": 1 }, { "do": "capture", "name": "{{name}}" } ] }
            """);
        using World world = NewJoltWorld();
        VulkanRenderer renderer = GpuTesting.Headless(16, 16);
        DPose view;
        double verticalFov;
        try
        {
            SceneInstance scene = All.Single(s => s.Name == "demo-surface").Build(world, renderer);
            if (!withLamps)
            {
                foreach ((string id, _) in Shuttle.Lamps)
                    world.Destroy(scene[id]);
            }
            ScriptRunner.Run(script, world, scene, folder, new HeadlessPlayback(renderer, script.Window, new PngFolder(folder)));
            view = world.PoseIn(scene[camera], world.GetOwnedFrame(scene["shuttle"]));
            verticalFov = world.VerticalFov;
        }
        finally
        {
            renderer.Dispose();
            renderer.ValidationMessages.Should().BeEmpty();
        }
        return (Png.Load(Path.Combine(folder, name + ".png")), view, verticalFov);
    }
}
