using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>Half the side of each card of the hair card scenes, in meters.</summary>
    private const float HairCardHalfSize = 0.3f;

    /// <summary>The distance between the centers of two neighbor cards of one fan, in meters.</summary>
    private const double HairCardSpacing = 0.7;

    /// <summary>The turn of each card of a fan about its vertical strand, from -X to +X, in degrees.</summary>
    private static readonly double[] HairCardTurns = [-60, -30, 0, 30, 60];

    /// <summary>The review scenes of hair shading (docs/plans/skin-and-hair.md): the fans lit from the front and from behind.</summary>
    private static IEnumerable<Scene> HairCardScenes =>
        [
            new("hair-cards", (world, resources) => HairCards(world, resources, toSun: new(0.3, 0.6, 1))),
            new("hair-cards-backlit", (world, resources) => HairCards(world, resources, toSun: new(0.25, 0.45, -1))),
        ];

    /// <summary>
    /// Two fans of upright square cards on the gray floor of <see cref="EarthSurface"/>, under the sun along
    /// <paramref name="toSun"/> and a blue sky color and a brown ground color.
    /// <list type="bullet">
    /// <item>Each fan holds five brown cards, 0.6 m square, 0.7 m apart along X, each turned about its vertical strand by
    /// the angle of <see cref="HairCardTurns"/>. The fan <c>hair-0</c> to <c>hair-4</c> on the -X side is hair, whose
    /// strands run down from the top edge. The fan <c>standard-0</c> to <c>standard-4</c> on the +X side is a standard
    /// non-metal of the same color.</item>
    /// <item>The view <c>front-camera</c> looks at the fans' fronts from 6 m along +Z and 1.6 m up, so the sunlit floor lies
    /// behind them. The view <c>back-camera</c> looks at their backs from 6 m along -Z.</item>
    /// </list>
    /// </summary>
    private static SceneInstance HairCards(World world, IRenderResources resources, Double3 toSun)
    {
        var scene = new SceneInstance();
        FrameHandle surface = EarthSurface(
            world: world,
            resources: resources,
            scene: scene,
            floorHalfExtents: new(10, 0.5f, 10)
        );
        // The pbr-spheres scene's sky and ground, so that no side of a card is black.
        world.Ambient = new()
        {
            SkyColor = new(x: 160, y: 220, z: 330),
            GroundColor = new(x: 80, y: 70, z: 55),
            Intensity = 1,
        };
        world.VerticalFov = 40 * MathF.PI / 180;

        MeshId card = resources.CreateMesh(Primitives.Plane(HairCardHalfSize));
        var brown = new Vector4(x: 0.45f, y: 0.25f, z: 0.12f, w: 1);
        MaterialId hair = resources.CreateMaterial(
            new(BaseColor: brown, Roughness: 0.35f, Reflectance: 0.5f, Model: new HairShading(StrandDirection.PlusV))
        );
        MaterialId standard = resources.CreateMaterial(
            new(BaseColor: brown, Roughness: 0.35f, Reflectance: 0.5f, Model: new StandardShading(0))
        );
        HairCardFan(
            world: world,
            scene: scene,
            surface: surface,
            name: "hair",
            mesh: card,
            material: hair,
            centerX: -2
        );
        HairCardFan(
            world: world,
            scene: scene,
            surface: surface,
            name: "standard",
            mesh: card,
            material: standard,
            centerX: 2
        );

        var target = new Double3(0, EarthRadius + 0.5, 0);
        Entity frontCamera = world.Spawn(ViewIn(surface, LookingAt(new(0, EarthRadius + 1.6, 6), target)));
        scene.Add("front-camera", frontCamera);
        scene.Add("back-camera", world.Spawn(ViewIn(surface, LookingAt(new(0, EarthRadius + 1.6, -6), target))));
        Star(world: world, scene: scene, frame: surface, center: new(0, EarthRadius, 0), direction: toSun);

        scene.AddView("front-camera");
        scene.AddView("back-camera");
        world.Camera = frontCamera;
        return scene;
    }

    /// <summary>
    /// The cards <c>{name}-0</c> to <c>{name}-4</c> of one fan in <paramref name="surface"/>, centered on
    /// <paramref name="centerX"/>, 0.3 m above the floor: the plane mesh stood up with its front along +Z and its v down,
    /// then turned about +Y by its angle of <see cref="HairCardTurns"/>.
    /// </summary>
    private static void HairCardFan(
        World world,
        SceneInstance scene,
        FrameHandle surface,
        string name,
        MeshId mesh,
        MaterialId material,
        double centerX
    )
    {
        // The plane mesh faces +Y and its v grows along +Z: a quarter turn about +X faces it along +Z with v down.
        var upright = DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2);
        for (int i = 0; i < HairCardTurns.Length; i++)
        {
            var turn = DQuaternion.FromAxisAngle(Double3.UnitY, HairCardTurns[i] * Math.PI / 180);
            double x = centerX + (i - (HairCardTurns.Length - 1) / 2.0) * HairCardSpacing;
            scene.Add(
                $"{name}-{i}",
                world.Spawn(
                    new()
                    {
                        Frame = surface,
                        Pose = new(new(x, EarthRadius + 0.3 + HairCardHalfSize, 0), turn * upright),
                        Render = new RenderData
                        {
                            Mesh = mesh,
                            Material = material,
                            BoundingRadius = HairCardHalfSize * MathF.Sqrt(2),
                            Flags = RenderFlags.Visible | RenderFlags.CastsShadow | RenderFlags.NoImpostor,
                        },
                    }
                )
            );
        }
    }
}
