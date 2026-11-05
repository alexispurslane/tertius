namespace Tertius.Rendering;

/// <summary>The diffuse and specular terms of a surface's ambient light, as AmbientTerms in lighting.glsl stores them.</summary>
internal readonly record struct AmbientTerms(Double3 Diffuse, Double3 Specular);
