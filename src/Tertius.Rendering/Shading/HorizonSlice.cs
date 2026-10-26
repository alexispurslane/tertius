namespace Tertius.Rendering;

/// <summary>
/// One slice of GTAO in view space (docs/plans/ambient-occlusion.md, "The visibility of a slice"): the geometry of the
/// sectors' slices, and its horizons the reference that the sectors reduce to at infinite thickness. gtao.glsl's
/// gtaoSliceGeometry computes the same geometry, and BounceLightShaderTests pins it.
/// </summary>
internal readonly record struct HorizonSlice
{
    // The guard against the 0 / 0 of a normal with no part in the slice plane (the same section).
    private const double MinProjectedNormalLength = 1e-6;

    /// <summary>
    /// The slice at the screen angle <paramref name="angle"/>, around the unit view vector <paramref name="toEye"/>,
    /// of the unit view-space shading <paramref name="normal"/>.
    /// </summary>
    public HorizonSlice(Double3 toEye, Double3 normal, double angle)
    {
        ToEye = toEye;
        Direction = new(Math.Cos(angle), Math.Sin(angle), 0);
        Double3 ortho = Direction - toEye * Double3.Dot(Direction, toEye);
        TowardPlusSide = ortho.Normalized();
        Double3 axis = Double3.Cross(ortho, toEye).Normalized();
        Double3 projected = normal - axis * Double3.Dot(normal, axis);
        ProjectedNormalLength = projected.Length;
        double cosNormal = Math.Clamp(
            Double3.Dot(projected, toEye) / Math.Max(ProjectedNormalLength, MinProjectedNormalLength),
            0,
            1
        );
        NormalAngle = Math.Sign(Double3.Dot(ortho, projected)) * Math.Acos(cosNormal);
    }

    /// <summary>V̂, the unit vector from the center toward the eye.</summary>
    public Double3 ToEye { get; }

    /// <summary>ô, the unit vector in the slice plane at right angles to V̂, toward the plus side.</summary>
    public Double3 TowardPlusSide { get; }

    /// <summary>The direction of the slice in view space, (cos φ, sin φ, 0).</summary>
    public Double3 Direction { get; }

    /// <summary>|n_p|, the length of the normal projected into the slice plane.</summary>
    public double ProjectedNormalLength { get; }

    /// <summary>γ, the signed angle from the view vector to the projected normal, positive toward the plus side.</summary>
    public double NormalAngle { get; }

    /// <summary>The horizon cosine of the tangent plane: cos(γ + π/2) for <paramref name="side"/> +1, cos(γ − π/2) for −1.</summary>
    public double TangentHorizonCosine(int side) => Math.Cos(NormalAngle + side * Math.PI / 2);

    /// <summary>
    /// |n_p| · â: the cosine-weighted visibility of the slice between the horizons of its two sides. A cosine that
    /// rounding puts past ±1 counts as ±1, so acos stays defined.
    /// </summary>
    public double Visibility(double horizonCosinePlus, double horizonCosineMinus)
    {
        double h0 = -Math.Acos(Math.Clamp(horizonCosineMinus, -1, 1));
        double h1 = Math.Acos(Math.Clamp(horizonCosinePlus, -1, 1));
        return ProjectedNormalLength * (SliceArc(h0, NormalAngle) + SliceArc(h1, NormalAngle));
    }

    /// <summary>
    /// ∫ from 0 to <paramref name="horizon"/> of cos(θ − γ) · |sin θ| dθ, for γ = <paramref name="normalAngle"/>: half of
    /// the paper's inner integral.
    /// </summary>
    public static double SliceArc(double horizon, double normalAngle) =>
        (-Math.Cos(2 * horizon - normalAngle) + Math.Cos(normalAngle) + 2 * horizon * Math.Sin(normalAngle)) / 4;

    /// <summary>Whether a sample <paramref name="sampleDistance"/> from the center can raise a horizon: only inside the radius.</summary>
    public static bool Hides(double sampleDistance, double radius) => sampleDistance < radius;
}
