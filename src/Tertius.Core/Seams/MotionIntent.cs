using System.Runtime.CompilerServices;
using Tertius.Frames.Rails;

namespace Tertius;

/// <summary>
/// The pose of an entity at the end of the next step, with nothing about the motion on the way: the entity moves to it at
/// a constant velocity from where the step starts (docs/plans/rails-components.md, "MoveKinematic as an end pose").
/// </summary>
/// <param name="Pose">The pose at the end of the step, relative to the entity's frame, in its axes.</param>
public readonly record struct EndPose(DPose Pose);

/// <summary>
/// A pose that physics steers a dynamic body toward over the next step: it gives the body the velocities that carry it
/// there, and contacts and gravity still act on it.
/// </summary>
/// <param name="Pose">The pose to steer toward, relative to the entity's frame, in its axes.</param>
public readonly record struct SteerTarget(DPose Pose);

/// <summary>
/// How an entity moves in one step, which a component of the entity writes for the next step: an <see cref="EndPose"/>, a
/// full <see cref="MotionSample"/>, or a <see cref="SteerTarget"/> (docs/plans/rails-components.md, "The motion intent").
/// An end pose and a full sample are the motion itself, for a kinematic body or an entity without a body. A steer target is
/// an input that physics integrates for a dynamic body. The empty union is the all-zero default: the entity has no motion
/// intent in the step.
/// This is a custom C# 15 union. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler pattern-match
/// it without boxing. <see cref="Value"/> boxes, so use it only for debugging.
/// </summary>
[Union]
public readonly struct MotionIntent : IEquatable<MotionIntent>
{
    private const byte KindNone = 0, KindEndPose = 1, KindSample = 2, KindSteer = 3;

    // The three cases share one field. An end pose and a steer target fill only its pose, and leave every other field zero,
    // so equality needs to compare only the kind and this field.
    private readonly MotionSample sample;
    private readonly byte kind;

    public MotionIntent(EndPose end) { kind = KindEndPose; sample = default(MotionSample) with { Pose = end.Pose }; }
    public MotionIntent(MotionSample sample) { kind = KindSample; this.sample = sample; }
    public MotionIntent(SteerTarget steer) { kind = KindSteer; sample = default(MotionSample) with { Pose = steer.Pose }; }

    /// <summary>False for the empty union: no motion intent.</summary>
    public bool HasValue => kind != KindNone;

    public bool TryGetValue(out EndPose end)
    {
        end = new EndPose(sample.Pose);
        return kind == KindEndPose;
    }

    public bool TryGetValue(out MotionSample value)
    {
        value = sample;
        return kind == KindSample;
    }

    public bool TryGetValue(out SteerTarget steer)
    {
        steer = new SteerTarget(sample.Pose);
        return kind == KindSteer;
    }

    public object? Value => kind switch
    {
        KindEndPose => new EndPose(sample.Pose),
        KindSample => sample,
        KindSteer => new SteerTarget(sample.Pose),
        _ => null,
    };

    /// <summary>
    /// The reason that an entity refuses this motion intent in the step that applies it, or null when it takes it, by the
    /// rules of the guard (docs/plans/rails-components.md, "The flow per kind of entity"), in this order:
    /// <list type="number">
    /// <item>An attached entity refuses every case: its pose follows its parent.</item>
    /// <item>A character refuses every case: a walk, a jump, forces and impulses move it.</item>
    /// <item>An end pose or a full sample needs a kinematic body, no body, or the owner of a root frame, whose body is only
    /// the frame's static interior, so that physics does not move it.</item>
    /// <item>A steer target needs a dynamic body. The owner of a root frame refuses it whatever its body, because that body is
    /// only the frame's static interior.</item>
    /// </list>
    /// No motion intent has no reason to be refused. The function is pure, so that a writer, the drops of step 4 and the
    /// check of a spawn judge each case by the same rules.
    /// </summary>
    /// <param name="motion">The motion of the entity's body in the step that applies the intent, or null without a body.</param>
    /// <param name="attached">True when the entity is attached to a parent.</param>
    /// <param name="ownsRootFrame">True when the entity owns a root frame.</param>
    internal string? ProblemFor(BodyMotion? motion, bool attached, bool ownsRootFrame)
    {
        if (!HasValue)
            return null;
        string name = kind switch { KindEndPose => "an end pose", KindSample => "a full sample", _ => "a steer target" };
        if (attached)
            return $"is attached, so its pose follows its parent, and it takes no motion intent, not {name}";
        if (motion == BodyMotion.Character)
            return $"is a character, which a walk, a jump, forces and impulses move, so it takes no motion intent, not {name}";
        if (kind != KindSteer)
            return motion is null or BodyMotion.Kinematic || ownsRootFrame
                ? null
                : $"has a {motion} body, so it cannot take {name}: only a kinematic body, an entity without a body and the owner of a root frame take one";
        if (ownsRootFrame)
            return "owns a root frame, whose body is only the frame's static interior, so it has nothing to steer with a steer target";
        return motion switch
        {
            BodyMotion.Dynamic => null,
            null => "has no body, so it has nothing to steer with a steer target",
            _ => $"has a {motion} body, so it cannot take a steer target: only a dynamic body takes one",
        };
    }

    public bool Equals(MotionIntent other) => kind == other.kind && sample.Equals(other.sample);
    public override bool Equals(object? obj) => obj is MotionIntent other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, sample);
    public static bool operator ==(MotionIntent a, MotionIntent b) => a.Equals(b);
    public static bool operator !=(MotionIntent a, MotionIntent b) => !a.Equals(b);

    public override string ToString() => this switch
    {
        EndPose end => $"MotionIntent({end})",
        MotionSample full => $"MotionIntent({full})",
        SteerTarget steer => $"MotionIntent({steer})",
        _ => "MotionIntent(none)",
    };
}
