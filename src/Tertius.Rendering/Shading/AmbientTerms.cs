namespace Tertius.Rendering;

/// <summary>
/// The ambient light that a surface point sends toward the eye, before the estimate applies, in two terms. The C# copy of
/// AmbientTerms in lighting.glsl. Any ambient source gives them.
/// </summary>
internal readonly record struct AmbientTerms(Double3 Diffuse, Double3 Specular)
{
    /// <summary>
    /// The C# copy of occludedAmbient, the one place where the estimate applies: the diffuse term by the occlusion V
    /// plus the bounce G under the diffuse color, and the specular term by its specular occlusion.
    /// </summary>
    /// <param name="bounce">G, in plain radiance: the light of the blockers, which only the diffuse part reflects.</param>
    public AmbientTerms Occluded(double occlusion, Double3 bounce, Double3 diffuseColor, double nDotV, GgxLobe lobe) =>
        new(
            Diffuse: Diffuse * occlusion + diffuseColor * bounce,
            Specular: Specular * lobe.SpecularOcclusion(nDotV: nDotV, occlusion: occlusion)
        );
}
