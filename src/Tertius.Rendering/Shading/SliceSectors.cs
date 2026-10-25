namespace Tertius.Rendering;

/// <summary>
/// The 32 sectors of one slice: equal shares of its cosine-weighted arc around the projected normal
/// (docs/plans/screen-space-illumination.md, "The slice and its sectors"). sectors.glsl computes the same.
/// </summary>
internal readonly record struct SliceSectors
{
    /// <summary>The sectors of a slice: the bits of one 32-bit word, the width of Vulkan's core bit instructions.</summary>
    public const int Count = 32;

    /// <summary>The halvings of the search for a sector's center angle: π / 4,096 at the end.</summary>
    public const int AngleHalvings = 12;

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

    /// <summary>The angle where m is (sector + ½) / 32, by AngleHalvings halvings.</summary>
    public double CenterAngle(int sector)
    {
        double target = (sector + 0.5) / Count;
        double low = Slice.NormalAngle - Math.PI / 2;
        double high = Slice.NormalAngle + Math.PI / 2;
        for (int i = 0; i < AngleHalvings; i++)
        {
            double middle = 0.5 * (low + high);
            if (MeasureFraction(middle) < target)
                low = middle;
            else
                high = middle;
        }
        return 0.5 * (low + high);
    }

    /// <summary>The sectors whose centers lie between the two angles: floor(32 m + ½) at each end.</summary>
    public SectorMask Covering(double angleA, double angleB) =>
        SectorMask.Run(
            start: Boundary(MeasureFraction(Math.Min(angleA, angleB))),
            end: Boundary(MeasureFraction(Math.Max(angleA, angleB)))
        );

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
