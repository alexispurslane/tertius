namespace Tertius.Rendering;

/// <summary>The GGX lobe of one roughness (docs/plans/pbr-brdf.md, "The terms").</summary>
internal readonly record struct GgxLobe
{
    public const double MinRoughness = 0.045;

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
        Double3 mix = Double3.Lerp(normal, mirror, towardMirror);
        return mix.LengthSquared >= ShaderInput.SmallestNormal ? mix.Normalized() : normal;
    }
}
