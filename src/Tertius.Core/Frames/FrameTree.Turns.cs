using System.Diagnostics;
using Tertius.Frames.Rails;

namespace Tertius;

/// <summary>
/// The turns of every frame for the step in flight (docs/plans/spin-gravity.md, "Values"): V1, the turn of the step in
/// flight, V2, the turn of the step that ended, which the frame tree keeps together in one <see cref="FrameTurns"/> per
/// frame, and V3, the angular acceleration of the frame's last step record. Physics and the numeric update read the same
/// numbers, which <see cref="BeginStep"/> fixes once per step, in one pass, from double poses and step records.
/// </summary>
public sealed partial class FrameTree
{
    /// <summary>No turn in either step, the value of every slot before its frame's first <see cref="BeginStep"/>.</summary>
    private static readonly FrameTurns NoTurns = new(FrameTurn.None, FrameTurn.None);

    // V1 and V2 of each semantic frame (see Turns). A numeric frame never turns, so its own slot keeps no turns, and it reads
    // its semantic frame's. The default FrameTurn holds zero quaternions, not the identity, so every slot starts at NoTurns.
    private FrameTurns[] turns = NewTurns(64);

    // V3 of each frame (see AngularAcceleration).
    private Double3[] angularAcceleration = new Double3[64];

    // The frames without a last turn (see BeginStep), by stable index.
    private bool[] withoutLastTurn = new bool[64];

    // The length in seconds of the step that BeginStep last prepared, whose turns the store holds: the step over which the
    // angular velocity of V2 grew into that of V1 (see WithRotationTerms). Zero before the first BeginStep, when no frame turns.
    private double turnsStep;

    private static FrameTurns[] NewTurns(int size)
    {
        var slots = new FrameTurns[size];
        slots.AsSpan().Fill(NoTurns);
        return slots;
    }

    /// <summary>
    /// The turns of <paramref name="frame"/> that a reader in the step in flight needs: the turn of the step that
    /// <see cref="BeginStep"/> last prepared, V1, and the turn of the step before, V2, each with the frame's inertial angular
    /// velocity at that step's end, in the frame's axes then (docs/plans/spin-gravity.md, "V1, the turn of the step in
    /// flight"). From <see cref="BeginStep"/> until the end of the step they describe the step in flight. From step 2 of the
    /// sync point after it until the next <see cref="BeginStep"/>, they describe the last finished step. A numeric frame
    /// reads its semantic frame's turns. A frame that has had no <see cref="BeginStep"/> yet reads no turn
    /// (<see cref="FrameTurn.None"/> in both), so its readers get the linear gravity. No gameplay code reads them: a tick reads
    /// the tree row's angular velocity, which describes the last finished step. Throws
    /// <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    internal FrameTurns Turns(FrameHandle frame) => turns[SemanticOf(frame).Index];

    /// <summary>V1 of <paramref name="frame"/>, the turn of the step in flight (see <see cref="Turns"/>).</summary>
    internal FrameTurn StepTurn(FrameHandle frame) => Turns(frame).Step;

    /// <summary>V2 of <paramref name="frame"/>, the turn of the step that ended (see <see cref="Turns"/>).</summary>
    internal FrameTurn LastTurn(FrameHandle frame) => Turns(frame).Last;

    /// <summary>
    /// The angular acceleration of the frame's step record relative to its tree parent, in the parent's axes, in rad/s²: the
    /// mean over the last finished step, the change of the frame's angular velocity over the step divided by the step
    /// (docs/plans/spin-gravity.md, "V3, the angular acceleration of a frame's last record"). Step 2 of every sync point
    /// writes it with the frame's step record (see <see cref="TakeStepRecords"/>), and zero for a frame that holds still. A
    /// move to another parent converts it (see <see cref="Reparent"/>). A new frame reads zero. Like the row's angular
    /// velocity (see <see cref="AngularVelocity"/>), it describes the last finished step. <see cref="BeginStep"/> predicts
    /// the turn of a dynamic hull's frame with it. Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    public Double3 AngularAcceleration(FrameHandle frame) => angularAcceleration[Node(frame).Id];

    /// <summary>
    /// Prepares every frame for the step that starts now, a step of <paramref name="step"/> seconds that applies
    /// <paramref name="stepIntents"/>, in one pass in dense order, parents first (docs/plans/spin-gravity.md, "V1, the turn of
    /// the step in flight"). Each semantic frame copies its V1 into its V2 and composes its new V1 from its tree parent's V1
    /// and its own level (see <see cref="BeginTurn"/>). Each numeric frame fixes its acceleration for the step, and a
    /// following frame its step plan (see <see cref="BeginNumericFrame"/>), after its semantic frame fixed its turns.
    /// <para>
    /// A frame without a last turn takes its new V1 as its V2, so that its step assumes a steady turn, with no angular
    /// acceleration: a new frame (<see cref="Create"/>), a frame whose axes jumped against inertial space, by a teleport in
    /// <paramref name="stepIntents"/> that turns its level or by a write of its owner's transform that turned it at the last
    /// step 2 (see <see cref="TakeStepRecords"/>), every frame below such a frame, and every frame when
    /// <paramref name="lastStepFailed"/>, because the results of the last step were dropped and the chords of the bodies may
    /// or may not come from its turn ("Frames without a last turn"). The pass empties the list.
    /// </para>
    /// Call it after <see cref="Update"/> and before the physics step, which reads the turns and the numeric accelerations,
    /// while the tree stands where the step starts. The World calls it in each tick's preparation. The pass walks the tree's
    /// own storage, so it checks no handle for a semantic frame, and it allocates nothing.
    /// </summary>
    internal void BeginStep(ReadOnlySpan<PhysicsIntent> stepIntents, double step, bool lastStepFailed)
    {
        // The dense order puts parents first only after Update.
        Contract.Requires(!poses.WorldIsStale && step > 0);
        ReadOnlySpan<int> parents = poses.ParentsDense;
        ReadOnlySpan<DPose> locals = poses.LocalPoses;
        ReadOnlySpan<int> ids = poses.IdsDense;
        for (int dense = 0; dense < ids.Length; dense++)
        {
            int id = ids[dense];
            if (numeric[id] != NumericMode.None)
            {
                BeginNumericFrame(dense: dense, id: id, stepIntents: stepIntents, step: step);
                continue;
            }

            int parent = parents[dense];
            while (parent >= 0 && numeric[ids[parent]] != NumericMode.None)
                parent = parents[parent];
            BeginTurn(
                id: id,
                parent: parent < 0 ? -1 : ids[parent],
                treeRotation: locals[dense].Rotation,
                stepIntents: stepIntents,
                step: step,
                lastStepFailed: lastStepFailed
            );
        }

        // Every frame that the list held took its new turn as its last turn in the pass.
        Array.Clear(withoutLastTurn);
        turnsStep = step;
    }

    /// <summary>
    /// The part of <see cref="BeginStep"/> for the semantic frame with stable index <paramref name="id"/>, whose semantic
    /// parent has the stable index <paramref name="parent"/>, or −1 for a root frame, and whose rotation in its tree parent
    /// is <paramref name="treeRotation"/>. Its new V1 is
    /// <c>end⁻¹ · parentTurn · start</c>, with the inertial angular velocity <c>end⁻¹ (parentTurn.AngularVelocity +
    /// level)</c>, where <c>parentTurn</c> is the parent's V1, which the pass fixed already, <c>start</c> and <c>end</c> are
    /// the frame's rotation relative to its parent at the start and the end of the step, and <c>level</c> is its angular
    /// velocity relative to the parent at the end of the step, in the parent's axes. A numeric tree parent has the axes of
    /// the semantic parent and does not turn, so the level is relative to the semantic parent. When no level of the chain
    /// turns, the turn is exactly the identity, so a frame that does not turn is no turning frame, to the bit.
    /// <para>
    /// The level's row is chosen by the hull's motion in the step (see
    /// <see cref="HullMotionInStep(FrameHandle, ReadOnlySpan{PhysicsIntent})"/>) and the case of the
    /// owner's motion intent, as the follow kind is (see <see cref="FollowKindOf"/>), never by the stored motion of the last
    /// finished step:
    /// <list type="bullet">
    /// <item>An end pose or a full sample, which a kinematic hull or a frame without a hull follows: the record's rotation
    /// and angular velocity (see <see cref="MotionIntent.TryRecordFrom"/>). The records of the rails components are exact, so
    /// their turns hold no float rounding of a rate.</item>
    /// <item>A dynamic hull: the turn that Jolt gives the hull, predicted from its last record (fork F3 of the design). Its
    /// rate is the row's angular velocity plus the record's angular acceleration times the step (see
    /// <see cref="AngularAcceleration"/>), and the end is the start turned about the parent's axes by that rate times the
    /// step, as Jolt turns a body after its velocity update. It takes no dead band: the frame's axes turn by what the hull's
    /// record says. A hull that does not turn, such as one that sleeps, ends where it starts.</item>
    /// <item>Any other frame holds still: it ends where it starts.</item>
    /// </list>
    /// The step starts at the tree rotation, or at the rotation of a teleport in the owner's intent when the level takes the
    /// teleport (see <see cref="TeleportsAt"/>). A teleport that turns the level lists the frame among the frames without a
    /// last turn.
    /// </para>
    /// </summary>
    private void BeginTurn(
        int id,
        int parent,
        DQuaternion treeRotation,
        ReadOnlySpan<PhysicsIntent> stepIntents,
        double step,
        bool lastStepFailed
    )
    {
        FrameTurn parentTurn = FrameTurn.None;
        // A frame below a frame that the pass listed has no last turn either: its axes jumped with its parent's.
        bool listed = withoutLastTurn[id] || lastStepFailed;
        if (parent >= 0)
        {
            parentTurn = turns[parent].Step;
            listed |= withoutLastTurn[parent];
        }

        uint slot = owner[id].Index;
        // A root frame has no hull (see HullMotion).
        BodyMotion? stored = parent < 0 ? null : hullMotion[id];
        FollowKind kind = FollowKindOf(HullMotionInStep(stored, slot, stepIntents), slot, stepIntents);
        DQuaternion start = treeRotation;
        if (
            TeleportsAt(
                slot: slot,
                stepIntents: stepIntents,
                hull: stored.HasValue,
                kind: kind,
                pose: out DPose teleport
            )
            && teleport.Rotation != treeRotation
        )
        {
            start = teleport.Rotation.Normalized();
            listed = true;
        }

        DQuaternion end = start;
        Double3 level = Double3.Zero;
        switch (kind)
        {
            case FollowKind.Rails or FollowKind.Kinematic:
                // The record's rotation and angular velocity depend only on the rotation where the step starts, which is
                // all that the pose of the start gives here.
                stepIntents[(int)slot].Move.TryRecordFrom(new(Double3.Zero, start), step, out MotionSample record);
                end = record.Pose.Rotation;
                level = record.AngularVelocity;
                break;
            case FollowKind.Physics:
                // A zero rate gives exactly the identity rotation, so a hull that does not turn ends where it starts, to the bit.
                level = angularVelocity[id] + angularAcceleration[id] * step;
                end = DQuaternion.FromAxisAngle(level, level.Length * step) * start;
                break;
            case FollowKind.Static:
                break;
            default:
                throw new UnreachableException($"A frame has the follow kind {kind}, which is not a member.");
        }

        DQuaternion turn =
            parentTurn.IsIdentity && start == end ? DQuaternion.Identity : end.Conjugate() * parentTurn.Turn * start;
        var stepTurn = new FrameTurn(turn, end.Conjugate().Rotate(parentTurn.AngularVelocity + level));
        turns[id] = new(stepTurn, listed ? stepTurn : turns[id].Step);
        withoutLastTurn[id] = listed;
    }

    /// <summary>
    /// The pose of the teleport in the intent of the owner with slot <paramref name="slot"/> in
    /// <paramref name="stepIntents"/>, relative to the owner's frame, the frame's semantic parent, when the owner's frame takes
    /// the teleport at the start of the step, so that its level starts the step there: a hull of any motion
    /// (<paramref name="hull"/>), which physics teleports, and a frame without a hull whose owner's motion intent gives a
    /// record (<paramref name="kind"/> rails or kinematic), which starts at the teleport (see
    /// <see cref="PhysicsIntent.StepRecord"/>). A frame without a hull and without such a motion intent holds still where it
    /// stands. False when the frame takes no teleport.
    /// </summary>
    private static bool TeleportsAt(
        uint slot,
        ReadOnlySpan<PhysicsIntent> stepIntents,
        bool hull,
        FollowKind kind,
        out DPose pose
    )
    {
        if (
            slot < (uint)stepIntents.Length
            && (stepIntents[(int)slot].Flags & IntentFlags.Teleport) != 0
            && (hull || kind is FollowKind.Rails or FollowKind.Kinematic)
        )
        {
            pose = stepIntents[(int)slot].TeleportPose;
            return true;
        }

        pose = default;
        return false;
    }

    /// <summary>
    /// <paramref name="linear"/>, the linear gravity on a body at <paramref name="place"/> in the semantic frame with stable
    /// index <paramref name="semantic"/>, plus the frame's centrifugal and Euler term there (docs/plans/spin-gravity.md, "V5,
    /// the effective gravity"): the centrifugal term at the inertial angular velocity of V1, the end of the step in flight,
    /// and the Euler term at the mean angular acceleration over that step, from V2's angular velocity to V1's. A frame that
    /// does not turn (see <see cref="FrameTurns.Turns"/>) adds nothing, so its readers keep the linear gravity to the bit, and
    /// so does every frame before its first <see cref="BeginStep"/>, when its turns are <see cref="FrameTurn.None"/> and no
    /// step length is known.
    /// </summary>
    private Double3 WithRotationTerms(Double3 linear, int semantic, Double3 place)
    {
        ref readonly FrameTurns frameTurns = ref turns[semantic];
        return frameTurns.Turns ? linear + frameTurns.Centrifugal(place) + frameTurns.Euler(place, turnsStep) : linear;
    }

    /// <summary>
    /// The angular acceleration of frame <paramref name="of"/> relative to frame <paramref name="to"/>, in
    /// <paramref name="to"/> axes, in rad/s², from the angular velocity and the angular acceleration (see
    /// <see cref="AngularAcceleration"/>) of every frame on the path through the common ancestor, each relative to its tree
    /// parent. It composes them level by level by the rules of <see cref="FrameMotion"/>: up a level by
    /// <see cref="FrameMotion.AngularAccelerationToParent(Double3, Double3)"/>, and down a level by
    /// <see cref="FrameMotion.AngularAccelerationFromParent(Double3, Double3)"/>, each with the angular velocity of the path
    /// so far. A null frame is the virtual root above every root frame.
    /// </summary>
    internal Double3 AngularAccelerationRelative(FrameHandle of, FrameHandle to)
    {
        FrameHandle x = of;
        Double3 omega = Double3.Zero,
            alpha = Double3.Zero;
        while (!x.IsNull && x != to && !IsAncestorOrNull(x, to))
        {
            FrameMotion level = AngularLevel(x);
            alpha = level.AngularAccelerationToParent(alpha, omega);
            omega = level.AngularToParent(omega);
            x = TreeParent(x);
        }

        if (x == to)
            return alpha;
        var path = new Stack<FrameHandle>();
        for (FrameHandle y = to; !y.IsNull && y != x; y = TreeParent(y))
            path.Push(y);
        while (path.Count > 0)
        {
            FrameMotion level = AngularLevel(path.Pop());
            alpha = level.AngularAccelerationFromParent(alpha, omega);
            omega = level.AngularFromParent(omega);
        }

        return alpha;
    }

    /// <summary>The turning part of the motion of <paramref name="frame"/> relative to its tree parent: its pose, its row's angular velocity and its angular acceleration.</summary>
    private FrameMotion AngularLevel(FrameHandle frame)
    {
        int id = Node(frame).Id;
        return new(
            Pose: TreeLocalPose(frame),
            Velocity: Double3.Zero,
            AngularVelocity: angularVelocity[id],
            Acceleration: Double3.Zero,
            AngularAcceleration: angularAcceleration[id]
        );
    }

    /// <summary>Grows the stores of the turns to <paramref name="size"/> slots, each new slot with no turn (see
    /// <see cref="Grow"/>).</summary>
    private void GrowTurns(int size)
    {
        int old = turns.Length;
        Array.Resize(ref turns, size);
        turns.AsSpan(old).Fill(NoTurns);
        Array.Resize(ref angularAcceleration, size);
        Array.Resize(ref withoutLastTurn, size);
    }
}
