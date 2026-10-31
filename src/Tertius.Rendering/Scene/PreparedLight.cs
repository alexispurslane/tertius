using System.Numerics;

namespace Tertius.Rendering;

/// <summary>A point light's spread: every direction.</summary>
internal readonly record struct EveryDirection;

/// <summary>
/// A spot light's spread: a cone around a unit axis. Build it with <see cref="Of"/>, which keeps its invariants:
/// <list type="bullet">
/// <item><see cref="OuterAngle"/> is in [LightUnits.MinConeAngle, π].</item>
/// <item>CosInner ≥ CosOuter + min(10^−4, (1 − CosOuter) / 2), so smoothstep's edges differ and the axis is inside the inner edge.</item>
/// </list>
/// </summary>
/// <param name="Axis">The unit axis of the cone, camera-relative, interpolated.</param>
/// <param name="CosInner">The cosine of the inner half-angle, inside which the light is full.</param>
/// <param name="CosOuter">The cosine of the outer half-angle, outside which the light adds nothing.</param>
/// <param name="OuterAngle">The outer half-angle, in rad.</param>
internal readonly record struct Cone(Vector3 Axis, float CosInner, float CosOuter, float OuterAngle)
{
    /// <summary>The widest fade between the two edges, in cosine: today's edge for a cone wider than about 0.014 rad.</summary>
    private const float WidestFade = 1e-4f;

    /// <summary>
    /// The cone of a spot light with the unit <paramref name="axis"/>, its half-angles clamped, and its inner edge moved
    /// inward where it lies too near the outer edge or beyond it (docs/plans/local-light-shadows.md, "Portable Vulkan").
    /// </summary>
    public static Cone Of(Vector3 axis, float innerAngle, float outerAngle)
    {
        Contract.Requires(MathF.Abs(axis.LengthSquared() - 1) < 1e-4f);
        float outer = Math.Clamp(outerAngle, LightUnits.MinConeAngle, MathF.PI);
        float cosOuter = MathF.Cos(outer);
        // The fade takes at most half of the cone's cosines, so the axis, at cosine 1, gets the whole intensity, which
        // SpotIntensity spreads over the whole cone.
        float fade = MathF.Min(WidestFade, (1 - cosOuter) / 2);
        float cosInner = MathF.Max(MathF.Cos(Math.Clamp(innerAngle, 0f, MathF.PI)), cosOuter + fade);
        return new(Axis: axis, CosInner: cosInner, CosOuter: cosOuter, OuterAngle: outer);
    }
}

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

    /// <summary>The color × intensity, in W/sr per channel, from <see cref="LightUnits.RadiantIntensity"/>.</summary>
    public Vector3 RadiantIntensity;

    /// <summary>The radius of the emitting sphere, in m.</summary>
    public float Radius;

    /// <summary>Where the brightest channel falls to the cutoff irradiance, in m.</summary>
    public float Reach;

    /// <summary>The distance from the eye, in m.</summary>
    public double Distance;

    public bool CastsShadow;

    /// <summary>
    /// Whether the light can reach the main view's frustum: a point light's reach sphere, or a spot light's cone cut at its
    /// reach (<see cref="CameraView.Culls(Vector3, in Cone, float)"/>). False for a light that only a drawn face of a map keeps:
    /// every shadow budget ranks such a light after the lights of the main view, and the main view's shaders and its
    /// impostors' light signatures leave it out (docs/plans/environment-map.md, decision 20).
    /// </summary>
    public bool InMainView;

    public LightSpread Spread;

    /// <summary>How the light casts shadows in this frame. The default holds none.</summary>
    public LightShadow Shadow;

    /// <summary>The light's map, <see cref="EnvironmentChain.MapOf"/> of its chain depth, or 0 with the map off.</summary>
    public int Map;

    /// <summary>
    /// Whether the light is the sun of the snapshot's atmosphere (<see cref="RenderAtmosphere.Sun"/>), whose light takes the
    /// air's tint (docs/plans/atmosphere.md, "The sun's tint").
    /// </summary>
    public bool AirSun;
}
