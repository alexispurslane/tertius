namespace Tertius.Rendering;

/// <summary>
/// The ambient light that a surface point sends toward the eye, before the estimate applies, in two terms. The C# copy of
/// AmbientTerms in lighting.glsl. Any ambient source gives them.
/// </summary>
internal readonly record struct AmbientTerms(Double3 Diffuse, Double3 Specular)
{
    /// <summary>
    /// The C# copy of occludedAmbient, the one place where the estimate applies (docs/plans/screen-space-illumination.md,
    /// decision 42): the diffuse term times the open share S plus the bounce under the diffuse color, and the specular
    /// term by its specular occlusion. S and G are not negative, so the diffuse term never goes below 0.
    /// </summary>
    /// <param name="openShare">S, per channel, from 0 to 1: the open directions' share of the source's light.</param>
    /// <param name="bounce">G, in plain radiance: the light of the blockers.</param>
    public AmbientTerms Occluded(
        double occlusion,
        Double3 openShare,
        Double3 bounce,
        Double3 diffuseColor,
        double nDotV,
        GgxLobe lobe
    ) =>
        new(
            Diffuse: Diffuse * openShare + diffuseColor * bounce,
            Specular: Specular * lobe.SpecularOcclusion(nDotV: nDotV, occlusion: occlusion)
        );
}
