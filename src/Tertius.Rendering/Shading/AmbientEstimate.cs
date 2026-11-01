namespace Tertius.Rendering;

/// <summary>
/// The estimate of a texel or a pixel: G, the bounce, pre-exposed, V, the occlusion, and S, the open share of the map's
/// light in each channel (docs/plans/screen-space-illumination.md, "The estimate of a texel"). occlusion_sectors.comp
/// computes the same, and the images store 1 − S.
/// </summary>
internal readonly record struct AmbientEstimate(Double3 Bounce, double Occlusion, Double3 OpenShare)
{
    /// <summary>The estimate of a texel that nothing blocks, or that holds no information: G = 0, V = 1 and S = 1.</summary>
    public static AmbientEstimate Open => new(Double3.Zero, 1, new(1, 1, 1));

    /// <summary>ΣW: the sum of the weights of a texel's slices.</summary>
    public static double TotalWeight(ReadOnlySpan<NearSlice> slices)
    {
        double totalWeight = 0;
        foreach (NearSlice slice in slices)
            totalWeight += slice.Sectors.Weight;
        return totalWeight;
    }

    /// <summary>
    /// The estimate: each slice's open share and its blockers' light by its weight, over the total weight, and in each
    /// channel S = 1 − (the map's light along the blocked sectors) / (the map's light along all sectors), each slice's
    /// sums by its weight. An open sector takes the ambient light, so V is the open share. A channel in which the map
    /// holds no light along any sector, or every channel with the map off, takes V's share: 1 − the blocked share of
    /// the cosine weight.
    /// </summary>
    /// <remarks>
    /// The shares sum to 1 in every texel, so an open surface has V = 1 at any tilt. A texel that blocks nothing has a
    /// blocked map light of exactly 0, so S is exactly 1.
    /// </remarks>
    public static AmbientEstimate OfSlices(ReadOnlySpan<NearSlice> slices)
    {
        double totalWeight = TotalWeight(slices);
        if (totalWeight <= SectorSearch.MinTotalWeight)
            return Open;
        double open = 0;
        double blocked = 0;
        Double3 bounce = Double3.Zero;
        Double3 blockedMapLight = Double3.Zero;
        Double3 openMapLight = Double3.Zero;
        foreach (NearSlice slice in slices)
        {
            double weight = slice.Sectors.Weight;
            open += weight * slice.Blocked.Open.Count;
            blocked += weight * slice.Blocked.Count;
            bounce += slice.Light * weight;
            blockedMapLight += slice.BlockedMapLight * weight;
            openMapLight += slice.OpenMapLight * weight;
        }
        double scale = SliceSectors.Count * totalWeight;
        Double3 allMapLight = blockedMapLight + openMapLight;
        double blockedShare = blocked / scale;
        return new(
            Bounce: bounce / scale,
            Occlusion: open / scale,
            OpenShare: new(
                1 - BlockedShare(blockedMapLight.X, allMapLight.X, blockedShare),
                1 - BlockedShare(blockedMapLight.Y, allMapLight.Y, blockedShare),
                1 - BlockedShare(blockedMapLight.Z, allMapLight.Z, blockedShare)
            )
        );
    }

    /// <summary>One channel's share of the map's light along the blocked sectors, or <paramref name="fallback"/> where the map holds none.</summary>
    private static double BlockedShare(double blockedMapLight, double allMapLight, double fallback) =>
        allMapLight > 0 ? blockedMapLight / allMapLight : fallback;
}
