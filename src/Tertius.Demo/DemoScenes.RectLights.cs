using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>
    /// The review scene of the rectangle lights (ARCHITECTURE.md, "Rectangle lights"): a mirror strip and a rough strip
    /// on the gray slab of <see cref="EarthSurface"/>, each under a standing lamp panel whose glowing face is a
    /// rectangle light. A dim gray ambient light and no sun, so the panels light the floors.
    /// <list type="bullet">
    /// <item>The strips <c>mirror-floor</c> (roughness 0) at -X and <c>rough-floor</c> (roughness 0.45) at +X are the
    /// reflections scene's strips: 2 m wide, 12 m long along Z, dark gray.</item>
    /// <item>The panels <c>mirror-panel</c> and <c>rough-panel</c> stand over the far half of their strips, 1.6 m wide
    /// and 0.8 m tall, with their lower edges 0.4 m above the floor. Their faces shine toward +Z, the views.</item>
    /// <item>The view <c>floor-camera</c> stands 1.5 m above the strips' near end and looks at the floor below the
    /// panels.</item>
    /// </list>
    /// </summary>
    private static SceneInstance RectLights(World world, IRenderResources resources)
    {
        var scene = new SceneInstance();
        FrameHandle surface = EarthSurface(
            world: world,
            resources: resources,
            scene: scene,
            floorHalfExtents: ReflectionsSlabHalfExtents
        );
        world.Ambient = SpaceAmbient;

        MeshId strip = resources.CreateMesh(Primitives.Box(StripHalfExtents));
        (string Name, float X, float Roughness)[] floors = [("mirror", -1.5f, 0), ("rough", 1.5f, 0.45f)];
        var panelHalfExtents = new Vector3(0.8f, 0.4f, 0.05f);
        RectLightData light = PanelLight(
            offset: new(0, 0, panelHalfExtents.Z),
            front: Vector3.UnitZ,
            across: Vector3.UnitX,
            width: 2 * panelHalfExtents.X,
            height: 2 * panelHalfExtents.Y,
            power: CabinLampPower
        );
        MeshId panelMesh = resources.CreateMesh(Primitives.Box(panelHalfExtents));
        MaterialId panelMaterial = LampPanelMaterial(resources: resources, light: light);
        foreach ((string name, float x, float roughness) in floors)
        {
            scene.Add(
                $"{name}-floor",
                world.Spawn(
                    DrawnAt(
                        frame: surface,
                        position: new(x, EarthRadius + StripHalfExtents.Y, 0),
                        render: Shaded(
                            mesh: strip,
                            material: resources.CreateMaterial(new(BaseColor: StripGray, Roughness: roughness)),
                            boundingRadius: StripHalfExtents.Length()
                        )
                    )
                )
            );
            scene.Add(
                $"{name}-panel",
                world.Spawn(
                    new()
                    {
                        Frame = surface,
                        Pose = new(new(x, ReflectionsFloor + 0.4 + panelHalfExtents.Y, -3), DQuaternion.Identity),
                        Render = new RenderData
                        {
                            Mesh = panelMesh,
                            Material = panelMaterial,
                            BoundingRadius = panelHalfExtents.Length(),
                            Flags = RenderFlags.Visible | RenderFlags.NoImpostor,
                            Light = light,
                        },
                    }
                )
            );
        }

        Entity camera = world.Spawn(
            ViewIn(surface, LookingAt(new(0, ReflectionsFloor + 1.5, 5), new(0, ReflectionsFloor, -1.5)))
        );
        scene.Add("floor-camera", camera);
        scene.AddView("floor-camera");
        world.Camera = camera;
        return scene;
    }
}
