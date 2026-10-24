using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The radius of each ball of the scene demo-meshes, in meters.</summary>
    private const float MeshesBallRadius = 0.25f;

    /// <summary>The cylinder of demo-meshes: 1 m in radius and 1.5 m tall, in 48 segments.</summary>
    private const float MeshesCylinderRadius = 1, MeshesCylinderHalfHeight = 0.75f;

    private const int MeshesCylinderSegments = 48;

    /// <summary>The torus of demo-meshes: a tube 0.5 m in radius round a circle 1.6 m in radius, in 64 segments of 24 sides.</summary>
    private const float MeshesTorusMajor = 1.6f, MeshesTorusMinor = 0.5f;

    private const int MeshesTorusSegments = 64, MeshesTorusSides = 24;

    /// <summary>
    /// The ring of demo-meshes, a station's floor in small: 3 m to 3.25 m from its axis and 1.5 m wide along it, in 256
    /// segments.
    /// </summary>
    private const float MeshesRingInner = 3, MeshesRingOuter = 3.25f, MeshesRingHalfWidth = 0.75f;

    private const int MeshesRingSegments = 256;

    /// <summary>Where the centers of the cylinder, the torus and the ring of demo-meshes stand along X, in meters.</summary>
    private const double MeshesCylinderX = -5, MeshesTorusX = 0, MeshesRingX = 6;

    /// <summary>
    /// The scene demo-meshes shows the solids of <see cref="RevolvedSolid"/>, each with a ball that rests on it, in a root
    /// frame <c>ground</c> with 1 g along -Y. The solids and their balls are static and dynamic bodies, each solid with the
    /// mesh and the collision shape of one <see cref="RevolvedSolid"/>.
    /// <list type="bullet">
    /// <item>A <c>floor</c> of 100 m by 100 m, whose top is at y = 0.</item>
    /// <item>The <c>cylinder</c>, blue, stands on the floor at x = <see cref="MeshesCylinderX"/>. Its ball,
    /// <c>cylinder-ball</c>, drops 0.5 m onto its top cap, 0.3 m off its center.</item>
    /// <item>The <c>torus</c>, orange, lies on the floor at x = <see cref="MeshesTorusX"/>, on the flat bottom of its tube.
    /// Its ball, <c>torus-ball</c>, drops 0.5 m onto the flat top of its tube, in the middle of a segment.</item>
    /// <item>The <c>ring</c>, light grey, stands on edge on the floor at x = <see cref="MeshesRingX"/>, its axis along Z, on
    /// the middle of one of its outer facets. Its ball, <c>ring-ball</c>, drops 0.5 m inside it onto its floor, the middle
    /// of its lowest inner facet.</item>
    /// <item>The views: <c>camera</c>, the camera at the start, sees all three from the front and above. <c>cylinder-view</c>,
    /// <c>torus-view</c> and <c>ring-view</c> look at each solid closer. The ring's view looks into it along its axis.</item>
    /// </list>
    /// </summary>
    private static SceneInstance DemoMeshes(World world, IRenderResources resources)
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
            Render = Drawn(resources: resources, mesh: Primitives.Box(new(50, 0.5f, 50)), color: new(x: 0.35f, y: 0.35f, z: 0.35f, w: 1),
                boundingRadius: 71)
        }));
        var ball = new SphereShapeDesc(MeshesBallRadius);
        MeshData ballMesh = Primitives.Sphere(MeshesBallRadius);

        void Solid(string id, RevolvedSolid solid, DPose pose, Vector4 color)
        {
            ShapeDesc shape = solid.Shape();
            scene.Add(id, world.Spawn(new()
            {
                Frame = ground,
                Pose = pose,
                Body = new(Shape: shape, Motion: BodyMotion.Static),
                Render = Drawn(resources: resources, mesh: solid.Mesh(), color: color, boundingRadius: shape.BoundingRadius())
            }));
        }

        void Ball(string id, Double3 position) => scene.Add(id, world.Spawn(new()
        {
            Frame = ground,
            Pose = new(position, DQuaternion.Identity),
            Body = new(Shape: ball, Mass: 1),
            Render = Drawn(resources: resources, mesh: ballMesh, color: new(x: 0.85f, y: 0.15f, z: 0.15f, w: 1), boundingRadius: MeshesBallRadius)
        }));

        Solid(id: "cylinder",
            solid: RevolvedSolid.Cylinder(MeshesCylinderRadius, MeshesCylinderHalfHeight, MeshesCylinderSegments),
            pose: new(new(MeshesCylinderX, MeshesCylinderHalfHeight, 0), DQuaternion.Identity), color: new(x: 0.25f, y: 0.5f, z: 0.85f, w: 1));
        Ball("cylinder-ball", new(MeshesCylinderX + 0.3, 2 * MeshesCylinderHalfHeight + MeshesBallRadius + 0.5, 0));

        // The tube's flat bottom and top are r cos(π / sides) below and above its center circle.
        double tubeFlat = MeshesTorusMinor * Math.Cos(Math.PI / MeshesTorusSides);
        Solid(id: "torus", solid: RevolvedSolid.Torus(majorRadius: MeshesTorusMajor, minorRadius: MeshesTorusMinor, segments: MeshesTorusSegments, sides: MeshesTorusSides),
            pose: new(new(MeshesTorusX, tubeFlat, 0), DQuaternion.Identity), color: new(x: 0.9f, y: 0.55f, z: 0.15f, w: 1));
        // The middle of segment 0's facet, half a segment from +X toward -Z.
        double half = Math.PI / MeshesTorusSegments, middle = MeshesTorusMajor * Math.Cos(half);
        Ball("torus-ball",
            new(MeshesTorusX + middle * Math.Cos(half), 2 * tubeFlat + MeshesBallRadius + 0.5,
                -middle * Math.Sin(half)));

        // On edge, the ring's angle θ is at (ρ cos θ, ρ sin θ) across its axis, and half a segment's turn about its axis puts
        // the middle of a facet at its lowest point, θ = 3π / 2, inside and out.
        double facet = Math.Cos(Math.PI / MeshesRingSegments);
        double ringY = MeshesRingOuter * facet;
        DQuaternion onEdge = DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2) *
                             DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / MeshesRingSegments);
        Solid(id: "ring", solid: RevolvedSolid.Ring(innerRadius: MeshesRingInner, outerRadius: MeshesRingOuter, halfWidth: MeshesRingHalfWidth, segments: MeshesRingSegments),
            pose: new(new(MeshesRingX, ringY, 0), onEdge), color: new(x: 0.75f, y: 0.75f, z: 0.8f, w: 1));
        Ball("ring-ball", new(MeshesRingX, ringY - MeshesRingInner * facet + MeshesBallRadius + 0.5, 0));

        Entity camera = world.Spawn(new()
            { Frame = ground, Pose = LookingAt(new(0.5, 6, 15), new(0.5, 1.2, 0)), Render = new RenderData() });
        scene.Add("camera", camera);
        scene.Add("cylinder-view", world.Spawn(new()
        {
            Frame = ground,
            Pose = LookingAt(new(MeshesCylinderX + 1.5, 3.2, 3.5), new(MeshesCylinderX, 1.1, 0)),
            Render = new RenderData()
        }));
        scene.Add("torus-view", world.Spawn(new()
        {
            Frame = ground,
            Pose = LookingAt(new(MeshesTorusX + 1, 3.2, 4.5), new(MeshesTorusX, 0.4, 0)),
            Render = new RenderData()
        }));
        scene.Add("ring-view", world.Spawn(new()
        {
            Frame = ground,
            Pose = LookingAt(new(MeshesRingX + 0.8, 2.2, 6.5), new(MeshesRingX, 0.6, 0)),
            Render = new RenderData()
        }));
        world.Camera = camera;

        // The sun frame's origin is where the light comes from: up, to the right and in front of the solids.
        Entity sun = world.Spawn(new()
        {
            Frame = ground,
            Pose = new(new(30, 50, 40), DQuaternion.Identity),
            AsFrame = new(FrameBounds.Sphere(1))
        });
        scene.Add("sun", sun);
        world.SunFrame = world.GetOwnedFrame(sun);

        scene.AddView("camera");
        scene.AddView("cylinder-view");
        scene.AddView("torus-view");
        scene.AddView("ring-view");
        return scene;
    }
}
