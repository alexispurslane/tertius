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

    /// <summary>
    /// The models of <see cref="All"/> whose cooked file is missing under <paramref name="content"/>, or every model when
    /// no content root was found.
    /// </summary>
    public static IReadOnlyList<AssetReference> Missing(ContentFolders? content) =>
        content is null ? All : [.. All.Where(reference => !File.Exists(content.CookedModel(reference)))];

    private static AssetReference Art(string path) => new(Package: Package, Path: $"blue-danube/{path}", Piece: null);
}
