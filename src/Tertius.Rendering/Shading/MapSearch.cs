using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The inverse distance w, in km⁻¹, of the stored surface in a direction from the eye, in a map's axes: a read of the
/// distance cube or a test's analytic scene (docs/plans/environment-map-distances.md, "The quantity"). The sky is 0.
/// </summary>
internal interface IInverseDistanceField
{
    /// <summary>w along <paramref name="direction"/>, which need not be unit, as a cube read takes it.</summary>
    /// <param name="level">The level of the read, from 0 to log₂ W, which a read of the cube's chain filters by.</param>
    double At(Double3 direction, double level);
}

/// <summary>
/// Where a ray first goes behind the stored surfaces of a map, the result of <see cref="MapSearch.Hit"/> and the C# twin
/// of MapHit in environment.glsl (docs/plans/environment-map-distances.md, "The usage code").
/// </summary>
/// <param name="Found">False where the ray goes behind no stored surface.</param>
/// <param name="Direction">L(t*), the eye's direction to the hit in the map's axes, not unit. The ray's direction without a hit.</param>
/// <param name="Reach">ρ(t*): the hit's distance from the shaded point over its distance from the eye. 1 without a hit.</param>
internal readonly record struct MapHit(bool Found, Double3 Direction, double Reach)
{
    /// <summary>
    /// The roughness of the specular map's read at the hit: r √ρ*, at most 1, so that the lobe's footprint at the hit as
    /// the eye sees it chooses the blur (docs/plans/environment-map-distances.md, "A rough lobe"). r itself without a hit.
    /// </summary>
    /// <param name="roughness">The floored roughness r of the lobe, from the floor to 1.</param>
    public double ReadRoughness(double roughness) => Math.Min(roughness * Math.Sqrt(Reach), 1);
}

/// <summary>
/// The search of a map for the place where a reflected ray ends: a march of a near sample and even samples in the sweep,
/// then regula falsi steps in the first bracket (docs/plans/environment-map-distances.md, "The search"). The C# twin of
/// mapHit in environment.glsl.
/// </summary>
internal static class MapSearch
{
    /// <summary>N, the march's even samples, at t = k / N for k = 1 to N, after the <see cref="NearSample"/>.</summary>
    public const int Samples = 8;

    /// <summary>
    /// t₀, the sweep of the march's first sample, before the even ones. Its point lies about one level-0 texel from x
    /// at W = 128, the nearest place where the map can see a separate surface (docs/plans/environment-map-distances.md,
    /// "The steps").
    /// </summary>
    public const double NearSample = 1.0 / 64;

    /// <summary>K, the regula falsi steps in the first bracket, before the last secant place.</summary>
    public const int SecantSteps = 3;

    // The shortest lookup vector that the search reads, 2⁻¹⁰. A shorter one passes through the eye's neighborhood, which
    // lies in front of every stored surface, and a cube read along a zero vector is undefined.
    private const double ShortestLookup = 1.0 / 1024;

    // w is in km⁻¹ and the distances in m.
    private const double KilometersPerMeter = 0.001;

    /// <summary>
    /// The inverse distance w that a face stores for a surface <paramref name="distance"/> m from the eye along the texel's
    /// direction: 1,000 m over that distance, in km⁻¹ (docs/plans/environment-map-distances.md, "The quantity"). A
    /// distance below the faces' near plane counts as the near plane, so w is at most 20,000 km⁻¹.
    /// </summary>
    public static double InverseDistance(double distance) => 1000 / Math.Max(distance, DepthRanges.NearPlane);

    /// <summary>
    /// Where the ray from the shaded point <paramref name="position"/> along the unit <paramref name="direction"/> d first
    /// goes behind the stored surfaces of <paramref name="field"/>, in the field's axes with the eye at the origin
    /// (docs/plans/environment-map-distances.md, "The search"). Without a hit, d itself with a reach of 1.
    /// </summary>
    /// <param name="position">The shaded point x relative to the eye, in m, not at the eye.</param>
    /// <param name="geometricNormal">
    /// n_g, the unit geometric normal of the shaded point's surface. A ray with d · n_g ≤ 0 points into that surface and
    /// finds no hit, as the screen tracer takes no such ray (docs/plans/skin-and-hair.md, decision 35).
    /// </param>
    /// <param name="alpha">α, the floored roughness squared, which sets the lobe's <see cref="MapSearchCone"/> that chooses each read's level.</param>
    /// <param name="faceSize">W, the face size of level 0 in texels, a power of two.</param>
    public static MapHit Hit<TField>(
        ref TField field,
        Double3 position,
        Double3 direction,
        Double3 geometricNormal,
        double alpha,
        int faceSize
    )
        where TField : struct, IInverseDistanceField
    {
        // A ray that points into its own surface, as a normal map can turn d, would find that surface at once, which no
        // sample can see (docs/plans/environment-map-distances.md, "Error cases"). The screen tracer skips such rays too.
        if (Double3.Dot(direction, geometricNormal) <= 0)
            return new MapHit(Found: false, Direction: direction, Reach: 1);
        return March(
            field: ref field,
            ray: new SweptRay(position: position, direction: direction, alpha: alpha, faceSize: faceSize),
            direction: direction
        );
    }

    /// <summary>
    /// The march of <paramref name="ray"/>: the near sample, then the even samples, until the near entry or the first
    /// bracket, which <see cref="Refined"/> narrows. Without a hit, <paramref name="direction"/> d with a reach of 1.
    /// </summary>
    private static MapHit March<TField>(ref TField field, SweptRay ray, Double3 direction)
        where TField : struct, IInverseDistanceField
    {
        // The last sample in front, null before the first one.
        Sample? front = null;
        // Whether the near sample lies inside the thickness of its stored surface: behind it, by at most the share κ of
        // the surface's distance D that a surface fills behind it, as for SSR's trace (CellBounds.ThicknessShare). The
        // clearance is (1 − t)(1 − |p| / D) for the point p, so c + κ (1 − t) ≥ 0 means |p| ≤ (1 + κ) D.
        bool nearInSlab = false;
        for (int k = 0; k <= Samples; k++)
        {
            double sweep = k == 0 ? NearSample : (double)k / Samples;
            var sample = new Sample(sweep, ray.Clearance(ref field, sweep));
            // The near entry: a near sample just behind its surface and a first even sample behind too mean that the ray
            // entered a surface nearer than the near sample. A shaded point that the filter puts a little behind its own
            // surface has its first even sample in front, and the point beside a nearer object lies beyond the slab.
            if (k == 1 && nearInSlab && sample.Clearance < 0)
                return ray.HitAt(NearSample);
            if (k == 0)
                nearInSlab = sample.Clearance < 0 && sample.Clearance + CellBounds.ThicknessShare * (1 - sweep) >= 0;
            // A sample with c = 0 counts as in front, so the sky at t = 1 is never a crossing.
            if (sample.Clearance >= 0)
                front = sample;
            // Front first: a sample behind counts only after a sample in front. The first bracket holds the first
            // crossing, and later samples cannot change it.
            else if (front is { } inFront)
                return Refined(field: ref field, ray: ray, front: inFront, back: sample);
        }
        return new MapHit(Found: false, Direction: direction, Reach: 1);
    }

    /// <summary>
    /// The hit in the bracket from <paramref name="front"/>, whose c ≥ 0, to <paramref name="back"/>, whose c &lt; 0: after
    /// <see cref="SecantSteps"/> regula falsi steps, the last secant place, with no read.
    /// </summary>
    private static MapHit Refined<TField>(ref TField field, SweptRay ray, Sample front, Sample back)
        where TField : struct, IInverseDistanceField
    {
        // Always K steps, with no early stop on a small clearance, so that each pixel's reads have one bound.
        for (int step = 0; step < SecantSteps; step++)
        {
            double sweep = Sample.SecantPlace(front, back);
            var sample = new Sample(sweep, ray.Clearance(ref field, sweep));
            if (sample.Clearance >= 0)
                front = sample;
            else
                back = sample;
        }
        return ray.HitAt(Sample.SecantPlace(front, back));
    }

    /// <summary>The clearance c of the ray at the sweep t.</summary>
    private readonly record struct Sample(double Sweep, double Clearance)
    {
        /// <summary>
        /// Where the line through a bracket's two ends crosses c = 0: tA + (tB − tA) · cA / (cA − cB). The front's
        /// cA ≥ 0 &gt; cB, the back's, so the divisor is positive.
        /// </summary>
        public static double SecantPlace(Sample front, Sample back) =>
            front.Sweep + (back.Sweep - front.Sweep) * front.Clearance / (front.Clearance - back.Clearance);
    }

    /// <summary>
    /// The ray x + s d in the sweep t, s = |x| t / (1 − t): its lookup vector and its clearance at each t
    /// (docs/plans/environment-map-distances.md, "The ray in the map's sweep").
    /// </summary>
    private readonly struct SweptRay(Double3 position, Double3 direction, double alpha, int faceSize)
    {
        // |x|, in m.
        private readonly double distance = position.Length;

        // x̂, the eye's unit direction to the shaded point.
        private readonly Double3 toPoint = position / position.Length;

        // The cone of the accepted distance-cube search (docs/plans/temporal-renderer.md, section 14, ticket 8).
        private readonly MapSearchCone cone = MapSearchCone.OfAlpha(alpha);

        // 2 / W, a level-0 texel's angle at a face's center, in rad.
        private readonly float texelAngle = 2f / faceSize;

        // λmax, the last level, at 1 × 1.
        private readonly int lastLevel = BitOperations.Log2((uint)faceSize);

        /// <summary>L(t) = (1 − t) x̂ + t d: the eye's direction to the ray's point at the sweep t, not unit.</summary>
        public Double3 Lookup(double sweep) => (1 - sweep) * toPoint + sweep * direction;

        /// <summary>The hit at the sweep t, with no read: its direction L(t) and its reach t / |L(t)|.</summary>
        public MapHit HitAt(double sweep)
        {
            Double3 lookup = Lookup(sweep);
            // The floor keeps the division finite where the hit lies at the eye, which a ray through the eye can approach.
            return new MapHit(Found: true, Direction: lookup, Reach: sweep / Math.Max(lookup.Length, ShortestLookup));
        }

        /// <summary>
        /// c(t) = (1 − t) − 0.001 |x| |L(t)| w(L(t), λ(t)): above 0 where the ray's point at t lies in front of the stored
        /// surface in its direction, below 0 where it lies behind.
        /// </summary>
        public double Clearance<TField>(ref TField field, double sweep)
            where TField : struct, IInverseDistanceField
        {
            Double3 lookup = Lookup(sweep);
            double length = lookup.Length;
            // In front, with no read: the limit of c as |L| goes to 0, because w is bounded.
            if (length < ShortestLookup)
                return 1 - sweep;
            // The level whose texel side is the cone's radius as the eye sees it, ρ(t) tan θ_c. The ray's point lies
            // |x| t / (1 − t) along the ray and |x| |L(t)| / (1 − t) from the eye, so t and |L(t)| stand for both.
            double level = cone.Level(
                rayLength: (float)sweep,
                hitDistance: (float)length,
                pixelAngle: texelAngle,
                topLevel: lastLevel
            );
            return 1 - sweep - KilometersPerMeter * distance * length * field.At(lookup, level);
        }
    }
}
