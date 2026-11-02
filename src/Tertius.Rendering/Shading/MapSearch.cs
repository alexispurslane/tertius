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
    /// t₀, the sweep of the march's first sample, before the even ones. Its point lies |x| / 63 from the shaded point, and
    /// the eye sees it at most about 1/63 rad from x: about one level-0 texel, 2 / W rad, at the default W = 128. A nearer
    /// hit falls in the shaded point's own texel, where the map cannot tell it from the shaded point's own surface. The
    /// sample changes no hit of the even samples: in front, it is only an earlier front end, and behind, front first
    /// ignores it. So it adds only the bracket from t₀ to 1 / N, the hit nearer than the first even sample.
    /// </summary>
    public const double NearSample = 1.0 / 64;

    /// <summary>K, the regula falsi steps in the first bracket, before the last secant place.</summary>
    public const int SecantSteps = 3;

    /// <summary>
    /// θ / α: the full width at half maximum of the GGX lobe about its central ray, in rad per unit of α. GGX falls to
    /// half its peak at a half vector 0.644 α from the normal, and the reflected direction turns by twice that.
    /// </summary>
    public const double ConeWidthPerAlpha = 2.57;

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
    /// <param name="alpha">α, the floored roughness squared, which sets the cone that chooses each read's level.</param>
    /// <param name="faceSize">W, the face size of level 0 in texels, a power of two.</param>
    public static MapHit Hit<TField>(ref TField field, Double3 position, Double3 direction, double alpha, int faceSize)
        where TField : struct, IInverseDistanceField
    {
        var ray = new SweptRay(position: position, direction: direction, alpha: alpha, faceSize: faceSize);
        // The last sample in front, null before the first one.
        Sample? front = null;
        for (int k = 0; k <= Samples; k++)
        {
            double sweep = k == 0 ? NearSample : (double)k / Samples;
            var sample = new Sample(sweep, ray.Clearance(ref field, sweep));
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
        double hit = Sample.SecantPlace(front, back);
        Double3 lookup = ray.Lookup(hit);
        // The floor keeps the division finite where the hit lies at the eye, which a ray through the eye can approach.
        return new MapHit(Found: true, Direction: lookup, Reach: hit / Math.Max(lookup.Length, ShortestLookup));
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

        // θ W / 2: the cone's full width at a reach of 1, in level-0 texels at a face's center, 2 / W rad each.
        private readonly double coneTexels = ConeWidthPerAlpha * alpha * faceSize / 2;

        // λmax, the last level, at 1 × 1.
        private readonly double lastLevel = Math.Log2(faceSize);

        /// <summary>L(t) = (1 − t) x̂ + t d: the eye's direction to the ray's point at the sweep t, not unit.</summary>
        public Double3 Lookup(double sweep) => (1 - sweep) * toPoint + sweep * direction;

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
            double reach = sweep / length;
            // The level whose texel spans the cone's cross-section θ ρ(t) as the eye sees it. The max keeps level 0 for
            // a cross-section narrower than a texel, and keeps log₂(0) out.
            double level = Math.Min(Math.Log2(Math.Max(coneTexels * reach, 1)), lastLevel);
            return 1 - sweep - KilometersPerMeter * distance * length * field.At(lookup, level);
        }
    }
}
