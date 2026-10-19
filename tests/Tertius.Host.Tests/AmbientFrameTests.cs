using System.Numerics;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The hemisphere ambient light does not depend on the frame that the camera rides in. A white sphere stands in a root
/// frame whose gravity pulls along -Y, lit only by a red sky and a blue ground. Two cameras see it from the same place
/// and in the same direction: one in the root frame, one in a frame that is turned against it.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class AmbientFrameTests : IDisposable
{
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static SceneInstance Build(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e4f), GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        world.SunIntensity = 0;
        world.Ambient = new AmbientLight { SkyColor = new Vector3(0.6f, 0, 0), GroundColor = new Vector3(0, 0, 0.6f), Intensity = 1 };
        world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 0, -4), DQuaternion.Identity),
            Render = new RenderData
            {
                Mesh = resources.CreateMesh(Primitives.Sphere(1, 64, 32)),
                Material = resources.CreateMaterial(new MaterialDesc(Vector4.One, SpecularIntensity: 0)),
                BoundingRadius = 1,
                Flags = RenderFlags.Visible,
            },
        });
        // The turned frame has no gravity of its own. Its camera stands where the root camera stands, and looks the same
        // way: its pose in the turned frame undoes the frame's turn.
        var turn = new DPose(new Double3(1, 2, 3), DQuaternion.FromAxisAngle(new Double3(1, 1, 0).Normalized(), 1.1));
        FrameHandle turned = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = turn,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
        }));
        scene.Add("root-camera", world.Spawn(new SpawnDesc { Frame = ground, Pose = DPose.Identity, Render = new RenderData() }));
        scene.Add("turned-camera", world.Spawn(new SpawnDesc { Frame = turned, Pose = turn.Inverse(), Render = new RenderData() }));
        world.Camera = scene["root-camera"];
        return scene;
    }

    [Fact]
    public void TheSameSurfaceGetsTheSameAmbientLightFromCamerasInFramesThatAreTurnedAgainstEachOther()
    {
        Script script = Script.Parse("""
            { "scene": "ambient", "window": { "width": 320, "height": 180 },
              "steps": [ { "do": "capture", "name": "root" }, { "do": "camera", "entity": "turned-camera" }, { "do": "capture", "name": "turned" } ] }
            """);
        using World world = NewWorld();
        VulkanRenderer renderer = GpuTesting.Headless(16, 16);
        try
        {
            ScriptRunner.Run(script, world, Build(world, renderer), folder, new HeadlessPlayback(renderer, script.Window, new PngFolder(folder)));
        }
        finally
        {
            renderer.Dispose();
            renderer.ValidationMessages.Should().BeEmpty();
        }
        Png root = Png.Load(Path.Combine(folder, "root.png")), turned = Png.Load(Path.Combine(folder, "turned.png"));

        // The probes are the projections of the points 0.8 m from the sphere's center toward the top, the right, the bottom
        // and the left, 31 px from the image's center, and the center. The sphere's edge is 40 px from the center. The top
        // of the sphere faces up: the red sky lights it. Its side faces sideways: sky and ground mix.
        (int r, int g, int b) top = root.Average(160, 59), side = root.Average(191, 90);
        (top.r > top.b + 40).Should().BeTrue($"the top faces the sky, not {top}");
        Math.Abs(side.r - side.b).Should().BeLessThan(12, $"the side faces sideways, not {side}");
        (side.r + side.b).Should().BeGreaterThan(60, $"the probe is on the sphere, not on the sky: {side}");
        foreach ((int x, int y) in new[] { (160, 59), (191, 90), (160, 121), (129, 90), (160, 90) })
        {
            (int r, int g, int b) a = root.Average(x, y), c = turned.Average(x, y);
            (Math.Abs(a.r - c.r) + Math.Abs(a.g - c.g) + Math.Abs(a.b - c.b)).Should().BeLessThan(4, $"at ({x}, {y}) the root view shows {a} and the turned view {c}");
        }
    }
}
