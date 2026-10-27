namespace Tertius.Rendering;

/// <summary>
/// A hair fiber at one point, after its maps: the C# copy of hairResponse in hair.glsl and of strandOf and fiberNormal in
/// material.glsl (docs/plans/skin-and-hair.md, "The hair model", "The strand and the fiber normal").
/// </summary>
internal readonly record struct HairFiber(Double3 BaseColor, double Roughness, double Reflectance)
{
    // Marschner's shifts of the two highlights, in radians: the primary toward the root, the secondary 1.5 times as far
    // toward the tip.
    private const double PrimaryShift = -5 * Math.PI / 180;
    private const double SecondaryShift = 7.5 * Math.PI / 180;

    // The square of a tangent's length below which a vertex has no tangent.
    private const double NoTangent = 1e-12;

    /// <summary>
    /// T, the strand from root to tip: the axis of the shading frame of <paramref name="normal"/> and the vertex
    /// tangent that <paramref name="direction"/> names. The tangent, made perpendicular to the normal, is +u. v shrinks
    /// along the bitangent n × t times the sign, as in glTF, so +v is t × n times the sign.
    /// </summary>
    /// <param name="normal">The unit shading normal, after the normal map.</param>
    public static Double3 Strand(Double3 normal, Double3 tangent, double bitangentSign, StrandDirection direction)
    {
        Double3 across = tangent - normal * Double3.Dot(normal, tangent);
        Double3 alongU = across.LengthSquared > NoTangent ? across.Normalized() : AnyPerpendicular(normal);
        Double3 alongV = Double3.Cross(alongU, normal) * (bitangentSign < 0 ? -1 : 1);
        return direction switch
        {
            StrandDirection.PlusU => alongU,
            StrandDirection.MinusU => -alongU,
            StrandDirection.PlusV => alongV,
            StrandDirection.MinusV => -alongV,
        };
    }

    /// <summary>
    /// n_f, the unit vector across <paramref name="strand"/> toward the eye, or <paramref name="cardNormal"/>, which is
    /// across the strand, when the strand lies along the line of sight.
    /// </summary>
    public static Double3 FiberNormal(Double3 strand, Double3 toView, Double3 cardNormal) =>
        (toView - strand * Double3.Dot(strand, toView)).NormalizedOr(cardNormal);

    /// <summary>
    /// The light that the fiber sends toward <paramref name="toView"/> per unit irradiance from <paramref name="toLight"/>:
    /// the diffuse part, and the two highlights in the specular part.
    /// </summary>
    /// <param name="fiberNormal">n_f, the fallback of a direction along the strand.</param>
    public DirectTerms Reflected(Double3 strand, Double3 fiberNormal, Double3 toView, Double3 toLight)
    {
        var lobes = new MaterialSample(
            BaseColor: BaseColor,
            Metallic: 0,
            Roughness: Roughness,
            Reflectance: Reflectance
        );
        double sinI = Math.Clamp(Double3.Dot(strand, toLight), -1, 1);
        double sinR = Math.Clamp(Double3.Dot(strand, toView), -1, 1);
        double cosI = Math.Sqrt(1 - sinI * sinI);
        double halfAngle = 0.5 * (Math.Asin(sinI) + Math.Asin(sinR));
        Double3 lightAcross = (toLight - strand * sinI).NormalizedOr(fiberNormal);
        Double3 viewAcross = (toView - strand * sinR).NormalizedOr(fiberNormal);
        double azimuth = 0.25 * Math.Sqrt(0.5 + 0.5 * Math.Clamp(Double3.Dot(lightAcross, viewAcross), -1, 1));
        double width = lobes.Lobe.Alpha;
        Double3 halfVector = (toLight + toView).NormalizedOr(fiberNormal);
        Double3 primary =
            lobes.Fresnel(Double3.Dot(toView, halfVector))
            * LongitudinalLobe(halfAngle, shift: PrimaryShift, width: width);
        Double3 secondary =
            lobes.F0 * lobes.DiffuseColor * LongitudinalLobe(halfAngle, shift: SecondaryShift, width: 2 * width);
        return new(
            Diffuse: lobes.DiffuseColor * (cosI / (Math.PI * Math.PI)),
            Specular: (primary + secondary) * (azimuth * cosI)
        );
    }

    /// <summary>
    /// M, a Gaussian of unit area over θ_r around a lobe's cone: <paramref name="width"/> in θ_h, so twice that in θ_r.
    /// </summary>
    private static double LongitudinalLobe(double halfAngle, double shift, double width)
    {
        double x = halfAngle - shift;
        return Math.Exp(-x * x / (2 * width * width)) / (2 * width * Math.Sqrt(2 * Math.PI));
    }

    /// <summary>The C# copy of anyPerpendicular in material.glsl: a unit vector at right angles to <paramref name="normal"/>.</summary>
    private static Double3 AnyPerpendicular(Double3 normal)
    {
        Double3 axis = Math.Abs(normal.X) < 0.9 ? Double3.UnitX : Double3.UnitY;
        return Double3.Cross(axis, normal).Normalized();
    }
}
