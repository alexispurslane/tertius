namespace Tertius.Rendering;

/// <summary>One half-resolution texel that the upsample reads: its view position, its occlusion and its bilinear weight.</summary>
internal readonly record struct UpsampleTexel(Double3 Position, double Occlusion, double BilinearWeight);

/// <summary>
/// The joint bilateral upsample of the occlusion from half to full resolution (docs/plans/ambient-occlusion.md, "The
/// upsample"). occlusion_upsample.comp computes the same.
/// </summary>
internal static class OcclusionUpsample
{
    /// <summary>The bilinear weights of the texels at c / 2 and c / 2 + 1 along one axis: (1, 0) for an even c, (0.5, 0.5) for odd.</summary>
    public static (double Low, double High) AxisWeights(int fullResolutionCoordinate) =>
        fullResolutionCoordinate % 2 == 0 ? (1, 0) : (0.5, 0.5);

    /// <summary>
    /// The occlusion of the pixel at the view <paramref name="position"/>, clamped to [0, 1]: the bilinear weights cut
    /// by each texel's distance from the pixel's tangent plane, or the texel nearest the plane when none lies on it.
    /// </summary>
    /// <param name="texelWidth">p(z) at the pixel: a texel this far from the plane gets no weight.</param>
    /// <param name="texels">The texels that the half-resolution image holds, whose bilinear weight is above 0.</param>
    public static double Occlusion(
        Double3 position,
        Double3 geometricNormal,
        double texelWidth,
        ReadOnlySpan<UpsampleTexel> texels
    )
    {
        double sum = 0;
        double weights = 0;
        double nearestOffPlane = double.PositiveInfinity;
        double nearestOcclusion = 1;
        foreach (UpsampleTexel texel in texels)
        {
            double offPlane = Math.Abs(Double3.Dot(geometricNormal, texel.Position - position));
            double weight = texel.BilinearWeight * Math.Max(0, 1 - offPlane / texelWidth);
            sum += weight * texel.Occlusion;
            weights += weight;
            if (offPlane < nearestOffPlane)
            {
                nearestOffPlane = offPlane;
                nearestOcclusion = texel.Occlusion;
            }
        }
        double occlusion = weights > 0 ? sum / weights : nearestOcclusion;
        return Math.Clamp(occlusion, 0, 1);
    }
}
