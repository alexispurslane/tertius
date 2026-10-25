using System.Numerics;
using Tertius.Frames.Rails;

namespace Tertius;

/// <summary>
/// What the World knows about the owner of each frame at step 2 of the sync point, which the frame tree does not (see
/// <see cref="FrameTree.TakeStepRecords"/>). Each member answers for the owner of the frame that it names, and must not add,
/// remove or move a frame.
/// </summary>
internal interface IFrameOwnerFacts
{
    /// <summary>
    /// The record that physics released for the hull of <paramref name="frame"/> in the step that ended, relative to the
    /// frame's tree parent, with the proper acceleration that physics measured beside it for a dynamic hull. Null when
    /// physics released none for it.
    /// </summary>
    FrameStepRecord? HullRecord(FrameHandle frame);

    /// <summary>True when the check of the physics output skipped the update of the owner of <paramref name="frame"/> in the step.</summary>
    bool IsSkipped(FrameHandle frame);

    /// <summary>
    /// The pose that gameplay wrote for the owner of <paramref name="frame"/> in the tick, with its rotation made unit length,
    /// relative to the owner's frame, which is the frame's semantic parent. Null when gameplay wrote none.
    /// </summary>
    DPose? TransformWrite(FrameHandle frame);
}

/// <summary>
/// Step 2 of the sync point: every frame takes its step record of the step that ended (docs/plans/rails-components.md, "The
/// step record of the last finished step"), and its proper acceleration ("The proper acceleration of a frame").
/// </summary>
public sealed partial class FrameTree
{
    /// <summary>
    /// A frame of the pass of <see cref="TakeStepRecords"/>: its handle, its dense index, its tree parent and its semantic
    /// parent, which the pass reads from the tree's own storage, so that no member below checks a handle again.
    /// </summary>
    private readonly record struct RecordTaker(
        FrameHandle Frame,
        int Dense,
        FrameHandle TreeParent,
        FrameHandle Parent
    );

    /// <summary>
    /// Step 2 of the sync point, once, after the physics output of a step of <paramref name="step"/> seconds that applied
    /// <paramref name="stepIntents"/>: every frame that is not numeric takes its step record, in dense order, which puts
    /// parents first. The owner's kind decides where the record comes from, and <paramref name="owners"/> tells the tree
    /// what only the World knows:
    /// <list type="bullet">
    /// <item>A frame with a hull, a stored hull motion (see <see cref="HullMotion"/>): the record that physics released for
    /// the hull, relative to the frame's tree parent. Physics releases one when the hull is dynamic, kinematic and aimed by
    /// a motion intent, or static or kinematic without one and teleported in the step.</item>
    /// <item>A frame without a hull: the record of its owner's motion intent in <paramref name="stepIntents"/>, relative to
    /// the frame's semantic parent, from where the frame stands, which nothing moved since the step started, by the rule of
    /// <see cref="StepRecordInStep"/> (see <see cref="StepRecordOfIntent"/>), so that it agrees to the last bit with the
    /// record that physics predicted for the frame in the step. Then a write of the owner's transform in the tick, which the
    /// frame takes as a record at rest. The write comes last, so it wins for one tick, and its record is static. The next
    /// motion intent puts the frame back on its rails.</item>
    /// </list>
    /// Each record comes with its follow kind (see <see cref="FollowKindInStep"/>), and the frame takes the record's pose, its
    /// instant velocity and its angular velocity, narrowed to a float. Under a numeric tree parent the instant velocity is
    /// relative to the parent's frame velocity, the chord of the step, so that a read in the semantic parent gives the
    /// instant velocity (see <see cref="HomeInstantVelocityOverChord"/>). A frame at the origin of its following frame that takes
    /// a record with another place left that origin in the step, because its hull teleported: <see cref="JumpFollowingFrame"/>
    /// brings it back after this pass. The frame keeps the record's angular acceleration (see
    /// <see cref="AngularAcceleration"/>),
    /// and not its chord or its acceleration. A write of the owner's transform that turns the frame lists it among the frames
    /// without a last turn, because its axes jump against inertial space before the next step (see <see cref="BeginStep"/>).
    /// <para>
    /// A frame without a record holds still in its tree parent: it keeps its pose and does not turn, with no angular
    /// acceleration, and it moves with its tree parent, so a read in the semantic parent gives the parent's instant velocity. Under a numeric parent it keeps the
    /// parent's instant velocity past its frame velocity, and in a semantic parent it has no velocity. Its follow kind
    /// becomes <see cref="FollowKind.Static"/>. A frame keeps its whole state when its owner's update was skipped: the check
    /// of the physics output skipped it in a Release build, or the frame has a hull and the step failed
    /// (<paramref name="stepFailed"/>), so that physics released no record and may have moved the hull or not. The next step
    /// that works moves it on from Jolt's state.
    /// </para>
    /// <para>
    /// Every frame that takes a record or holds still takes the proper acceleration that its record gives by the rule of
    /// <see cref="ProperAccelerationFrom"/>, or, for a dynamic hull, the one that physics measured beside its record. The rule
    /// reads the parent's value as the step found it, so the tree keeps a copy of every row's value before the pass writes
    /// them (see <see cref="FieldTheStepFound"/>), and the order of the visits does not matter (see "Which proper acceleration
    /// of the parent" in docs/plans/rails-components.md). A frame that takes two records takes the value of the last.
    /// </para>
    /// The pass walks the tree's own storage, so it checks no handle, and it allocates nothing. <paramref name="owners"/> must
    /// not add, remove or move a frame, which would replace the arrays that the walk reads: a contract checks it after each
    /// frame.
    /// </summary>
    internal void TakeStepRecords<TOwners>(
        ref TOwners owners,
        ReadOnlySpan<PhysicsIntent> stepIntents,
        bool stepFailed,
        double step
    )
        where TOwners : struct, IFrameOwnerFacts
    {
        properAcceleration.AsSpan().CopyTo(properAccelerationTheStepFound);
        Span<DPose> locals = LocalPosesForWrite();
        ReadOnlySpan<int> parents = poses.ParentsDense;
        ReadOnlySpan<int> ids = poses.IdsDense;
        ReadOnlySpan<uint> generations = poses.Generations;
        int shape = poses.Version;
        for (int dense = 0; dense < ids.Length; dense++)
        {
            int id = ids[dense];
            if (numeric[id] != NumericMode.None)
                continue;
            int p = parents[dense];
            FrameHandle treeParent = FrameHandle.Null,
                parent = FrameHandle.Null;
            if (p >= 0)
            {
                treeParent = HandleOf(ids[p], generations);
                int s = p;
                while (numeric[ids[s]] != NumericMode.None)
                    s = parents[s];
                parent = s == p ? treeParent : HandleOf(ids[s], generations);
            }

            TakeStepRecord(
                owners: ref owners,
                taker: new(Frame: HandleOf(id, generations), Dense: dense, TreeParent: treeParent, Parent: parent),
                locals: locals,
                stepIntents: stepIntents,
                stepFailed: stepFailed,
                step: step
            );
            Contract.Assert(poses.Version == shape, "The owner facts added, removed or moved a frame during the pass.");
        }
    }

    /// <summary>One frame of <see cref="TakeStepRecords"/>, by the rules of its summary.</summary>
    private void TakeStepRecord<TOwners>(
        ref TOwners owners,
        in RecordTaker taker,
        Span<DPose> locals,
        ReadOnlySpan<PhysicsIntent> stepIntents,
        bool stepFailed,
        double step
    )
        where TOwners : struct, IFrameOwnerFacts
    {
        FrameHandle frame = taker.Frame;
        bool hull = !taker.TreeParent.IsNull && hullMotion[frame.Index].HasValue;
        if (owners.HullRecord(frame) is { } released)
            FollowAt(
                taker: taker,
                locals: locals,
                record: released.Motion,
                inTreeParent: true,
                kind: FollowKindAt(taker, stepIntents),
                measured: released.ProperAcceleration,
                step: step
            );
        // A frame without a hull hangs in its semantic parent, so its tree pose is its pose there, which FollowAt checks.
        else if (
            !hull
            && StepRecordOfIntent(
                slot: frame.Entity.Index,
                parent: taker.Parent,
                standing: locals[taker.Dense],
                stepIntents: stepIntents,
                step: step
            ) is { } move
        )
            FollowAt(
                taker: taker,
                locals: locals,
                record: move,
                inTreeParent: false,
                kind: FollowKindAt(taker, stepIntents),
                measured: null,
                step: step
            );
        else if (!(hull && stepFailed) && !owners.IsSkipped(frame))
            HoldStillAt(taker, locals, step);

        // Physics owns the transform of a hull, so only an owner without one writes it. A write that turns the frame jumps
        // its axes against inertial space, so its next step has no last turn (see BeginStep).
        if (!hull && owners.TransformWrite(frame) is { } written)
        {
            if (written.Rotation != locals[taker.Dense].Rotation)
                withoutLastTurn[frame.Index] = true;
            var rest = new MotionSample(
                Pose: written,
                ChordVelocity: Double3.Zero,
                Velocity: Double3.Zero,
                Acceleration: Double3.Zero,
                AngularVelocity: Double3.Zero,
                AngularAcceleration: Double3.Zero
            );
            FollowAt(
                taker: taker,
                locals: locals,
                record: rest,
                inTreeParent: false,
                kind: FollowKind.Static,
                measured: null,
                step: step
            );
        }
    }

    /// <summary>
    /// The frame takes <paramref name="record"/>, of the kind <paramref name="kind"/>, and the proper acceleration that it
    /// gives (see <see cref="ProperAccelerationOf"/>), or <paramref name="measured"/>. The record is relative to the frame's
    /// tree parent when <paramref name="inTreeParent"/>, and else to its semantic parent, which is then the same frame:
    /// only a frame with a hull hangs in a numeric frame (see the class summary).
    /// </summary>
    private void FollowAt(
        in RecordTaker taker,
        Span<DPose> locals,
        in MotionSample record,
        bool inTreeParent,
        FollowKind kind,
        Vector3? measured,
        double step
    )
    {
        Contract.Requires(record.Pose.Rotation.IsUnit && (inTreeParent || taker.TreeParent == taker.Parent));
        int id = taker.Frame.Index;
        locals[taker.Dense] = record.Pose;
        LinearVelocityForWrite(id) = record.Velocity;
        AngularVelocityForWrite(id) = record.AngularVelocity.ToVector3();
        angularAcceleration[id] = record.AngularAcceleration;
        followKind[id] = kind;
        properAcceleration[id] =
            measured ?? ProperAccelerationOf(taker: taker, inTreeParent: inTreeParent, record: record, step: step);
    }

    /// <summary>The frame holds still in its tree parent over a step of <paramref name="dt"/> seconds, by the rules of
    /// <see cref="TakeStepRecords"/>.</summary>
    private void HoldStillAt(in RecordTaker taker, ReadOnlySpan<DPose> locals, double dt)
    {
        int id = taker.Frame.Index;
        FrameHandle treeParent = taker.TreeParent;
        LinearVelocityForWrite(id) =
            treeParent == taker.Parent ? Double3.Zero : HomeInstantVelocityOverChord(treeParent, taker.Parent, dt);
        AngularVelocityForWrite(id) = Vector3.Zero;
        angularAcceleration[id] = Double3.Zero;
        followKind[id] = FollowKind.Static;
        var rest = new MotionSample(
            Pose: locals[taker.Dense],
            ChordVelocity: Double3.Zero,
            Velocity: Double3.Zero,
            Acceleration: Double3.Zero,
            AngularVelocity: Double3.Zero,
            AngularAcceleration: Double3.Zero
        );
        properAcceleration[id] = ProperAccelerationOf(taker: taker, inTreeParent: true, record: rest, step: dt);
    }

    /// <summary>
    /// The proper acceleration that <paramref name="record"/> gives the frame by the rule of
    /// <see cref="ProperAccelerationFrom"/>,
    /// after the record moves into the frame's semantic parent (docs/plans/rails-components.md, "The proper acceleration of a
    /// frame"). A record relative to the semantic parent is there already. The anchor of a following frame that has a step
    /// plan moved on the plan, which is its record in the semantic parent (see <see cref="BeginStep"/>). Any other
    /// record relative to a numeric tree parent converts as <see cref="InSemanticParent"/> says.
    /// </summary>
    private Vector3 ProperAccelerationOf(in RecordTaker taker, bool inTreeParent, in MotionSample record, double step)
    {
        FrameHandle treeParent = taker.TreeParent;
        if (inTreeParent && treeParent != taker.Parent)
        {
            int t = treeParent.Index;
            return anchor[t] == taker.Frame.Entity && stepPlan[t] is { } plan
                ? ProperAccelerationFrom(taker.Parent, plan, step)
                : ProperAccelerationFrom(taker.Parent, InSemanticParent(treeParent, record), step);
        }

        return ProperAccelerationFrom(taker.Parent, record, step);
    }

    /// <summary>
    /// The proper acceleration of a frame whose semantic parent is <paramref name="parent"/> after a step of
    /// <paramref name="step"/> seconds, from its step record in that parent, <paramref name="record"/>
    /// (docs/plans/rails-components.md, "The proper acceleration of a frame", and docs/plans/spin-gravity.md, "The proper
    /// acceleration of a frame"): the frame's acceleration over the step minus the gravity that acts at its place where the
    /// step started, turned into the frame's own axes by its rotation at the end of the step, and narrowed once to a float.
    /// The gravity at its place is the semantic parent's field there minus the parent's own proper acceleration as the step
    /// found it, as for a body at the frame's origin (see <see cref="FieldTheStepFound"/>), plus the parent's rotation terms
    /// when the parent turns (see <see cref="FrameTurns.Turns"/>): its centrifugal and Euler term there, and its Coriolis term
    /// on the record's chord, from the parent's turns of the step that ended, which <see cref="BeginStep"/> fixed before it.
    /// So the rotation terms reach a frame in the step 2 after its parent's turn, and only a change of the parent's proper
    /// acceleration moves down one level of a chain in each step. A frame at rest in a spinning station is pushed toward the
    /// axis by ω² R. No gravity acts at the place of a root frame. The place where the step started is the record's place
    /// less its chord over the step, the same expression from which <see cref="KeplerOrbit.Sample"/> takes its acceleration,
    /// so a frame on a Kepler orbit in a parent that falls freely and does not turn gets exactly zero. A parent that does not
    /// turn adds no term, so its children keep the linear rule to the bit.
    /// </summary>
    private Vector3 ProperAccelerationFrom(FrameHandle parent, in MotionSample record, double step)
    {
        Double3 start = record.Pose.Position - record.ChordVelocity * step;
        Double3 gravity = Double3.Zero;
        if (!parent.IsNull)
        {
            gravity = FieldTheStepFound(parent.Index, start);
            ref readonly FrameTurns parentTurns = ref turns[parent.Index];
            if (parentTurns.Turns)
                gravity +=
                    parentTurns.Centrifugal(start)
                    + parentTurns.Euler(start, step)
                    + parentTurns.Coriolis(record.ChordVelocity);
        }

        return record.Pose.Rotation.Conjugate().Rotate(record.Acceleration - gravity).ToVector3();
    }

    /// <summary>
    /// <paramref name="record"/>, a step record relative to <paramref name="treeParent"/>, in the semantic parent of
    /// <paramref name="treeParent"/>. In a semantic tree parent it is the record itself. Under a numeric tree parent the frame
    /// moved with the numeric frames between the two over the step, which never turn: its place gains their places, its
    /// chord their velocities, which are their chords of the step (see <see cref="AdvanceNumericFrames"/>), and its
    /// acceleration their step accelerations (see <see cref="HomeAcceleration"/>). Numeric levels turn nothing, so no rotation
    /// term enters, and it takes no <see cref="FrameMotion"/>. Call it at step 2, after <see cref="AdvanceNumericFrames"/> and
    /// <see cref="PlaceFollowingFrame"/>, and before <see cref="JumpFollowingFrame"/>.
    /// </summary>
    private MotionSample InSemanticParent(FrameHandle treeParent, in MotionSample record)
    {
        if (treeParent.IsNull || !IsNumeric(treeParent))
            return record;
        FrameHandle semantic = SemanticOf(treeParent);
        return record with
        {
            Pose = new(InSemantic(treeParent, semantic, record.Pose.Position), record.Pose.Rotation),
            ChordVelocity = record.ChordVelocity + HomeVelocity(treeParent, semantic),
            Acceleration = record.Acceleration + HomeAcceleration(treeParent, semantic),
        };
    }

    /// <summary>
    /// What made the record that <paramref name="frame"/> takes of the step that applies <paramref name="stepIntents"/>, when
    /// the frame takes the record of its hull or of its owner's motion intent (docs/plans/rails-components.md, "The follow
    /// kind"): physics, when the hull was dynamic in the step, with or without a steer target; rails for a full sample and
    /// kinematic for an end pose, which a kinematic hull or a frame without a hull follows; and static otherwise, for a
    /// teleport, a static hull, which physics moves only by a teleport, and a frame without a motion intent. A write of the
    /// owner's transform wins over the motion intent, so its record is static whatever this says. The motion of the hull in
    /// the step comes from the intents (see <see cref="HullMotionInStep(FrameHandle, ReadOnlySpan{PhysicsIntent})"/>), because the stored motion takes their changes only
    /// at step 4.
    /// </summary>
    internal FollowKind FollowKindInStep(FrameHandle frame, ReadOnlySpan<PhysicsIntent> stepIntents) =>
        FollowKindOf(HullMotionInStep(frame, stepIntents), frame.Entity.Index, stepIntents);

    /// <summary>The follow kind of <see cref="FollowKindInStep"/> for the frame of the pass.</summary>
    private FollowKind FollowKindAt(in RecordTaker taker, ReadOnlySpan<PhysicsIntent> stepIntents)
    {
        uint slot = taker.Frame.Entity.Index;
        BodyMotion? stored = taker.TreeParent.IsNull ? null : hullMotion[taker.Frame.Index];
        return FollowKindOf(HullMotionInStep(stored, slot, stepIntents), slot, stepIntents);
    }

    /// <summary>
    /// The rule of <see cref="FollowKindInStep"/>, for a frame whose owner has the slot <paramref name="slot"/> and whose
    /// hull has <paramref name="motion"/> in the step, or no hull.
    /// </summary>
    private static FollowKind FollowKindOf(BodyMotion? motion, uint slot, ReadOnlySpan<PhysicsIntent> stepIntents)
    {
        if (motion == BodyMotion.Dynamic)
            return FollowKind.Physics;
        if (motion == BodyMotion.Static || slot >= (uint)stepIntents.Length)
            return FollowKind.Static;
        return stepIntents[(int)slot].Move.FollowKind;
    }
}
