namespace Tertius.Rendering;

/// <summary>
/// A rectangle light's face seen from a shaded point, per unit of its face radiance (ARCHITECTURE.md, "Rectangle
/// lights"): the C# copy of rectView and the polygon sums in lighting.glsl. A point behind the face takes none of it.
/// </summary>
internal readonly record struct RectView
{
    /// <summary>
    /// The face centered <paramref name="toCenter"/> from the shaded point, in m, with the half sides
    /// <paramref name="halfWidth"/> and <paramref name="halfHeight"/>, perpendicular, whose cross product points to its
    /// front.
    /// </summary>
    public RectView(Double3 toCenter, Double3 halfWidth, Double3 halfHeight)
    {
        ToCenter = toCenter;
        HalfWidth = halfWidth;
        HalfHeight = halfHeight;
        var area = Double3.Cross(halfWidth, halfHeight);
        // The point's height over the face's plane, times the half sides' lengths: in front where it is above 0.
        double height = -Double3.Dot(toCenter, area);
        InFront = height > 0;
        if (!InFront)
            return;
        // Van Oosterom and Strackee's solid angle of the triangles (c0, c1, c2) and (c0, c2, c3), each of whose triple
        // product is 4 height: the half sides are exact, so it keeps its precision far from the face.
        Double3 c0 = Corner(0),
            c1 = Corner(1),
            c2 = Corner(2),
            c3 = Corner(3);
        double l0 = c0.Length,
            l1 = c1.Length,
            l2 = c2.Length,
            l3 = c3.Length;
        double first = l0 * l1 * l2 + Double3.Dot(c0, c1) * l2 + Double3.Dot(c0, c2) * l1 + Double3.Dot(c1, c2) * l0;
        double second = l0 * l2 * l3 + Double3.Dot(c0, c2) * l3 + Double3.Dot(c0, c3) * l2 + Double3.Dot(c2, c3) * l0;
        SolidAngle = 2 * (Math.Atan2(4 * height, first) + Math.Atan2(4 * height, second));
    }

    /// <summary>From the shaded point to the face's center, in m.</summary>
    public Double3 ToCenter { get; }

    /// <summary>From the center to the middle of the edge at +width, in m.</summary>
    public Double3 HalfWidth { get; }

    /// <summary>From the center to the middle of the edge at +height, in m.</summary>
    public Double3 HalfHeight { get; }

    /// <summary>Whether the point lies on the side of the face's front, where it takes its light.</summary>
    public bool InFront { get; }

    /// <summary>Ω, the solid angle that the face fills seen from the point, in sr, or 0 behind it.</summary>
    public double SolidAngle { get; }

    /// <summary>
    /// ∫ max(n · ω, 0) dω over the face, in sr: its irradiance on a surface of normal <paramref name="normal"/>, over its
    /// face radiance. Lambert's edge sum over the face cut at the surface's horizon, so it is exact; 0 behind the face.
    /// </summary>
    public double ClippedCosine(Double3 normal)
    {
        if (!InFront)
            return 0;
        (Double3 first, Double3 second) = Basis(normal);
        return CutCosine(
            Corners(corner => new(Double3.Dot(corner, first), Double3.Dot(corner, second), Double3.Dot(corner, normal)))
        );
    }

    /// <summary>
    /// ∫ D dω over the face of the linearly transformed cosine D of <paramref name="inverse"/> (Heitz et al. 2016), for a
    /// surface of normal <paramref name="normal"/> seen from <paramref name="toView"/>: the share of the lobe's light that
    /// the face sends. The face is cut where the cosine's own space has its horizon; 0 behind the face.
    /// </summary>
    public double LtcShare(Double3 normal, Double3 toView, LtcMatrix inverse)
    {
        if (!InFront)
            return 0;
        (Double3 first, Double3 second) = Basis(normal, toView);
        return CutCosine(
                Corners(corner =>
                    inverse.Transform(
                        new(Double3.Dot(corner, first), Double3.Dot(corner, second), Double3.Dot(corner, normal))
                    )
                )
            ) / Math.PI;
    }

    /// <summary>
    /// Corner <paramref name="index"/> of the face relative to the point, from 0 to 3: −w −h, −w +h, +w +h, +w −h, which
    /// winds clockwise about the front, so the edge sum is above 0 seen from the front.
    /// </summary>
    private Double3 Corner(int index) =>
        index switch
        {
            0 => ToCenter - HalfWidth - HalfHeight,
            1 => ToCenter - HalfWidth + HalfHeight,
            2 => ToCenter + HalfWidth + HalfHeight,
            _ => ToCenter + HalfWidth - HalfHeight,
        };

    private Double3[] Corners(Func<Double3, Double3> map) =>
        [map(Corner(0)), map(Corner(1)), map(Corner(2)), map(Corner(3))];

    /// <summary>
    /// ∫ max(ω_z, 0) dω over the convex quad <paramref name="q"/> seen from the origin, in sr: Lambert's edge sum over its
    /// part above z = 0. The cut leaves at most one way out and one way back in, which the horizon's arc joins.
    /// </summary>
    internal static double CutCosine(Double3[] q)
    {
        double sum = 0;
        Double3 exit = Double3.Zero,
            entry = Double3.Zero;
        bool cut = false;
        for (int i = 0; i < 4; i++)
        {
            Double3 a = q[i],
                b = q[(i + 1) & 3];
            bool aAbove = a.Z >= 0,
                bAbove = b.Z >= 0;
            if (aAbove && bAbove)
            {
                sum += EdgeVector(a, b).Z;
                continue;
            }
            if (!aAbove && !bAbove)
                continue;
            var crossing = Double3.Lerp(a, b, a.Z / (a.Z - b.Z));
            cut = true;
            if (aAbove)
            {
                sum += EdgeVector(a, crossing).Z;
                exit = crossing;
            }
            else
            {
                sum += EdgeVector(crossing, b).Z;
                entry = crossing;
            }
        }
        if (cut)
            sum += EdgeVector(exit, entry).Z;
        return Math.Max(0.5 * sum, 0);
    }

    /// <summary>
    /// θ n̂ of the arc from the direction of <paramref name="a"/> to that of <paramref name="b"/>: its angle times the unit
    /// normal of its plane. Zero for an arc of no length.
    /// </summary>
    private static Double3 EdgeVector(Double3 a, Double3 b)
    {
        var normal = Double3.Cross(a, b);
        double sine = normal.Length;
        // atan2 of the unnormalized sine and cosine keeps the angle exact near 0 and π, where acos loses it.
        return sine > 0 ? normal * (Math.Atan2(sine, Double3.Dot(a, b)) / sine) : Double3.Zero;
    }

    /// <summary>Two unit tangents that make a right-handed frame with <paramref name="normal"/>.</summary>
    private static (Double3 First, Double3 Second) Basis(Double3 normal)
    {
        Double3 first = Double3.Cross(Math.Abs(normal.X) < 0.9 ? Double3.UnitX : Double3.UnitY, normal).Normalized();
        return (first, Double3.Cross(normal, first));
    }

    /// <summary>
    /// The frame of the LTC: the first tangent toward <paramref name="toView"/> in the surface's plane, as the fit's view
    /// lies in its x–z plane, or any tangent head on, where the cosine is round.
    /// </summary>
    private static (Double3 First, Double3 Second) Basis(Double3 normal, Double3 toView)
    {
        Double3 along = toView - Double3.Dot(toView, normal) * normal;
        if (along.LengthSquared <= 1e-10)
            return Basis(normal);
        Double3 first = along.Normalized();
        return (first, Double3.Cross(normal, first));
    }
}
