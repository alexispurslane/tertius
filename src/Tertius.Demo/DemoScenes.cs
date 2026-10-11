using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The scenes that scripts can name. The vertical slice adds its scenes here, and the content packages work later moves
/// them into the first content package.
/// </summary>
public static class DemoScenes
{
    public static IReadOnlyList<Scene> All { get; } = [new Scene("drop-box", DropBox)];

    /// <summary>
    /// A 1 m orange <c>box</c> that falls 2.5 m onto a gray static <c>floor</c>, in the root frame <c>ground</c> with
    /// 9.81 m/s² of gravity along -Y. The top of the floor is at y = 0, and the box starts with its center at y = 3. The
    /// entity <c>camera</c> stands at (0, 2.5, 9) and looks along -Z, tilted down by 6.3 degrees toward (0, 1.5, 0). The
    /// frame <c>sun</c> at (30, 50, 40) is where the light comes from. The box and the floor cast shadows.
    /// </summary>
    private static SceneInstance DropBox(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        scene.Add("ground", ground);
        scene.Add("floor", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, -0.5, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
            Render = Drawn(resources, Primitives.Box(new Vector3(50, 0.5f, 50)), new Vector4(0.35f, 0.35f, 0.35f, 1), boundingRadius: 71),
        }));
        scene.Add("box", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 3, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10),
            Render = Drawn(resources, Primitives.Box(new Vector3(0.5f)), new Vector4(1, 0.45f, 0.1f, 1), boundingRadius: 0.87f),
        }));
        Entity camera = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 2.5, 9), DQuaternion.FromAxisAngle(new Double3(1, 0, 0), -Math.Atan(1.0 / 9))),
            Render = new RenderData(),
        });
        scene.Add("camera", camera);
        world.Camera = camera;
        // The sun frame's origin is where the light comes from: up, to the right and in front of the box.
        Entity sun = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(30, 50, 40), DQuaternion.Identity),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1), MotionDriver.Static),
        });
        scene.Add("sun", sun);
        world.SunFrame = world.GetOwnedFrame(sun);
        return scene;
    }

    private static RenderData Drawn(IRenderResources resources, MeshData mesh, Vector4 color, float boundingRadius) => new()
    {
        Mesh = resources.CreateMesh(mesh),
        Material = resources.CreateMaterial(MaterialDesc.Color(color)),
        BoundingRadius = boundingRadius,
        Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
    };
}
