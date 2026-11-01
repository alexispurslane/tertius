namespace Tertius.Rendering;

/// <summary>One half-resolution texel that the upsample reads: its view position and its estimate.</summary>
internal readonly record struct UpsampleTexel(Double3 Position, AmbientEstimate Estimate);

/// <summary>
/// The joint bilateral upsample of the estimate, the bounce and the occlusion, from half to full resolution
/// (docs/plans/ambient-occlusion.md, "The upsample", and docs/plans/screen-space-illumination.md, "The denoise and the
/// upsample"). occlusion_upsample.comp computes the same.
/// </summary>
internal static class OcclusionUpsample
{
    /// <summary>The bilinear weights of the texels at c / 2 and c / 2 + 1 along one axis: (1, 0) for an even c, (0.5, 0.5) for odd.</summary>
    public static (double Low, double High) AxisWeights(int fullResolutionCoordinate) =>
        fullResolutionCoordinate % 2 == 0 ? (1, 0) : (0.5, 0.5);

    /// <summary>
    /// The texels of the 2 × 2 block of the full-resolution <paramref name="pixel"/> that have a bilinear weight above 0
    /// and lie inside the half-resolution image of <paramref name="halfSize"/>, with that weight. A texel beyond the edge
    /// of the image gets no weight.
    /// </summary>
    public static IEnumerable<((int X, int Y) Coordinates, double Bilinear)> BlockTexels(
        (int X, int Y) pixel,
        (int Width, int Height) halfSize
    )
    {
        (double low, double high) = AxisWeights(pixel.X);
        (double top, double bottom) = AxisWeights(pixel.Y);
        for (int b = 0; b < 2; b++)
        for (int a = 0; a < 2; a++)
        {
            double bilinear = (a == 0 ? low : high) * (b == 0 ? top : bottom);
            (int X, int Y) coordinates = (X: pixel.X / 2 + a, Y: pixel.Y / 2 + b);
            if (bilinear > 0 && coordinates.X < halfSize.Width && coordinates.Y < halfSize.Height)
                yield return (Coordinates: coordinates, Bilinear: bilinear);
        }
    }

    /// <summary>
    /// The estimate of the full-resolution <paramref name="pixel"/> at the view <paramref name="position"/>, with its
    /// occlusion and its open share clamped to [0, 1]: the bilinear weights of its 2 × 2 block of texels cut by each
    /// texel's distance from the pixel's tangent plane, or the texel nearest the plane when none lies on it. The bounce
    /// and the open share take the same weights.
    /// </summary>
    /// <param name="texelWidth">p(z) at the pixel: a texel this far from the plane gets no weight.</param>
    /// <param name="halfSize">The size of the half-resolution image, in texels.</param>
    /// <param name="texelAt">Reads a texel of the image: only one inside it, of a bilinear weight above 0.</param>
    public static AmbientEstimate Estimate(
        (int X, int Y) pixel,
        Double3 position,
        Double3 geometricNormal,
        double texelWidth,
        (int Width, int Height) halfSize,
        Func<(int X, int Y), UpsampleTexel> texelAt
    )
    {
        Double3 bounceSum = Double3.Zero;
        double occlusionSum = 0;
        Double3 openShareSum = Double3.Zero;
        double weights = 0;
        double nearestOffPlane = double.PositiveInfinity;
        AmbientEstimate nearest = AmbientEstimate.Open;
        foreach (((int X, int Y) coordinates, double bilinear) in BlockTexels(pixel, halfSize))
        {
            UpsampleTexel texel = texelAt(coordinates);
            double offPlane = Math.Abs(Double3.Dot(geometricNormal, texel.Position - position));
            double weight = bilinear * Math.Max(0, 1 - offPlane / texelWidth);
            bounceSum += texel.Estimate.Bounce * weight;
            occlusionSum += weight * texel.Estimate.Occlusion;
            openShareSum += texel.Estimate.OpenShare * weight;
            weights += weight;
            if (offPlane < nearestOffPlane)
            {
                nearestOffPlane = offPlane;
                nearest = texel.Estimate;
            }
        }
        AmbientEstimate estimate =
            weights > 0
                ? new(Bounce: bounceSum / weights, Occlusion: occlusionSum / weights, OpenShare: openShareSum / weights)
                : nearest;
        // The bounce needs no clamp: a mean of non-negative light with non-negative weights. The shares' clamps keep the
        // rounding of the sectors pass's division and of the blurs' sums within [0, 1].
        return estimate with
        {
            Occlusion = Math.Clamp(estimate.Occlusion, 0, 1),
            OpenShare = new(
                Math.Clamp(estimate.OpenShare.X, 0, 1),
                Math.Clamp(estimate.OpenShare.Y, 0, 1),
                Math.Clamp(estimate.OpenShare.Z, 0, 1)
            ),
        };
    }
}
