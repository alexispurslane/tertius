using Tertius.Assets;
using Tertius.Host;
using Tertius.Simulation;

namespace Tertius.Demo;

public static partial class DemoScenes
{
    /// <summary>What a script's "scene" and <c>--scene</c> name to run <see cref="ArtPreview"/>, and what the art check looks for.</summary>
    public const string ArtPreviewSceneName = "art-preview";

    /// <summary>Where the station's origin stands in the ground frame: 2.5 km up and 10 km away along -Z, in meters.</summary>
    private static readonly Double3 PreviewStation = new(0, 2500, -10000);

    /// <summary>The origins of the two shuttles, whose lowest point, the gear, rests on the floor, in meters.</summary>
    private static readonly Double3 PreviewShuttleA = new(-18, 3.07, 0),
        PreviewShuttleB = new(-6, 3.07, 0);

    /// <summary>The passenger's feet, in meters. The props stand in a row beside the passenger, along +X.</summary>
    private static readonly Double3 PreviewPassenger = new(12, 0, -2);

    /// <summary>
    /// The scene <c>art-preview</c>: every Blue Danube model at its true size under plain light, with fixed views, so
    /// that a change to the converter shows on screen (tools/README.md, "The Blue Danube art"). One
    /// <see cref="ModelStore"/> on <paramref name="content"/> loads the models, so each repeated placement shares one
    /// renderer model. A model that the store cannot load is not placed, and the views stay.
    /// <list type="bullet">
    /// <item>The ground frame of <see cref="GroundWithFloor"/>, with the Sun up, to the left of the views and in front
    /// of the models, and the ambient light of <see cref="Daylight"/>.</item>
    /// <item>The <c>station</c> 10 km away along -Z and 2.5 km up, its ring's axis along +Y.</item>
    /// <item>Two shuttles, <c>shuttle-a</c> and <c>shuttle-b</c>, on their gear, their noses toward -Z. Three cargo pods,
    /// <c>pod-0</c> to <c>pod-2</c>, two on the floor and one on top.</item>
    /// <item>The <c>passenger</c>, 1.8 m tall, then the <c>cart</c>, the <c>toolbox</c>, the <c>thermos</c>,
    /// <c>stool-0</c> to <c>stool-3</c> and the <c>seating</c> kit in a row, and the <c>sconces</c> kit on a gray
    /// <c>wall</c>. Every model faces -Z.</item>
    /// <item>The views: <c>overview</c>, <c>station-view</c>, <c>hub-below</c> and <c>hub-above</c> (the hub's two
    /// axial faces), <c>shuttle-front</c> and <c>shuttle-rear</c> (three-quarter views of shuttle-a), <c>pod-view</c>,
    /// <c>props-left</c>, <c>props-right</c>, <c>passenger-front</c>, <c>passenger-three-quarter</c> and <c>relief</c>,
    /// a close view of the passenger's chest, whose normal maps the low Sun brings out.</item>
    /// </list>
    /// </summary>
    public static Scene ArtPreview(ContentFolders? content) =>
        new(
            ArtPreviewSceneName,
            (world, resources) => BuildArtPreview(world: world, resources: resources, content: content)
        );

    private static SceneInstance BuildArtPreview(World world, IRenderResources resources, ContentFolders? content)
    {
        var scene = new SceneInstance();
        FrameHandle ground = GroundWithFloor(world: world, resources: resources, scene: scene);
        Star(world: world, scene: scene, frame: ground, center: Double3.Zero, direction: new(-30, 45, -40));
        world.Ambient = Daylight;
        if (content is not null)
            PlaceArt(new PreviewPlacer(world: world, scene: scene, ground: ground, store: new ModelStore(resources, content)), resources);

        AddPreviewViews(world, scene, ground);
        return scene;
    }

    private static void PlaceArt(PreviewPlacer place, IRenderResources resources)
    {
        place.Model("station", BlueDanubeArt.Station, PreviewStation);
        place.Model("shuttle-a", BlueDanubeArt.Shuttle, PreviewShuttleA);
        place.Model("shuttle-b", BlueDanubeArt.Shuttle, PreviewShuttleB);
        place.Model("pod-0", BlueDanubeArt.CargoPod, new(5, 1.25, 0));
        place.Model("pod-1", BlueDanubeArt.CargoPod, new(5, 3.75, 0));
        place.Model("pod-2", BlueDanubeArt.CargoPod, new(7.6, 1.25, 0));

        Double3 feet = PreviewPassenger;
        place.Model("passenger", BlueDanubeArt.Passenger, feet);
        place.Model("cart", BlueDanubeArt.StorageCart, feet + new Double3(2.2, 0, 0));
        place.Model("toolbox", BlueDanubeArt.Toolbox, feet + new Double3(3.6, 0, 0));
        place.Model("thermos", BlueDanubeArt.Thermos, feet + new Double3(4.2, 0, 0));
        for (int i = 0; i < 4; i++)
            place.Model($"stool-{i}", BlueDanubeArt.Stool, feet + new Double3(5 + 0.6 * i, 0, 0));
        place.Model("seating", BlueDanubeArt.StreetSeating, feet + new Double3(9, 0, 0));

        // The sconces mount on a wall with their backs at z = 0 of their model, so the wall's front face stands there.
        Double3 sconces = feet + new Double3(14, 1.5, 0);
        place.Box(id: "wall", resources: resources, center: sconces + new Double3(0, 0, 0.05), halfExtents: new(1, 1.2f, 0.05f));
        place.Model("sconces", BlueDanubeArt.CagedSconce, sconces);
    }

    private static void AddPreviewViews(World world, SceneInstance scene, FrameHandle ground)
    {
        Double3 hub = PreviewStation;
        Double3 shuttle = PreviewShuttleA;
        Double3 feet = PreviewPassenger;
        (string Id, Double3 Eye, Double3 Target)[] views =
        [
            ("overview", new(2, 14, -38), new(2, 1, 0)),
            ("station-view", hub + new Double3(2600, 1800, -4200), hub),
            ("hub-below", hub + new Double3(260, -560, -300), hub + new Double3(0, -150, 0)),
            ("hub-above", hub + new Double3(260, 520, -300), hub + new Double3(0, 120, 0)),
            ("shuttle-front", shuttle + new Double3(-14, 4, -22), shuttle),
            ("shuttle-rear", shuttle + new Double3(-14, 4, 21), shuttle),
            ("pod-view", new(1.5, 3.5, -7.5), new(6.3, 2, 0)),
            ("props-left", feet + new Double3(4, 1.5, -4.5), feet + new Double3(4, 0.5, 0)),
            ("props-right", feet + new Double3(11.5, 1.8, -5.5), feet + new Double3(11.5, 0.8, 0)),
            ("passenger-front", feet + new Double3(0, 1.3, -3.2), feet + new Double3(0, 0.95, 0)),
            ("passenger-three-quarter", feet + new Double3(-1.8, 1.5, -2.4), feet + new Double3(0, 1, 0)),
            ("relief", feet + new Double3(-0.25, 1.45, -0.75), feet + new Double3(0, 1.3, 0)),
        ];
        foreach ((string id, Double3 eye, Double3 target) in views)
        {
            scene.Add(id, world.Spawn(ViewIn(ground, LookingAt(eye, target))));
            scene.AddView(id);
        }

        world.Camera = scene["overview"];
    }

    /// <summary>Places the art-preview's models in its ground frame through one store.</summary>
    private sealed class PreviewPlacer(World world, SceneInstance scene, FrameHandle ground, ModelStore store)
    {
        /// <summary>Places the model of <paramref name="reference"/> at <paramref name="position"/> as <paramref name="id"/>, unrotated, if it loads.</summary>
        public void Model(string id, AssetReference reference, Double3 position)
        {
            if (store.Load(reference).Ok is not { } model)
                return;
            Spawn(
                id,
                position,
                new RenderData
                {
                    Model = model.Id,
                    BoundingRadius = model.BoundingRadius,
                    Flags = RenderFlags.Visible | RenderFlags.CastsShadow,
                }
            );
        }

        /// <summary>A gray box of <paramref name="halfExtents"/> at <paramref name="center"/>, as <paramref name="id"/>.</summary>
        public void Box(string id, IRenderResources resources, Double3 center, System.Numerics.Vector3 halfExtents) =>
            Spawn(
                id,
                center,
                Drawn(
                    resources: resources,
                    mesh: Primitives.Box(halfExtents),
                    color: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1),
                    boundingRadius: halfExtents.Length()
                )
            );

        private void Spawn(string id, Double3 position, RenderData render) =>
            scene.Add(
                id,
                world.Spawn(
                    new()
                    {
                        Frame = ground,
                        Pose = new(position, DQuaternion.Identity),
                        Render = render,
                    }
                )
            );
    }
}
