using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The half size of each crate of the yard's stack, in meters: the crates are cubes 0.5 m on a side.</summary>
    public const float YardCrateHalfSize = 0.25f;

    /// <summary>
    /// How far the yard's top crate sticks out over the side of the stack toward the person, along +Z, in meters. Its
    /// center stays 0.05 m inside the edge of the crate below it, so it rests there.
    /// </summary>
    public const double TopCrateOverhang = 0.2;

    /// <summary>Where the person in the yard starts, in meters along +Z from the stack's axis.</summary>
    public const double YardPersonStartZ = 12.25;

    /// <summary>The mass of each of the two lower crates of the yard's stack, in kilograms.</summary>
    public const float HeavyCrateMass = 100;

    /// <summary>The mass of the yard's top crate, in kilograms.</summary>
    public const float LightCrateMass = 5;

    /// <summary>The mass of the person in the yard, in kilograms.</summary>
    public const float YardPersonMass = 70;

    /// <summary>The friction of the yard's top crate. Jolt combines two frictions by their geometric mean.</summary>
    public const float TopCrateFriction = 0.1f;

    // The half extents of the floor slab: 50 by 50 m, and 1 m thick.
    private static readonly Vector3 YardFloorHalfExtents = new(25, 0.5f, 25);

    /// <summary>
    /// A yard on the ground, 6,371 km from the origin of its frame. That is nearer than the 8,389 km from which a slow group
    /// gets a resting numeric frame, so the Jolt system of the frame itself holds the bodies, in double precision.
    /// <list type="bullet">
    /// <item>The root frame <c>earth-surface</c> has its origin at Earth's center and Earth's point gravity, μ =
    /// <see cref="EarthMu"/>. It does not turn. The scene leaves out the sun, Earth's orbit round it and Earth's spin,
    /// because none of them changes what happens on the ground here: Earth falls freely round the sun, and the yard
    /// lies at the pole, where Earth's spin of 7.3e-5 rad/s would give no centrifugal term and only a Coriolis term of
    /// 1.5e-4 m/s² per m/s, which the scene does not need. The yard lies at the north pole, so +Y is up, and the surface
    /// gravity there is μ / R² ≈ 9.820 m/s², with R = <see cref="EarthRadius"/>.</item>
    /// <item>The planet has no ground (ticket ter-7u6t), so a static gray slab, the <c>floor</c>, 50 by 50 m and 1 m
    /// thick, has its top at y = R.</item>
    /// <item>A stack of three crates, cubes 0.5 m on a side, stands on the floor on the Y axis. The <c>bottom-crate</c>
    /// and the <c>middle-crate</c> are brown and heavy, 100 kg each, so the person cannot shove them. The
    /// <c>top-crate</c> is orange, 5 kg, with a friction of 0.1, and it sticks out 0.2 m toward +Z, the person's
    /// side. Its bottom is 1 m above the floor.</item>
    /// <item>The <c>person</c>, the capsule character of the orbit scene, 1.8 m tall, stands on the floor 12.25 m from
    /// the stack along +Z and faces it, along -Z. With W it walks at 3 m/s toward the stack. It reaches the top crate's
    /// face first, pushes that crate off the far side of the stack, and stops against the heavy crates.</item>
    /// <item>The view <c>yard-camera</c>, the camera at the start, stands 10 m from the stack along +X, 3 m up, and
    /// looks along -X at the person's way. The view <c>stack-camera</c> stands 4 m from the stack along +X, at the
    /// height of the stack's top, and looks along -X at the stack and the floor behind it, where the top crate lands.</item>
    /// <item>The frame <c>sun</c>, 50 m up and to the side, is where the light comes from.</item>
    /// </list>
    /// </summary>
    private static SceneInstance SurfaceYard(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle surface = EarthSurface(
            world: world,
            resources: resources,
            scene: scene,
            floorHalfExtents: YardFloorHalfExtents
        );

        MeshId crateMesh = resources.CreateMesh(Primitives.Box(new(YardCrateHalfSize)));
        var heavy = new CrateSpec(
            HalfExtents: new(YardCrateHalfSize),
            Mass: HeavyCrateMass,
            Mesh: crateMesh,
            Material: resources.CreateMaterial(
                MaterialDesc.Color(new(x: 0.45f, y: 0.3f, z: 0.15f, w: 1), UsualRoughness)
            )
        );
        var light = new CrateSpec(
            HalfExtents: new(YardCrateHalfSize),
            Mass: LightCrateMass,
            Mesh: crateMesh,
            Material: resources.CreateMaterial(
                MaterialDesc.Color(new(x: 0.95f, y: 0.5f, z: 0.1f, w: 1), UsualRoughness)
            )
        );

        // The crates start just touching: the bottom of each on the top of the one below.
        static DPose Stacked(int level, double z) =>
            new(new(0, EarthRadius + YardCrateHalfSize * (2 * level + 1), z), DQuaternion.Identity);

        scene.Add("bottom-crate", world.Spawn(heavy.Spawn(surface, Stacked(0, 0), Double3.Zero)));
        scene.Add("middle-crate", world.Spawn(heavy.Spawn(surface, Stacked(1, 0), Double3.Zero)));
        // A crate as CrateSpec.Spawn makes it, with less friction, so that it slides off the stack quickly once pushed.
        scene.Add(
            "top-crate",
            world.Spawn(
                new()
                {
                    Frame = surface,
                    Pose = Stacked(2, TopCrateOverhang),
                    Body = new(
                        Shape: new BoxShapeDesc(light.HalfExtents),
                        Mass: light.Mass,
                        Friction: TopCrateFriction,
                        LinearDamping: 0,
                        AngularDamping: 0
                    ),
                    Render = light.Drawn(),
                    Components = [new Crate(light)],
                }
            )
        );

        // The capsule is 1.8 m tall, and its center rests 0.9 m above the floor. The eye is 1.65 m above the floor.
        var capsule = new CapsuleShapeDesc(0.6f, 0.3f);
        scene.Add(
            "person",
            world.Spawn(
                new()
                {
                    Frame = surface,
                    Pose = new(new(0, EarthRadius + 0.95, YardPersonStartZ), DQuaternion.Identity),
                    Body = new(Shape: capsule, Motion: BodyMotion.Character, Mass: YardPersonMass),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(new(0.3f, 0.9f, 0.3f)),
                        color: new(x: 0.2f, y: 0.45f, z: 0.8f, w: 1),
                        boundingRadius: 1
                    ),
                    Components = [new PersonControl(new(0, 0.75f, 0)), new Carry(capsule)],
                    Subscriptions = [Topics.Input],
                }
            )
        );

        Entity yardCamera = world.Spawn(
            new()
            {
                Frame = surface,
                Pose = LookingAt(new(10, EarthRadius + 3, 4), new(0, EarthRadius + 0.8, 4)),
                Render = new RenderData(),
            }
        );
        scene.Add("yard-camera", yardCamera);
        scene.Add(
            "stack-camera",
            world.Spawn(
                new()
                {
                    Frame = surface,
                    Pose = LookingAt(new(4, EarthRadius + 1.5, -0.5), new(0, EarthRadius + 0.7, -0.5)),
                    Render = new RenderData(),
                }
            )
        );

        // The sun frame's origin is where the light comes from: up, and to the cameras' side of the yard.
        SunAt(world: world, scene: scene, frame: surface, position: new(30, EarthRadius + 50, 20));

        scene.AddView("yard-camera");
        scene.AddView("stack-camera");
        scene.AddView("person");
        world.Camera = yardCamera;
        return scene;
    }

    /// <summary>
    /// The ground at Earth's north pole of the surface yard: the root frame <c>earth-surface</c>, which has its origin at
    /// Earth's center and Earth's point gravity, μ = <see cref="EarthMu"/>, and does not turn, so +Y is up at the pole, with
    /// the slab of <see cref="Floor"/> on it.
    /// </summary>
    private static FrameHandle EarthSurface(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        Vector3 floorHalfExtents
    )
    {
        // The frame's bounds do not matter for a root frame, which nothing leaves.
        FrameHandle surface = world.CreateRootFrame(new(FrameBounds.Sphere(1e8f), GravityField.Point(EarthMu)));
        scene.Add("earth-surface", surface.Entity);
        Floor(world: world, resources: resources, scene: scene, surface: surface, floorHalfExtents: floorHalfExtents);
        return surface;
    }

    /// <summary>
    /// The ground at Earth's north pole, which the surface scenes share: in <paramref name="surface"/>, a frame with its
    /// origin at Earth's center and its +Y at the north pole, a static gray slab, the <c>floor</c>, with the half extents
    /// <paramref name="floorHalfExtents"/>, centered on the Y axis with its top at y = <see cref="EarthRadius"/>. The
    /// planet has no ground of its own (ticket ter-7u6t).
    /// </summary>
    private static void Floor(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle surface,
        Vector3 floorHalfExtents
    ) =>
        scene.Add(
            "floor",
            world.Spawn(
                new()
                {
                    Frame = surface,
                    Pose = new(new(0, EarthRadius - floorHalfExtents.Y, 0), DQuaternion.Identity),
                    Body = new(Shape: new BoxShapeDesc(floorHalfExtents), Motion: BodyMotion.Static),
                    Render = Drawn(
                        resources: resources,
                        mesh: Primitives.Box(floorHalfExtents),
                        color: new(x: 0.4f, y: 0.4f, z: 0.38f, w: 1),
                        boundingRadius: MathF.Ceiling(floorHalfExtents.Length())
                    ),
                }
            )
        );

    /// <summary>The frame <c>sun</c> in <paramref name="frame"/>, whose origin at <paramref name="position"/> is where the world's light comes from.</summary>
    private static void SunAt(World world, SceneInstance scene, FrameHandle frame, Double3 position)
    {
        Entity sun = world.Spawn(
            new()
            {
                Frame = frame,
                Pose = new(position, DQuaternion.Identity),
                AsFrame = new(FrameBounds.Sphere(1)),
            }
        );
        scene.Add("sun", sun);
        world.SunFrame = world.GetOwnedFrame(sun);
    }

    /// <summary>
    /// A view at <paramref name="eye"/> that looks at <paramref name="target"/>, with no roll: it turns about +Y, then
    /// tilts about its own +X. A view with no rotation looks along -Z.
    /// </summary>
    private static DPose LookingAt(Double3 eye, Double3 target)
    {
        Double3 look = target - eye;
        double yaw = Math.Atan2(-look.X, -look.Z);
        double pitch = Math.Atan2(look.Y, Math.Sqrt(look.X * look.X + look.Z * look.Z));
        return new(
            eye,
            DQuaternion.FromAxisAngle(Double3.UnitY, yaw) * DQuaternion.FromAxisAngle(Double3.UnitX, pitch)
        );
    }
}
