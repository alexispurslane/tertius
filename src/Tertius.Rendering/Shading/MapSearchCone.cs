namespace Tertius.Rendering;

/// <summary>The accepted distance-cube search's lobe cone; the C# twin of specular_cone.glsl.</summary>
internal readonly record struct MapSearchCone(float TanHalfAngle)
{
    private static readonly float TangentPerAlpha = (float)Math.Sqrt((1 - 0.244) / 0.244);

    /// <summary>The cone of the floored roughness squared, alpha: its tangent is sqrt((1 - 0.244) / 0.244) times alpha.</summary>
    public static MapSearchCone OfAlpha(double alpha) => new(TangentPerAlpha * (float)alpha);

    /// <summary>Log2 of the cone's radius in texels, clamped to the distance cube's levels.</summary>
    /// <param name="rayLength">Distance along the ray, in m, or its swept-ray equivalent.</param>
    /// <param name="hitDistance">Distance from the eye, in m, in the same scale as rayLength.</param>
    /// <param name="pixelAngle">The angle of one level-zero texel at a face's center, in rad.</param>
    /// <param name="topLevel">The last level of the distance cube.</param>
    public float Level(float rayLength, float hitDistance, float pixelAngle, int topLevel)
    {
        float radiusTexels = rayLength * TanHalfAngle / (hitDistance * pixelAngle);
        return Math.Clamp(MathF.Log2(MathF.Max(radiusTexels, 1e-6f)), 0, topLevel);
    }
}
