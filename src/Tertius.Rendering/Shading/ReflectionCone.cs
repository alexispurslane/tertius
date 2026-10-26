namespace Tertius.Rendering;

/// <summary>
/// The cone that stands for the specular lobe around a reflected ray and the gap to the next ray
/// (docs/plans/screen-space-reflections.md, "The cone"). The C# copy of coneLevel in reflection.glsl.
/// </summary>
/// <param name="TanHalfAngle">tan θ_c, the tangent of the cone's half angle.</param>
internal readonly record struct ReflectionCone(float TanHalfAngle)
{
    /// <summary>ξ of Uludag 2014: the cone's lobe holds the share 1 − ξ of the reflected directions, before he halves
    /// its angle.</summary>
    public const double UludagShare = 0.244;

    /// <summary>tan θ_c over α: √((1 − ξ) / ξ), about 1.7602.</summary>
    public static readonly float TangentPerAlpha = (float)Math.Sqrt((1 - UludagShare) / UludagShare);

    /// <summary>The cone of a roughness r₀, floored as the BRDF floors it: tan θ_c = TangentPerAlpha · r².</summary>
    public static ReflectionCone OfRoughness(float roughness) =>
        new(TangentPerAlpha * (float)new GgxLobe(roughness).Alpha);

    /// <summary>
    /// The cone of a ray in a trace whose texels have the side s: tan θ_c is the larger of the lobe's spread and the
    /// chord f · s to the next texel's ray, so that the read covers the gap between neighboring rays.
    /// </summary>
    /// <param name="roughness">The surface's roughness r₀, before the BRDF's floor.</param>
    /// <param name="footprint">f, in rad: how far the pixel's reflection direction moves to the next pixel.</param>
    /// <param name="texelSide">s, in pixels: 1 for <see cref="TraceResolution.Full"/>, 2 for <see cref="TraceResolution.Half"/>.</param>
    public static ReflectionCone Of(float roughness, float footprint, int texelSide) =>
        new(MathF.Max(OfRoughness(roughness).TanHalfAngle, footprint * texelSide));

    /// <summary>log₂ of the cone's radius at the hit in pixels, clamped to 0 to topLevel.</summary>
    /// <param name="rayLength">The ray's length from its origin to the hit, in m.</param>
    /// <param name="hitDistance">The hit's distance from the eye, in m.</param>
    /// <param name="pixelAngle">The angle that one pixel covers at the center of the screen, in rad.</param>
    /// <param name="topLevel">The radiance pyramid's top level.</param>
    public float Level(float rayLength, float hitDistance, float pixelAngle, int topLevel)
    {
        float radiusPixels = rayLength * TanHalfAngle / (hitDistance * pixelAngle);
        // The floor keeps log₂ finite for a hit at the origin; any level below 0 reads level 0.
        return Math.Clamp(MathF.Log2(MathF.Max(radiusPixels, 1e-6f)), 0, topLevel);
    }
}
