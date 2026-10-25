using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The thickness of the strips, in meters. They lie on the gray slab, whose top is at <see cref="EarthRadius"/>, so
    /// the reflecting floor, their tops, is this high above it.
    /// </summary>
    private const float StripThickness = 0.05f;

    /// <summary>The height of the reflecting floor of the reflections scene, the strips' tops, in meters.</summary>
    private const double ReflectionsFloor = EarthRadius + StripThickness;

    // The half extents of one strip: 2 m wide, 12 m long along Z, and StripThickness thick.
    private static readonly Vector3 StripHalfExtents = new(1, StripThickness / 2, 6);

    // The half extents of the gray slab under the strips: 24 by 24 m, so that it shows around them, and 1 m thick.
    private static readonly Vector3 ReflectionsSlabHalfExtents = new(12, 0.5f, 12);

    // The base color of the dielectric strips: a dark gray, so that their reflections stand out against them.
    private static readonly Vector4 StripGray = new(x: 0.06f, y: 0.06f, z: 0.06f, w: 1);

    /// <summary>The strips' materials from -X to +X: dielectrics from a mirror to roughness 0.45, then gold.</summary>
    private static readonly MaterialDesc[] StripMaterials =
    [
        // Roughness 0 gives the smoothest lobe that the renderer draws, which clamps it to GgxLobe.MinRoughness.
        new(BaseColor: StripGray, Roughness: 0),
        new(BaseColor: StripGray, Roughness: 0.1f),
        new(BaseColor: StripGray, Roughness: 0.2f),
        new(BaseColor: StripGray, Roughness: 0.3f),
        new(BaseColor: StripGray, Roughness: 0.45f),
        new(BaseColor: new(x: 1, y: 0.71f, z: 0.29f, w: 1), Roughness: 0.1f, Metallic: 1),
    ];

    /// <summary>
    /// The review scene of the screen-space reflections (docs/plans/screen-space-reflections.md, "Part 3"): six strips
    /// on the gray slab of <see cref="EarthSurface"/>, objects on them, a gray ambient light and the sun.
    /// <list type="bullet">
    /// <item>The strips <c>strip-0</c> to <c>strip-5</c>, 2 m wide and 12 m long along Z, lie side by side from -X to
    /// +X with the materials of <see cref="StripMaterials"/>.</item>
    /// <item>The <c>red-box</c> spans the mirror and the strip of roughness 0.1. The <c>bar</c>, white, glowing and 2 m
    /// tall, stands on the strip of 0.2. The <c>lamp</c> floats 1 m over the strip of 0.3. The <c>blue-sphere</c> rests
    /// on the gold.</item>
    /// <item>The view <c>low-camera</c> stands 0.3 m above the strips at their near end and looks along them. The view
    /// <c>front-camera</c> stands 3 m above them there and looks down at 30 degrees. The view <c>edge-camera</c> looks
    /// level at the bar from 3.5 m, with the bar's foot near the bottom of the screen, so the bar's reflection runs off
    /// it.</item>
    /// </list>
    /// </summary>
    private static SceneInstance Reflections(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle surface = EarthSurface(
            world: world,
            resources: resources,
            scene: scene,
            floorHalfExtents: ReflectionsSlabHalfExtents
        );
        world.Ambient = new()
        {
            SkyColor = new(0.12f),
            GroundColor = new(0.12f),
            Intensity = 1,
        };

        Strips(world: world, resources: resources, scene: scene, surface: surface);
        ReflectedObjects(world: world, resources: resources, scene: scene, surface: surface);

        // The target lies on the floor 3 √3 m ahead of the eye, which stands 3 m above it: the view looks down at 30°.
        Entity frontCamera = world.Spawn(
            ViewIn(surface, LookingAt(new(0, ReflectionsFloor + 3, 6), new(0, ReflectionsFloor, 6 - 3 * Math.Sqrt(3))))
        );
        scene.Add("front-camera", frontCamera);
        scene.Add(
            "low-camera",
            world.Spawn(ViewIn(surface, LookingAt(new(0, ReflectionsFloor + 0.3, 6), new(0, ReflectionsFloor, -6))))
        );
        scene.Add(
            "edge-camera",
            world.Spawn(
                ViewIn(surface, LookingAt(new(-1, ReflectionsFloor + 1.5, 1.5), new(-1, ReflectionsFloor + 1.5, -2)))
            )
        );

        // The sun stands up and behind the views, so no view sees its glint and the shadows fall away from them.
        SunAt(world: world, scene: scene, frame: surface, position: new(20, EarthRadius + 30, 40));

        scene.AddView("front-camera");
        scene.AddView("low-camera");
        scene.AddView("edge-camera");
        world.Camera = frontCamera;
        return scene;
    }

    /// <summary>
    /// The strips <c>strip-0</c> to <c>strip-5</c> in <paramref name="surface"/>, centered on X and Z, from -X to +X,
    /// each with the material of <see cref="StripMaterials"/> at its index, with their tops at
    /// <see cref="ReflectionsFloor"/>.
    /// </summary>
    private static void Strips(World world, IRenderResources resources, SceneInstance scene, FrameHandle surface)
    {
        MeshId mesh = resources.CreateMesh(Primitives.Box(StripHalfExtents));
        for (int i = 0; i < StripMaterials.Length; i++)
        {
            double x = (i - (StripMaterials.Length - 1) / 2.0) * 2 * StripHalfExtents.X;
            scene.Add(
                $"strip-{i}",
                world.Spawn(
                    new()
                    {
                        Frame = surface,
                        Pose = new(new(x, EarthRadius + StripHalfExtents.Y, 0), DQuaternion.Identity),
                        Render = Shaded(
                            mesh: mesh,
                            material: resources.CreateMaterial(StripMaterials[i]),
                            boundingRadius: StripHalfExtents.Length()
                        ),
                    }
                )
            );
        }
    }

    /// <summary>
    /// The objects that the strips of the reflections scene reflect, in <paramref name="surface"/>: the
    /// <c>red-box</c>, the <c>bar</c>, the <c>lamp</c> and the <c>blue-sphere</c>, 6 to 8 m ahead of the views at the
    /// strips' near end, so that the widest view holds them all.
    /// </summary>
    private static void ReflectedObjects(
        World world,
        IRenderResources resources,
        SceneInstance scene,
        FrameHandle surface
    )
    {
        var boxHalfExtents = new Vector3(1.5f, 0.5f, 0.5f);
        scene.Add(
            "red-box",
            world.Spawn(
                new()
                {
                    Frame = surface,
                    Pose = new(new(-4, ReflectionsFloor + boxHalfExtents.Y, -1), DQuaternion.Identity),
                    Render = Shaded(
                        mesh: resources.CreateMesh(Primitives.Box(boxHalfExtents)),
                        material: resources.CreateMaterial(
                            MaterialDesc.Color(new(x: 0.8f, y: 0.05f, z: 0.05f, w: 1), UsualRoughness)
                        ),
                        boundingRadius: boxHalfExtents.Length()
                    ),
                }
            )
        );

        var barHalfExtents = new Vector3(0.1f, 1, 0.1f);
        scene.Add(
            "bar",
            world.Spawn(
                new()
                {
                    Frame = surface,
                    Pose = new(new(-1, ReflectionsFloor + barHalfExtents.Y, -2), DQuaternion.Identity),
                    Render = Shaded(
                        mesh: resources.CreateMesh(Primitives.Box(barHalfExtents)),
                        material: resources.CreateMaterial(
                            new(
                                BaseColor: new(x: 0, y: 0, z: 0, w: 1),
                                Roughness: UsualRoughness,
                                Reflectance: 0,
                                Emissive: new(5)
                            )
                        ),
                        boundingRadius: barHalfExtents.Length()
                    ),
                }
            )
        );

        scene.Add(
            "lamp",
            world.Spawn(GlowLamp(surface: surface, resources: resources, position: new(1, ReflectionsFloor + 1, 0)))
        );

        const float sphereRadius = 0.5f;
        scene.Add(
            "blue-sphere",
            world.Spawn(
                new()
                {
                    Frame = surface,
                    Pose = new(new(5, ReflectionsFloor + sphereRadius, 0), DQuaternion.Identity),
                    Render = Shaded(
                        mesh: resources.CreateMesh(Primitives.Sphere(sphereRadius, segments: 96, rings: 48)),
                        material: resources.CreateMaterial(
                            MaterialDesc.Color(new(x: 0.05f, y: 0.12f, z: 0.8f, w: 1), UsualRoughness)
                        ),
                        boundingRadius: sphereRadius
                    ),
                }
            )
        );
    }

    /// <summary>
    /// A drawn object of the reflections scene that casts shadows and is never an impostor, so it receives the
    /// shadow-map shadows of the others.
    /// </summary>
    private static RenderData Shaded(MeshId mesh, MaterialId material, float boundingRadius) =>
        new()
        {
            Mesh = mesh,
            Material = material,
            BoundingRadius = boundingRadius,
            Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
        };
}
