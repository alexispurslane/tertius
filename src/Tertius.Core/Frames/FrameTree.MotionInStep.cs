using System.Diagnostics;
using Tertius.Frames.Rails;

namespace Tertius;

/// <summary>
/// The motion of one frame relative to another over the step in flight (docs/plans/spin-gravity.md, "V7, the relative
/// motion of two frames"), by which the World converts an intent at steps 5 and 7b of the sync point (see
/// <see cref="MotionRelativeInStep"/>).
/// </summary>
public sealed partial class FrameTree
{
    /// <summary>A frame at rest in itself: where a composition of levels starts.</summary>
    private static readonly FrameMotion NoMotion = new(
        Pose: DPose.Identity,
        Velocity: Double3.Zero,
        AngularVelocity: Double3.Zero,
        Acceleration: Double3.Zero,
        AngularAcceleration: Double3.Zero
    );

    /// <summary>
    /// The motion of <paramref name="of"/> relative to <paramref name="to"/>, in <paramref name="to"/> axes, over a step of
    /// <paramref name="step"/> seconds that applies <paramref name="stepIntents"/>: <c>Start</c>, the relation where the step
    /// starts, and <c>End</c>, the relation where it ends, with both accelerations (docs/plans/spin-gravity.md, "V7, the
    /// relative motion of two frames"). It composes the motion of every level of the path through the two frames' lowest
    /// common ancestor, from the inner end of each half, by the rules of <see cref="FrameMotion"/>, which hold the rotation
    /// terms of every level that turns. Each level's motion relative to its tree parent:
    /// <list type="bullet">
    /// <item>At <c>Start</c>, every level stands at its tree pose, or at the pose of a teleport in the step's intents that it
    /// takes, with the velocities that its row holds, which describe that moment, and no accelerations. Without a
    /// teleport on the path, the pose is the tree's relative pose (see <see cref="Relative"/>) to the bit.</item>
    /// <item>At <c>End</c>, a level whose owner has an end pose or a full sample takes the record of its motion intent (see
    /// <see cref="StepRecordInStep"/>). A level with a dynamic hull takes what physics predicts from its row (see
    /// <see cref="HullPrediction"/>). A level that dies at step 5, whose owner's slot <paramref name="dying"/> marks, keeps
    /// its relation at the moment of its death, its <c>Start</c> without a teleport, at both ends of the step: that is the
    /// relation by which the rows of its contents convert at the same moment. Any other level holds still where it
    /// starts. A numeric level moves as the numeric update moves it (see <see cref="NumericLevelEnd"/>).</item>
    /// </list>
    /// The tree must stand where the step starts, as at steps 5 and 7b, where the intents are those of the next step, and
    /// where the turns, the rows and the step accelerations describe the step that ended: a dynamic hull and a numeric
    /// frame are predicted from them. A null frame is the virtual root above every root frame. The intents and the marks
    /// are by entity slot; a slot past their end holds no intent and does not die. The walk allocates nothing.
    /// </summary>
    internal (FrameMotion Start, FrameMotion End) MotionRelativeInStep(
        FrameHandle of,
        FrameHandle to,
        ReadOnlySpan<PhysicsIntent> stepIntents,
        double step,
        ReadOnlySpan<bool> dying = default
    )
    {
        Contract.Requires(step > 0);
        FrameHandle common = of;
        while (!common.IsNull && common != to && !IsAncestorOrNull(common, to))
            common = TreeParent(common);
        (FrameMotion ofStart, FrameMotion ofEnd) = ChainInStep(
            frame: of,
            ancestor: common,
            stepIntents: stepIntents,
            step: step,
            dying: dying
        );
        (FrameMotion toStart, FrameMotion toEnd) = ChainInStep(
            frame: to,
            ancestor: common,
            stepIntents: stepIntents,
            step: step,
            dying: dying
        );
        return (toStart.Inverse().MotionToParent(ofStart), toEnd.Inverse().MotionToParent(ofEnd));
    }

    /// <summary>
    /// The motion of <paramref name="frame"/> relative to its ancestor <paramref name="ancestor"/>, or to the virtual root,
    /// at both ends of the step: the levels between them composed from <paramref name="frame"/> up, as the pose tree
    /// composes a chain of poses (see <see cref="MotionRelativeInStep"/>).
    /// </summary>
    private (FrameMotion Start, FrameMotion End) ChainInStep(
        FrameHandle frame,
        FrameHandle ancestor,
        ReadOnlySpan<PhysicsIntent> stepIntents,
        double step,
        ReadOnlySpan<bool> dying
    )
    {
        FrameMotion start = NoMotion,
            end = NoMotion;
        for (FrameHandle x = frame; x != ancestor; x = TreeParent(x))
        {
            (FrameMotion levelStart, FrameMotion levelEnd) = LevelInStep(
                frame: x,
                stepIntents: stepIntents,
                step: step,
                dying: dying
            );
            start = levelStart.MotionToParent(start);
            end = levelEnd.MotionToParent(end);
        }

        return (start, end);
    }

    /// <summary>The motion of <paramref name="frame"/> relative to its tree parent at both ends of the step, by the rules of
    /// <see cref="MotionRelativeInStep"/>.</summary>
    private (FrameMotion Start, FrameMotion End) LevelInStep(
        FrameHandle frame,
        ReadOnlySpan<PhysicsIntent> stepIntents,
        double step,
        ReadOnlySpan<bool> dying
    )
    {
        int id = Node(frame).Id;
        DPose tree = TreeLocalPose(frame);
        var now = new FrameMotion(
            Pose: tree,
            Velocity: linearVelocity[id],
            AngularVelocity: angularVelocity[id],
            Acceleration: Double3.Zero,
            AngularAcceleration: Double3.Zero
        );
        if (numeric[id] != NumericMode.None)
            return (now, NumericLevelEnd(frame: frame, local: tree, stepIntents: stepIntents, step: step));
        uint slot = owner[id].Index;
        if (slot < (uint)dying.Length && dying[(int)slot])
            return (now, now);

        FrameHandle treeParent = TreeParent(frame);
        FrameHandle parent = treeParent.IsNull ? FrameHandle.Null : SemanticOf(treeParent);
        // A root frame has no hull (see HullMotion).
        BodyMotion? stored = treeParent.IsNull ? null : hullMotion[id];
        FollowKind kind = FollowKindOf(HullMotionInStep(stored, slot, stepIntents), slot, stepIntents);
        DPose start = tree;
        if (
            TeleportsAt(
                slot: slot,
                stepIntents: stepIntents,
                hull: stored.HasValue,
                kind: kind,
                pose: out DPose teleport
            )
        )
            // The teleport is relative to the owner's frame, the semantic parent, and the numeric frames between never turn.
            start = new(
                treeParent == parent ? teleport.Position : teleport.Position - HomeOffset(treeParent, parent),
                teleport.Rotation.Normalized()
            );

        FrameMotion end;
        switch (kind)
        {
            case FollowKind.Rails or FollowKind.Kinematic:
                StepRecordInStep(frame: frame, stepIntents: stepIntents, step: step, record: out MotionSample record);
                end = InTreeParent(
                    treeParent: treeParent,
                    parent: parent,
                    inParent: new(
                        Pose: record.Pose,
                        Velocity: record.Velocity,
                        AngularVelocity: record.AngularVelocity,
                        Acceleration: record.Acceleration,
                        AngularAcceleration: record.AngularAcceleration
                    ),
                    stepIntents: stepIntents,
                    step: step
                );
                break;
            case FollowKind.Physics:
                end = InTreeParent(
                    treeParent: treeParent,
                    parent: parent,
                    inParent: HullPrediction(id: id, treeParent: treeParent, parent: parent, start: start, step: step),
                    stepIntents: stepIntents,
                    step: step
                );
                break;
            case FollowKind.Static:
                end = new(
                    Pose: start,
                    Velocity: Double3.Zero,
                    AngularVelocity: Double3.Zero,
                    Acceleration: Double3.Zero,
                    AngularAcceleration: Double3.Zero
                );
                break;
            default:
                throw new UnreachableException($"A frame has the follow kind {kind}, which is not a member.");
        }

        return (now with { Pose = start }, end);
    }

    /// <summary>
    /// The motion at the end of the step, relative to the semantic parent <paramref name="parent"/>, of the frame with
    /// stable index <paramref name="id"/>, whose dynamic hull starts the step at <paramref name="start"/> in its tree parent
    /// <paramref name="treeParent"/>: physics' motion of the hull, predicted from its row as the turns of its frame are (see
    /// <see cref="BeginTurn"/>, fork F3 of docs/plans/spin-gravity.md). Its rate is the row's angular velocity plus the
    /// angular acceleration of its last record times the step (see <see cref="AngularAcceleration"/>), and its rotation the
    /// start turned about the parent's axes by that rate times the step. Its acceleration is the accelerometer rule read
    /// backward (see <see cref="ProperAccelerationFrom"/>): its proper acceleration of the last step turned out of its axes,
    /// plus the parent's effective gravity at its semantic place and the parent's Coriolis term on its velocity there. Jolt
    /// adds the acceleration to the velocity and then moves the body by the new velocity, so from the row's instant velocity
    /// v the hull moves by the chord v + a dt / 2 and ends at the instant velocity v + a dt.
    /// </summary>
    private FrameMotion HullPrediction(int id, FrameHandle treeParent, FrameHandle parent, in DPose start, double step)
    {
        Double3 place = InSemantic(treeParent, parent, start.Position);
        // The row keeps a velocity under a numeric home relative to the home's frame velocity, so the sum is the instant
        // velocity in the semantic parent (see HomeInstantVelocityOverChord).
        Double3 velocity = linearVelocity[id] + HomeVelocity(treeParent, parent);
        Double3 spin = angularVelocity[id] + angularAcceleration[id] * step;
        DQuaternion rotation = DQuaternion.FromAxisAngle(spin, spin.Length * step) * start.Rotation;
        Double3 acceleration = rotation.Rotate((Double3)properAcceleration[id]) + SemanticGravity(parent, place);
        ref readonly FrameTurns parentTurns = ref turns[parent.Index];
        if (parentTurns.Turns)
            acceleration += parentTurns.Coriolis(velocity);
        return new(
            Pose: new(place + (velocity + acceleration * (step / 2)) * step, rotation),
            Velocity: velocity + acceleration * step,
            AngularVelocity: spin,
            Acceleration: acceleration,
            AngularAcceleration: angularAcceleration[id]
        );
    }

    /// <summary>
    /// <paramref name="inParent"/>, the motion of a frame at the end of the step relative to its semantic parent
    /// <paramref name="parent"/>, relative to its tree parent <paramref name="treeParent"/>: through the numeric frames
    /// between the two, each at the end of the step (see <see cref="NumericLevelEnd"/>), which never turn. In a semantic
    /// tree parent it is <paramref name="inParent"/> itself.
    /// </summary>
    private FrameMotion InTreeParent(
        FrameHandle treeParent,
        FrameHandle parent,
        in FrameMotion inParent,
        ReadOnlySpan<PhysicsIntent> stepIntents,
        double step
    )
    {
        if (treeParent == parent)
            return inParent;
        FrameMotion home = NoMotion;
        for (FrameHandle f = treeParent; f != parent; f = TreeParent(f))
            home = NumericLevelEnd(frame: f, local: TreeLocalPose(f), stepIntents: stepIntents, step: step)
                .MotionToParent(home);
        return home.Inverse().MotionToParent(inParent);
    }

    /// <summary>
    /// The motion at the end of the step, relative to its tree parent, of the numeric frame <paramref name="frame"/>, which
    /// stands at <paramref name="local"/> where the step starts: where the numeric update moves
    /// it over the step (see <see cref="AdvanceNumericFrames"/>), with its instant velocity at the end of the step, its frame
    /// velocity plus what the instant velocity exceeds it by (see <see cref="HomeInstantVelocityOverChord"/>), and its step
    /// acceleration. A following frame lands on its anchor's step plan, with the plan's instant velocity and the
    /// acceleration that lands it there (see <see cref="BeginNumericFrame"/>), and in a step without a motion intent that
    /// plan holds it still (ticket ter-zsku). A falling frame keeps the step acceleration of the step that ended, which the
    /// next <see cref="BeginStep"/> fixes anew from nearly the same field. A resting frame does not move. A numeric frame
    /// never turns.
    /// </summary>
    private FrameMotion NumericLevelEnd(
        FrameHandle frame,
        in DPose local,
        ReadOnlySpan<PhysicsIntent> stepIntents,
        double step
    )
    {
        int id = Node(frame).Id;
        Double3 velocity = linearVelocity[id];
        if (numeric[id] == NumericMode.Following)
        {
            MotionSample plan = AnchorPlanInStep(frame, stepIntents, step);
            return new(
                Pose: new(plan.Pose.Position, local.Rotation),
                Velocity: plan.Velocity,
                AngularVelocity: Double3.Zero,
                Acceleration: (plan.ChordVelocity - velocity) / step,
                AngularAcceleration: Double3.Zero
            );
        }

        if (numeric[id] != NumericMode.Falling)
            return new(
                Pose: local,
                Velocity: velocity,
                AngularVelocity: Double3.Zero,
                Acceleration: Double3.Zero,
                AngularAcceleration: Double3.Zero
            );
        Double3 acceleration = stepAcceleration[id];
        Double3 chord = velocity + acceleration * step;
        return new(
            Pose: new(local.Position + chord * step, local.Rotation),
            Velocity: chord + acceleration * (0.5 * step),
            AngularVelocity: Double3.Zero,
            Acceleration: acceleration,
            AngularAcceleration: Double3.Zero
        );
    }
}
