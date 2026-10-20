using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The half extents of the landing pad of demo-shuttle, in meters: 16 by 16 m and 1.2 m tall, as the live world's pad.</summary>
    private static readonly Vector3 ShuttlePadHalf = new(8, 0.6f, 8);

    /// <summary>Where the parked shuttle of demo-shuttle lies along X, in meters: beside the pad, with its bubble clear of the standing shuttle's.</summary>
    private const double LyingShuttleX = 20;

    /// <summary>
    /// The scene demo-shuttle shows the shuttle's hull of <see cref="Shuttle.Loft"/>, in a root frame <c>ground</c> with 1 g
    /// along -Y (docs/plans/blue-danube-demo.md, "The shuttle's tram shape and glass canopy").
    /// <list type="bullet">
    /// <item>A <c>floor</c> of 100 m by 100 m, whose top is at y = 0, and on it a static <c>pad</c>, as the live world's
    /// landing pad (docs/plans/live-world.md, part 3).</item>
    /// <item><c>standing</c>, a dynamic shuttle, stands on its tail on the pad, 1 cm above it at the start. It is turned a
    /// quarter turn about +X, so its nose points up and its roof faces +Z.</item>
    /// <item><c>lying</c>, a parked shuttle, rests on its belly on the floor beside the pad, unturned.</item>
    /// <item>Each carries its nozzle in its frame, <c>standing-nozzle</c> and <c>lying-nozzle</c>
    /// (see <see cref="Shuttle.SpawnNozzle"/>).</item>
    /// <item>The views: <c>side-camera</c>, the camera at the start, sees both from the side, from the port and aft quarter.
    /// <c>tail-camera</c> looks at the tail face of <c>lying</c> from behind and a little below its middle, and
    /// <c>nose-camera</c> at its front from ahead and above.</item>
    /// </list>
    /// </summary>
    private static SceneInstance DemoShuttle(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        scene.Add("ground", ground);
        scene.Add("floor", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, -0.5, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
            Render = Drawn(resources, Primitives.Box(new Vector3(50, 0.5f, 50)), new Vector4(0.35f, 0.35f, 0.35f, 1), boundingRadius: 71),
        }));
        scene.Add("pad", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, ShuttlePadHalf.Y, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(ShuttlePadHalf), BodyMotion.Static),
            Render = Drawn(resources, Primitives.Box(ShuttlePadHalf), new Vector4(0.55f, 0.55f, 0.52f, 1), boundingRadius: ShuttlePadHalf.Length()),
        }));

        MeshData hull = Shuttle.Loft.Mesh();
        RenderData Hull() => Drawn(resources, hull, new Vector4(0.85f, 0.85f, 0.88f, 1), boundingRadius: MathF.Ceiling(Shuttle.Hull.Length()));
        Entity standing = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(0, 2 * ShuttlePadHalf.Y + Shuttle.Hull.Z + 0.01, 0), DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2)),
            Body = Shuttle.Body(BodyMotion.Dynamic),
            AsFrame = new FrameSpec(new FrameBounds(Shuttle.Bubble)),
            Render = Hull(),
        });
        scene.Add("standing", standing);
        Shuttle.SpawnNozzle(world, world.GetOwnedFrame(standing), scene, resources, "standing");
        Entity lying = Shuttle.Park(world, ground, new DPose(new Double3(LyingShuttleX, Shuttle.Hull.Y, 0), DQuaternion.Identity), Hull());
        scene.Add("lying", lying);
        Shuttle.SpawnNozzle(world, world.GetOwnedFrame(lying), scene, resources, "lying");

        Entity camera = world.Spawn(new SpawnDesc { Frame = ground, Pose = LookingAt(new Double3(-28, 7, 32), new Double3(7, 5, 0)), Render = new RenderData() });
        scene.Add("side-camera", camera);
        scene.Add("tail-camera", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = LookingAt(new Double3(LyingShuttleX + 2, 1, Shuttle.Hull.Z + 9), new Double3(LyingShuttleX, 1.9, Shuttle.Hull.Z)),
            Render = new RenderData(),
        }));
        scene.Add("nose-camera", world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = LookingAt(new Double3(LyingShuttleX + 3, 6, -Shuttle.Hull.Z - 9), new Double3(LyingShuttleX, 1.5, -Shuttle.Hull.Z)),
            Render = new RenderData(),
        }));
        world.Camera = camera;

        // The sun frame's origin is where the light comes from: up, to the port side and aft of the shuttles.
        Entity sun = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = new DPose(new Double3(-30, 50, 40), DQuaternion.Identity),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1)),
        });
        scene.Add("sun", sun);
        world.SunFrame = world.GetOwnedFrame(sun);

        scene.AddView("side-camera");
        scene.AddView("tail-camera");
        scene.AddView("nose-camera");
        return scene;
    }
}
