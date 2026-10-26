using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Demo.DemoScenes;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The window films look out through the glass at the station. A cabin lamp's reflection on a pane appears where the
/// lamp's mirror image in the pane's inner face lies. Seen head-on, glass with an anti-reflection coating reflects about
/// 0.5 % of the light, so the reflection is a soft bright spot, as in a train window at night, and the view behind it
/// still shows. Glass without the coating reflects 16 % head-on, and the reflection would be a white blob that hides the
/// view. At a grazing angle, glass reflects much more, so the highlights on the side and roof glass in the cabin views
/// stay.
/// </summary>
public sealed class ShuttleGlareTests : IDisposable
{
    /// <summary>
    /// A brightest channel of 224 of 255, seven eighths of white. Near white, the tone curve is nearly flat, so a pixel
    /// brighter than this barely changes with what lies behind the glass, and the view behind the reflection is lost.
    /// </summary>
    private const int ShortOfWhite = 224;

    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>
    /// In <c>demo-surface</c>, the camera of a window film sees the mirror image of a lamp in the inner face of the
    /// pane that the film looks through. The test renders the view with the real renderer and reads the pixel of the
    /// mirror image, where the lamp's reflection is brightest. Its brightest channel stays below 224 of 255, short of
    /// the white that hides the view. It does not check that the reflection or the view shows. Before the scenes moved to
    /// SI units, the lamps sent many times the sunlight, and at 0 stops the coated glass gave 186 at the nose pane and 200
    /// at the port pane, while glass that reflects 16 % head-on, as the orbit scene's glass does, gave 254 at all three: a
    /// white blob. In SI units the lamps send less than a thousandth of the sunlit view outside, which sets the film's
    /// exposure, so their reflection stays far below white (docs/plans/local-light-shadows.md, Part 5).
    /// </summary>
    [Theory]
    [InlineData("window-camera", "pane-nose", "lamp-port-fore")]
    [InlineData("window-camera", "pane-nose", "lamp-starboard-fore")]
    [InlineData("port-window-camera", "pane-port-7", "lamp-starboard-aft")]
    public void ALampsReflectionInTheGlassThatAWindowFilmLooksThroughStaysShortOfWhite(
        string camera,
        string paneId,
        string lampId
    )
    {
        HullPane pane = Shuttle.Glass.Single(p => p.Id == paneId);
        (string Id, BoundingBox Panel) lamp = Shuttle.Lamps.Single(l => l.Id == lampId);
        // The lamp's light lies 0.3 m in front of its panel, toward the cabin's middle. The pane's inner face lies 1 cm
        // inside the middle of its wall.
        Double3 light = lamp.Panel.Center + new Double3(-Math.Sign(lamp.Panel.Center.X) * 0.3, 0, 0);
        Double3 normal = PaneChecks.Normal(pane),
            face = pane.Center - normal * 0.01;
        Double3 mirror = light - normal * (2 * Double3.Dot(light - face, normal));

        (Png picture, DPose view, double verticalFov) = Render(camera);

        PaneChecks
            .CrossesTheHole(pane.Inner, view.Position, mirror)
            .Should()
            .BeTrue($"the mirror image of {lampId} lies behind {paneId} as {camera} sees it");
        Double3 projected = Picture.Project(view, mirror, verticalFov);
        Picture.Contains(projected).Should().BeTrue($"{camera} sees the mirror image of {lampId}");
        int x = (int)Math.Round((1 + projected.X) / 2 * picture.Width),
            y = (int)Math.Round((1 - projected.Y) / 2 * picture.Height);
        (int R, int G, int B) reflection = picture.Average(x, y);
        Math.Max(reflection.R, Math.Max(reflection.G, reflection.B))
            .Should()
            .BeLessThan(
                ShortOfWhite,
                $"the reflection of {lampId} in {paneId} at ({x}, {y}) leaves the view visible: it shows {reflection}"
            );
    }

    /// <summary>
    /// Builds <c>demo-surface</c> on a real world, runs one tick and captures the view of <paramref name="camera"/> at the
    /// long script's size with the real headless renderer. Returns the picture, the camera's pose in the shuttle's frame and the world's vertical view angle.
    /// </summary>
    private (Png Picture, DPose View, double VerticalFov) Render(string camera)
    {
        var script = Script.Parse(
            $$"""
            { "scene": "demo-surface", "window": { "width": {{BlueDanube.Window.Width}}, "height": {{BlueDanube.Window.Height}} },
              "steps": [ { "do": "camera", "entity": "{{camera}}" }, { "do": "run", "ticks": 1 }, { "do": "capture", "name": "view" } ] }
            """
        );
        using World world = NewJoltWorld();
        // The scene is in SI units, from sunlight to cabin lamps, so the view shows it as a window film does.
        VulkanRenderer renderer = GpuTesting.LikeAScriptedRun(16, 16);
        DPose view;
        double verticalFov;
        try
        {
            SceneInstance scene = All.Single(s => s.Name == "demo-surface").Build(world, renderer);
            ScriptRunner.Run(
                script: script,
                world: world,
                scene: scene,
                outputFolder: folder,
                playback: new HeadlessPlayback(renderer, script.Window, new PngFolder(folder))
            );
            view = world.PoseIn(scene[camera], world.GetOwnedFrame(scene["shuttle"]));
            verticalFov = world.VerticalFov;
        }
        finally
        {
            renderer.Dispose();
            renderer.ValidationMessages.Should().BeEmpty();
        }

        return (Png.Load(Path.Combine(folder, "view.png")), view, verticalFov);
    }
}
