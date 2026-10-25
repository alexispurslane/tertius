namespace Tertius.Rendering;

/// <summary>
/// The ambient light that a surface point sends toward the eye, before ambient occlusion, in two terms. The C# copy of
/// AmbientTerms in lighting.glsl. Any ambient source gives them.
/// </summary>
internal readonly record struct AmbientTerms(Double3 Diffuse, Double3 Specular)
{
    /// <summary>The C# copy of occludedAmbient: the diffuse term by the occlusion, the specular term by its specular occlusion.</summary>
    public AmbientTerms Occluded(double occlusion, double nDotV, GgxLobe lobe) =>
        new(
            Diffuse: Diffuse * occlusion,
            Specular: Specular * lobe.SpecularOcclusion(nDotV: nDotV, occlusion: occlusion)
        );
}
