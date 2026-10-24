using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>Where the parked shuttle of demo-shuttle lies along X, in meters: beside the pad, with its bubble clear of the standing shuttle's.</summary>
    private const double LyingShuttleX = 20;

    /// <summary>The half extents of the landing pad of demo-shuttle, in meters: 16 by 16 m and 1.2 m tall, as the live world's pad.</summary>
    private static readonly Vector3 ShuttlePadHalf = new(8, 0.6f, 8);

    /// <summary>
    /// The scene demo-shuttle shows the shuttle's hull of <see cref="Shuttle.Loft"/>, in a root frame <c>ground</c> with 1 g
    /// along -Y (docs/plans/blue-danube-demo.md, "The shuttle's tram shape and glass canopy").
    /// <list type="bullet">
    /// <item>A <c>floor</c> of 100 m by 100 m, whose top is at y = 0, and on it a static <c>pad</c>, as the live world's
    /// landing pad (docs/plans/live-world.md, part 3).</item>
    /// <item><c>standing</c>, a dynamic shuttle, stands on its tail on the pad, 1 cm above it at the start. It is turned a
    /// quarter turn about +X, so its nose points up and its roof faces +Z.</item>
    /// <item><c>lying</c>, a parked shuttle, rests on its belly on the floor beside the pad, unturned.</item>
    /// <item>Each carries its nozzle and its 40 panes of glass in its frame, with its id as a prefix, such as
    /// <c>standing-nozzle</c> and <c>lying-pane-roof-4</c> (see <see cref="Shuttle.SpawnNozzle"/> and
    /// <see cref="Shuttle.Panes"/>).</item>
    /// <item>The views: <c>side-camera</c>, the camera at the start, sees both from the side, from the port and aft quarter.
    /// <c>tail-camera</c> looks at the tail face of <c>lying</c> from behind and a little below its middle, and
    /// <c>nose-camera</c> at its front from ahead and above.</item>
    /// </list>
    /// </summary>
    private static SceneInstance DemoShuttle(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle ground =
            world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), GravityField.Uniform(new(0, -9.81f, 0))));
        scene.Add("ground", ground);
        scene.Add("floor", world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(0, -0.5, 0), DQuaternion.Identity),
            Body = new(Shape: new BoxShapeDesc(new(50, 0.5f, 50)), Motion: BodyMotion.Static),
            Render = Drawn(resources: resources, mesh: Primitives.Box(new(50, 0.5f, 50)),
                color: new(x: 0.35f, y: 0.35f, z: 0.35f, w: 1),
                boundingRadius: 71)
        }));
        scene.Add("pad", world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(0, ShuttlePadHalf.Y, 0), DQuaternion.Identity),
            Body = new(Shape: new BoxShapeDesc(ShuttlePadHalf), Motion: BodyMotion.Static),
            Render = Drawn(resources: resources, mesh: Primitives.Box(ShuttlePadHalf),
                color: new(x: 0.55f, y: 0.55f, z: 0.52f, w: 1),
                boundingRadius: ShuttlePadHalf.Length())
        }));

        Entity standing = world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(0, 2 * ShuttlePadHalf.Y + Shuttle.Hull.Z + 0.01, 0),
                DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2)),
            Body = Shuttle.Body(BodyMotion.Dynamic),
            AsFrame = new(new(Shuttle.Bubble)),
            Render = Shuttle.DrawnHull(resources)
        });
        scene.Add("standing", standing);
        Shuttle.SpawnNozzle(world: world, cabin: world.GetOwnedFrame(standing), scene: scene, resources: resources,
            prefix: "standing");
        Shuttle.Panes(world: world, cabin: world.GetOwnedFrame(standing), scene: scene, resources: resources,
            prefix: "standing");
        Entity lying = Shuttle.Park(world: world, frame: ground,
            pose: new(new(LyingShuttleX, Shuttle.Hull.Y, 0), DQuaternion.Identity),
            render: Shuttle.DrawnHull(resources));
        scene.Add("lying", lying);
        Shuttle.SpawnNozzle(world: world, cabin: world.GetOwnedFrame(lying), scene: scene, resources: resources,
            prefix: "lying");
        Shuttle.Panes(world: world, cabin: world.GetOwnedFrame(lying), scene: scene, resources: resources,
            prefix: "lying");

        Entity camera = world.Spawn(new()
            { Frame = ground, Pose = LookingAt(new(-28, 7, 32), new(7, 5, 0)), Render = new RenderData() });
        scene.Add("side-camera", camera);
        scene.Add("tail-camera", world.Spawn(new()
        {
            Frame = ground,
            Pose = LookingAt(new(LyingShuttleX + 2, 1, Shuttle.Hull.Z + 9), new(LyingShuttleX, 1.9, Shuttle.Hull.Z)),
            Render = new RenderData()
        }));
        scene.Add("nose-camera", world.Spawn(new()
        {
            Frame = ground,
            Pose = LookingAt(new(LyingShuttleX + 3, 6, -Shuttle.Hull.Z - 9), new(LyingShuttleX, 1.5, -Shuttle.Hull.Z)),
            Render = new RenderData()
        }));
        world.Camera = camera;

        // The sun frame's origin is where the light comes from: up, to the port side and aft of the shuttles.
        Entity sun = world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(-30, 50, 40), DQuaternion.Identity),
            AsFrame = new(FrameBounds.Sphere(1))
        });
        scene.Add("sun", sun);
        world.SunFrame = world.GetOwnedFrame(sun);

        scene.AddView("side-camera");
        scene.AddView("tail-camera");
        scene.AddView("nose-camera");
        return scene;
    }
}
