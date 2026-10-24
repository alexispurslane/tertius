using System.Numerics;

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
        return Double3.Distance(velocity, target) <= RoundingShare * (target.Length + 1);
    }

    /// <summary>
    /// The same expectation in another home, whose origin is at <paramref name="offset"/> from the old home's and whose
    /// frame velocity differs by <paramref name="velocity"/>, both in the new home: the place gains the offset, and both
    /// velocities gain the difference of the frame velocities, the float target in doubles and narrowed once, as the body's
    /// own velocity does when it moves.
    /// </summary>
    public ShadowTracking Moved(Double3 offset, Double3 velocity) =>
        new(Chord + velocity, Place + offset, (JoltTarget + velocity).ToVector3(), Step);
}

/// <summary>
/// A dynamic body's velocity shadow: <see cref="ShadowTracking"/>, or none. The default holds no case: the body has no
/// shadow, and the next update starts from Jolt's velocity. Only a dynamic body whose system's semantic frame is a turning
/// frame keeps one (docs/plans/spin-gravity.md, fork F8).
/// This is a generated union (docs/plans/union-tooling.md): the union generator writes its members.
/// </summary>
[GeneratedUnion(typeof(ShadowTracking))]
internal readonly partial struct VelocityShadow
{
    /// <summary>The shadow carried into another home, as <see cref="ShadowTracking.Moved"/> says, or none for none.</summary>
    public VelocityShadow Moved(Double3 offset, Double3 velocity) => this switch
    {
        // The first arm names the union, so that the null arm's default is the empty shadow, not a zero tracking.
        ShadowTracking tracking => new VelocityShadow(tracking.Moved(offset, velocity)),
        null => default
    };
}
