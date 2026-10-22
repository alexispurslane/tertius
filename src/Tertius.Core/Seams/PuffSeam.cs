using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>Puffs that glow: hot exhaust. A glow puff emits its own light and takes none from the sun or the lamps.</summary>
/// <param name="Radiance">
/// The light that a puff emits at its center at its birth, in linear RGB, in the HDR units of
/// <see cref="MaterialDesc.Emissive"/>. The demo's lamp panels emit (6, 5.4, 4.5). The light fades with the puff's own share.
/// </param>
public readonly record struct GlowPuffs(Vector3 Radiance);

/// <summary>Puffs that the sun and the lamps light: cold gas. A lit puff is lit once, at its center.</summary>
/// <param name="Albedo">
/// The share of the light that reaches a puff that it scatters, in linear RGB, from 0 to 1 per channel for a physical puff.
/// </param>
public readonly record struct LitPuffs(Vector3 Albedo);

/// <summary>
/// How the puffs of one look show: <see cref="GlowPuffs"/>, <see cref="LitPuffs"/> or neither. The empty value is the
/// all-zero default, and it holds no case. A look with the empty shading is not valid, so its emitter makes no puffs.
/// </summary>
/// <remarks>
/// This is a custom C# 15 union. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler pattern-match
/// it without boxing. <see cref="Value"/> boxes, so use it only for debugging. Both cases keep their one color in the same
/// place, because a value holds only one case, so the union is 16 bytes. Equality needs the same case and the same color,
/// and counts a NaN as equal to a NaN, as <see cref="Vector3.Equals(Vector3)"/> does.
/// </remarks>
[Union]
public readonly struct PuffShading : IEquatable<PuffShading>
{
    private const byte KindNone = 0, KindGlow = 1, KindLit = 2;

    /// <summary>The radiance of <see cref="GlowPuffs"/> or the albedo of <see cref="LitPuffs"/>.</summary>
    private readonly Vector3 color;

    private readonly byte kind;

    public PuffShading(GlowPuffs glow)
    {
        kind = KindGlow;
        color = glow.Radiance;
    }

    public PuffShading(LitPuffs lit)
    {
        kind = KindLit;
        color = lit.Albedo;
    }

    /// <summary>False for the default value, which holds no case.</summary>
    public bool HasValue => kind != KindNone;

    public object? Value => kind switch
    {
        KindGlow => new GlowPuffs(color),
        KindLit => new LitPuffs(color),
        _ => null
    };

    /// <summary>
    /// Whether the shading holds a case, and every channel of its color is finite and 0 or more. An emitter whose look has
    /// a shading that is not valid makes no puffs.
    /// </summary>
    public bool IsValid => kind != KindNone && IsChannel(color.X) && IsChannel(color.Y) && IsChannel(color.Z);

    /// <summary>Glow puffs that emit <paramref name="radiance"/> at their birth, in linear RGB, in HDR units.</summary>
    public static PuffShading Glow(Vector3 radiance) => new(new GlowPuffs(radiance));

    /// <summary>Lit puffs that scatter the share <paramref name="albedo"/> of the light that reaches them, in linear RGB.</summary>
    public static PuffShading Lit(Vector3 albedo) => new(new LitPuffs(albedo));

    public bool TryGetValue(out GlowPuffs value)
    {
        value = kind == KindGlow ? new GlowPuffs(color) : default;
        return kind == KindGlow;
    }

    public bool TryGetValue(out LitPuffs value)
    {
        value = kind == KindLit ? new LitPuffs(color) : default;
        return kind == KindLit;
    }

    /// <summary>Whether a channel is finite and 0 or more. A NaN fails the comparison, and the finite check leaves out an infinity.</summary>
    private static bool IsChannel(float channel) => channel >= 0 && float.IsFinite(channel);

    // Vector3.Equals, not the operator ==. The operator follows IEEE 754, so it calls a NaN unequal to itself, and a shading
    // that holds a NaN would not equal itself. Equals treats a NaN as equal to a NaN, which agrees with GetHashCode.
    public bool Equals(PuffShading other) => kind == other.kind && color.Equals(other.color);
    public override bool Equals(object? obj) => obj is PuffShading other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, color);
    public static bool operator ==(PuffShading a, PuffShading b) => a.Equals(b);
    public static bool operator !=(PuffShading a, PuffShading b) => !a.Equals(b);

    // A null arm and no discard arm: the compiler checks that every case has an arm, so a new case without one fails the build.
    public override string ToString() => this switch
    {
        GlowPuffs glow => $"PuffShading({glow})",
        LitPuffs lit => $"PuffShading({lit})",
        null => "PuffShading(none)"
    };
}

/// <summary>
/// What the puffs of one emitter share: their life, their two radii, their temperature ratio and their shading. The
/// simulation's look table and the render snapshot hold it with the same fields, so one type serves both. It is 32 bytes.
/// </summary>
/// <param name="Life">How long each puff lives, in s, above 0. A puff dies when its age reaches its life.</param>
/// <param name="StartRadius">A puff's radius at its birth, in m, 0 or more.</param>
/// <param name="EndRadius">
/// A puff's radius at the end of its life, in m, 0 or more. The radius grows linearly with the age between the two.
/// </param>
/// <param name="TemperatureRatio">
/// The temperature of the puff's own gas over the medium's temperature, a pure number above 0: 3 for hot exhaust, 0.75
/// for cold gas. Where the frame's medium fills a puff's place, its lift share is 1 + w (θ − 1), with w its own share and
/// θ this ratio. A puff where no medium fills its place does not read it.
/// </param>
/// <param name="Shading">How the puffs show. It must hold a case for the look to be valid.</param>
public readonly record struct PuffLook(
    float Life,
    float StartRadius,
    float EndRadius,
    float TemperatureRatio,
    PuffShading Shading)
{
    /// <summary>
    /// Whether every number is finite, <see cref="Life"/> and <see cref="TemperatureRatio"/> are above 0, both radii are 0 or
    /// more, and <see cref="Shading"/> is valid. An emitter whose look is not valid makes no puffs, without an error, as a
    /// light whose range or intensity is zero or less is left out.
    /// </summary>
    public bool IsValid => IsPositive(Life) && IsPositive(TemperatureRatio) && IsRadius(StartRadius) &&
                           IsRadius(EndRadius) && Shading.IsValid;

    // A NaN fails each comparison, and the finite check leaves out an infinity.
    private static bool IsPositive(float value) => value > 0 && float.IsFinite(value);
    private static bool IsRadius(float value) => value >= 0 && float.IsFinite(value);
}

/// <summary>
/// One live puff in the render snapshot. The renderer draws it as a camera-facing sprite. It is 32 bytes. Its age at the
/// previous build is <see cref="Age"/> less <see cref="RenderSnapshot.PuffAgeStep"/>, which is below 0 for a newborn.
/// </summary>
public struct RenderPuff
{
    /// <summary>The puff's place relative to the render origin, in the axes of the camera's frame, in m.</summary>
    public Vector3 Position;

    /// <summary>
    /// The puff's place at the previous build, relative to the same origin, in m. It equals <see cref="Position"/> when the
    /// build gives no motion.
    /// </summary>
    public Vector3 PreviousPosition;

    /// <summary>The puff's age at the snapshot's tick, in s.</summary>
    public float Age;

    /// <summary>The index of the puff's look in <see cref="RenderSnapshot.PuffLooks"/>.</summary>
    public int Look;
}
