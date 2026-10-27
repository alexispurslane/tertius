namespace Tertius.Rendering;

/// <summary>
/// The estimate of a texel or a pixel: G − M, the net bounce, pre-exposed, and V, the occlusion
/// (docs/plans/screen-space-illumination.md, "The estimate of a texel"). occlusion_sectors.comp computes the same.
/// </summary>
internal readonly record struct AmbientEstimate(Double3 Bounce, double Occlusion)
{
    /// <summary>The estimate of a texel that nothing blocks, or that holds no information: (0, 0, 0, 1).</summary>
    public static AmbientEstimate Open => new(Double3.Zero, 1);

    /// <summary>ΣW: the sum of the weights of a texel's slices.</summary>
    public static double TotalWeight(ReadOnlySpan<NearSlice> slices)
    {
        double totalWeight = 0;
        foreach (NearSlice slice in slices)
            totalWeight += slice.Sectors.Weight;
        return totalWeight;
    }

    /// <summary>
    /// The estimate: each slice's open share and its light less its map light by its weight, over the total weight. An
    /// open sector takes the ambient light, so V is the open share.
    /// </summary>
    /// <remarks>The shares sum to 1 in every texel, so an open surface has V = 1 at any tilt.</remarks>
    public static AmbientEstimate OfSlices(ReadOnlySpan<NearSlice> slices)
    {
        double totalWeight = TotalWeight(slices);
        if (totalWeight <= SectorSearch.MinTotalWeight)
            return Open;
        double open = 0;
        Double3 bounce = Double3.Zero;
        foreach (NearSlice slice in slices)
        {
            double weight = slice.Sectors.Weight;
            open += weight * slice.Blocked.Open.Count;
            bounce += (slice.Light - slice.MapLight) * weight;
        }
        double scale = SliceSectors.Count * totalWeight;
        return new(Bounce: bounce / scale, Occlusion: open / scale);
    }
}
