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
    /// A 1 m <c>box</c> that falls 2.5 m onto a static <c>floor</c>, in the root frame <c>ground</c> with 9.81 m/s² of
    /// gravity along -Y. The top of the floor is at y = 0, and the box starts with its center at y = 3.
    /// </summary>
    private static SceneInstance DropBox(World world)
    {
        var scene = new SceneInstance();
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        scene.Add("ground", ground);
        scene.Add("floor", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, -0.5, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
        }));
        scene.Add("box", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 3, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10),
        }));
        return scene;
    }
}
