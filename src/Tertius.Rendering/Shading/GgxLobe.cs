namespace Tertius.Rendering;

/// <summary>The GGX lobe of one roughness (docs/plans/pbr-brdf.md, "The terms").</summary>
internal readonly record struct GgxLobe
{
    public const double MinRoughness = 0.045;

    /// <summary>
    /// α per rad of the width that a read of the specular map stands for. With n = v, the lobe's weight across a line x
    /// rad from its axis falls about as (a² + x²)^(−3/2), a = 2α, which leaves 5 % beyond x where a = 0.484 x.
    /// </summary>
    public const double AlphaPerWidth = 0.12108;

    /// <summary>The floor of v·h in <see cref="SphereNormalization"/>, as MinNdotV floors μ: v·h is 0 only where v = −l.</summary>
    public const double MinHalfCosine = 1e-4;

    /// <summary>
    /// The lower bound on the roughness of a read for a part: the lobe for a part 2.07 rad wide. A sharper read lets a
    /// small bright part of the map show in some texels and not in their neighbors, a speck (decision 42). The price is
    /// light from beyond a narrow part: the bound's lobe puts 11 % of its weight beyond either end of a part 0.95 rad
    /// wide, and 24 % beyond a part 0.5 rad wide.
    /// </summary>
    public const double MinPieceRoughness = 0.5;

    /// <summary>Clamps <paramref name="roughness"/> to the range from <see cref="MinRoughness"/> to 1.</summary>
    public GgxLobe(double roughness)
    {
        // The one place of the roughness floor in C# (docs/plans/pbr-brdf.md, "The roughness floor").
        Roughness = Math.Clamp(roughness, MinRoughness, 1);
        Alpha = Roughness * Roughness;
    }

    /// <summary>The perceptual roughness r, after the floor.</summary>
    public double Roughness { get; }

    /// <summary>The width of the lobe, α = r².</summary>
    public double Alpha { get; }

    /// <summary>
    /// The roughness of the specular map's lobe that blurs a read over a part of the sky <paramref name="width"/> rad
    /// wide: max(√(<see cref="AlphaPerWidth"/> · width), <see cref="MinPieceRoughness"/>)
    /// (docs/plans/screen-space-illumination.md, decisions 41 and 42). Only a part wider than 2.07 rad keeps 95 % of the
    /// lobe's weight on itself. A narrower part takes the bound's lobe, which puts up to 11 % of its weight beyond either
    /// end of a part 0.95 rad wide and 24 % beyond a part 0.5 rad wide. roughnessForWidth in environment.glsl is the GPU
    /// copy.
    /// </summary>
    public static double RoughnessForWidth(double width) =>
        Math.Max(Math.Sqrt(AlphaPerWidth * width), MinPieceRoughness);

    /// <summary>The GGX distribution D of the microfacet normals, per steradian, at the cosine of h to n.</summary>
    public double Distribution(double nDotH)
    {
        double alphaSquared = Alpha * Alpha;
        double denominator = nDotH * nDotH * (alphaSquared - 1) + 1;
        return alphaSquared / (Math.PI * denominator * denominator);
    }

    /// <summary>The height-correlated Smith visibility term Vis = G₂ / (4 (n·l) (n·v)).</summary>
    public double Visibility(double nDotL, double nDotV)
    {
        double alphaSquared = Alpha * Alpha;
        double lightPart = nDotL * Math.Sqrt(nDotV * nDotV * (1 - alphaSquared) + alphaSquared);
        double viewPart = nDotV * Math.Sqrt(nDotL * nDotL * (1 - alphaSquared) + alphaSquared);
        return 0.5 / (lightPart + viewPart);
    }

    /// <summary>
    /// (α / α′)², the normalization of the lobe that a sphere light widens (Karis 2013, p. 16, equation 14), with
    /// α′ = min(α + sin ϕ / (2 √(v·h)), 1) (docs/plans/pbr-brdf.md, "Sized lights"). 1 for a light of radius 0.
    /// sphereNormalization in brdf.glsl is the GPU copy.
    /// </summary>
    /// <param name="lightSine">sin ϕ of the light's angular radius ϕ at the point, R / d, from 0 to 1.</param>
    /// <param name="vDotH">The cosine of the view direction to the half vector of the representative point.</param>
    public double SphereNormalization(double lightSine, double vDotH)
    {
        double widened = Math.Min(Alpha + lightSine / (2 * Math.Sqrt(Math.Max(vDotH, MinHalfCosine))), 1);
        double share = Alpha / widened;
        return share * share;
    }

    /// <summary>
    /// The unit direction toward Karis's representative point of a sphere light: the point of the sphere nearest the
    /// mirror ray (Karis 2013, p. 15, equation 11). <paramref name="toLight"/> if the ray meets no direction.
    /// sphereLightDirection in brdf.glsl is the GPU copy.
    /// </summary>
    /// <param name="toLight">The unit direction toward the light's center.</param>
    /// <param name="mirror">The unit mirror direction of the view about the normal.</param>
    /// <param name="lightSine">sin ϕ of the light's angular radius ϕ at the point, R / d, from 0 to 1.</param>
    public static Double3 SphereLightDirection(Double3 toLight, Double3 mirror, double lightSine)
    {
        // In units of the distance d: the center at toLight, the radius sin ϕ. The notes print L − (L·r) r, which points
        // away from the ray. The closest point needs the way from the center toward the ray.
        Double3 centerToRay = Double3.Dot(toLight, mirror) * mirror - toLight;
        double centerToRaySquared = centerToRay.LengthSquared;
        // A ray through the sphere has its closest point on itself: saturate's 1.
        Double3 closest =
            centerToRaySquared <= lightSine * lightSine
                ? toLight + centerToRay
                : toLight + centerToRay * (lightSine / Math.Sqrt(centerToRaySquared));
        return closest.LengthSquared >= ShaderInput.SmallestNormal ? closest.Normalized() : toLight;
    }

    /// <summary>
    /// The unit half vector that the point (<paramref name="u1"/>, <paramref name="u2"/>) of the unit square maps to,
    /// with density D (n·h), in tangent space with +Z the normal.
    /// </summary>
    public Double3 SampleHalfVector(double u1, double u2)
    {
        double azimuth = 2 * Math.PI * u1;
        double cosTheta = Math.Sqrt((1 - u2) / (1 + (Alpha * Alpha - 1) * u2));
        double sinTheta = Math.Sqrt(1 - cosTheta * cosTheta);
        return new(sinTheta * Math.Cos(azimuth), sinTheta * Math.Sin(azimuth), cosTheta);
    }

    /// <summary>
    /// The unit direction d where the lobe gathers most of its light, between the unit <paramref name="mirror"/>
    /// direction and the unit <paramref name="normal"/> (Lagarde and de Rousiers 2014).
    /// </summary>
    public Double3 DominantDirection(Double3 normal, Double3 mirror) =>
        DominantDirection(normal: normal, mirror: mirror, towardMirror: (1 - Alpha) * (Math.Sqrt(1 - Alpha) + Alpha));

    /// <summary>
    /// The unit direction <paramref name="towardMirror"/> of the way from the unit <paramref name="normal"/> to the unit
    /// <paramref name="mirror"/> direction, as specularDominantDirection in Shaders/brdf.glsl mixes them. The normal where
    /// the mix is too short to have a direction, as the shader's normalizedOr gives it.
    /// </summary>
    internal static Double3 DominantDirection(Double3 normal, Double3 mirror, double towardMirror)
    {
        var mix = Double3.Lerp(normal, mirror, towardMirror);
        return mix.LengthSquared >= ShaderInput.SmallestNormal ? mix.Normalized() : normal;
    }
}
