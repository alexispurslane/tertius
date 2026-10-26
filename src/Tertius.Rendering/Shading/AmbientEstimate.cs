namespace Tertius.Rendering;

/// <summary>
/// The estimate of a texel or a pixel: G, the bounce, pre-exposed, and V, the occlusion
/// (docs/plans/screen-space-illumination.md, "The estimate of a texel"). occlusion_sectors.comp and
/// occlusion_far_rays.comp compute the same.
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

    /// <summary>The near estimate: each slice's open share and light by its weight, over the total weight.</summary>
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
            bounce += slice.Light * weight;
        }
        double scale = SliceSectors.Count * totalWeight;
        return new(Bounce: bounce / scale, Occlusion: open / scale);
    }

    /// <summary>U = W · count / (32 ΣW): the share of the texel's estimate that the slice's far ray decides.</summary>
    /// <param name="totalWeight">ΣW, above <see cref="SectorSearch.MinTotalWeight"/>: a texel with less traces no ray.</param>
    public static double OpenShare(NearSlice slice, double totalWeight) =>
        slice.Sectors.Weight * slice.Blocked.Open.Count / (SliceSectors.Count * totalWeight);

    /// <summary>The estimate after one slice's far ray: openShare times the outcome moves from open to blocked.</summary>
    /// <param name="openShare">U, the share of the estimate that the ray decides, from <see cref="OpenShare"/>.</param>
    public AmbientEstimate WithFarRay(double openShare, FarOutcome outcome) =>
        new(Bounce: Bounce + outcome.Light * openShare, Occlusion: Occlusion - openShare * outcome.Blocked);

    /// <summary>
    /// The estimate after a texel's one far ray under <see cref="FarRays.OnePerTexel"/>: the ray decides the texel's
    /// whole open share, which is the near estimate's V.
    /// </summary>
    public AmbientEstimate WithTexelFarRay(FarOutcome outcome) => WithFarRay(openShare: Occlusion, outcome: outcome);
}
