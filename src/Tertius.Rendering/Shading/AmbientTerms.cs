namespace Tertius.Rendering;

/// <summary>
/// The ambient light that a surface point sends toward the eye, before the estimate applies, in two terms. The C# copy of
/// AmbientTerms in lighting.glsl. Any ambient source gives them.
/// </summary>
internal readonly record struct AmbientTerms(Double3 Diffuse, Double3 Specular)
{
    /// <summary>
    /// The C# copy of occludedAmbient, the one place where the estimate applies (docs/plans/screen-space-illumination.md,
    /// decision 41): the diffuse term less its light along the blocked directions plus the net bounce under the diffuse
    /// color, and the specular term by its specular occlusion. The map's light along them is in the net bounce, and the
    /// hemisphere's is (1 − V) of its term.
    /// </summary>
    /// <param name="bounce">G − M, the net bounce, in plain radiance: the light of the blockers, less the map's along the directions that they block while the map is on.</param>
    /// <param name="environmentMapOn">Whether the map is the source, whose light along the blocked directions the net bounce holds.</param>
    public AmbientTerms Occluded(
        double occlusion,
        Double3 bounce,
        Double3 diffuseColor,
        double nDotV,
        GgxLobe lobe,
        bool environmentMapOn
    ) =>
        new(
            Diffuse: environmentMapOn
                ? NotBelowZero(Diffuse + diffuseColor * bounce)
                : Diffuse * occlusion + diffuseColor * bounce,
            Specular: Specular * lobe.SpecularOcclusion(nDotV: nDotV, occlusion: occlusion)
        );

    /// <summary>Each channel of <paramref name="light"/>, or 0 where it is negative: a surface sends no negative light.</summary>
    private static Double3 NotBelowZero(Double3 light) =>
        new(Math.Max(light.X, 0), Math.Max(light.Y, 0), Math.Max(light.Z, 0));
}
