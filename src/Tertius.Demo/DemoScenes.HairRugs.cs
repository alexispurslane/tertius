using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>Half the side of each hair rug, in meters.</summary>
    private const float HairRugHalfSize = 1;

    /// <summary>How far the normal map of the tilted rug leans its strands up off the rug, in degrees.</summary>
    private const double HairRugTilt = 10;

    /// <summary>
    /// The review scene of the hair reflections (docs/plans/skin-and-hair.md, decision 35): two square hair cards of
    /// 2 m that lie on the gray floor of <see cref="EarthSurface"/>, under the sun and a glowing bar behind them.
    /// <list type="bullet">
    /// <item>Both rugs are brown hair of roughness 0.15, whose strands run along the floor toward +Z. The rug
    /// <c>flat-rug</c>, on the -X side, has no normal map. The rug <c>tilted-rug</c>, on the +X side, has a normal map
    /// that leans its strands <see cref="HairRugTilt"/> up off the rug toward +Z, as a strand of a real hair card can
    /// lean. The far part of that rug then reflects below itself, and keeps the environment map's reflection.</item>
    /// <item>The <c>bar</c>, 4 m wide and 1.5 m tall, glows white 1.5 m behind the rugs, so that a rug's rays meet it on
    /// the screen.</item>
    /// <item>The view <c>rug-camera</c> looks at the rugs from 3.5 m along +Z and 1.2 m up, and <c>low-camera</c> from
    /// 3 m along +Z and 0.5 m up, where more of the tilted rug reflects below itself.</item>
    /// </list>
    /// </summary>
    private static SceneInstance HairRugs(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle surface = EarthSurface(
            world: world,
            resources: resources,
            scene: scene,
            floorHalfExtents: new(10, 0.5f, 10)
        );
        ReviewSky(world);
        world.VerticalFov = 40 * MathF.PI / 180;

        MeshId rug = resources.CreateMesh(Primitives.Plane(HairRugHalfSize));
        var hair = new MaterialDesc(
            BaseColor: new(x: 0.45f, y: 0.25f, z: 0.12f, w: 1),
            Roughness: 0.15f,
            Reflectance: 0.5f,
            Model: new HairShading(StrandDirection.PlusV)
        );
        // The plane's bitangent is −Z, so a normal that leans toward it turns the strand, +v, up off the plane.
        float tilt = (float)(HairRugTilt * Math.PI / 180);
        static byte Code(float value) => (byte)MathF.Round((value + 1) / 2 * 255);
        TextureId leaning = resources.CreateTexture(
            new(Width: 1, Height: 1, Rgba: [128, Code(MathF.Sin(tilt)), Code(MathF.Cos(tilt)), 255], Srgb: false)
        );
        float rugRadius = HairRugHalfSize * MathF.Sqrt(2);
        scene.Add(
            "flat-rug",
            world.Spawn(
                DrawnAt(
                    frame: surface,
                    position: new(-1.1, EarthRadius + 0.01, 0),
                    render: Shaded(mesh: rug, material: resources.CreateMaterial(hair), boundingRadius: rugRadius)
                )
            )
        );
        scene.Add(
            "tilted-rug",
            world.Spawn(
                DrawnAt(
                    frame: surface,
                    position: new(1.1, EarthRadius + 0.01, 0),
                    render: Shaded(
                        mesh: rug,
                        material: resources.CreateMaterial(hair with { NormalMap = leaning }),
                        boundingRadius: rugRadius
                    )
                )
            )
        );
        var barHalfExtents = new Vector3(2, 0.75f, 0.1f);
        scene.Add(
            "bar",
            world.Spawn(
                DrawnAt(
                    frame: surface,
                    position: new(0, EarthRadius + barHalfExtents.Y, -2.5),
                    render: Shaded(
                        mesh: resources.CreateMesh(Primitives.Box(barHalfExtents)),
                        material: resources.CreateMaterial(
                            new(
                                BaseColor: new(x: 0, y: 0, z: 0, w: 1),
                                Roughness: UsualRoughness,
                                Reflectance: 0,
                                Emissive: new(6800)
                            )
                        ),
                        boundingRadius: barHalfExtents.Length()
                    )
                )
            )
        );

        var target = new Double3(0, EarthRadius, -0.5);
        Entity rugCamera = world.Spawn(ViewIn(surface, LookingAt(new(0, EarthRadius + 1.2, 3.5), target)));
        scene.Add("rug-camera", rugCamera);
        scene.Add("low-camera", world.Spawn(ViewIn(surface, LookingAt(new(0, EarthRadius + 0.5, 3), target))));
        Star(world: world, scene: scene, frame: surface, center: new(0, EarthRadius, 0), direction: new(0.3, 0.6, 1));

        scene.AddView("rug-camera");
        scene.AddView("low-camera");
        world.Camera = rugCamera;
        return scene;
    }
}
