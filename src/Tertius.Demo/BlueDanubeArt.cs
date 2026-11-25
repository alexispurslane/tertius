using Tertius.Assets;

namespace Tertius.Demo;

/// <summary>
/// The converted models of the Blue Danube demo: Pioneer's station, lunar shuttle and cargo pod, Poly Haven props, and
/// Unvanquished's passenger.
/// Their sidecars in content/tertius.demo/assets/blue-danube pin the sources, and <see cref="SetupCommand"/> converts
/// them into the package's git-ignored bin/assets (tools/README.md, "The Blue Danube art").
/// </summary>
public static class BlueDanubeArt
{
    /// <summary>The command that a fresh checkout runs once, from the repository's root, to fetch and convert the art.</summary>
    public const string SetupCommand = "bash tools/blue-danube-assets/setup.sh";

    private const string Package = "tertius.demo";

    public static AssetReference Station { get; } = Art("orbital-station/orbital_station_2-2k.model");

    public static AssetReference Shuttle { get; } = Art("lunar-shuttle/lunarshuttle.model");

    public static AssetReference CargoPod { get; } = Art("cargo-pod/cargo.model");

    public static AssetReference StorageCart { get; } = Art("props/industrial_storage_cart.gltf");

    public static AssetReference CagedSconce { get; } = Art("props/industrial_caged_sconce.gltf");

    public static AssetReference Toolbox { get; } = Art("props/metal_toolbox.gltf");

    public static AssetReference Thermos { get; } = Art("props/modified_thermos.gltf");

    public static AssetReference Stool { get; } = Art("props/metal_stool_01.gltf");

    public static AssetReference StreetSeating { get; } = Art("props/modular_street_seating.gltf");

    /// <summary>The passenger: Unvanquished's human male in light armour, posed with the arms at the sides, 1.8 m tall.</summary>
    public static AssetReference Passenger { get; } = Art("crew/human_male.blend");

    /// <summary>Every model that the setup command converts.</summary>
    public static IReadOnlyList<AssetReference> All { get; } =
    [Station, Shuttle, CargoPod, StorageCart, CagedSconce, Toolbox, Thermos, Stool, StreetSeating, Passenger];

    /// <summary>True for the scenes that draw the art: the Blue Danube scenes and <see cref="DemoScenes.ArtPreview"/>.</summary>
    public static bool NeededBy(string scene) =>
        BlueDanube.Scenes.Contains(scene) || scene == DemoScenes.ArtPreviewSceneName;

    /// <summary>
    /// The problem of each model of <see cref="All"/> whose cooked file under <paramref name="content"/> does not load,
    /// read by the same reader that the scenes' model store uses, or a missing model for each one when no content root
    /// was found. Empty when every model loads. A scene that cannot load one model builds as the gray box, so a run checks
    /// this first and does not film the gray box by mistake.
    /// </summary>
    public static IReadOnlyList<ModelProblem> Problems(ContentFolders? content) =>
        content is null
            ? [.. All.Select(reference => new MissingModel(reference.ToString()))]
            :
            [
                .. All.Select(reference => CookedModel.Load(content.CookedModel(reference)))
                    .Select(loaded => loaded.TryGetValue(out ModelProblem problem) ? problem : null)
                    .OfType<ModelProblem>(),
            ];

    /// <summary>
    /// The models of <see cref="All"/>, uploaded to <paramref name="resources"/> through one store on
    /// <paramref name="content"/>, or null when there is no content root or a model does not load. So a scene draws all of
    /// the art or none, and without it builds as the gray box.
    /// </summary>
    public static BlueDanubeModels? Load(ContentFolders? content, IRenderResources resources)
    {
        if (content is null)
            return null;
        var store = new ModelStore(resources, content);
        StoredModel? Of(AssetReference reference) => store.Load(reference).Ok;
        StoredModel? shuttleModel = Of(Shuttle with { Piece = "shuttle" }) ?? Of(Shuttle);
        StoredModel? shuttleHatchDoor = Of(Shuttle with { Piece = "hatch_door" });
        return
            (
                Of(Station),
                shuttleModel,
                Of(CargoPod),
                Of(StorageCart),
                Of(CagedSconce),
                Of(Toolbox),
                Of(Thermos),
                Of(Stool),
                Of(StreetSeating),
                Of(Passenger)
            )
                is (
                    StoredModel station,
                    StoredModel shuttle,
                    StoredModel pod,
                    StoredModel cart,
                    StoredModel sconce,
                    StoredModel toolbox,
                    StoredModel thermos,
                    StoredModel stool,
                    StoredModel seating,
                    StoredModel passenger
                )
            ? new(
                Station: station,
                Shuttle: shuttle,
                CargoPod: pod,
                StorageCart: cart,
                CagedSconce: sconce,
                Toolbox: toolbox,
                Thermos: thermos,
                Stool: stool,
                StreetSeating: seating,
                Passenger: passenger,
                ShuttleHatchDoor: shuttleHatchDoor
            )
            {
                Earth = EarthMaterial.Load(content, resources),
            }
            : null;
    }

    private static AssetReference Art(string path) => new(Package: Package, Path: $"blue-danube/{path}", Piece: null);
}

/// <summary>
/// The Blue Danube models of one scene, each uploaded once to the scene's render resources (see
/// <see cref="BlueDanubeArt.Load"/>). Each placement of a model shares its <see cref="StoredModel.Id"/>.
/// </summary>
public sealed record BlueDanubeModels(
    StoredModel Station,
    StoredModel Shuttle,
    StoredModel CargoPod,
    StoredModel StorageCart,
    StoredModel CagedSconce,
    StoredModel Toolbox,
    StoredModel Thermos,
    StoredModel Stool,
    StoredModel StreetSeating,
    StoredModel Passenger,
    StoredModel? ShuttleHatchDoor = null
)
{
    public MaterialId? Earth { get; init; }
}
