namespace Tertius.Rendering;

/// <summary>No near light casts shadows: the renderer makes no atlas and no local shadow pass.</summary>
public readonly record struct NoLocalShadows;

/// <summary>
/// Near lights cast shadows from an atlas of <paramref name="Tiles"/> depth tiles of <paramref name="TileSize"/>²
/// texels, ranked first by their tier, which says how the main view keeps them, then by their intensity over the square of
/// the eye's distance to their spot of light (ARCHITECTURE.md, "Local shadows").
/// </summary>
/// <param name="Tiles">The budget: 1 up to the device's largest image array. A spot light takes 1, a point light 6.</param>
/// <param name="TileSize">The side of a tile in texels: a power of two from 64 to 4,096, and at most the device's largest image.</param>
public readonly record struct LocalShadowAtlas(int Tiles, int TileSize)
{
    /// <summary>64 tiles of 512², 64 MB of D32.</summary>
    public static LocalShadowAtlas Default => new(Tiles: 64, TileSize: 512);
}

/// <summary>
/// Whether near lights cast shadows: <see cref="NoLocalShadows"/> or <see cref="LocalShadowAtlas"/>. The default value
/// is NoLocalShadows. It is a generated union (see <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(NoLocalShadows), typeof(LocalShadowAtlas), ZeroCase = typeof(NoLocalShadows))]
public readonly partial struct LocalShadowSetting
{
    public const int MinTileSize = 64;

    /// <summary>The largest tile side, the least <c>maxImageDimension2D</c> that the Vulkan specification guarantees.</summary>
    public const int MaxTileSize = 4096;

    /// <summary>Why the renderer cannot use the setting, or null when it can. The device's limits are checked apart.</summary>
    internal string? Problem =>
        this switch
        {
            NoLocalShadows => null,
            LocalShadowAtlas atlas => ProblemOf(atlas),
        };

    private static string? ProblemOf(LocalShadowAtlas atlas)
    {
        if (atlas.Tiles < 1)
            return $"LocalShadowAtlas.Tiles must be at least 1, not {atlas.Tiles}.";
        bool sizeInRange = atlas.TileSize is >= MinTileSize and <= MaxTileSize;
        if (!sizeInRange || !int.IsPow2(atlas.TileSize))
            return $"LocalShadowAtlas.TileSize must be a power of two from {MinTileSize} to {MaxTileSize}, not {atlas.TileSize}.";
        return null;
    }
}
