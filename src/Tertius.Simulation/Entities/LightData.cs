using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>
/// A point light that an entity carries: it shines the same in every direction. It sits at the entity's pose composed with
/// <see cref="Offset"/>.
/// </summary>
/// <param name="Offset">The light's pose relative to the entity. A zeroed rotation, as in a defaulted pose, counts as the identity.</param>
/// <param name="Color">Linear color.</param>
/// <param name="Intensity">Brightness multiplier of <paramref name="Color"/>.</param>
/// <param name="Range">The distance at which the light falls to zero, in meters.</param>
public readonly record struct PointLightData(Pose Offset, Vector3 Color, float Intensity, float Range);

/// <summary>
/// A spot light that an entity carries: it shines in a cone along the local -Z axis of its pose, which is the entity's pose
/// composed with <see cref="Offset"/>.
/// </summary>
/// <param name="Offset">The light's pose relative to the entity. A zeroed rotation, as in a defaulted pose, counts as the identity.</param>
/// <param name="Color">Linear color.</param>
/// <param name="Intensity">Brightness multiplier of <paramref name="Color"/>.</param>
/// <param name="Range">The distance at which the light falls to zero, in meters.</param>
/// <param name="InnerConeAngle">Half-angle of the full-intensity cone, in radians.</param>
/// <param name="OuterConeAngle">Half-angle of the outer cone, in radians. The light fades to zero between the two angles.</param>
public readonly record struct SpotLightData(
    Pose Offset,
    Vector3 Color,
    float Intensity,
    float Range,
    float InnerConeAngle,
    float OuterConeAngle);

/// <summary>
/// The dynamic light that an entity carries: a <see cref="PointLightData"/> or a <see cref="SpotLightData"/>. The default
/// value holds no light, so an entity whose <see cref="RenderData.Light"/> is the default carries none and puts nothing in
/// the render snapshot. To switch a light off and on again, keep the light value and write the default over it.
/// </summary>
/// <remarks>
/// This is a custom C# 15 union. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler pattern-match
/// it without boxing. <see cref="Value"/> boxes, so use it only for debugging. A light reads only the fields of its own
/// case, so the fields of the case that it does not hold may hold anything and never matter. The two cases keep the
/// fields that they share (offset, color, intensity and range) in the same place. Equality needs the same case and the
/// same fields, and counts a NaN as equal to a NaN, as <see cref="Vector3.Equals(Vector3)"/> does.
/// </remarks>
[Union]
public readonly struct LightData : IEquatable<LightData>
{
    private const byte KindNone = 0, KindPoint = 1, KindSpot = 2;

    private readonly byte kind;

    /// <summary>The offset of both cases.</summary>
    private readonly Pose offset;

    private readonly Vector3 color;
    private readonly float intensity;
    private readonly float range;

    /// <summary>The inner cone angle of <see cref="SpotLightData"/>.</summary>
    private readonly float innerConeAngle;

    /// <summary>The outer cone angle of <see cref="SpotLightData"/>.</summary>
    private readonly float outerConeAngle;

    public LightData(PointLightData light)
    {
        kind = KindPoint;
        offset = light.Offset;
        color = light.Color;
        intensity = light.Intensity;
        range = light.Range;
    }

    public LightData(SpotLightData light)
    {
        kind = KindSpot;
        offset = light.Offset;
        color = light.Color;
        intensity = light.Intensity;
        range = light.Range;
        innerConeAngle = light.InnerConeAngle;
        outerConeAngle = light.OuterConeAngle;
    }

    /// <summary>False for the default value, which holds no light.</summary>
    public bool HasValue => kind != KindNone;

    public object? Value => kind switch
    {
        KindPoint => new PointLightData(offset, color, intensity, range),
        KindSpot => new SpotLightData(offset, color, intensity, range, innerConeAngle, outerConeAngle),
        _ => null
    };

    public static implicit operator LightData(PointLightData light) => new(light);
    public static implicit operator LightData(SpotLightData light) => new(light);

    public bool TryGetValue(out PointLightData value)
    {
        value = kind == KindPoint ? new PointLightData(offset, color, intensity, range) : default;
        return kind == KindPoint;
    }

    public bool TryGetValue(out SpotLightData value)
    {
        value = kind == KindSpot
            ? new SpotLightData(offset, color, intensity, range, innerConeAngle, outerConeAngle)
            : default;
        return kind == KindSpot;
    }

    // Equals on the fields, not the operator ==. The operator follows IEEE 754, so it calls a NaN unequal to itself, and a
    // light that holds a NaN would not equal itself. Equals treats a NaN as equal to a NaN, which agrees with GetHashCode.
    // The held case alone decides equality: the fields of the other case are not compared.
    public bool Equals(LightData other) => kind == other.kind && kind switch
    {
        KindPoint => offset.Equals(other.offset) && color.Equals(other.color) && intensity.Equals(other.intensity) &&
                     range.Equals(other.range),
        KindSpot => offset.Equals(other.offset) && color.Equals(other.color) && intensity.Equals(other.intensity) &&
                    range.Equals(other.range)
                    && innerConeAngle.Equals(other.innerConeAngle) && outerConeAngle.Equals(other.outerConeAngle),
        _ => true
    };

    public override bool Equals(object? obj) => obj is LightData other && Equals(other);

    public override int GetHashCode() => kind switch
    {
        KindPoint => HashCode.Combine(kind, offset, color, intensity, range),
        KindSpot => HashCode.Combine(kind, offset, color, intensity, range, innerConeAngle, outerConeAngle),
        _ => 0
    };

    public static bool operator ==(LightData a, LightData b) => a.Equals(b);
    public static bool operator !=(LightData a, LightData b) => !a.Equals(b);

    // A null arm and no discard arm: the compiler checks that every case has an arm, so a new case without one fails the build.
    public override string ToString() => this switch
    {
        PointLightData light => $"LightData({light})",
        SpotLightData light => $"LightData({light})",
        null => "LightData(none)"
    };
}
