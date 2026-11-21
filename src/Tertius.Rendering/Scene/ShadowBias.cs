namespace Tertius.Rendering;

/// <summary>
/// The one bias model of the cascades and the tiles (docs/plans/local-light-shadows.md, decision 33,
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
    /// depth change per texel. Every tap of the filter compares against the receiver's own depth, and the filter at its
    /// largest reach, <see cref="FilterReachTexels"/>, compares texels up to 2.5 texels from the lookup along each texel
    /// axis, so the bias covers the receiver's slope along an axis. On a slope diagonal to the grid the corner texels can
    /// fall a little short (ARCHITECTURE.md,
    /// "Shadow bias"). A receiver-plane term in its place let a grazing light pass under a wall that stands on the
    /// receiver (ter-kn1d). Measured between acne and shadows that leave their casters
    /// (docs/plans/local-light-shadows.md, decision 33).
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
    /// How far the filter's taps at their largest reach compare from the lookup, in texels: the 5 × 5 texels around the
    /// lookup's texel, up to 2 texels from it. The maps' margins keep this clearance. shadows.glsl holds the same value
    /// as FilterClearanceTexels.
    /// </summary>
    public const float FilterClearanceTexels = FilterReachTexels - 0.5f;

    /// <summary>
    /// The least reach of the shadow filter's taps from the lookup, in texels: one bilinear comparison, which reaches the
    /// texel centers within one texel of its lookup. A smaller reach shows the texel grid as steps. shadows.glsl holds the
    /// same value as MinFilterReachTexels.
    /// </summary>
    public const float MinFilterReachTexels = 1f;

    /// <summary>
    /// The largest reach of the shadow filter's taps from the lookup, in texels: 4 × 4 bilinear comparisons one texel
    /// apart. The biases and the maps' margins are sized for it. The local shadow tiles filter at this reach.
    /// </summary>
    public const float FilterReachTexels = 2.5f;

    /// <summary>
    /// The reach of the cascades' filter, in texels, which the batcher writes into the frame constants
    /// (shadowParams.w). The filter only hides the map's texel grid, and shadows.glsl puts its blur on the lit side of
    /// each edge (lightOutsideShadow), so the reach takes light from lit areas and puts none inside a shadow. One bilinear
    /// comparison put no light inside a shadow either, but it showed the texel grid as a crawling staircase on a shadow
    /// near the camera. So the cascades take the largest reach, which hides the grid (t8-rjno).
    /// </summary>
    public const float CascadeFilterReachTexels = FilterReachTexels;
}
