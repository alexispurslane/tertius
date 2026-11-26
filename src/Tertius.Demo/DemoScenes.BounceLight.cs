using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The perceptual roughness of the slabs of the bounce light's scenes, as in the GPU tests.</summary>
    private const float BounceSlabRoughness = 0.8f;

    /// <summary>The scenes of the GPU tests of the bounce light (https://github.com/alexispurslane/tertius/wiki/screen-space-illumination, "Part 7").</summary>
    private static IEnumerable<Scene> BounceLightScenes =>
        [
            new("bounce-red-wall", BounceRedWall),
            new("bounce-raised-box", BounceRaisedBox),
            new("bounce-moving-reflector", (world, resources) => MovingPanels(world, resources, emissive: false)),
            new("bounce-moving-emission", (world, resources) => MovingPanels(world, resources, emissive: true)),
        ];

    /// <summary>
    /// The red wall of <c>BounceLightRenderTests</c> in the root frame <c>room</c>: a white <c>floor</c> with its top 1.5 m
    /// below the eye, and a red <c>back-wall</c> 5 m ahead, both 20 m wide, under the sun high to the right and behind,
    /// and a white ambient light of a tenth of the sun's irradiance per steradian, as in the test. The view
    /// <c>camera</c> stands at the origin and looks along -Z, 30 degrees down.
    /// </summary>
    private static SceneInstance BounceRedWall(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle room = world.CreateRootFrame(new(FrameBounds.Sphere(1e4f)));
        scene.Add("room", room.Entity);
        BounceSlab(
            world: world,
            resources: resources,
            scene: scene,
            frame: room,
            name: "floor",
            center: new(0, -2, 0),
            size: new(20, 1, 20),
            color: new(x: 1, y: 1, z: 1, w: 1)
        );
        BounceSlab(
            world: world,
            resources: resources,
            scene: scene,
            frame: room,
            name: "back-wall",
            center: new(0, 3.5, -5.5),
            size: new(20, 20, 1),
            color: new(x: 0.9f, y: 0.05f, z: 0.05f, w: 1)
        );
        Star(world: world, scene: scene, frame: room, center: Double3.Zero, direction: new(0.4, 0.8, 0.45));
        // The test's ambient radiance is a tenth of its sun's irradiance, and the sun sends the solar constant here.
        world.Ambient = new()
        {
            SkyColor = new(136.1f),
            GroundColor = new(136.1f),
            Intensity = 1,
        };
        AddCamera(
            world: world,
            scene: scene,
            frame: room,
            pose: new(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitX, -Math.PI / 6))
        );
        return scene;
    }

    /// <summary>
    /// The raised box of <c>BounceLightRenderTests</c> in the root frame <c>room</c>: a white <c>floor</c> with its top
    /// 0.1 m below the eye, and a white <c>box</c> 0.6 by 0.6 by 0.8 m whose underside hangs 0.3 m above it, 1.2 m ahead,
    /// under the sun 30 degrees high to the right and no ambient light. The view <c>camera</c> stands at the origin and
    /// looks level along -Z, so it sees the underside from below.
    /// </summary>
    private static SceneInstance BounceRaisedBox(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle room = world.CreateRootFrame(new(FrameBounds.Sphere(1e4f)));
        scene.Add("room", room.Entity);
        var white = new Vector4(x: 1, y: 1, z: 1, w: 1);
        BounceSlab(
            world: world,
            resources: resources,
            scene: scene,
            frame: room,
            name: "floor",
            center: new(0, -0.6, -5),
            size: new(20, 1, 20),
            color: white
        );
        BounceSlab(
            world: world,
            resources: resources,
            scene: scene,
            frame: room,
            name: "box",
            center: new(0, 0.5, -1.2),
            size: new(0.6f, 0.6f, 0.8f),
            color: white
        );
        Star(world: world, scene: scene, frame: room, center: Double3.Zero, direction: new(0.85, 0.5, 0.15));
        world.Ambient = new() { Intensity = 0 };
        AddCamera(world: world, scene: scene, frame: room, pose: new(Double3.Zero, DQuaternion.Identity));
        return scene;
    }

    /// <summary>Shows the lighting on neutral receivers as colored panels turn behind the camera once in six seconds.</summary>
    private static SceneInstance MovingPanels(World world, IRenderResources resources, bool emissive)
    {
        var scene = new SceneInstance();
        FrameHandle stage = world.CreateRootFrame(new(FrameBounds.Sphere(100)));
        scene.Add("stage", stage.Entity);
        var spin = new FrameSpin(Tilt: DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2), Rate: Math.Tau / 6);
        var pivot = new Double3(0, 0.35, 0.25);
        FrameHandle wheel = world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = stage,
                    Pose = new(pivot, spin.RotationAt(world.Time)),
                    FirstMove = new(spin.Sample(pivot, world.TimeAt(world.TickNumber + 1))),
                    AsFrame = new(FrameBounds.Sphere(4)),
                    Components = [new SpinMotion { Spin = spin }],
                }
            )
        );
        var red = new Vector3(1, 0.008f, 0.003f);
        Panel("red-panel", -1.55, red);
        if (emissive)
            Panel("blue-panel", 1.55, new(0.005f, 0.28f, 1));
        else
            Star(world: world, scene: scene, frame: stage, center: Double3.Zero, direction: new(0.7, 0.8, -1));

        float fill = emissive ? 0.2f : 8;
        world.Ambient = new()
        {
            SkyColor = new(fill),
            GroundColor = new(fill * 0.35f),
            Intensity = 1,
        };
        var neutral = new Vector4(new Vector3(emissive ? 0.15f : 0.78f), 1);
        BounceSlab(
            world: world,
            resources: resources,
            scene: scene,
            frame: stage,
            name: "backdrop",
            center: new(0, 0, -2.9),
            size: new(6.8f, 3.2f, 0.12f),
            color: neutral
        );
        BounceSlab(
            world: world,
            resources: resources,
            scene: scene,
            frame: stage,
            name: "shelf",
            center: new(0, -0.88, -2.25),
            size: new(6.8f, 0.16f, 1.5f),
            color: neutral
        );
        const float radius = 0.48f;
        MeshId sphere = resources.CreateMesh(Primitives.Sphere(radius, 48, 32));
        for (int i = 0; i < 3; i++)
        {
            MaterialDesc material = emissive
                ? new(BaseColor: Vector4.One, Roughness: 0.55f + i * 0.18f, Model: new StandardShading(Metallic: 1))
                : MaterialDesc.Color(Vector4.One, 1);
            scene.Add(
                $"ball-{i}",
                world.Spawn(
                    DrawnAt(
                        stage,
                        new((i - 1) * 1.18, -0.8 + radius, -2.05),
                        Shaded(sphere, resources.CreateMaterial(material), radius)
                    )
                )
            );
        }
        AddCamera(world: world, scene: scene, frame: stage, pose: LookingAt(new(0, 0.04, 0), new(0, -0.2, -2.6)));
        scene.Add("source-camera", world.Spawn(ViewIn(stage, LookingAt(new(3.7, 3.1, -2.3), new(0, 0, -1.1)))));
        scene.AddView("source-camera");
        return scene;

        void Panel(string name, double x, Vector3 color)
        {
            var half = new Vector3(0.86f, 0.05f, 1.08f);
            var material = new MaterialDesc(
                BaseColor: new(color, 1),
                Roughness: 1,
                Emissive: emissive ? 26 * color : Vector3.Zero
            );
            scene.Add(
                name,
                world.Spawn(
                    DrawnAt(
                        wheel,
                        new(x, 0, 0),
                        Shaded(
                            resources.CreateMesh(Primitives.Box(half)),
                            resources.CreateMaterial(material),
                            half.Length()
                        )
                    )
                )
            );
        }
    }

    /// <summary>
    /// The entity <paramref name="name"/>: a slab in <paramref name="frame"/> with no body, of <paramref name="size"/> m
    /// around <paramref name="center"/>, of <paramref name="color"/> at <see cref="BounceSlabRoughness"/>, that casts shadows.
    /// </summary>
    private static void BounceSlab(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle frame,
        string name,
        Double3 center,
        Vector3 size,
        Vector4 color
    ) =>
        scene.Add(
            name,
            world.Spawn(
                new()
                {
                    Frame = frame,
                    Pose = new(center, DQuaternion.Identity),
                    Render = new RenderData
                    {
                        Mesh = resources.CreateMesh(Primitives.Box(size / 2)),
                        Material = resources.CreateMaterial(MaterialDesc.Color(color, BounceSlabRoughness)),
                        BoundingRadius = size.Length() / 2,
                        Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
                    },
                }
            )
        );
}
