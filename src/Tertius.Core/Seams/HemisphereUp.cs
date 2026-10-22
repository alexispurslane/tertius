using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>Up is one direction, as in a uniform gravity field.</summary>
/// <param name="Direction">The up direction: a unit vector in the axes of the camera's frame.</param>
public readonly record struct UpAlong(Vector3 Direction);

/// <summary>Up leads away from one point, as in the gravity field of a planet.</summary>
/// <param name="Center">
/// The point that up leads away from, relative to the instance's position, in the axes of the camera's frame. It moves
/// with the instance, so a planet's own center is exactly zero.
/// </param>
public readonly record struct UpAwayFrom(Vector3 Center);

/// <summary>
/// Which way is up for the hemisphere ambient light of one instance: the sky color lights a surface that faces up, the
/// ground color one that faces down, and a surface that faces sideways gets their mean. It is <see cref="UpAlong"/>,
/// <see cref="UpAwayFrom"/> or neither. The empty value is <see cref="Even"/>: no way is up, so every surface gets the
/// mean of the sky and the ground color, as in a frame without gravity.
/// This is a custom C# 15 union. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler pattern-match
/// it without boxing. <see cref="Value"/> boxes, so use it only for debugging. The empty union is the all-zero default.
/// It is 16 bytes: both cases keep their one vector in the same place, because a value holds only one case, and every
/// render instance carries one.
/// </summary>
[Union]
public readonly struct HemisphereUp : IEquatable<HemisphereUp>
{
    private const byte KindEven = 0, KindAlong = 1, KindAwayFrom = 2;

    /// <summary>No way is up.</summary>
    public static HemisphereUp Even => default;

    private readonly Vector3 vector;
    private readonly byte kind;

    public HemisphereUp(UpAlong along)
    {
        kind = KindAlong;
        vector = along.Direction;
    }

    public HemisphereUp(UpAwayFrom away)
    {
        kind = KindAwayFrom;
        vector = away.Center;
    }

    /// <summary>False for <see cref="Even"/>.</summary>
    public bool HasValue => kind != KindEven;

    public object? Value => kind switch
    {
        KindAlong => new UpAlong(vector),
        KindAwayFrom => new UpAwayFrom(vector),
        _ => null
    };

    /// <summary>Up is <paramref name="direction"/>, a unit vector in the axes of the camera's frame.</summary>
    public static HemisphereUp Along(Vector3 direction) => new(new UpAlong(direction));

    /// <summary>Up leads away from <paramref name="center"/>, relative to the instance's position.</summary>
    public static HemisphereUp AwayFrom(Vector3 center) => new(new UpAwayFrom(center));

    public bool TryGetValue(out UpAlong value)
    {
        value = kind == KindAlong ? new UpAlong(vector) : default;
        return kind == KindAlong;
    }

    public bool TryGetValue(out UpAwayFrom value)
    {
        value = kind == KindAwayFrom ? new UpAwayFrom(vector) : default;
        return kind == KindAwayFrom;
    }

    // Vector3.Equals, not the operator ==. The operator follows IEEE 754, so it calls a NaN unequal to itself, and an up that
    // holds a NaN would not equal itself. Equals treats a NaN as equal to a NaN, which agrees with GetHashCode.
    public bool Equals(HemisphereUp other) => kind == other.kind && vector.Equals(other.vector);
    public override bool Equals(object? obj) => obj is HemisphereUp other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, vector);
    public static bool operator ==(HemisphereUp a, HemisphereUp b) => a.Equals(b);
    public static bool operator !=(HemisphereUp a, HemisphereUp b) => !a.Equals(b);

    public override string ToString() => this switch
    {
        UpAlong a => $"HemisphereUp({a})",
        UpAwayFrom a => $"HemisphereUp({a})",
        _ => "HemisphereUp(even)"
    };
}
