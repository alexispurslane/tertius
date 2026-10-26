namespace Tertius.Rendering;

/// <summary>What one ray of the reflections brings: its pre-exposed radiance C and its surface coverage a, both times the ray's own fade.</summary>
internal readonly record struct RayResult(Double3 Radiance, double Coverage);

/// <summary>
/// One texel of the half-resolution trace: its representative pixel's view position and geometric normal, and the
/// result of that pixel's ray, or null when the pixel starts no ray.
/// </summary>
internal readonly record struct TracedTexel(Double3 Position, Double3 GeometricNormal, RayResult? Ray);

/// <summary>
/// The joint bilateral upsample of the half-resolution trace of the reflections (docs/plans/screen-space-reflections.md,
/// "The half-resolution trace"), in the form of <see cref="OcclusionUpsample"/>. reflection_upsample.comp computes the
/// same.
/// </summary>
internal static class ReflectionUpsample
{
    /// <summary>
    /// The power of the cosine between the pixel's and the texel's geometric normals in a texel's weight: a turn of 10°
    /// keeps 0.89 of it, 45° keeps 0.06, and a right angle none.
    /// </summary>
    public const double NormalPower = 8;

    /// <summary>
    /// The ray of the full-resolution <paramref name="pixel"/> at the view <paramref name="position"/>: the mean of the
    /// rays of its 2 × 2 block of texels under their bilinear weights, cut by each texel's distance from the pixel's
    /// tangent plane and by the turn of its normal, or null when no texel with a ray keeps a weight.
    /// </summary>
    /// <param name="texelWidth">The width of one texel at the pixel: a texel this far from the plane gets no weight.</param>
    /// <param name="halfSize">The size of the half-resolution image, in texels.</param>
    /// <param name="texelAt">Reads a texel of the image: only one inside it, of a bilinear weight above 0.</param>
    public static RayResult? Ray(
        (int X, int Y) pixel,
        Double3 position,
        Double3 geometricNormal,
        double texelWidth,
        (int Width, int Height) halfSize,
        Func<(int X, int Y), TracedTexel> texelAt
    )
    {
        (double low, double high) = OcclusionUpsample.AxisWeights(pixel.X);
        (double top, double bottom) = OcclusionUpsample.AxisWeights(pixel.Y);
        Double3 radiance = default;
        double coverage = 0;
        double weights = 0;
        for (int b = 0; b < 2; b++)
        for (int a = 0; a < 2; a++)
        {
            double bilinear = (a == 0 ? low : high) * (b == 0 ? top : bottom);
            (int X, int Y) coordinates = (X: pixel.X / 2 + a, Y: pixel.Y / 2 + b);
            bool inside = coordinates.X < halfSize.Width && coordinates.Y < halfSize.Height;
            // A texel beyond the edge of the half-resolution image gets no weight.
            if (bilinear == 0 || !inside)
                continue;
            TracedTexel texel = texelAt(coordinates);
            if (texel.Ray is not { } ray)
                continue;
            double offPlane = Math.Abs(Double3.Dot(geometricNormal, texel.Position - position));
            double facing = Math.Max(0, Double3.Dot(geometricNormal, texel.GeometricNormal));
            double weight = bilinear * Math.Max(0, 1 - offPlane / texelWidth) * Math.Pow(facing, NormalPower);
            radiance += weight * ray.Radiance;
            coverage += weight * ray.Coverage;
            weights += weight;
        }

        return weights > 0 ? new RayResult(Radiance: radiance / weights, Coverage: coverage / weights) : null;
    }
}
