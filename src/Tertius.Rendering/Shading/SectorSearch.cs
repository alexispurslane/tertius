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

/// <summary>What a near step reads at its texel's representative pixel, in view space.</summary>
/// <param name="Position">P(t), the pixel's view position at the texel's view depth.</param>
/// <param name="GeometricNormal">The unit geometric normal at that pixel.</param>
/// <param name="DirectLight">The direct image at that pixel, pre-exposed.</param>
internal readonly record struct StepTexel(Double3 Position, Double3 GeometricNormal, Double3 DirectLight);

/// <summary>
/// The own surface of a receiver that is not hair: the plane at right angles to its geometric normal, which blocks the
/// sectors below it with no light (docs/plans/screen-space-illumination.md, decision 39).
/// </summary>
/// <param name="Normal">The receiver's unit view-space geometric normal.</param>
internal readonly record struct OwnPlane(Double3 Normal);

/// <summary>
/// A hair receiver's own card, which blocks nothing, because its own surface is its fibers: a step that reads a point
/// of the card is left out (docs/plans/skin-and-hair.md, "Hair in each pass"). The card is the plane through the
/// receiver's surface point at right angles to its geometric normal, the card's, in view space.
/// </summary>
/// <param name="Point">The receiver's surface point: the view position of its texel's representative pixel.</param>
/// <param name="Normal">The receiver's unit view-space geometric normal.</param>
/// <param name="VertexSnap">How far the rasterizer's snapping can move a vertex across its view ray, per m of its distance: <see cref="SnapPerMeter"/>.</param>
internal readonly record struct OwnCard(Double3 Point, Double3 Normal, double VertexSnap)
{
    /// <summary>
    /// The angle in radians by which a normal that the normals target stores can miss the normal that it encodes: a step
    /// of one SNORM16 component turns an octahedral normal by at most 3√2 / 32767, and a driver may round 1.5 steps from
    /// the value, so 6.4 / 32767; 8 / 32767 leaves room. sectors.glsl holds the same.
    /// </summary>
    public const double NormalPrecision = 8.0 / 32767;

    /// <summary>
    /// How far the rasterizer's snapping can move a vertex across its view ray, per m of its distance: a vertex snaps
    /// to 1 / 2^bits of a pixel, so it moves up to 2^−(bits + 1) pixels on each axis, √2 times that across.
    /// </summary>
    /// <param name="pixelWidth">The width in m of a full-resolution pixel at a view depth of 1 m: 2 t_x / W.</param>
    /// <param name="subPixelPrecisionBits">The device's limit of that name, at least 4 by the Vulkan spec.</param>
    public static double SnapPerMeter(double pixelWidth, int subPixelPrecisionBits) =>
        pixelWidth * Math.Sqrt(2) / Math.Pow(2, subPixelPrecisionBits + 1);

    /// <summary>
    /// Whether <paramref name="texelPoint"/>, the real point that a step read, lies on the card's plane, within what the
    /// reads can put a point of that plane off it (sectors.glsl, onOwnCard): each point's depth by 1 −
    /// <see cref="HorizonSearch.CenterBias"/> and the snapping by <see cref="VertexSnap"/> of its distance, and the
    /// stored normal by <see cref="NormalPrecision"/> times the distance between the two points.
    /// </summary>
    public bool Holds(Double3 texelPoint)
    {
        double offPlane = Math.Abs(Double3.Dot(Normal, texelPoint - Point));
        double tolerance =
            (1 - HorizonSearch.CenterBias + VertexSnap) * (Point.Length + texelPoint.Length)
            + NormalPrecision * Double3.Distance(texelPoint, Point);
        return offPlane <= tolerance;
    }
}

/// <summary>
/// A receiver's own surface, which the near steps of its slices see first: <see cref="OwnPlane"/>, or a hair
/// receiver's <see cref="OwnCard"/>. occlusion_sectors.comp tells them apart by the shading target's model code. This
/// is a generated union.
/// </summary>
[GeneratedUnion(typeof(OwnPlane), typeof(OwnCard))]
internal readonly partial struct OwnSurface
{
    /// <summary>The sectors of <paramref name="sectors"/> that the own surface blocks, with no light.</summary>
    public SectorMask Blocks(SliceSectors sectors) =>
        this switch
        {
            OwnPlane(var normal) => sectors.BelowPlane(normal),
            OwnCard => SectorMask.None,
            null => throw new InvalidOperationException("A receiver's own surface has a kind."),
        };

    /// <summary>Whether a step that reads <paramref name="texelPoint"/>, P(t), is left out: a point of a hair receiver's own card.</summary>
    public bool LeavesOut(Double3 texelPoint) =>
        this switch
        {
            OwnPlane => false,
            OwnCard card => card.Holds(texelPoint),
            null => throw new InvalidOperationException("A receiver's own surface has a kind."),
        };
}

/// <summary>A slice after its near steps: its sectors, its blocked sectors and the sum of newly blocked sectors times their light.</summary>
internal readonly record struct NearSlice(SliceSectors Sectors, SectorMask Blocked, Double3 Light);

/// <summary>
/// The search of one slice's sectors: the near steps, which block what their slabs cover and give it their direct
/// light (docs/plans/screen-space-illumination.md, "A near step"). sectors.glsl and occlusion_sectors.comp compute the
/// same.
/// </summary>
internal static class SectorSearch
{
    /// <summary>The total slice weight at or below which a texel holds no estimate and is open.</summary>
    public const double MinTotalWeight = 1e-6;

    // Plus side first, as XeGTAO's order: a step gives light only to the sectors that no nearer step blocked.
    private static readonly SliceSide[] SidesInOrder = [SliceSide.Plus, SliceSide.Minus];

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
    /// s_f: where the view ray through a step's exact place, <paramref name="exactRay"/>, meets the plane of its texel's
    /// surface. Null where it meets the plane behind the eye or never. At a view depth outside
    /// <paramref name="depthsAround"/>, widened by 1 − <see cref="HorizonSearch.CenterBias"/>, the plane does not reach
    /// the place, and the step stands at <paramref name="texelPosition"/>, the real point that it read.
    /// </summary>
    /// <param name="exactRay">The view ray, whose z is 1, through the step's exact place on the slice line.</param>
    /// <param name="texelPosition">P(t), the view position of the step texel's representative pixel.</param>
    /// <param name="texelNormal">The unit view-space geometric normal at that pixel.</param>
    /// <param name="depthsAround">The least and the greatest view depth of the texels around the exact place, <see cref="DepthsAround"/>.</param>
    public static Double3? SurfacePoint(
        Double3 exactRay,
        Double3 texelPosition,
        Double3 texelNormal,
        (double Least, double Greatest) depthsAround
    )
    {
        // The quotient is the meeting point's view depth: one that is not positive lies behind the eye or nowhere.
        double planeOffset = Double3.Dot(texelNormal, texelPosition);
        double rayAlongNormal = Double3.Dot(texelNormal, exactRay);
        if (!(planeOffset * rayAlongNormal > 0))
            return null;
        double depth = planeOffset / rayAlongNormal;
        bool betweenAround =
            depth >= depthsAround.Least * HorizonSearch.CenterBias
            && depth <= depthsAround.Greatest / HorizonSearch.CenterBias;
        return betweenAround ? exactRay * depth : texelPosition;
    }

    /// <summary>
    /// The least and the greatest view depth of the texels around a step's exact place: its texel and that texel's two
    /// neighbors toward the place, along the row and along the column, whose triangle holds the place.
    /// </summary>
    /// <param name="texelDepth">The view depth of the step's texel.</param>
    /// <param name="towardPlace">The exact place minus the step's texel, in half-resolution texels: at most ½ on each axis.</param>
    /// <param name="depthAt">The view depth of a texel, <see cref="HorizonSearch.SkyDepth"/> for sky.</param>
    public static (double Least, double Greatest) DepthsAround(
        (int X, int Y) texel,
        double texelDepth,
        (double X, double Y) towardPlace,
        Func<(int X, int Y), double> depthAt
    )
    {
        double alongRow = depthAt((X: texel.X + Math.Sign(towardPlace.X), texel.Y));
        double alongColumn = depthAt((texel.X, Y: texel.Y + Math.Sign(towardPlace.Y)));
        return (
            Least: Math.Min(texelDepth, Math.Min(alongRow, alongColumn)),
            Greatest: Math.Max(texelDepth, Math.Max(alongRow, alongColumn))
        );
    }

    /// <summary>
    /// GTAO's near steps of one slice, nearest first and plus side first, as occlusion_sectors.comp's loop builds them:
    /// each reads the texel nearest its exact place and stands at its surface point on the slice. A step outside the
    /// image, on sky, or whose surface does not reach the slice is left out.
    /// </summary>
    /// <param name="texel">The receiver's half-resolution texel.</param>
    /// <param name="rayThrough">The view ray, whose z is 1, through a place in half-resolution texels, at whole numbers a representative pixel.</param>
    /// <param name="texelAt">What a step reads at a texel inside the image, or null for sky.</param>
    /// <param name="ownSurface">The receiver's own surface: a step that it leaves out reads nothing.</param>
    public static List<NearStep> SliceSteps(
        (int X, int Y) texel,
        int slice,
        double sliceAngle,
        int stepsPerSide,
        float stepNoise,
        double radiusInTexels,
        (int Width, int Height) halfSize,
        Func<(double X, double Y), Double3> rayThrough,
        Func<(int X, int Y), StepTexel?> texelAt,
        OwnSurface ownSurface
    )
    {
        var steps = new List<NearStep>();
        // A texel outside the image reads the border texel, as the shader's viewDepthAt does.
        double DepthAt((int X, int Y) t) =>
            texelAt((X: Math.Clamp(t.X, 0, halfSize.Width - 1), Y: Math.Clamp(t.Y, 0, halfSize.Height - 1)))?.Position.Z
            ?? HorizonSearch.SkyDepth;
        for (int step = 0; HorizonSearch.NearRangeHoldsSteps(radiusInTexels) && step < stepsPerSide; step++)
        {
            (double X, double Y) exact = HorizonSearch.ExactStepOffset(
                stepFraction: HorizonSearch.StepFraction(
                    step: step,
                    slice: slice,
                    stepsPerSide: stepsPerSide,
                    stepNoise: stepNoise,
                    radiusInTexels: radiusInTexels
                ),
                sliceAngle: sliceAngle,
                radiusInTexels: radiusInTexels,
                halfSize: halfSize
            );
            (int X, int Y) nearest = HorizonSearch.StepOffset(exact);
            foreach (SliceSide side in SidesInOrder)
            {
                int sign = (int)side;
                (int X, int Y) stepTexel = (X: texel.X + sign * nearest.X, Y: texel.Y + sign * nearest.Y);
                bool inside =
                    stepTexel.X >= 0
                    && stepTexel.Y >= 0
                    && stepTexel.X < halfSize.Width
                    && stepTexel.Y < halfSize.Height;
                if (!inside || texelAt(stepTexel) is not { } read)
                    continue;
                if (ownSurface.LeavesOut(read.Position))
                    continue;
                Double3? surfacePoint = SurfacePoint(
                    exactRay: rayThrough((X: texel.X + sign * exact.X, Y: texel.Y + sign * exact.Y)),
                    texelPosition: read.Position,
                    texelNormal: read.GeometricNormal,
                    depthsAround: DepthsAround(
                        texel: stepTexel,
                        texelDepth: read.Position.Z,
                        towardPlace: (X: sign * (exact.X - nearest.X), Y: sign * (exact.Y - nearest.Y)),
                        depthAt: DepthAt
                    )
                );
                if (surfacePoint is not { } position)
                    continue;
                steps.Add(
                    new(
                        Position: position,
                        GeometricNormal: read.GeometricNormal,
                        DirectLight: read.DirectLight,
                        Side: side
                    )
                );
            }
        }
        return steps;
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
    /// <param name="ownSurface">The receiver's own surface, which blocks before every step.</param>
    public static NearSlice Near(
        SliceSectors sectors,
        Double3 center,
        OwnSurface ownSurface,
        double radius,
        double thicknessShare,
        ReadOnlySpan<NearStep> steps
    )
    {
        // The receiver's own surface first, so no step gives light to a direction below it.
        SectorMask blocked = ownSurface.Blocks(sectors);
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

    /// <summary>σ acos(V̂ · d / |d|): the signed angle of the direction <paramref name="direction"/> in the slice.</summary>
    private static double SliceAngle(Double3 toEye, Double3 direction, SliceSide side) =>
        (int)side * Math.Acos(Math.Clamp(Double3.Dot(direction.Normalized(), toEye), -1, 1));
}
