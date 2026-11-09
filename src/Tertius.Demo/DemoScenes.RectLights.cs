using System.Numerics;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>The half extents of a standing panel of the rectangle lights' scene: 1.6 m wide, 0.8 m tall, 0.1 m thick.</summary>
    private static readonly Vector3 StandingPanelHalfExtents = new(0.8f, 0.4f, 0.05f);

    /// <summary>The light of a standing panel: its front face, which shines toward +Z with a cabin lamp's power.</summary>
    private static RectLightData StandingPanelLight =>
        PanelLight(
            offset: new(0, 0, StandingPanelHalfExtents.Z),
            front: Vector3.UnitZ,
            across: Vector3.UnitX,
            width: 2 * StandingPanelHalfExtents.X,
            height: 2 * StandingPanelHalfExtents.Y,
            power: CabinLampPower
        );

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
        Vector3 panelHalfExtents = StandingPanelHalfExtents;
        RectLightData light = StandingPanelLight;
        var panel = new RenderData
        {
            Mesh = resources.CreateMesh(Primitives.Box(panelHalfExtents)),
            Material = LampPanelMaterial(resources: resources, light: light),
            BoundingRadius = panelHalfExtents.Length(),
            Flags = RenderFlags.Visible | RenderFlags.NoImpostor,
            Light = light,
        };
        foreach ((string name, float x, float roughness) in new[] { ("mirror", -1.5f, 0f), ("rough", 1.5f, 0.45f) })
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
            // The panel's lower edge stands 0.4 m above the floor, over the strip's far half.
            scene.Add(
                $"{name}-panel",
                world.Spawn(
                    DrawnAt(
                        frame: surface,
                        position: new(x, ReflectionsFloor + 0.4 + panelHalfExtents.Y, -3),
                        render: panel
                    )
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
