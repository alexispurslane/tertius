using System.Numerics;

namespace Tertius.Benchmarks;

/// <summary>The lights of the benchmark scenes, in SI units.</summary>
internal static class BenchmarkLights
{
    /// <summary>How far a star stands from the camera, in m: about 1 AU.</summary>
    private const double StarDistance = 1.5e11;

    /// <summary>
    /// A white star along <paramref name="direction"/> that casts shadows, whose power gives the irradiance
    /// <paramref name="irradiance"/> at the camera, and whose sphere covers 0.00467 rad there, as the Sun does from Earth.
    /// </summary>
    public static PointRenderLight Star(Vector3 direction, float irradiance)
    {
        Vector3 position = Vector3.Normalize(direction) * (float)StarDistance;
        return new(
            EntityIndex: 0,
            Position: position,
            PreviousPosition: position,
            Color: Vector3.One,
            Power: (float)(4 * Math.PI * StarDistance * StarDistance * irradiance),
            Radius: (float)(StarDistance * Math.Sin(0.00467)),
            CastsShadow: true
        );
    }

    /// <summary>A lamp of radius 0 that casts no shadow, of radiant intensity <paramref name="intensity"/> W/sr per unit of color.</summary>
    public static PointRenderLight Lamp(Vector3 position, Vector3 color, float intensity) =>
        new(
            EntityIndex: 0,
            Position: position,
            PreviousPosition: position,
            Color: color,
            Power: (float)(4 * Math.PI * intensity),
            Radius: 0,
            CastsShadow: false
        );

    /// <summary>
    /// A spot light of radius 0 that casts no shadow, of radiant intensity <paramref name="intensity"/> W/sr per unit of
    /// color inside its cone: its power is I · 2π (1 − cos θo).
    /// </summary>
    public static SpotRenderLight Spot(
        Vector3 position,
        Vector3 direction,
        Vector3 color,
        float intensity,
        float innerConeAngle,
        float outerConeAngle
    ) =>
        new(
            EntityIndex: 0,
            Position: position,
            PreviousPosition: position,
            Direction: direction,
            PreviousDirection: direction,
            Color: color,
            Power: (float)(intensity * 2 * Math.PI * (1 - Math.Cos(outerConeAngle))),
            Radius: 0,
            CastsShadow: false,
            InnerConeAngle: innerConeAngle,
            OuterConeAngle: outerConeAngle
        );
}
