namespace Tertius.Rendering;

/// <summary>The side of a slice on which a step lies, as the sign of its angles: plus on ô's side of V̂.</summary>
internal enum SliceSide
{
    Plus = 1,
    Minus = -1,
}

/// <summary>One near step as the search sees it, in view space.</summary>
/// <param name="Position">s_f, the step's surface point on its slice, <see cref="SectorSearch.SurfacePoint"/>.</param>
/// <param name="GeometricNormal">The unit geometric normal at the step texel's representative pixel.</param>
/// <param name="DirectLight">The direct image at that pixel, pre-exposed.</param>
/// <param name="Side">σ, the side of the slice on which the step lies.</param>
internal readonly record struct NearStep(
    Double3 Position,
    Double3 GeometricNormal,
    Double3 DirectLight,
    SliceSide Side
);

/// <summary>A slice after its near steps: its sectors, its blocked sectors and the sum of newly blocked sectors times their light.</summary>
internal readonly record struct NearSlice(SliceSectors Sectors, SectorMask Blocked, Double3 Light);

/// <summary>What a far ray decides for its slice's open sectors: the share it blocks, from 0 to 1, and that share times its light.</summary>
internal readonly record struct FarOutcome(double Blocked, Double3 Light)
{
    /// <summary>A trace that leaves the screen, reaches the near plane or its vanishing point, or runs out of steps.</summary>
    public static FarOutcome Open => new(0, Double3.Zero);

    /// <summary>A ray that points into its own geometric surface, which blocks it and shows it no light.</summary>
    public static FarOutcome IntoItsOwnSurface => new(1, Double3.Zero);
}

/// <summary>
/// The search of one slice's sectors: the near steps, which block what their slabs cover and give it their direct
/// light, and the pick of the far ray's open sector (docs/plans/screen-space-illumination.md, "A near step" and "The far
/// ray of a slice"). sectors.glsl and occlusion_sectors.comp compute the same.
/// </summary>
internal static class SectorSearch
{
    /// <summary>The total slice weight at or below which a texel holds no estimate and is open.</summary>
    public const double MinTotalWeight = 1e-6;

    // The golden ratio's inverse, the R1 sequence of the steps, in single floats as the shader holds it.
    private const float StepSequence = 0.6180339887498948482f;

    /// <summary>
    /// θ_f and θ_b: the angles in the slice from V̂ = <paramref name="toEye"/> of the directions from the center to a
    /// step's surface point and to the back of its slab, s_f (1 + κ). A cosine that rounding puts past ±1 counts as ±1.
    /// </summary>
    public static (double Front, double Back) StepAngles(
        Double3 center,
        Double3 toEye,
        Double3 position,
        SliceSide side,
        double thicknessShare
    ) =>
        (
            Front: SliceAngle(toEye, position - center, side),
            Back: SliceAngle(toEye, position * (1 + thicknessShare) - center, side)
        );

    /// <summary>
    /// s_f: a step's surface point on its slice, where the view ray through its exact place,
    /// <paramref name="exactRay"/>, meets the plane of its texel's surface. Null where that ray meets the plane only
    /// behind the eye or never: the surface does not reach the slice there, and the step blocks nothing.
    /// </summary>
    /// <param name="exactRay">The view ray, whose z is 1, through the step's exact place on the slice line.</param>
    /// <param name="texelPosition">P(t), the view position of the step texel's representative pixel.</param>
    /// <param name="texelNormal">The unit view-space geometric normal at that pixel.</param>
    public static Double3? SurfacePoint(Double3 exactRay, Double3 texelPosition, Double3 texelNormal)
    {
        // The quotient is the meeting point's view depth: one that is not positive lies behind the eye or nowhere.
        double planeOffset = Double3.Dot(texelNormal, texelPosition);
        double rayAlongNormal = Double3.Dot(texelNormal, exactRay);
        if (!(planeOffset * rayAlongNormal > 0))
            return null;
        return exactRay * (planeOffset / rayAlongNormal);
    }

    /// <summary>Whether a blocker's geometric normal has a positive cosine with the direction back toward the point that it blocks.</summary>
    public static bool Faces(Double3 geometricNormal, Double3 towardReceiver) =>
        Double3.Dot(geometricNormal, towardReceiver) > 0;

    /// <summary>
    /// The near steps of one slice, in order, nearest first: each blocks what it covers and neither the receiver's own
    /// surface nor a nearer step blocked.
    /// </summary>
    /// <remarks>
    /// A step blocks only within <paramref name="radius"/> of the center, GTAO's hard edge. A blocker that faces away
    /// blocks with no light: no pixel holds the light of its back.
    /// </remarks>
    /// <param name="geometricNormal">The receiver's unit view-space geometric normal, whose plane blocks what lies below it.</param>
    public static NearSlice Near(
        SliceSectors sectors,
        Double3 center,
        Double3 geometricNormal,
        double radius,
        double thicknessShare,
        ReadOnlySpan<NearStep> steps
    )
    {
        // The receiver's own surface first, so no step gives light to a direction below it.
        SectorMask blocked = sectors.BelowPlane(geometricNormal);
        Double3 light = Double3.Zero;
        foreach (NearStep step in steps)
        {
            if (!HorizonSlice.Hides(sampleDistance: Double3.Distance(step.Position, center), radius: radius))
                continue;
            (double front, double back) = StepAngles(
                center: center,
                toEye: sectors.Slice.ToEye,
                position: step.Position,
                side: step.Side,
                thicknessShare: thicknessShare
            );
            SectorMask fresh = sectors.Covering(front, back).Except(blocked);
            blocked = blocked.Union(fresh);
            if (Faces(step.GeometricNormal, center - step.Position))
                light += step.DirectLight * fresh.Count;
        }
        return new(Sectors: sectors, Blocked: blocked, Light: light);
    }

    /// <summary>η: the next value of the slice's R1 sequence past its steps, index slice + n² for n steps per side.</summary>
    public static float FarRayNoise(float stepNoise, int slice, int stepsPerSide)
    {
        float shift = stepNoise + (slice + stepsPerSide * stepsPerSide) * StepSequence;
        return shift - MathF.Floor(shift);
    }

    /// <summary>
    /// The open sector of rank min(floor(η · count), count − 1), from sector 0: a cosine-weighted pick, because the
    /// sectors are equal shares. Null when no sector is open: the slice traces no far ray.
    /// </summary>
    public static int? FarRaySector(SectorMask blocked, float farRayNoise)
    {
        SectorMask open = blocked.Open;
        int count = open.Count;
        if (count == 0)
            return null;
        return open.SectorOfRank(Math.Min((int)(farRayNoise * count), count - 1));
    }

    /// <summary>σ acos(V̂ · d / |d|): the signed angle of the direction <paramref name="direction"/> in the slice.</summary>
    private static double SliceAngle(Double3 toEye, Double3 direction, SliceSide side) =>
        (int)side * Math.Acos(Math.Clamp(Double3.Dot(direction.Normalized(), toEye), -1, 1));
}
