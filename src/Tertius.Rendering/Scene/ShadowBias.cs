namespace Tertius.Rendering;

/// <summary>
/// The one bias model of the cascades and the tiles (https://github.com/alexispurslane/tertius/wiki/local-light-shadows, decision 33,
/// ARCHITECTURE.md, "Shadow bias"). Each part scales with the texel size. The shadow passes draw the casters with
/// <see cref="SlopeBias"/>, the shading passes move each lookup by <see cref="NormalOffsetTexels"/>, and the cascades
/// add <see cref="ConstantBiasTexels"/>. The maps keep <see cref="ShadowCascades.MarginTexels"/> free at their edges, which
/// derives from <see cref="FilterClearanceTexels"/> and <see cref="NormalOffsetTexels"/>, as the faces' holding margin in
/// shadows.glsl does.
/// </summary>
internal static class ShadowBias
{
    /// <summary>
    /// The casters' slope-scaled depth bias in the cascades and in the local shadow atlas, in multiples of their largest
    /// depth change per texel. Every tap of the filter compares against the receiver's own depth, and the filter
    /// compares texels up to its reach, <see cref="FilterReachTexels"/>, from the lookup along each texel axis, so the
    /// bias covers the receiver's slope along an axis. On a slope diagonal to the grid the corner texels can fall a
    /// little short (ARCHITECTURE.md, "Shadow bias"). A receiver-plane term in its place let a grazing light pass
    /// under a wall that stands on the receiver (ter-kn1d). Measured between acne and shadows that leave their casters
    /// (https://github.com/alexispurslane/tertius/wiki/local-light-shadows, decision 33).
    /// </summary>
    public const float SlopeBias = 2.5f;

    /// <summary>
    /// How far the shading passes move a shadow lookup along the geometric normal, in texels of the cascade or of the tile
    /// at the receiver's distance, times sin(angle to the light). The batcher writes it into the frame constants in every
    /// frame, so the cascades and the tiles read this one value. The maps' margins grow with it.
    /// </summary>
    public const float NormalOffsetTexels = 1f;

    /// <summary>
    /// The cascades' constant depth bias toward the light, in texels of depth, which every pass that samples the cascades
    /// adds (shadowParams.z). The tiles take none.
    /// </summary>
    public const float ConstantBiasTexels = 0.5f;

    /// <summary>
    /// How far the filter's taps compare from the lookup, in texels: the 5 × 5 texels around the lookup's texel, up to
    /// 2 texels from it. The maps' margins keep this clearance. shadows.glsl holds the same value
    /// as FilterClearanceTexels.
    /// </summary>
    public const float FilterClearanceTexels = FilterReachTexels - 0.5f;

    /// <summary>
    /// The reach of the shadow filter's taps from the lookup, in texels: 4 × 4 bilinear comparisons one texel apart, in
    /// the cascades and the tiles. The biases and the maps' margins are sized for it. The cascades put the filter's blur
    /// on the lit side of each edge (lightOutsideShadow in shadows.glsl).
    /// </summary>
    public const float FilterReachTexels = 2.5f;
}
