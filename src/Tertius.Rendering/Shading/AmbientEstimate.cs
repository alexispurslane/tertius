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

    /// <summary>The near estimate: each slice's open share and light by its weight, over the total weight.</summary>
    /// <remarks>The shares sum to 1 in every texel, so an open surface has V = 1 at any tilt.</remarks>
    /// <param name="sectors">The texel's slices.</param>
    /// <param name="near">Each slice's near steps, in the order of <paramref name="sectors"/>.</param>
    public static AmbientEstimate OfSlices(ReadOnlySpan<SliceSectors> sectors, ReadOnlySpan<NearSlice> near)
    {
        double totalWeight = 0;
        double open = 0;
        Double3 bounce = Double3.Zero;
        for (int slice = 0; slice < sectors.Length; slice++)
        {
            double weight = sectors[slice].Weight;
            totalWeight += weight;
            open += weight * near[slice].Blocked.Open.Count;
            bounce += near[slice].Light * weight;
        }
        if (totalWeight <= SectorSearch.MinTotalWeight)
            return Open;
        double scale = SliceSectors.Count * totalWeight;
        return new(Bounce: bounce / scale, Occlusion: open / scale);
    }

    /// <summary>The estimate after one slice's far ray: openShare times the outcome moves from open to blocked.</summary>
    /// <param name="openShare">U, the share of the estimate that the ray decides: the slice's open weight over the total.</param>
    public AmbientEstimate WithFarRay(double openShare, FarOutcome outcome) =>
        new(Bounce: Bounce + outcome.Light * openShare, Occlusion: Occlusion - openShare * outcome.Blocked);
}
