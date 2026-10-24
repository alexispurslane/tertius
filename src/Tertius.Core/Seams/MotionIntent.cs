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
/// The union generator writes its union members (docs/plans/union-tooling.md). The three cases share the slot of their
/// pose, and only a full sample fills the five slots of its velocities and accelerations.
/// </summary>
[GeneratedUnion(typeof(EndPose), typeof(MotionSample), typeof(SteerTarget))]
public readonly partial struct MotionIntent
{
    /// <summary>
    /// The pose that physics aims the entity at, which every case gives. For an end pose and a full sample, it is the pose at
    /// the end of the step, where physics or the sync point puts the entity exactly. For a steer target, it is the pose that
    /// physics steers a dynamic body toward, which contacts and gravity can keep the body from. The empty union gives the
    /// default pose.
    /// </summary>
    public DPose AimPose => this switch
    {
        EndPose(var pose) => pose,
        MotionSample full => full.Pose,
        SteerTarget(var pose) => pose,
        null => default
    };

    /// <summary>
    /// How a frame follows when it takes the record of this intent (docs/plans/rails-components.md, "The follow kind"):
    /// <see cref="FollowKind.Rails"/> for a full sample, <see cref="FollowKind.Kinematic"/> for an end pose, and
    /// <see cref="FollowKind.Static"/> for a steer target and the empty union, which give no record (see
    /// <see cref="FrameTree.FollowKindInStep"/>).
    /// </summary>
    internal FollowKind FollowKind => this switch
    {
        MotionSample => FollowKind.Rails,
        EndPose => FollowKind.Kinematic,
        SteerTarget => FollowKind.Static,
        null => FollowKind.Static
    };

    /// <summary>
    /// The step record of this intent, the entity's motion over a step that starts at <paramref name="stepStart"/> and lasts
    /// <paramref name="step"/> seconds, both in the intent's frame. A full sample is its own record. An end pose gives the
    /// motion at a constant velocity from <paramref name="stepStart"/> to the end pose, as Jolt moves a kinematic body to
    /// its target (docs/plans/rails-components.md, "MoveKinematic as an end pose"): the chord is also the instant velocity,
    /// the angular velocity turns the start's rotation into the end's, and both accelerations are zero. A steer target gives
    /// no record, because physics measures the motion of a dynamic body, and neither does the empty union: both return false.
    /// </summary>
    public bool TryRecordFrom(in DPose stepStart, double step, out MotionSample record)
    {
        (bool recorded, record) = this switch
        {
            MotionSample sample => (true, sample),
            EndPose(var end) => (true, KinematicRecord(stepStart, end, step)),
            SteerTarget => (false, default),
            null => (false, default)
        };
        return recorded;
    }

    /// <summary>
    /// The record of a move at a constant velocity from <paramref name="stepStart"/> to <paramref name="end"/> over a step
    /// of <paramref name="step"/> seconds (see <see cref="TryRecordFrom"/>).
    /// </summary>
    private static MotionSample KinematicRecord(in DPose stepStart, DPose end, double step)
    {
        Double3 chord = (end.Position - stepStart.Position) / step;
        Double3 spin = stepStart.Rotation.AngularVelocityTo(end.Rotation, step);
        return new(end, chord, chord, Double3.Zero, spin, Double3.Zero);
    }

    /// <summary>
    /// This intent in another frame, which the two relations call the parent: <paramref name="start"/> and
    /// <paramref name="end"/>, the motion of the intent's frame relative to it where the step of <paramref name="step"/>
    /// seconds that applies the intent starts and where it ends (see <see cref="FrameTree.MotionRelativeInStep"/>). Each
    /// part converts by the moment that it describes (docs/plans/spin-gravity.md, "V7, the relative motion of two frames").
    /// An end pose and a steer target are poses at the end of the step, so they convert by the pose of
    /// <paramref name="end"/> and keep their case, and the frames' motion gives them no velocity. A full sample converts by
    /// <see cref="MotionSample.Converted"/>. The empty union stays empty.
    /// </summary>
    internal MotionIntent Converted(in FrameMotion start, in FrameMotion end, double step) => this switch
    {
        EndPose(var pose) => new(new EndPose(end.PoseToParent(pose))),
        MotionSample sample => new(sample.Converted(start, end, step)),
        SteerTarget(var pose) => new(new SteerTarget(end.PoseToParent(pose))),
        null => this
    };

    /// <summary>
    /// Which rule of the guard refuses this motion intent for an entity in the step that applies it, or
    /// <see cref="MotionIntentRefusal.None"/> when the entity takes it (docs/plans/rails-components.md, "The flow per kind of
    /// entity"). The rules, in this order:
    /// <list type="number">
    /// <item>An attached entity refuses every case: its pose follows its parent.</item>
    /// <item>A character refuses every case: a walk, a jump, forces and impulses move it.</item>
    /// <item>An end pose or a full sample needs a kinematic body, no body, or the owner of a root frame, whose body is only
    /// the frame's static interior, so that physics does not move it.</item>
    /// <item>A steer target needs a dynamic body. The owner of a root frame refuses it whatever its body, because that body is
    /// only the frame's static interior.</item>
    /// </list>
    /// No motion intent is never refused. The function is pure and allocates nothing, so that a writer, the drops of step 4,
    /// the check of a spawn and a rails component in every tick judge each case by the same rules. <see cref="ProblemFor"/>
    /// words the refusal.
    /// </summary>
    /// <param name="motion">The motion of the entity's body in the step that applies the intent, or null without a body.</param>
    /// <param name="attached">True when the entity is attached to a parent.</param>
    /// <param name="ownsRootFrame">True when the entity owns a root frame.</param>
    internal MotionIntentRefusal RefusalFor(BodyMotion? motion, bool attached, bool ownsRootFrame)
    {
        return !HasValue
            ? MotionIntentRefusal.None
            : attached
                ? MotionIntentRefusal.Attached
                : motion == BodyMotion.Character
                    ? MotionIntentRefusal.Character
                    : this is not SteerTarget
                        ? motion is null or BodyMotion.Kinematic || ownsRootFrame
                            ? MotionIntentRefusal.None
                            : MotionIntentRefusal.NeedsKinematicBody
                        : ownsRootFrame
                            ? MotionIntentRefusal.SteerOfRootFrame
                            : motion switch
                            {
                                BodyMotion.Dynamic => MotionIntentRefusal.None,
                                null => MotionIntentRefusal.SteerWithoutBody,
                                BodyMotion.Static or BodyMotion.Kinematic or BodyMotion.Character =>
                                    MotionIntentRefusal.NeedsDynamicBody
                            };
    }

    /// <summary>
    /// The reason that an entity refuses this motion intent in the step that applies it, or null when it takes it: the
    /// rule of <see cref="RefusalFor"/>, in words. It builds a string for a refusal, so a reader that asks in every tick asks
    /// <see cref="RefusalFor"/> and words only the refusal that it reports.
    /// </summary>
    /// <param name="motion">The motion of the entity's body in the step that applies the intent, or null without a body.</param>
    /// <param name="attached">True when the entity is attached to a parent.</param>
    /// <param name="ownsRootFrame">True when the entity owns a root frame.</param>
    internal string? ProblemFor(BodyMotion? motion, bool attached, bool ownsRootFrame) =>
        Reason(RefusalFor(motion, attached, ownsRootFrame), motion);

    /// <summary>The words of <paramref name="refusal"/> of this motion intent for a body of <paramref name="motion"/>, or null for none.</summary>
    private string? Reason(MotionIntentRefusal refusal, BodyMotion? motion)
    {
        string name = this switch
        {
            EndPose => "an end pose",
            MotionSample => "a full sample",
            SteerTarget => "a steer target",
            null => "no motion intent"
        };
        return refusal switch
        {
            MotionIntentRefusal.None => null,
            MotionIntentRefusal.Attached =>
                $"is attached, so its pose follows its parent, and it takes no motion intent, not {name}",
            MotionIntentRefusal.Character =>
                $"is a character, which a walk, a jump, forces and impulses move, so it takes no motion intent, not {name}",
            MotionIntentRefusal.NeedsKinematicBody =>
                $"has a {motion} body, so it cannot take {name}: only a kinematic body, an entity without a body and the owner of a root frame take one",
            MotionIntentRefusal.SteerOfRootFrame =>
                "owns a root frame, whose body is only the frame's static interior, so it has nothing to steer with a steer target",
            MotionIntentRefusal.SteerWithoutBody => "has no body, so it has nothing to steer with a steer target",
            MotionIntentRefusal.NeedsDynamicBody =>
                $"has a {motion} body, so it cannot take a steer target: only a dynamic body takes one"
        };
    }
}

/// <summary>Which rule of the guard refuses a motion intent (see <see cref="MotionIntent.RefusalFor"/>), or none.</summary>
internal enum MotionIntentRefusal : byte
{
    /// <summary>The entity takes the motion intent.</summary>
    None,

    /// <summary>An attached entity takes no motion intent.</summary>
    Attached,

    /// <summary>A character takes no motion intent.</summary>
    Character,

    /// <summary>An end pose or a full sample needs a kinematic body, no body, or the owner of a root frame.</summary>
    NeedsKinematicBody,

    /// <summary>The owner of a root frame has only a static interior to steer.</summary>
    SteerOfRootFrame,

    /// <summary>An entity without a body has nothing to steer.</summary>
    SteerWithoutBody,

    /// <summary>A steer target needs a dynamic body.</summary>
    NeedsDynamicBody
}
