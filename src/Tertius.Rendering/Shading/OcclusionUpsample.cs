namespace Tertius.Rendering;

/// <summary>One half-resolution texel that the upsample reads: its view position and its occlusion.</summary>
internal readonly record struct UpsampleTexel(Double3 Position, double Occlusion);

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
    /// The occlusion of the full-resolution <paramref name="pixel"/> at the view <paramref name="position"/>, clamped
    /// to [0, 1]: the bilinear weights of its 2 × 2 block of texels cut by each texel's distance from the pixel's
    /// tangent plane, or the texel nearest the plane when none lies on it.
    /// </summary>
    /// <param name="texelWidth">p(z) at the pixel: a texel this far from the plane gets no weight.</param>
    /// <param name="halfSize">The size of the half-resolution image, in texels.</param>
    /// <param name="texelAt">Reads a texel of the image: only one inside it, of a bilinear weight above 0.</param>
    public static double Occlusion(
        (int X, int Y) pixel,
        Double3 position,
        Double3 geometricNormal,
        double texelWidth,
        (int Width, int Height) halfSize,
        Func<(int X, int Y), UpsampleTexel> texelAt
    )
    {
        (double low, double high) = AxisWeights(pixel.X);
        (double top, double bottom) = AxisWeights(pixel.Y);
        double sum = 0;
        double weights = 0;
        double nearestOffPlane = double.PositiveInfinity;
        double nearestOcclusion = 1;
        for (int b = 0; b < 2; b++)
        for (int a = 0; a < 2; a++)
        {
            double bilinear = (a == 0 ? low : high) * (b == 0 ? top : bottom);
            (int X, int Y) coordinates = (X: pixel.X / 2 + a, Y: pixel.Y / 2 + b);
            bool inside = coordinates.X < halfSize.Width && coordinates.Y < halfSize.Height;
            // A texel beyond the edge of the half-resolution image gets no weight.
            if (bilinear == 0 || !inside)
                continue;
            UpsampleTexel texel = texelAt(coordinates);
            double offPlane = Math.Abs(Double3.Dot(geometricNormal, texel.Position - position));
            double weight = bilinear * Math.Max(0, 1 - offPlane / texelWidth);
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
