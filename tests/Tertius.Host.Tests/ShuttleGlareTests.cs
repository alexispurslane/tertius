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
    /// In <c>demo-surface</c>, the window film cameras (<c>window-camera</c> looking through <c>pane-nose</c> and
    /// <c>port-window-camera</c> looking through <c>pane-cockpit-port-fore</c>) render through clear anti-reflective
    /// dielectric glazing without lamp glare washout or blue tint: the center sightline passes cleanly through the pane,
    /// and the rendered center pixels stay short of clipped white.
    /// </summary>
    [Theory]
    [InlineData("window-camera", "pane-nose")]
    [InlineData("port-window-camera", "pane-cockpit-port-fore")]
    public void TheWindowFilmsRenderClearThroughAntiReflectiveGlazingWithoutGlareWashout(string camera, string paneId)
    {
        HullPane pane = Shuttle.Glass.Single(p => p.Id == paneId);
        (Png picture, DPose view, double verticalFov) = Render(camera);

        Double3 ahead = view.Position + view.Rotation.Rotate(-Double3.UnitZ) * 10;
        PaneChecks
            .Through(pane, view.Position, ahead)
            .Should()
            .BeTrue($"{camera}'s center sightline passes through {paneId}");
        Double3 projected = Picture.Project(view, pane.Center, verticalFov);
        Picture.Contains(projected).Should().BeTrue($"{camera} frames {paneId}");
        int x = (int)Math.Round((1 + projected.X) / 2 * picture.Width),
            y = (int)Math.Round((1 - projected.Y) / 2 * picture.Height);
        (int R, int G, int B) sample = picture.Average(x, y);
        Math.Max(sample.R, Math.Max(sample.G, sample.B))
            .Should()
            .BeLessThan(
                ShortOfWhite,
                $"the view through {paneId} at ({x}, {y}) stays clear of glare washout: it shows {sample}"
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
