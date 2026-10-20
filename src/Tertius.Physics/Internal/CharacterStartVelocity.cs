using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Physics;

/// <summary>
/// The start of the update of a standing character that does not jump: the ground's velocity plus the walk, relative to
/// the character's home, in m/s. It takes no gravity and no rotation term, because the ground holds the character against
/// the whole acceleration of its frame (docs/plans/spin-gravity.md, "Characters").
/// </summary>
internal readonly record struct HeldStart(Vector3 Velocity);

/// <summary>
/// The start of the update of a character that jumps off its ground, or that the game gave a velocity since its last
/// update: its instant velocity where the step starts, relative to the instant velocity of its home there, in m/s. The
/// chord rule turns it into the chord of the step that ended, and the two-turn update carries that on.
/// </summary>
internal readonly record struct InstantStart(Double3 Velocity);

/// <summary>
/// The start of the update of a character that does not stand and that nothing gave a velocity: Jolt's chord of the step
/// that ended, relative to the character's home, in m/s, which the two-turn update carries on.
/// </summary>
internal readonly record struct ChordStart(Double3 Velocity);

/// <summary>
/// What the velocity of a walking character means at the start of its update, which decides its velocity rule
/// (docs/plans/spin-gravity.md, "Characters"): <see cref="HeldStart"/>, <see cref="InstantStart"/> or
/// <see cref="ChordStart"/>. The character's update builds one case after its standing test and reads it with a switch.
/// The default holds no case, which only a bug produces.
/// This is a custom C# 15 union. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler pattern-match it
/// without boxing. <see cref="Value"/> boxes, so use it only for debugging.
/// </summary>
[Union]
internal readonly struct CharacterStartVelocity
{
    private const byte KindNone = 0, KindHeld = 1, KindInstant = 2, KindChord = 3;

    // The three cases share it. The held case widens its float, which narrows back exactly.
    private readonly Double3 velocity;
    private readonly byte kind;

    public CharacterStartVelocity(HeldStart held) { kind = KindHeld; velocity = held.Velocity; }
    public CharacterStartVelocity(InstantStart instant) { kind = KindInstant; velocity = instant.Velocity; }
    public CharacterStartVelocity(ChordStart chord) { kind = KindChord; velocity = chord.Velocity; }

    public static implicit operator CharacterStartVelocity(HeldStart held) => new(held);
    public static implicit operator CharacterStartVelocity(InstantStart instant) => new(instant);
    public static implicit operator CharacterStartVelocity(ChordStart chord) => new(chord);

    public bool HasValue => kind != KindNone;

    public bool TryGetValue(out HeldStart held)
    {
        held = new HeldStart(velocity.ToVector3());
        return kind == KindHeld;
    }

    public bool TryGetValue(out InstantStart instant)
    {
        instant = new InstantStart(velocity);
        return kind == KindInstant;
    }

    public bool TryGetValue(out ChordStart chord)
    {
        chord = new ChordStart(velocity);
        return kind == KindChord;
    }

    /// <summary>The case as an object. It boxes, so use it only for debugging.</summary>
    public object? Value => kind switch
    {
        KindHeld => new HeldStart(velocity.ToVector3()),
        KindInstant => new InstantStart(velocity),
        KindChord => new ChordStart(velocity),
        _ => null,
    };

    public override string ToString() => Value is { } value ? $"CharacterStartVelocity({value})" : "CharacterStartVelocity(none)";
}
