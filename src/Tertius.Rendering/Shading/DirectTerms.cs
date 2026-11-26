namespace Tertius.Rendering;

/// <summary>
/// The light that one light sends toward the eye per unit irradiance, in two parts: the diffuse part, with the diffuse
/// color in it as in <see cref="AmbientTerms"/>, and the specular part. The C# copy of DirectTerms in lighting.glsl
/// (https://github.com/alexispurslane/tertius/wiki/skin-and-hair, "The light of each model").
/// </summary>
internal readonly record struct DirectTerms(Double3 Diffuse, Double3 Specular);
