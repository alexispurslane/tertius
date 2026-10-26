using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The radius of each sphere of the PBR scene, in meters.</summary>
    private const float PbrSphereRadius = 0.4f;

    /// <summary>The distance between the centers of two neighbor spheres of one row of the PBR scene, in meters.</summary>
    private const double PbrSphereSpacing = 1;

    /// <summary>How far the centers of the gold row stand above those of the red row, in meters.</summary>
    private const double PbrRowGap = 1;

    // The half extents of the floor slab: 20 by 20 m, and 1 m thick.
    private static readonly Vector3 PbrFloorHalfExtents = new(10, 0.5f, 10);

    /// <summary>The perceptual roughness of the spheres of each row, from -X to +X.</summary>
    private static readonly float[] PbrRoughnesses = [0.05f, 0.2f, 0.4f, 0.6f, 0.8f, 1];

    /// <summary>The two rows of the PBR scene: red non-metals on the floor, and gold metals above them.</summary>
    private static readonly SphereRowSpec[] PbrRows =
    [
        new(Name: "red", Height: PbrSphereRadius, BaseColor: new(x: 0.8f, y: 0.05f, z: 0.05f, w: 1), Metallic: 0),
        new(
            Name: "gold",
            Height: PbrSphereRadius + PbrRowGap,
            BaseColor: new(x: 1, y: 0.71f, z: 0.29f, w: 1),
            Metallic: 1
        ),
    ];

    /// <summary>
    /// The review scene of the PBR BRDF (docs/plans/pbr-brdf.md, "Part 4"): two rows of six spheres on the gray floor of
    /// <see cref="EarthSurface"/>, in sunlight, under a blue sky color and a brown ground color, with one lamp.
    /// <list type="bullet">
    /// <item>The spheres, 0.4 m in radius and 1 m apart along X, take the roughness of <see cref="PbrRoughnesses"/>
    /// from -X to +X. They have no bodies, and they are never impostors, so each shades in full.</item>
    /// <item>The red non-metals, <c>red-0</c> to <c>red-5</c>, rest on the floor at z = 0. The gold metals,
    /// <c>gold-0</c> to <c>gold-5</c>, float 1 m above them.</item>
    /// <item>The <c>lamp</c>, a small glowing sphere with a point light, stands 1 m in front of the rows, between
    /// them in height, on the -X side.</item>
    /// <item>The view <c>front-camera</c> looks at both rows from 7.5 m in front, along -Z. The view <c>low-camera</c>
    /// stands 0.15 m above the floor, 8 m in front, and looks along it at the rows, so it sees the floor at a grazing
    /// angle. The vertical field of view is 35 degrees.</item>
    /// </list>
    /// </summary>
    private static SceneInstance PbrSpheres(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle surface = EarthSurface(
            world: world,
            resources: resources,
            scene: scene,
            floorHalfExtents: PbrFloorHalfExtents
        );
        // A sky color and a ground color that differ, so that each sphere shows which way its hemisphere light is up.
        world.Ambient = new()
        {
            SkyColor = new(x: 0.12f, y: 0.16f, z: 0.24f),
            GroundColor = new(x: 0.06f, y: 0.05f, z: 0.04f),
            Intensity = 1,
        };

        // A narrow view keeps the spheres at the sides of the frame round.
        world.VerticalFov = 35 * MathF.PI / 180;

        MeshId sphereMesh = resources.CreateMesh(Primitives.Sphere(PbrSphereRadius, segments: 96, rings: 48));
        foreach (SphereRowSpec row in PbrRows)
            SphereRow(world: world, resources: resources, scene: scene, surface: surface, row: row, mesh: sphereMesh);

        scene.Add(
            "lamp",
            world.Spawn(GlowLamp(surface: surface, resources: resources, position: new(-1.5, EarthRadius + 0.9, 1)))
        );
        Entity frontCamera = world.Spawn(
            ViewIn(surface, LookingAt(new(0, EarthRadius + 1, 7.5), new(0, EarthRadius + 0.9, 0)))
        );
        scene.Add("front-camera", frontCamera);
        scene.Add(
            "low-camera",
            world.Spawn(ViewIn(surface, LookingAt(new(0, EarthRadius + 0.15, 8), new(0, EarthRadius + 0.75, 0))))
        );

        // The sun stands up, in front of the rows and to their +X side, so the front views see each sphere's highlight.
        Star(
            world: world,
            scene: scene,
            frame: surface,
            center: new(0, EarthRadius, 0),
            direction: new(20, 30, 40),
            irradiance: 1
        );

        scene.AddView("front-camera");
        scene.AddView("low-camera");
        world.Camera = frontCamera;
        return scene;
    }

    /// <summary>
    /// A lamp of the review scenes in <paramref name="surface"/> at <paramref name="position"/>: a glowing sphere 5 cm in
    /// radius with a warm point light of 2 W/sr.
    /// </summary>
    private static SpawnDesc GlowLamp(FrameHandle surface, IRenderResources resources, Double3 position) =>
        new()
        {
            Frame = surface,
            Pose = new(position, DQuaternion.Identity),
            Render = new RenderData
            {
                Mesh = resources.CreateMesh(Primitives.Sphere(0.05f)),
                Material = resources.CreateMaterial(
                    new(
                        BaseColor: new(x: 0, y: 0, z: 0, w: 1),
                        Roughness: UsualRoughness,
                        Reflectance: 0,
                        Emissive: new(6, 5.4f, 4.5f)
                    )
                ),
                BoundingRadius = 0.05f,
                Flags = RenderFlags.Visible | RenderFlags.NoImpostor,
                Light = new PointLightData(
                    Offset: new(Vector3.Zero, Quaternion.Identity),
                    Color: new(1, 0.9f, 0.75f),
                    Power: 4 * MathF.PI * 2,
                    Radius: LampRadius,
                    CastsShadow: false
                ),
            },
        };

    /// <summary>A view in <paramref name="frame"/> at <paramref name="pose"/>: an entity with no body that draws nothing.</summary>
    private static SpawnDesc ViewIn(FrameHandle frame, DPose pose) =>
        new()
        {
            Frame = frame,
            Pose = pose,
            Render = new RenderData(),
        };

    /// <summary>
    /// The six spheres <c>{row.Name}-0</c> to <c>{row.Name}-5</c> in <paramref name="surface"/>, centered on X at z = 0,
    /// each with <paramref name="row"/>'s base color and metallic, a reflectance of 0.5, and the roughness of
    /// <see cref="PbrRoughnesses"/> at its index.
    /// </summary>
    private static void SphereRow(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle surface,
        SphereRowSpec row,
        MeshId mesh
    )
    {
        for (int i = 0; i < PbrRoughnesses.Length; i++)
        {
            double x = (i - (PbrRoughnesses.Length - 1) / 2.0) * PbrSphereSpacing;
            scene.Add(
                $"{row.Name}-{i}",
                world.Spawn(
                    new()
                    {
                        Frame = surface,
                        Pose = new(new(x, EarthRadius + row.Height, 0), DQuaternion.Identity),
                        Render = new RenderData
                        {
                            Mesh = mesh,
                            Material = resources.CreateMaterial(
                                new(
                                    BaseColor: row.BaseColor,
                                    Roughness: PbrRoughnesses[i],
                                    Metallic: row.Metallic,
                                    Reflectance: 0.5f
                                )
                            ),
                            BoundingRadius = PbrSphereRadius,
                            Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                        },
                    }
                )
            );
        }
    }

    /// <summary>One row of spheres of the PBR scene.</summary>
    /// <param name="Name">The prefix of the names of the row's spheres.</param>
    /// <param name="Height">The height of the row's centers above the floor, in meters.</param>
    /// <param name="BaseColor">The base color of the row's material, linear RGB and opacity.</param>
    /// <param name="Metallic">The metallic of the row's material: 0 for a non-metal, 1 for a metal.</param>
    private sealed record SphereRowSpec(string Name, double Height, Vector4 BaseColor, float Metallic);
}
