using System.Numerics;

namespace Tertius.Rendering;

/// <summary>A point light's spread: every direction.</summary>
internal readonly record struct EveryDirection;

/// <summary>A spot light's spread: a cone around the unit <paramref name="Axis"/>, CosInner ≥ CosOuter + 1e-4.</summary>
/// <param name="Axis">The unit axis of the cone, camera-relative, interpolated.</param>
/// <param name="CosInner">The cosine of the inner half-angle, inside which the light is full.</param>
/// <param name="CosOuter">The cosine of the outer half-angle, outside which the light adds nothing.</param>
/// <param name="OuterAngle">The outer half-angle, in rad.</param>
internal readonly record struct Cone(Vector3 Axis, float CosInner, float CosOuter, float OuterAngle);

/// <summary>
/// Which directions a light sends its power to: <see cref="EveryDirection"/> for a point light, a <see cref="Cone"/> for a
/// spot light. The default value is EveryDirection. This is a generated union (docs/plans/union-tooling.md).
/// </summary>
[GeneratedUnion(typeof(EveryDirection), typeof(Cone), ZeroCase = typeof(EveryDirection))]
internal readonly partial struct LightSpread;

/// <summary>One light of a frame after interpolation and culling, before its GPU form: the analogue of PreparedInstance.</summary>
internal struct PreparedLight
{
    public uint EntityIndex;

    /// <summary>Camera-relative position, interpolated.</summary>
    public Vector3 Position;

    public Vector3 Color;

    /// <summary>W/sr.</summary>
    public float Intensity;

    /// <summary>The radius of the emitting sphere, in m.</summary>
    public float Radius;

    /// <summary>Where the brightest channel falls to the cutoff irradiance, in m.</summary>
    public float Reach;

    /// <summary>The distance from the eye, in m.</summary>
    public double Distance;

    public bool CastsShadow;
    public LightSpread Spread;

    /// <summary>How the light casts shadows in this frame. The default holds none.</summary>
    public LightShadow Shadow;
}
