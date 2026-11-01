using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The 32 sectors of one slice: equal shares of its cosine-weighted arc around the projected normal
/// (docs/plans/screen-space-illumination.md, "The slice and its sectors"). sectors.glsl computes the same.
/// </summary>
internal readonly record struct SliceSectors
{
    /// <summary>The sectors of a slice: the bits of one 32-bit word, the width of Vulkan's core bit instructions.</summary>
    public const int Count = 32;

    /// <summary>
    /// The steps of the bisection of <see cref="BoundaryAngle"/>: they leave the angle within π / 2⁹ rad, a fifth of the
    /// narrowest sector, which spans at least 1/32 rad because m rises at most 1 / T ≤ 1 per rad.
    /// </summary>
    public const int BoundarySteps = 8;

    public SliceSectors(HorizonSlice slice)
    {
        Slice = slice;
        double normalAngle = slice.NormalAngle;
        Arc = Math.Cos(normalAngle) + normalAngle * Math.Sin(normalAngle);
        Weight = slice.ProjectedNormalLength * Arc;
    }

    public HorizonSlice Slice { get; }

    /// <summary>T = cos γ + γ sin γ.</summary>
    /// <remarks>At least 1 for γ in [−π/2, π/2], so a division by it is safe.</remarks>
    public double Arc { get; }

    /// <summary>W = |n_p| T.</summary>
    public double Weight { get; }

    /// <summary>m(θ), from 0 at γ − π/2 to 1 at γ + π/2, clamped outside.</summary>
    /// <remarks>The clamp keeps m monotone: outside the hemisphere the integrand turns negative.</remarks>
    public double MeasureFraction(double angle)
    {
        double low = Slice.NormalAngle - Math.PI / 2;
        double clamped = Math.Clamp(angle, low, Slice.NormalAngle + Math.PI / 2);
        return (SignedArc(clamped) - SignedArc(low)) / Arc;
    }

    /// <summary>The sectors whose centers lie between the two angles: floor(32 m + ½) at each end.</summary>
    public SectorMask Covering(double angleA, double angleB) =>
        RunBetween(Math.Min(angleA, angleB), Math.Max(angleA, angleB)).Sectors;

    /// <summary>The run of the sectors whose centers lie between the angles <paramref name="low"/> and <paramref name="high"/>, low ≤ high.</summary>
    public SectorRun RunBetween(double low, double high)
    {
        double lowEdge = Slice.NormalAngle - Math.PI / 2;
        double highEdge = Slice.NormalAngle + Math.PI / 2;
        return new(
            First: Boundary(MeasureFraction(low)),
            End: Boundary(MeasureFraction(high)),
            Low: Math.Clamp(low, lowEdge, highEdge),
            High: Math.Clamp(high, lowEdge, highEdge)
        );
    }

    /// <summary>
    /// The sectors whose centers lie below the receiver's own geometric plane, at right angles to the unit view-space
    /// <paramref name="geometricNormal"/>: those that its surface blocks (docs/plans/screen-space-illumination.md,
    /// decision 39). None when the normal has no part in the slice plane.
    /// </summary>
    public SectorMask BelowPlane(Double3 geometricNormal) => RunBelowPlane(geometricNormal).Sectors;

    /// <summary>
    /// The run of <see cref="BelowPlane"/>. The plane's angle γ_g lies within π of γ, so its edges in the slice are
    /// γ_g ± π/2, and only one of them lies inside the hemisphere: the lower where γ_g &gt; γ, the upper where γ_g &lt; γ.
    /// </summary>
    public SectorRun RunBelowPlane(Double3 geometricNormal)
    {
        double normalAngle = Slice.NormalAngle;
        Double3 shading = DirectionAt(normalAngle);
        Double3 acrossShading = DirectionAt(normalAngle + Math.PI / 2);
        double alongShadingPart = Double3.Dot(geometricNormal, shading);
        double acrossShadingPart = Double3.Dot(geometricNormal, acrossShading);
        bool holdsTheSlice =
            Math.Sqrt(alongShadingPart * alongShadingPart + acrossShadingPart * acrossShadingPart)
            < HorizonSlice.MinProjectedNormalLength;
        if (holdsTheSlice)
            return NoRun;
        double planeAngle = normalAngle + Math.Atan2(acrossShadingPart, alongShadingPart);
        return planeAngle > normalAngle
            ? RunBetween(normalAngle - Math.PI / 2, planeAngle - Math.PI / 2)
            : RunBetween(planeAngle + Math.PI / 2, normalAngle + Math.PI / 2);
    }

    /// <summary>
    /// The angle of the boundary <paramref name="boundary"/>, from 0 to 32, where sector <paramref name="boundary"/>
    /// begins: the angle whose measure fraction is boundary / 32. The ends of the hemisphere, γ ∓ π/2, at 0 and 32, and
    /// in between the bisection of m over <see cref="BoundarySteps"/> steps, within 2⁻⁹ π rad of it. sectors.glsl's
    /// boundaryAngle computes the same.
    /// </summary>
    public double BoundaryAngle(int boundary)
    {
        Contract.Requires(boundary is >= 0 and <= Count);
        double low = Slice.NormalAngle - Math.PI / 2;
        double high = Slice.NormalAngle + Math.PI / 2;
        if (boundary == 0)
            return low;
        if (boundary == Count)
            return high;
        double fraction = (double)boundary / Count;
        for (int step = 0; step < BoundarySteps; step++)
        {
            double middle = (low + high) / 2;
            if (MeasureFraction(middle) < fraction)
                low = middle;
            else
                high = middle;
        }
        return (low + high) / 2;
    }

    /// <summary>
    /// The middle and the width of the angles of <paramref name="piece"/>, an unbroken piece of sectors that is not
    /// empty: from the boundary where its first sector begins to the one where its last sector ends.
    /// </summary>
    public (double Middle, double Width) PieceAngles(SectorMask piece)
    {
        Contract.Requires(piece.Count > 0 && piece.LowestPiece == piece);
        double start = BoundaryAngle(BitOperations.TrailingZeroCount(piece.Bits));
        double end = BoundaryAngle(Count - BitOperations.LeadingZeroCount(piece.Bits));
        return (Middle: (start + end) / 2, Width: end - start);
    }

    /// <summary>The empty run, which holds no sector, at the shading normal's angle γ.</summary>
    public SectorRun NoRun => new(First: 0, End: 0, Low: Slice.NormalAngle, High: Slice.NormalAngle);

    /// <summary>cos θ V̂ + sin θ ô, in view space.</summary>
    public Double3 DirectionAt(double angle) => Slice.ToEye * Math.Cos(angle) + Slice.TowardPlusSide * Math.Sin(angle);

    /// <summary>
    /// The first sector whose center lies after the measure fraction <paramref name="fraction"/>, from 0 to 32: a tie
    /// goes to the later sector. floor(x + ½), not round, whose ties GLSL leaves to the driver.
    /// </summary>
    private static int Boundary(double fraction) => (int)Math.Floor(fraction * Count + 0.5);

    /// <summary>F(h): the cosine-weighted measure from 0 to <paramref name="angle"/>, signed by the angle.</summary>
    private double SignedArc(double angle)
    {
        double arc = HorizonSlice.SliceArc(angle, Slice.NormalAngle);
        return angle >= 0 ? arc : -arc;
    }
}
