using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Physics;

/// <summary>
/// What the two-turn update of a turning frame expects of a dynamic body at the end of the step that it wrote
/// (docs/plans/spin-gravity.md, "V6, the velocity shadow"). The next update starts from these doubles while Jolt's float
/// velocity agrees with them to rounding, so that the rounding of Jolt's floats does not pile up over many steps.
/// </summary>
/// <param name="Chord">The double chord that the update gave the body, relative to its home, in m/s, with Jolt's damping.</param>
/// <param name="Place">The double place of the body's center of mass at which the update expects it at the end of the step, in the home, in m.</param>
/// <param name="JoltTarget">
/// The float velocity that physics expects Jolt to hold at the end of the step, after the force and Jolt's damping,
/// computed with Jolt's own float operations. A Jolt velocity that differs from it by more than rounding means that a
/// contact changed it.
/// </param>
/// <param name="Step">The index of the step that wrote it. Only the update of the next step reads it.</param>
internal readonly record struct ShadowTracking(Double3 Chord, Double3 Place, Vector3 JoltTarget, long Step)
{
    /// <summary>The share of the speed, plus 1 m/s, by which Jolt's velocity may differ from <see cref="JoltTarget"/> by rounding alone: the spike's bound.</summary>
    private const double RoundingShare = 1e-6;

    /// <summary>
    /// Whether Jolt's velocity <paramref name="velocity"/> at the end of the step differs from <see cref="JoltTarget"/> by
    /// no more than rounding: at most 1e-6 times the sum of the target's length and 1 m/s.
    /// </summary>
    public bool Matches(Vector3 velocity)
    {
        var target = (Double3)JoltTarget;
        return Double3.Distance((Double3)velocity, target) <= RoundingShare * (target.Length + 1);
    }

    /// <summary>
    /// The same expectation in another home, whose origin is at <paramref name="offset"/> from the old home's and whose
    /// frame velocity differs by <paramref name="velocity"/>, both in the new home: the place gains the offset, and both
    /// velocities gain the difference of the frame velocities, the float target in doubles and narrowed once, as the body's
    /// own velocity does when it moves.
    /// </summary>
    public ShadowTracking Moved(Double3 offset, Double3 velocity) =>
        new(Chord + velocity, Place + offset, ((Double3)JoltTarget + velocity).ToVector3(), Step);
}

/// <summary>
/// A dynamic body's velocity shadow: <see cref="ShadowTracking"/>, or none. The default holds no case: the body has no
/// shadow, and the next update starts from Jolt's velocity. Only a dynamic body whose system's semantic frame is a turning
/// frame keeps one (docs/plans/spin-gravity.md, fork F8).
/// This is a custom C# 15 union. <see cref="HasValue"/> and <see cref="TryGetValue"/> let the compiler pattern-match it
/// without boxing. <see cref="Value"/> boxes, so use it only for debugging.
/// </summary>
[Union]
internal readonly struct VelocityShadow
{
    private readonly ShadowTracking tracking;
    private readonly bool hasValue;

    public VelocityShadow(ShadowTracking tracking)
    {
        this.tracking = tracking;
        hasValue = true;
    }

    public bool HasValue => hasValue;

    public bool TryGetValue(out ShadowTracking value)
    {
        value = tracking;
        return hasValue;
    }

    /// <summary>The case as an object. It boxes, so use it only for debugging.</summary>
    public object? Value => hasValue ? tracking : null;

    /// <summary>The shadow carried into another home, as <see cref="ShadowTracking.Moved"/> says, or none for none.</summary>
    public VelocityShadow Moved(Double3 offset, Double3 velocity) =>
        hasValue ? new VelocityShadow(tracking.Moved(offset, velocity)) : default;

    public override string ToString() => hasValue ? $"VelocityShadow({tracking})" : "VelocityShadow(none)";
}
