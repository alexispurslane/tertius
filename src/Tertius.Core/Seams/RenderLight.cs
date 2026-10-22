using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>
/// A point light of the snapshot: it shines the same in every direction. Its position follows the same rules as the
/// position of a <see cref="RenderInstance"/>.
/// </summary>
/// <param name="Position">The place of the light, relative to the render origin, in the axes of the camera's frame, in meters.</param>
/// <param name="Color">Linear RGB.</param>
/// <param name="Intensity">
/// Luminous intensity: irradiance at distance d is <paramref name="Color"/> · <paramref name="Intensity"/> / d², with a
/// smooth window to zero at <paramref name="Range"/>. The renderer skips a light of zero or less.
/// </param>
/// <param name="Range">The distance at which the light falls to zero, in meters. The renderer skips a light of zero or less.</param>
public readonly record struct PointRenderLight(Vector3 Position, Vector3 Color, float Intensity, float Range);

/// <summary>
/// A spot light of the snapshot: it shines in a cone around one direction. Its position and direction follow the same
/// rules as the position of a <see cref="RenderInstance"/>.
/// </summary>
/// <param name="Position">The place of the light, relative to the render origin, in the axes of the camera's frame, in meters.</param>
/// <param name="Direction">
/// The axis of the cone, in the axes of the camera's frame. It need not be normalized. The renderer shines straight down
/// (-Y) when it is zero.
/// </param>
/// <param name="Color">Linear RGB.</param>
/// <param name="Intensity">As for <see cref="PointRenderLight"/>.</param>
/// <param name="Range">As for <see cref="PointRenderLight"/>.</param>
/// <param name="InnerConeAngle">Half-angle of the full-intensity cone, in radians.</param>
/// <param name="OuterConeAngle">Half-angle of the outer edge of the cone, in radians. The light fades to zero between the two angles.</param>
public readonly record struct SpotRenderLight(
    Vector3 Position,
    Vector3 Direction,
    Vector3 Color,
    float Intensity,
    float Range,
    float InnerConeAngle,
    float OuterConeAngle);

/// <summary>
/// One dynamic light of a <see cref="RenderSnapshot"/>: a <see cref="PointRenderLight"/> or a
/// <see cref="SpotRenderLight"/>.
/// The default value holds no light. The renderer skips it.
/// </summary>
/// <remarks>
/// This is a custom C# 15 union. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler pattern-match
/// it without boxing. <see cref="Value"/> boxes, so use it only for debugging. A light reads only the fields of its own
/// case, so the fields of the case that it does not hold may hold anything and never matter. The two cases keep the
/// fields that they share (position, color, intensity and range) in the same place. It takes 56 bytes, one spot light and
/// its kind byte, as the old struct did.
/// Equality needs the same case and the same fields, and counts a NaN as equal to a NaN, as
/// <see cref="Vector3.Equals(Vector3)"/> does.
/// </remarks>
[Union]
public readonly struct RenderLight : IEquatable<RenderLight>
{
    private const byte KindNone = 0, KindPoint = 1, KindSpot = 2;

    private readonly byte kind;

    /// <summary>The position of both cases.</summary>
    private readonly Vector3 position;

    /// <summary>The direction of <see cref="SpotRenderLight"/>.</summary>
    private readonly Vector3 direction;

    private readonly Vector3 color;
    private readonly float intensity;
    private readonly float range;

    /// <summary>The inner cone angle of <see cref="SpotRenderLight"/>.</summary>
    private readonly float innerConeAngle;

    /// <summary>The outer cone angle of <see cref="SpotRenderLight"/>.</summary>
    private readonly float outerConeAngle;

    public RenderLight(PointRenderLight light)
    {
        kind = KindPoint;
        position = light.Position;
        color = light.Color;
        intensity = light.Intensity;
        range = light.Range;
    }

    public RenderLight(SpotRenderLight light)
    {
        kind = KindSpot;
        position = light.Position;
        direction = light.Direction;
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
        KindPoint => new PointRenderLight(position, color, intensity, range),
        KindSpot => new SpotRenderLight(position, direction, color, intensity, range, innerConeAngle, outerConeAngle),
        _ => null
    };

    public static implicit operator RenderLight(PointRenderLight light) => new(light);
    public static implicit operator RenderLight(SpotRenderLight light) => new(light);

    public bool TryGetValue(out PointRenderLight value)
    {
        value = kind == KindPoint ? new PointRenderLight(position, color, intensity, range) : default;
        return kind == KindPoint;
    }

    public bool TryGetValue(out SpotRenderLight value)
    {
        value = kind == KindSpot
            ? new SpotRenderLight(position, direction, color, intensity, range, innerConeAngle, outerConeAngle)
            : default;
        return kind == KindSpot;
    }

    // Equals on the fields, not the operator ==. The operator follows IEEE 754, so it calls a NaN unequal to itself, and a
    // light that holds a NaN would not equal itself. Equals treats a NaN as equal to a NaN, which agrees with GetHashCode.
    // The held case alone decides equality: the fields of the other case are not compared.
    public bool Equals(RenderLight other) => kind == other.kind && kind switch
    {
        KindPoint => position.Equals(other.position) && color.Equals(other.color) &&
                     intensity.Equals(other.intensity) && range.Equals(other.range),
        KindSpot => position.Equals(other.position) && direction.Equals(other.direction) && color.Equals(other.color)
                    && intensity.Equals(other.intensity) && range.Equals(other.range)
                    && innerConeAngle.Equals(other.innerConeAngle) && outerConeAngle.Equals(other.outerConeAngle),
        _ => true
    };

    public override bool Equals(object? obj) => obj is RenderLight other && Equals(other);

    public override int GetHashCode() => kind switch
    {
        KindPoint => HashCode.Combine(kind, position, color, intensity, range),
        KindSpot => HashCode.Combine(kind, position, direction, color, intensity, range, innerConeAngle,
            outerConeAngle),
        _ => 0
    };

    public static bool operator ==(RenderLight a, RenderLight b) => a.Equals(b);
    public static bool operator !=(RenderLight a, RenderLight b) => !a.Equals(b);

    // A null arm and no discard arm: the compiler checks that every case has an arm, so a new case without one fails the build.
    public override string ToString() => this switch
    {
        PointRenderLight light => $"RenderLight({light})",
        SpotRenderLight light => $"RenderLight({light})",
        null => "RenderLight(none)"
    };
}
