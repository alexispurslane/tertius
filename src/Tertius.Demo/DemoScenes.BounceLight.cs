using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The perceptual roughness of the slabs of the bounce light's scenes, as in the GPU tests.</summary>
    private const float BounceSlabRoughness = 0.8f;

    /// <summary>The scenes of the GPU tests of the bounce light (docs/plans/screen-space-illumination.md, "Part 7").</summary>
    private static IEnumerable<Scene> BounceLightScenes =>
        [new("bounce-red-wall", BounceRedWall), new("bounce-raised-box", BounceRaisedBox)];

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
