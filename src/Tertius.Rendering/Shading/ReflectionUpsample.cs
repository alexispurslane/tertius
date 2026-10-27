using System.Diagnostics;

namespace Tertius.Rendering;

/// <summary>What one ray of the reflections brings: its pre-exposed radiance C and its surface coverage a, both times the ray's own fade.</summary>
internal readonly record struct RayResult(Double3 Radiance, double Coverage);

/// <summary>A texel's ray that hit a surface.</summary>
/// <param name="Result">What the ray brings.</param>
/// <param name="HitDepth">The view depth of the hit, in m.</param>
internal readonly record struct TexelHit(RayResult Result, double HitDepth);

/// <summary>A texel's ray that found nothing, or whose hit kept too little coverage: it brings no radiance and no coverage.</summary>
internal readonly record struct TexelMiss;

/// <summary>A texel whose representative pixel starts no ray, or a texel past the edge of the image.</summary>
internal readonly record struct NoRay;

/// <summary>
/// The ray of one texel of the half-resolution trace: <see cref="TexelHit"/>, <see cref="TexelMiss"/> or
/// <see cref="NoRay"/>, a coverage of at least 2^−14, of 0 or of −1 in the trace image. This is a generated union.
/// </summary>
[GeneratedUnion(typeof(TexelHit), typeof(TexelMiss), typeof(NoRay))]
internal readonly partial struct TexelRay;

/// <summary>One texel of the half-resolution trace: its representative pixel's view position and geometric normal, and the ray of that pixel.</summary>
internal readonly record struct TracedTexel(Double3 Position, Double3 GeometricNormal, TexelRay Ray);

/// <summary>The rays of the texels around a pixel agree, and the pixel takes their weighted <paramref name="Mean"/>.</summary>
internal readonly record struct TexelMean(RayResult Mean);

/// <summary>No texel with a ray keeps a weight: the pixel keeps the ambient specular term, as for a ray that finds nothing.</summary>
internal readonly record struct NoTexelOfWeight;

/// <summary>The rays of the texels around a pixel hit different surfaces: the pixel traces its own ray, as the full-resolution trace does.</summary>
internal readonly record struct OwnRay;

/// <summary>
/// What the upsample does at a reflecting pixel: <see cref="TexelMean"/>, <see cref="NoTexelOfWeight"/> or
/// <see cref="OwnRay"/>. This is a generated union.
/// </summary>
[GeneratedUnion(typeof(TexelMean), typeof(NoTexelOfWeight), typeof(OwnRay))]
internal readonly partial struct UpsampledRay;

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
    /// What the upsample does at the full-resolution <paramref name="pixel"/> at the view <paramref name="position"/>: the
    /// mean of its 2 × 2 texels' rays under bilinear weights cut by the distance from its tangent plane and the turn of the
    /// normal, or its own ray where the blended rays disagree. A weight below the smallest normal float counts as none.
    /// </summary>
    /// <param name="texelWidth">The width of one texel at the pixel: a texel this far from the plane gets no weight.</param>
    /// <param name="halfSize">The size of the half-resolution image, in texels.</param>
    /// <param name="texelAt">Reads a texel of the image: only one inside it, of a bilinear weight above 0.</param>
    public static UpsampledRay Ray(
        (int X, int Y) pixel,
        Double3 position,
        Double3 geometricNormal,
        double texelWidth,
        (int Width, int Height) halfSize,
        Func<(int X, int Y), TracedTexel> texelAt
    )
    {
        Double3 radiance = default;
        double coverage = 0;
        double weights = 0;
        var blended = new List<TexelRay>(4);
        foreach (((int X, int Y) coordinates, double bilinear) in OcclusionUpsample.BlockTexels(pixel, halfSize))
        {
            TracedTexel texel = texelAt(coordinates);
            RayResult ray;
            switch (texel.Ray)
            {
                case TexelHit { Result: var hit }:
                    ray = hit;
                    break;
                case TexelMiss:
                    ray = new RayResult(Radiance: default, Coverage: 0);
                    break;
                case NoRay or null:
                    // A texel with no ray holds no estimate, so it takes no part.
                    continue;
                default:
                    throw new UnreachableException();
            }

            double offPlane = Math.Abs(Double3.Dot(geometricNormal, texel.Position - position));
            double facing = Math.Max(0, Double3.Dot(geometricNormal, texel.GeometricNormal));
            double weight = bilinear * Math.Max(0, 1 - offPlane / texelWidth) * Math.Pow(facing, NormalPower);
            if (weight < ShaderInput.SmallestNormal)
                continue;
            radiance += weight * ray.Radiance;
            coverage += weight * ray.Coverage;
            weights += weight;
            blended.Add(texel.Ray);
        }

        if (blended.Count == 0)
            return new NoTexelOfWeight();
        return RaysDisagree(blended)
            ? new OwnRay()
            : new TexelMean(new RayResult(Radiance: radiance / weights, Coverage: coverage / weights));
    }

    /// <summary>
    /// Whether the <paramref name="rays"/> of the texels that a pixel blends hit different surfaces: a hit beside a miss,
    /// or two hits whose view depths differ by more than the trace's slab thickness (<see cref="CellBounds.ThicknessShare"/>).
    /// </summary>
    public static bool RaysDisagree(IEnumerable<TexelRay> rays)
    {
        bool anyHit = false,
            anyMiss = false;
        double nearest = double.MaxValue,
            farthest = 0;
        foreach (TexelRay ray in rays)
            switch (ray)
            {
                case TexelHit { HitDepth: var depth }:
                    anyHit = true;
                    nearest = Math.Min(nearest, depth);
                    farthest = Math.Max(farthest, depth);
                    break;
                case TexelMiss:
                    anyMiss = true;
                    break;
                case NoRay or null:
                    // A texel with no ray holds no estimate, so it takes no part.
                    break;
                default:
                    throw new UnreachableException();
            }

        // A surface at depth z fills z to (1 + share) z, as the trace's slab does.
        bool hitAndMiss = anyHit && anyMiss;
        bool apartInDepth = farthest > (1 + CellBounds.ThicknessShare) * nearest;
        return hitAndMiss || apartInDepth;
    }
}
