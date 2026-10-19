using System.Numerics;
using System.Runtime.InteropServices;
using Tertius.Frames.Rails;

namespace Tertius;

/// <summary>
/// A body whose physics home changes at <see cref="FrameTree.UpdateNumericFrames"/>: it moves from <see cref="From"/>
/// into <see cref="To"/>, two frames of its semantic frame (the semantic frame itself or a numeric frame below it).
/// </summary>
internal readonly record struct NumericMove(Entity Entity, FrameHandle From, FrameHandle To);

/// <summary>
/// A ghost of the anchor <see cref="Anchor"/> in the system of <see cref="Frame"/>, the home of a group that the merge test
/// links to it. An anchor is a static body or the hull of a frame that physics does not drive, and a group takes the home of
/// its anchors (ticket ter-dhkf). The only anchors whose homes a group cannot share are the hull of a Kepler frame that
/// lives in an orbit frame of its own, which moves with its orbit, and an anchor at rest or another such hull: the group
/// takes one home, and each Kepler hull that lives elsewhere keeps a ghost in it (docs/plans/numeric-frames.md, "Anchors"
/// and "Limits"). So <see cref="Anchor"/> is always such a hull, and <see cref="Frame"/> a frame of its semantic frame: the
/// semantic frame itself, or a numeric frame of it.
/// </summary>
internal readonly record struct NumericGhost(Entity Anchor, FrameHandle Frame);

/// <summary>
/// The numeric frames of the tree (docs/plans/numeric-frames.md): frames that the engine makes around a group of bodies,
/// only for precision. Gameplay never sees them. <see cref="UpdateNumericFrames"/> runs their rules once per sync point.
/// </summary>
public sealed partial class FrameTree
{
    /// <summary>The thresholds of the rules of numeric frames. The World sets them before its first update.</summary>
    internal NumericFrameOptions NumericOptions { get; set; } = new();

    /// <summary>
    /// Step 2 of the sync point, before the rows of the bodies take the end of the step: the orbit frame in which
    /// <paramref name="anchor"/> hangs takes the place and the chord of the anchor's step plan, the anchor's record of the step
    /// that just ended in its semantic parent (docs/plans/rails-components.md, "The step plan of a following frame"). The
    /// landing acceleration of the step put it there to its rounding (see <see cref="BeginNumericStep"/> and
    /// <see cref="AdvanceNumericFrames"/>), so its contents keep their places in it: the move is that rounding. An anchor
    /// without a step plan leaves its orbit frame where its fall put it. The anchor itself takes the record that physics
    /// releases for its hull (see <see cref="Follow"/>).
    /// </summary>
    internal void PlaceFollowingFrame(FrameHandle anchor)
    {
        Contract.Requires(InOrbitFrame(anchor));
        FrameHandle orbitFrame = TreeParent(anchor);
        int orbitId = Node(orbitFrame).Id;
        if (stepPlan[orbitId] is not { } plan)
            return;
        LocalForWrite(Node(orbitFrame)) = new DPose(plan.Pose.Position, DQuaternion.Identity);
        LinearVelocityForWrite(orbitId) = plan.ChordVelocity;
    }

    /// <summary>
    /// Step 2 of the sync point: <paramref name="frame"/> takes its step record, <paramref name="record"/>, its motion over the
    /// step that just ended (docs/plans/rails-components.md, "The step record of the last finished step"), which
    /// <paramref name="kind"/> made (see <see cref="Followed"/> and <see cref="FollowKindInStep"/>). The record is relative to
    /// <paramref name="relativeTo"/>, the frame's tree parent. A record that physics releases is relative to it. A motion
    /// intent or a write from an owner without a hull is relative to the semantic parent, which is the same frame, because
    /// only a frame with a hull hangs in a numeric frame (see the class summary). The frame takes the record's pose, its
    /// instant velocity, its angular velocity, narrowed to a float, and a proper acceleration: <paramref name="measured"/>,
    /// which physics measured for a dynamic hull, or else the value that the record gives by the rule of
    /// <see cref="ProperAccelerationFrom"/>, after a step of <paramref name="step"/> seconds, which the frame's row takes only
    /// at <see cref="StoreNewProperAccelerations"/>. Under a numeric parent the record's instant velocity is relative to the
    /// parent's frame velocity, the chord of the step, so that a read in the semantic parent gives the instant velocity (see
    /// <see cref="HomeInstantVelocityOverChord"/>). A frame at the origin of its orbit frame that takes a record with another
    /// place left that origin in the step, because its hull teleported: <see cref="JumpFollowingFrame"/> brings it back. The
    /// record's chord, acceleration and angular acceleration are not kept.
    /// </summary>
    internal void Follow(FrameHandle frame, FrameHandle relativeTo, in MotionSample record, FollowKind kind, Vector3? measured, double step)
    {
        PoseNode node = Node(frame);
        int id = node.Id;
        Contract.Requires(relativeTo == TreeParent(frame));
        Contract.Requires(record.Pose.Rotation.IsUnit);
        LocalForWrite(node) = record.Pose;
        LinearVelocityForWrite(id) = record.Velocity;
        AngularVelocityForWrite(id) = record.AngularVelocity.ToVector3();
        followKind[id] = kind;
        newProperAccelerations.Add((id, measured ?? ProperAccelerationFrom(frame, InSemanticParent(frame, relativeTo, record), step)));
    }

    /// <summary>
    /// Step 2 of the sync point, after a step of <paramref name="dt"/> seconds: a frame without a step record holds still in
    /// its tree parent. It keeps its pose and does not turn. It moves with its tree parent, so a read in the semantic parent
    /// gives the parent's instant velocity: under a numeric parent, the frame keeps the parent's instant velocity past its
    /// frame velocity (see <see cref="HomeInstantVelocityOverChord"/>), and in a semantic parent no velocity. Its proper
    /// acceleration follows from that rest by the rule of <see cref="ProperAccelerationFrom"/>: a frame that holds still in a
    /// gravity field feels that gravity reversed, so a parked cabin has weight, and one that holds still in a falling frame
    /// falls freely with it. The row takes that value only at <see cref="StoreNewProperAccelerations"/>. The frame followed
    /// nothing in the step (<see cref="FollowKind.Static"/>).
    /// </summary>
    internal void HoldStill(FrameHandle frame, double dt)
    {
        int id = Node(frame).Id;
        FrameHandle treeParent = TreeParent(frame);
        LinearVelocityForWrite(id) = treeParent.IsNull ? Double3.Zero : HomeInstantVelocityOverChord(treeParent, SemanticOf(treeParent), dt);
        AngularVelocityForWrite(id) = Vector3.Zero;
        followKind[id] = FollowKind.Static;
        var rest = new MotionSample(TreeLocalPose(frame), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero);
        newProperAccelerations.Add((id, ProperAccelerationFrom(frame, InSemanticParent(frame, treeParent, rest), dt)));
    }

    /// <summary>
    /// The proper acceleration of <paramref name="frame"/> after a step of <paramref name="step"/> seconds, from its step
    /// record in its semantic parent, <paramref name="record"/> (docs/plans/rails-components.md, "The proper acceleration of
    /// a frame"): the frame's acceleration over the step minus the gravity that acts at its place where the step started,
    /// turned into the frame's own axes by its rotation at the end of the step, and narrowed once to a float. The gravity at
    /// its place is the semantic parent's field there minus the parent's own proper acceleration, as for a body at the
    /// frame's origin (see <see cref="FieldOf"/>), with the parent's value as stored before step 2. No gravity acts at the
    /// place of a root frame. The place where the step
    /// started is the record's place less its chord over the step, the same expression from which
    /// <see cref="KeplerOrbit.Sample"/> takes its acceleration, so a frame on a Kepler orbit in a parent that falls freely
    /// gets exactly zero.
    /// </summary>
    internal Vector3 ProperAccelerationFrom(FrameHandle frame, in MotionSample record, double step)
    {
        Double3 start = record.Pose.Position - record.ChordVelocity * step;
        FrameHandle parent = Parent(frame);
        Double3 gravity = parent.IsNull ? Double3.Zero : FieldOf(parent.Index, start);
        return record.Pose.Rotation.Conjugate().Rotate(record.Acceleration - gravity).ToVector3();
    }

    /// <summary>
    /// The step record of <paramref name="frame"/> in its semantic parent, from <paramref name="record"/>, its record relative
    /// to <paramref name="treeParent"/>, the frame's tree parent (docs/plans/rails-components.md, "The proper acceleration of
    /// a frame"). The anchor of a following frame that has a step plan moved on the plan, which is its record in the semantic
    /// parent (see <see cref="BeginNumericStep"/>). Any other record converts as <see cref="InSemanticParent(FrameHandle, in MotionSample)"/>
    /// says.
    /// </summary>
    private MotionSample InSemanticParent(FrameHandle frame, FrameHandle treeParent, in MotionSample record) =>
        !treeParent.IsNull && orbiter[treeParent.Index] == frame && stepPlan[treeParent.Index] is { } plan ? plan : InSemanticParent(treeParent, record);

    /// <summary>
    /// <paramref name="record"/>, a step record relative to <paramref name="treeParent"/>, in the semantic parent of
    /// <paramref name="treeParent"/>. In a semantic tree parent it is the record itself. Under a numeric tree parent the frame
    /// moved with the numeric frames between the two over the step, which never turn: its place gains their places, its
    /// chord their velocities, which are their chords of the step (see <see cref="AdvanceNumericFrames"/>), and its
    /// acceleration their step accelerations, the analogue of <see cref="AccelerationRelative"/> for numeric frames (see
    /// <see cref="HomeAcceleration"/>). Call it at step 2, after <see cref="AdvanceNumericFrames"/> and
    /// <see cref="PlaceFollowingFrame"/>, and before <see cref="JumpFollowingFrame"/>.
    /// </summary>
    private MotionSample InSemanticParent(FrameHandle treeParent, in MotionSample record)
    {
        if (treeParent.IsNull || !IsNumeric(treeParent))
            return record;
        FrameHandle semantic = SemanticOf(treeParent);
        return record with
        {
            Pose = new DPose(InSemantic(treeParent, semantic, record.Pose.Position), record.Pose.Rotation),
            ChordVelocity = record.ChordVelocity + HomeVelocity(treeParent, semantic),
            Acceleration = record.Acceleration + HomeAcceleration(treeParent, semantic),
        };
    }

    /// <summary>
    /// Step 2 of the sync point, after <paramref name="anchor"/> took a record that put it away from the origin of its orbit
    /// frame: its hull teleported in the step, so physics aimed it at its step plan's place and not at that origin, or, in a
    /// step without a motion intent, left it at rest at the teleport's place (see docs/plans/rails-components.md, "The step
    /// plan of a following frame"). <paramref name="record"/> is the record that physics released for the anchor, relative to
    /// the orbit frame. The orbit frame jumps to the anchor's place in the semantic parent: the step plan's place and chord,
    /// or without a plan the record's, converted into the semantic parent as for the proper acceleration (see
    /// <see cref="InSemanticParent(FrameHandle, in MotionSample)"/>), and the anchor goes back to its origin. Everything else
    /// in the orbit frame keeps its place and velocity in the semantic frame, as without numeric frames: the other frames lose
    /// the orbit frame's move, and the move goes into <paramref name="changes"/>, so that physics shifts the bodies in the
    /// orbit frame's system, the hull with them, and the World the rows.
    /// </summary>
    internal void JumpFollowingFrame(FrameHandle anchor, in MotionSample record, PhysicsChanges changes)
    {
        Contract.Requires(InOrbitFrame(anchor));
        FrameHandle orbitFrame = TreeParent(anchor);
        int orbitId = Node(orbitFrame).Id;
        MotionSample landing = stepPlan[orbitId] ?? InSemanticParent(orbitFrame, record);
        ref DPose orbitLocal = ref LocalForWrite(Node(orbitFrame));
        Double3 offset = landing.Pose.Position - orbitLocal.Position, change = landing.ChordVelocity - linearVelocity[orbitId];
        orbitLocal = new DPose(landing.Pose.Position, DQuaternion.Identity);
        LinearVelocityForWrite(orbitId) = landing.ChordVelocity;
        ref DPose anchorLocal = ref LocalForWrite(Node(anchor));
        anchorLocal = new DPose(Double3.Zero, anchorLocal.Rotation);
        shiftChildren.Clear();
        GetTreeChildren(orbitFrame, shiftChildren);
        foreach (FrameHandle child in shiftChildren)
        {
            if (child == anchor)
                continue;
            PoseNode childNode = Node(child);
            ref DPose childLocal = ref LocalForWrite(childNode);
            childLocal = new DPose(childLocal.Position - offset, childLocal.Rotation);
            LinearVelocityForWrite(childNode.Id) -= change;
        }
        changes.FramesShifted.Add(new FrameShift(orbitFrame, offset, change));
    }

    /// <summary>True when <paramref name="frame"/> hangs in its orbit frame: an orbit frame whose orbiter it is.</summary>
    internal bool InOrbitFrame(FrameHandle frame)
    {
        FrameHandle parent = TreeParent(frame);
        return !parent.IsNull && orbiter[parent.Index] == frame;
    }

    /// <summary>
    /// Creates the orbit frame of the frame that <paramref name="hull"/> owns, a hull that is kinematic in the step that the
    /// update plans (<see cref="NumericBody.Orbits"/>), which the numeric update moves into it, and moves the frame under it.
    /// The new frame lies in the frame's semantic parent at the frame's place, with the frame's instant velocity there, as the
    /// frame's row holds them after the record of the step that ended (see <see cref="NumericMode.Orbiting"/>). The frame
    /// rests at its origin. The first step plan lands the new frame on the anchor's motion (see
    /// <see cref="BeginNumericStep"/>). The frame's place and velocity in the semantic parent stay what they were.
    /// </summary>
    private FrameHandle CreateOrbitFrame(in NumericBody hull)
    {
        FrameHandle frame = hull.OwnedFrame;
        Contract.Requires(hull.Orbits && !IsNumeric(frame) && !InOrbitFrame(frame));
        FrameHandle treeParent = TreeParent(frame);
        FrameHandle semantic = SemanticOf(treeParent);
        DPose local = TreeLocalPose(frame);
        Double3 position = InSemantic(treeParent, semantic, local.Position);
        Double3 velocity = HomeVelocity(treeParent, semantic) + linearVelocity[frame.Index];
        FrameHandle orbitFrame = CreateNumeric(semantic, position, velocity, NumericMode.Orbiting);
        orbiter[orbitFrame.Index] = frame;
        Reparent(frame, orbitFrame);
        LocalForWrite(Node(frame)) = new DPose(Double3.Zero, local.Rotation);
        LinearVelocityForWrite(frame.Index) = Double3.Zero;
        Contract.Ensures(InOrbitFrame(frame) && Parent(frame) == semantic);
        return orbitFrame;
    }

    /// <summary>
    /// The orbit frame <paramref name="orbitFrame"/> lost its orbiter, which moved out, died or got a hull motion other than
    /// kinematic for the next step (see <see cref="PlanHullMotion"/>). It turns into a falling frame that falls on from its place, at its
    /// velocity: the bodies in it keep their motion, and the next numeric update takes it as any falling frame. The ghosts
    /// of the orbiter's hull go, because the hull no longer lives in an orbit frame (see <see cref="AnchorGhosts"/>).
    /// </summary>
    private void LeaveOrbit(FrameHandle orbitFrame)
    {
        int id = Node(orbitFrame).Id;
        Contract.Requires(numeric[id] == NumericMode.Orbiting && !orbiter[id].IsNull);
        Entity hull = orbiter[id].Entity;
        for (int i = anchorGhosts.Count - 1; i >= 0; i--)
            if (anchorGhosts[i].Anchor == hull)
                anchorGhosts.RemoveAt(i);
        numeric[id] = NumericMode.Falling;
        orbiter[id] = FrameHandle.Null;
    }

    /// <summary>The bodies of one semantic frame that the merge test links, as a range of the sorted bodies.</summary>
    private struct BodyGroup
    {
        public int Start, End;
        public NumericGroup Motion;
        /// <summary>The mode that the group's frame gets, or None for the semantic frame's own system.</summary>
        public NumericMode Mode;
        /// <summary>True when the group keeps <see cref="NumericGroup.Frame"/>; false when another group with more mass in it does.</summary>
        public bool Keeps;
    }

    // Scratch of UpdateNumericFrames. The bodies sorted by semantic frame, then in the order of the caller, and within the
    // semantic frame that the update works on, in the order of their groups. Between updates, they are the bodies of the
    // last update with their new homes, which the invariants read.
    private readonly List<NumericBody> sortedBodies = new();
    private readonly List<NumericBody> bodyScratch = new();
    private long[] sortKeys = [];
    // Union-find parents by index in sortedBodies, and the group of each body.
    private int[] links = [];
    private int[] groupOf = [];
    private readonly List<BodyGroup> groups = new();
    private readonly List<(FrameHandle Frame, double Mass)> homeMasses = new();
    private readonly Dictionary<FrameHandle, int> keepers = new();
    private readonly List<FrameHandle> numericHomes = new();
    private readonly List<NumericMove> moves = new();
    private readonly List<FrameHandle> treeFrames = new();
    private readonly List<FrameHandle> frameChildren = new();
    private readonly List<Double3> nestMembers = new();
    private readonly List<(FrameHandle Nest, Double3 Place)> nestPlaces = new();
    // True for each body, by index in sortedBodies, that is a Kepler hull that lives in an orbit frame of its own which its
    // group does not take (see GroupBodies): it stays in or moves to that frame, apart from its group.
    private bool[] apart = [];
    // An orbiting frame's orbiter: the Kepler frame that hangs at its origin. Null for every other frame.
    private FrameHandle[] orbiter = new FrameHandle[64];
    private readonly List<NumericGhost> anchorGhosts = new();
    // Each semantic frame of the input, with the range of its bodies in sortedBodies, in the order of the frames' indices.
    private readonly List<SemanticRange> semanticRanges = new();
    // The places of the ghosts in the frames of the semantic frame that the update works on (see CollectGhostPlaces).
    private readonly List<GhostPlace> ghostPlaces = new();

    /// <summary>
    /// The bodies of one semantic frame of the input: their range in <see cref="sortedBodies"/>, and bounds of where those
    /// that can move are, in the frame's coordinates. <see cref="AddSiblingPlaces"/> fills in the bounds the first time that
    /// it needs them in an update, from the positions and velocities of the bodies in their semantic frame, which no update
    /// changes.
    /// </summary>
    private struct SemanticRange
    {
        public FrameHandle Frame;
        public int Start;
        public int End;

        /// <summary>True when the bounds below hold.</summary>
        public bool Bounded;

        /// <summary>The longest distance from the frame's origin of a body that can move, in meters, or -1 when none can move.</summary>
        public double Farthest;

        /// <summary>The longest distance from the frame's origin that the bounding sphere of a body that can move reaches, in meters.</summary>
        public double Extent;

        /// <summary>The highest speed relative to the frame of a body that can move, in m/s.</summary>
        public double Fastest;

        public readonly void Deconstruct(out FrameHandle frame, out int start, out int end) => (frame, start, end) = (Frame, Start, End);
    }
    // The leave clock of each body that the last update kept in its semantic frame's own system although it needs a frame by
    // itself (see NumericFrameRules.LeaveClock), and the clocks that the current update builds. An update keeps only the
    // clocks of its own bodies, so the clock of a body that is gone from its input goes with it.
    private Dictionary<Entity, double> leaveClocks = new();
    private Dictionary<Entity, double> nextLeaveClocks = new();
    // The thrust clock of each body of the last update that is not static and has thrust time (see
    // NumericFrameRules.ThrustClock), and the clocks that the current update builds. A body without an entry has a clock
    // at zero. An update keeps only the clocks of its own bodies, so the clock of a body that is gone from its input goes
    // with it.
    private Dictionary<Entity, (double ThrustTime, double QuietTime)> thrustClocks = new();
    private Dictionary<Entity, (double ThrustTime, double QuietTime)> nextThrustClocks = new();

    /// <summary>
    /// The ghosts that the homes of groups keep of the Kepler hulls that the groups link but cannot share a home with, which
    /// the last <see cref="UpdateNumericFrames"/> chose (see <see cref="NumericGhost"/>). Physics reads them at each step.
    /// </summary>
    internal ReadOnlySpan<NumericGhost> AnchorGhosts => CollectionsMarshal.AsSpan(anchorGhosts);

    /// <summary>
    /// Runs the membership and nesting rules of numeric frames (docs/plans/numeric-frames.md, "Membership" and "Nesting for
    /// vast distances") once, over a sync point of <paramref name="dt"/> seconds. <paramref name="bodies"/> holds every
    /// entity with a body that is not attached, in entity slot order, and a frame owner twice: its interior in its frame,
    /// then its hull in the frame's parent. First the thrust clock of every body that is not static runs over the sync
    /// point (see <see cref="NumericFrameRules.ThrustClock"/>), and a group with a body that thrusts then uses the lower
    /// speeds of a group that thrusts (see <see cref="NumericBody.Thrusts"/>). Within each semantic frame, the update links every
    /// pair of bodies that <see cref="NumericFrameRules.MayTouch"/> links, also an anchor (see <see cref="NumericBody.Anchor"/>)
    /// and a group that falls. It holds the members of each numeric frame that are not anchors together until
    /// <see cref="NumericFrameRules.SplitClock"/> lets them split. An anchor counts as infinitely heavy (see
    /// <see cref="NumericFrameRules.Summarize"/>), so a group with one takes its home (docs/plans/numeric-frames.md,
    /// "Anchors"). So does the ghost of a body of another semantic frame, which no body of the group can move either: the
    /// update links each body to the place of each ghost that physics can keep in a frame of its semantic frame before the
    /// next update, that the body meets as physics has it (see <see cref="NumericFrameRules.MeetsGhost"/>) and that it can
    /// touch (see <see cref="GhostPlace"/>, ticket ter-4psc). The ghost of a body that sticks out of a semantic child lives
    /// in the home of the child's hull, so the body joins the hull's group. Any other ghost is fixed in the semantic frame's
    /// own system, so the body's group stays there. The hull of a Kepler frame that its orbit moves fast or far lives in an orbit
    /// frame of its own (see <see cref="NumericMode.Orbiting"/>), which the update makes for it, and the frame hangs at its
    /// origin. A group that links such a hull and no anchor at rest takes that orbit frame; a group that cannot take it,
    /// because it links an anchor at rest or an earlier such hull, lives in that other home, and the hull's frame stays its
    /// own and that home keeps a ghost of the hull (see <see cref="AnchorGhosts"/>).
    /// Each linked group gets the mode of <see cref="NumericFrameRules.GroupMode"/>, but a group that would rest in a new frame
    /// stays in its semantic frame's own system until <see cref="NumericFrameRules.LeaveClock"/> lets a member that the last
    /// update kept there leave. A group keeps the numeric frame that holds most of its mass, which drains and moves its
    /// origin; the bodies of the other groups move. Physics drops the ghosts in each home that the last update named for the
    /// ghost of an anchor and this one does not, when it applies the changes of this sync point. Then far frames nest.
    /// Each frame that the update creates, shifts or destroys goes into <paramref name="changes"/>, where physics picks it up.
    /// <para>
    /// The update hands the moves to <paramref name="applyMoves"/>, which applies them to the rows and hands this
    /// update's changes to physics. Only then does the update destroy the numeric frames that hold no body and no frame,
    /// because physics converts each move through the frame that the body leaves, and a batch for physics destroys frames
    /// before it moves bodies.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Every step runs in a fixed order: semantic frames by index, bodies in the order of <paramref name="bodies"/>, groups
    /// by their first body, frames in dense order. So two runs with the same input make the same frames in the same order.
    /// The merge test compares every pair of bodies in a semantic frame that has a numeric frame or a body that needs one,
    /// which costs the square of their count. The merge test of the places of ghosts compares each body of such a semantic
    /// frame with each place of a ghost there, and finds the bodies behind the places among the bodies of the semantic
    /// parent, children and siblings in the input.
    /// </remarks>
    internal void UpdateNumericFrames(ReadOnlySpan<NumericBody> bodies, double dt, PhysicsChanges changes, Action<List<NumericMove>> applyMoves)
    {
        Contract.Requires(dt >= 0);
        SortBySemanticFrame(bodies, dt);
        moves.Clear();
        anchorGhosts.Clear();
        nextLeaveClocks.Clear();
        semanticRanges.Clear();
        for (int start = 0; start < sortedBodies.Count;)
        {
            int end = start + 1;
            while (end < sortedBodies.Count && sortedBodies[end].Semantic == sortedBodies[start].Semantic)
                end++;
            semanticRanges.Add(new SemanticRange { Frame = sortedBodies[start].Semantic, Start = start, End = end });
            start = end;
        }
        // The update of a semantic frame reads the bodies of its semantic parent, children and siblings, which keep their
        // ranges.
        for (int r = 0; r < semanticRanges.Count; r++)
        {
            (_, int start, int end) = semanticRanges[r];
            if (NeedsWork(CollectionsMarshal.AsSpan(sortedBodies)[start..end]))
                UpdateSemanticFrame(start, end, dt, changes);
        }
        (leaveClocks, nextLeaveClocks) = (nextLeaveClocks, leaveClocks);
        NestFarFrames(changes);
        CountBodies();
        applyMoves(moves);
        CollectEmptyFrames(changes);
    }

    /// <summary>
    /// Fills <see cref="sortedBodies"/> with <paramref name="bodies"/>, sorted by semantic frame, each with the mode of its
    /// home and whether it thrusts, after its thrust clock has run over <paramref name="dt"/> seconds (see
    /// <see cref="Thrusts"/>), and with no ghost place that fixes it yet (see <see cref="NumericBody.MayTouchFixedGhost"/>).
    /// </summary>
    private void SortBySemanticFrame(ReadOnlySpan<NumericBody> bodies, double dt)
    {
        if (sortKeys.Length < bodies.Length)
        {
            sortKeys = new long[Math.Max(bodies.Length, 2 * sortKeys.Length)];
            links = new int[sortKeys.Length];
            groupOf = new int[sortKeys.Length];
            apart = new bool[sortKeys.Length];
        }
        for (int i = 0; i < bodies.Length; i++)
        {
            Contract.Requires(IsAlive(bodies[i].Home) && SemanticOf(bodies[i].Home) == bodies[i].Semantic);
            sortKeys[i] = ((long)bodies[i].Semantic.Index << 32) | (uint)i;
        }
        Array.Sort(sortKeys, 0, bodies.Length);
        sortedBodies.Clear();
        nextThrustClocks.Clear();
        // The rules read the mode of each body's home and whether it thrusts, which only the tree knows. A semantic frame's
        // mode is None and most bodies do not thrust, which the record has already, so most bodies need no copy. The update
        // of the body's semantic frame finds the ghosts that fix it.
        for (int i = 0; i < bodies.Length; i++)
        {
            ref readonly NumericBody body = ref bodies[(int)(uint)sortKeys[i]];
            NumericMode homeMode = body.Home == body.Semantic ? NumericMode.None : numeric[body.Home.Index];
            bool thrusts = Thrusts(body, dt);
            sortedBodies.Add(homeMode == body.HomeMode && thrusts == body.Thrusts && !body.MayTouchFixedGhost ? body
                : body with { HomeMode = homeMode, Thrusts = thrusts, MayTouchFixedGhost = false });
        }
        (thrustClocks, nextThrustClocks) = (nextThrustClocks, thrustClocks);
    }

    /// <summary>
    /// Runs the thrust clock of <paramref name="body"/> over <paramref name="dt"/> seconds (see
    /// <see cref="NumericFrameRules.ThrustClock"/>) into the clocks of the current update, and returns true when the body
    /// thrusts: a character always does, and any other body when its clock says so. A clock without thrust time is a clock
    /// at zero, so it gets no entry. A static body has no clock: no force moves it. That is also the interior of a frame
    /// owner, so the owner's one clock is the one of its hull, and a hull that parks, with the static motion, drops its
    /// clock.
    /// </summary>
    private bool Thrusts(in NumericBody body, double dt)
    {
        if (body.Static)
            return false;
        thrustClocks.TryGetValue(body.Entity, out (double ThrustTime, double QuietTime) clock);
        (bool thrusts, double thrustTime, double quietTime) =
            NumericFrameRules.ThrustClock(body.ThrustAcceleration, clock.ThrustTime, clock.QuietTime, dt, NumericOptions);
        if (thrustTime > 0)
            nextThrustClocks[body.Entity] = (thrustTime, quietTime);
        return body.Character || thrusts;
    }

    /// <summary>
    /// True when a body of the semantic frame is in a numeric frame or needs one by itself. A group's common speed and
    /// center never pass those of its farthest and fastest body, so a semantic frame where no body needs a frame has no
    /// group that does. A group of free bodies can join a body that thrusts, though, and then use the lower speeds of a
    /// group that thrusts although no body passes the speeds of its own kind: a free body at 60 m/s and a character at
    /// 40 m/s thrust together at 50 m/s. So when a body of the semantic frame thrusts, every body is tested with the speeds
    /// of a body that thrusts, which are never above those of a free body (see <see cref="NumericFrameOptions.Validate"/>).
    /// </summary>
    private bool NeedsWork(ReadOnlySpan<NumericBody> bodies)
    {
        bool thrusts = false;
        foreach (ref readonly NumericBody body in bodies)
        {
            if (body.Home != body.Semantic)
                return true;
            thrusts |= body.Thrusts;
        }
        foreach (ref readonly NumericBody body in bodies)
            if (NumericFrameRules.BodyMode(body, thrusts, NumericOptions) != NumericMode.None)
                return true;
        return false;
    }

    private void UpdateSemanticFrame(int start, int end, double dt, PhysicsChanges changes)
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        for (int i = start; i < end; i++)
            links[i] = i;
        // Before any frame of the semantic frame shifts and any of its bodies moves, while the tree holds where each ghost
        // lives.
        CollectGhostPlaces(start, end);
        // Every body links to every group that it may touch, also an anchor to a group that falls, so a static wall is a
        // real body of a fast crate's system, and so is a Kepler hull in the orbit frame that its group takes
        // (docs/plans/numeric-frames.md, "Anchors"). This comes before the hold, so that members that an anchor links count
        // as together.
        for (int i = start; i < end; i++)
            for (int j = i + 1; j < end; j++)
                if (MayTouch(bodies[i], bodies[j]))
                    Union(i, j);
        // Every body links to the place of each ghost that it meets and can touch, which no body of the group can move, so it
        // counts as infinitely heavy.
        LinkGhostPlaces(start, end);
        HoldFramesTogether(start, end, dt);
        GroupBodies(start, end);
        HoldInSemanticSystem(dt);
        ChooseKeepers();
        // Every shift lands before any move: a body that leaves a shifted frame leaves it from its shifted state. An orbit
        // frame never drains or moves its origin: its orbit gives it its place and velocity.
        foreach (BodyGroup group in groups)
            if (group.Keeps && group.Mode != NumericMode.Orbiting)
                DrainFrame(group, changes);
        foreach (BodyGroup group in groups)
            MoveHome(group, changes);
        // After the moves, which give each group its home and each Kepler hull apart from its group its orbit frame.
        KeepAnchorGhosts();
    }

    /// <summary>
    /// The merge test of two bodies of one semantic frame, with the position of <paramref name="b"/> relative to
    /// <paramref name="a"/> taken through the lowest common ancestor of their homes, from their positions in their homes.
    /// So no large number enters between two bodies near each other, far from their semantic frame's origin.
    /// </summary>
    private bool MayTouch(in NumericBody a, in NumericBody b)
    {
        Double3 between = a.Home == b.Home ? Double3.Zero : Relative(b.Home, a.Home).Position;
        return NumericFrameRules.MayTouch(a, b, between + b.HomePosition - a.HomePosition, NumericOptions);
    }

    /// <summary>
    /// Keeps the members of each numeric frame that are not anchors in one group until they have been apart for the split
    /// cooldown. Then they may split, and the frame's clock starts again.
    /// </summary>
    private void HoldFramesTogether(int start, int end, double dt)
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        numericHomes.Clear();
        for (int i = start; i < end; i++)
            if (!bodies[i].Anchor && bodies[i].Home != bodies[i].Semantic && !numericHomes.Contains(bodies[i].Home))
                numericHomes.Add(bodies[i].Home);
        foreach (FrameHandle frame in numericHomes)
        {
            int first = -1;
            bool together = true;
            for (int i = start; i < end && together; i++)
            {
                if (bodies[i].Home != frame || bodies[i].Anchor)
                    continue;
                if (first < 0)
                    first = i;
                else
                    together = Find(i) == Find(first);
            }
            int id = Node(frame).Id;
            (bool hold, apartTime[id]) = NumericFrameRules.SplitClock(together, apartTime[id], dt, NumericOptions);
            if (!hold)
                continue;
            for (int i = first + 1; i < end; i++)
                if (bodies[i].Home == frame && !bodies[i].Anchor)
                    Union(first, i);
        }
    }

    /// <summary>
    /// Gives the home of each group a ghost of each Kepler hull of the group that lives apart from it, in an orbit frame of
    /// its own (see <see cref="GroupBodies"/>): no home holds both the group's other anchors and that hull at rest, so the
    /// hull's ghost stands for it there (docs/plans/numeric-frames.md, "Anchors"). Each hull is in one group, so no pair is
    /// named twice.
    /// </summary>
    private void KeepAnchorGhosts()
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        foreach (BodyGroup group in groups)
        {
            FrameHandle home = FrameHandle.Null;
            for (int i = group.Start; i < group.End; i++)
            {
                if (!apart[i])
                    continue;
                if (home.IsNull)
                    home = GroupHome(group);
                Contract.Assert(Numeric(bodies[i].Home) == NumericMode.Orbiting && bodies[i].Home != home);
                var ghost = new NumericGhost(bodies[i].Entity, home);
                Contract.Assert(!anchorGhosts.Contains(ghost));
                anchorGhosts.Add(ghost);
            }
        }
    }

    /// <summary>
    /// The home of a group after <see cref="MoveHome"/>: the home of its members that do not live apart, or the orbit frame
    /// of its orbiter, which a group whose every member is a Kepler hull with a frame of its own takes.
    /// </summary>
    private FrameHandle GroupHome(in BodyGroup group)
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        if (group.Motion.Orbits)
            return bodies[group.Start + group.Motion.Orbiter].Home;
        for (int i = group.Start; i < group.End; i++)
            if (!apart[i])
                return bodies[i].Home;
        Contract.Assert(false, "a group that does not take an orbit frame has a member that does not live apart");
        return FrameHandle.Null;
    }

    /// <summary>
    /// Fills <see cref="ghostPlaces"/> with the places of the ghosts that physics can keep, before the next update, in the
    /// frames of the semantic frame whose bodies are <c>sortedBodies[start..end]</c>: the ghosts that no body of that frame
    /// can move, so the merge test counts them as infinitely heavy (docs/plans/numeric-frames.md, "Bubbles and ghosts").
    /// <list type="bullet">
    /// <item>A body of a semantic child that can stick out of the child's bubble (see
    /// <see cref="AddStickOutPlaces"/>), whose ghost lives in the child's tree parent, the home of the child's hull.</item>
    /// <item>A body of the semantic parent that can touch the bubble (see <see cref="AddParentPlaces"/>), and a body of a
    /// semantic sibling that can stick out of its own bubble and touch this one (see <see cref="AddSiblingPlaces"/>), whose
    /// ghosts live in the semantic frame's own system.</item>
    /// </list>
    /// Each place comes from the tree and from the positions and velocities of the bodies in their semantic frames, never
    /// from another semantic frame's homes or places in them, which an earlier update of that frame can change. It
    /// runs before any frame of this semantic frame shifts and any of its bodies moves, while the tree holds where each
    /// ghost lives.
    /// </summary>
    private void CollectGhostPlaces(int start, int end)
    {
        ghostPlaces.Clear();
        FrameHandle semantic = sortedBodies[start].Semantic;
        FrameHandle parent = Parent(semantic);
        ReadOnlySpan<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        Span<SemanticRange> ranges = CollectionsMarshal.AsSpan(semanticRanges);
        for (int r = 0; r < ranges.Length; r++)
        {
            (FrameHandle frame, int from, int to) = ranges[r];
            FrameHandle frameParent = Parent(frame);
            if (frameParent == semantic)
                AddStickOutPlaces(frame, bodies[from..to], start, end);
            else if (frame == parent)
                AddParentPlaces(semantic, bodies[from..to]);
            else if (!parent.IsNull && frameParent == parent && frame != semantic)
                AddSiblingPlaces(semantic, ref ranges[r]);
        }
    }

    /// <summary>
    /// Adds the place of each body of <paramref name="members"/>, the bodies of the semantic child <paramref name="child"/>,
    /// that can stick out of the child's bubble before the next update (see <see cref="NumericFrameRules.MayStickOut"/>).
    /// Physics gives such a body a ghost in the child's tree parent, the home of the child's hull, wherever the update moves
    /// the hull. So the place's target is the hull, the body among <c>sortedBodies[start..end]</c> that owns the child, and
    /// a body that can touch the ghost joins the hull's group. A child without a hull hangs in the semantic frame (see the
    /// invariants of <see cref="FrameTree"/>), which holds the ghost fixed. The place has the body's radius, and its place
    /// and velocity relative to the semantic frame go through the child's pose, rotation and spin.
    /// </summary>
    private void AddStickOutPlaces(FrameHandle child, ReadOnlySpan<NumericBody> members, int start, int end)
    {
        FrameBounds bubble = Bounds(child);
        FrameHandle home = TreeParent(child);
        DPose pose = TreeLocalPose(child);
        // The hull and the child's motion, which the first body that can stick out looks up for all of them.
        bool prepared = false;
        int hull = -1;
        Double3 velocity = default, spin = default;
        foreach (ref readonly NumericBody body in members)
        {
            if (!NumericFrameRules.MayStickOut(bubble, body, NumericOptions))
                continue;
            if (!prepared)
            {
                for (int i = start; i < end && hull < 0; i++)
                    if (sortedBodies[i].OwnedFrame == child)
                        hull = i;
                velocity = LinearVelocity(child);
                spin = (Double3)AngularVelocity(child);
                prepared = true;
            }
            ghostPlaces.Add(new GhostPlace(home, pose.TransformPoint(body.Position),
                VelocityToParent(pose, velocity, spin, body.Position, body.Velocity), body.Radius, hull, body.Static, body.Sensor));
        }
    }

    /// <summary>
    /// Adds the place of each body of <paramref name="members"/>, the bodies of the semantic parent of
    /// <paramref name="semantic"/>, that can touch the bubble of <paramref name="semantic"/> before the next update (see
    /// <see cref="NumericFrameRules.MayTouchBubble"/>): physics gives such a body a ghost in the frame's own system while it
    /// touches the bubble without being in it (ticket ter-tenw). A body that can move counts, and the hull of a frame, which
    /// reaches as far as its own bubble, as physics has it. A static body that is not a hull has no ghost in a bubble, and
    /// the frame's own hull lives inside it. The ghost is fixed in the frame's own system. A first test on the distance from
    /// the bubble's center skips each body that cannot reach the bubble (see <see cref="NumericFrameRules.MayReachBubble"/>),
    /// before the full test turns the body's place and velocity into the frame's axes.
    /// </summary>
    private void AddParentPlaces(FrameHandle semantic, ReadOnlySpan<NumericBody> members)
    {
        FrameBounds bubble = Bounds(semantic);
        DPose pose = LocalPose(semantic);
        Double3 velocity = LinearVelocity(semantic), spin = (Double3)AngularVelocity(semantic);
        double radius = bubble.MaxRadius, spinRate = spin.Length, center = pose.Position.Length;
        foreach (ref readonly NumericBody body in members)
        {
            if (body.OwnedFrame == semantic || (body.Static && body.OwnedFrame.IsNull))
                continue;
            double reach = body.OwnedFrame.IsNull ? body.Radius : Math.Max(body.Radius, Bounds(body.OwnedFrame).MaxRadius);
            // The rounding scales with the distances of the body and the center from the semantic parent's origin. The body's is
            // at most the center's plus the distance between them.
            double distance = (body.Position - pose.Position).Length;
            if (!NumericFrameRules.MayReachBubble(distance, radius + reach, (body.Velocity - velocity).Length + spinRate * distance,
                2 * center + distance, NumericOptions))
                continue;
            Double3 position = pose.InverseTransformPoint(body.Position);
            Double3 relative = VelocityFromParent(pose, velocity, spin, body.Position, body.Velocity);
            if (NumericFrameRules.MayTouchBubble(bubble, position, relative, reach, NumericOptions))
                ghostPlaces.Add(new GhostPlace(semantic, position, relative, body.Radius, -1, body.Static, body.Sensor));
        }
    }

    /// <summary>
    /// Adds the place of each body of <paramref name="sibling"/>, the bodies of a semantic sibling of
    /// <paramref name="semantic"/>, that can stick out of the sibling's bubble and touch the bubble of
    /// <paramref name="semantic"/> before the next update: physics gives such a body a ghost in the frame's own system, as
    /// the body is partly in their semantic parent (ticket ter-tenw). The ghost is fixed in the frame's own system. A first
    /// test on the distance between the two centers skips the whole sibling when none of its bodies can reach the bubble
    /// (see <see cref="NumericFrameRules.MayReachBubble"/>), from bounds of where its bodies are (see
    /// <see cref="SemanticRange"/>).
    /// </summary>
    private void AddSiblingPlaces(FrameHandle semantic, ref SemanticRange sibling)
    {
        if (!sibling.Bounded)
            Bound(ref sibling);
        if (sibling.Farthest < 0)
            return;
        FrameBounds own = Bounds(sibling.Frame), bubble = Bounds(semantic);
        DPose siblingPose = LocalPose(sibling.Frame), pose = LocalPose(semantic);
        Double3 siblingVelocity = LinearVelocity(sibling.Frame), siblingSpin = (Double3)AngularVelocity(sibling.Frame);
        Double3 velocity = LinearVelocity(semantic), spin = (Double3)AngularVelocity(semantic);
        // A body of the sibling moves relative to the bubble's frame at most at its own speed, plus the speed that the
        // sibling's spin gives it, plus the sibling's speed relative to the frame, plus the speed that the frame's spin gives
        // it. The frame's spin gives a body more speed the farther it is from the frame's center. A body beyond the sibling's
        // center gets more of that speed, but it is also that much farther from the bubble, so the spin at the distance of
        // the two centers is enough for the test.
        double distance = (siblingPose.Position - pose.Position).Length;
        double speedBound = sibling.Fastest + siblingSpin.Length * sibling.Farthest + (siblingVelocity - velocity).Length + spin.Length * distance;
        if (!NumericFrameRules.MayReachBubble(distance, bubble.MaxRadius + sibling.Extent, speedBound,
            siblingPose.Position.Length + pose.Position.Length + sibling.Farthest, NumericOptions))
            return;
        foreach (ref readonly NumericBody body in CollectionsMarshal.AsSpan(sortedBodies)[sibling.Start..sibling.End])
        {
            if (!NumericFrameRules.MayStickOut(own, body, NumericOptions))
                continue;
            Double3 inParent = siblingPose.TransformPoint(body.Position);
            Double3 velocityInParent = VelocityToParent(siblingPose, siblingVelocity, siblingSpin, body.Position, body.Velocity);
            Double3 position = pose.InverseTransformPoint(inParent);
            Double3 relative = VelocityFromParent(pose, velocity, spin, inParent, velocityInParent);
            if (NumericFrameRules.MayTouchBubble(bubble, position, relative, body.Radius, NumericOptions))
                ghostPlaces.Add(new GhostPlace(semantic, position, relative, body.Radius, -1, body.Static, body.Sensor));
        }
    }

    /// <summary>
    /// Fills in the bounds of <paramref name="range"/> from its bodies that are not static, the only ones that can stick out
    /// of its bubble (see <see cref="NumericFrameRules.MayStickOut"/>).
    /// </summary>
    private void Bound(ref SemanticRange range)
    {
        double farthest = -1, extent = -1, fastest = 0;
        foreach (ref readonly NumericBody body in CollectionsMarshal.AsSpan(sortedBodies)[range.Start..range.End])
        {
            if (body.Static)
                continue;
            double distance = body.Position.Length;
            farthest = Math.Max(farthest, distance);
            extent = Math.Max(extent, distance + body.Radius);
            fastest = Math.Max(fastest, body.Velocity.Length);
        }
        (range.Farthest, range.Extent, range.Fastest, range.Bounded) = (farthest, extent, fastest, true);
    }

    /// <summary>
    /// Links each body of <c>sortedBodies[start..end]</c> to each place of <see cref="ghostPlaces"/> whose ghost it meets (see
    /// <see cref="NumericFrameRules.MeetsGhost"/>) and that it can touch, by the merge test of two bodies: it joins the group
    /// of the place's hull, whose home holds the ghost, or it stays in its semantic frame's own system with the ghost there
    /// (see <see cref="NumericBody.MayTouchFixedGhost"/>).
    /// </summary>
    private void LinkGhostPlaces(int start, int end)
    {
        if (ghostPlaces.Count == 0)
            return;
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        for (int i = start; i < end; i++)
        {
            ref NumericBody body = ref bodies[i];
            foreach (GhostPlace place in ghostPlaces)
            {
                if (!NumericFrameRules.MeetsGhost(body, place) || !MayTouch(body, place))
                    continue;
                if (place.Target >= 0)
                    Union(i, place.Target);
                else if (!body.MayTouchFixedGhost)
                    body = body with { MayTouchFixedGhost = true };
            }
        }
    }

    /// <summary>
    /// The merge test of a body and the place of a ghost of its semantic frame, with the place relative to the body taken
    /// through the lowest common ancestor of their homes, as for two bodies (see <see cref="MayTouch(in NumericBody, in NumericBody)"/>).
    /// </summary>
    private bool MayTouch(in NumericBody body, in GhostPlace place)
    {
        Double3 between = body.Home == place.Home ? Double3.Zero : Relative(place.Home, body.Home).Position;
        return NumericFrameRules.MayTouch(body, place, between + place.HomePosition - body.HomePosition, NumericOptions);
    }

    /// <summary>
    /// Numbers the groups by their first body, puts the bodies of the range in the order of their groups, keeping their
    /// order within a group, sums the motion of each group, and marks the Kepler hulls that live apart from their groups.
    /// </summary>
    private void GroupBodies(int start, int end)
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        groups.Clear();
        for (int i = start; i < end; i++)
            groupOf[i] = -1;
        // The root of a set is its body with the lowest index, so it comes first.
        for (int i = start; i < end; i++)
        {
            int root = Find(i);
            if (groupOf[root] < 0)
            {
                groupOf[root] = groups.Count;
                groups.Add(default);
            }
            groupOf[i] = groupOf[root];
        }
        // A counting sort: each group's range starts after the bodies of the groups before it.
        Span<BodyGroup> spans = CollectionsMarshal.AsSpan(groups);
        for (int i = start; i < end; i++)
            spans[groupOf[i]].End++;
        int next = start;
        foreach (ref BodyGroup group in spans)
        {
            int size = group.End;
            group.Start = group.End = next;
            next += size;
        }
        bodyScratch.Clear();
        bodyScratch.AddRange(bodies[start..end]);
        for (int i = start; i < end; i++)
            bodies[spans[groupOf[i]].End++] = bodyScratch[i - start];
        foreach (ref BodyGroup group in spans)
        {
            group.Motion = NumericFrameRules.Summarize(bodies[group.Start..group.End], homeMasses, NumericOptions);
            NumericMode current = group.Motion.Frame.IsNull ? NumericMode.None : Numeric(group.Motion.Frame);
            group.Mode = NumericFrameRules.GroupMode(current, group.Motion, NumericOptions);
            // A Kepler hull that lives in an orbit frame of its own and that its group does not follow there stays apart:
            // its orbit moves it, so no group can take it into another home.
            for (int i = group.Start; i < group.End; i++)
                apart[i] = NumericFrameRules.OrbitsInFrame(bodies[i], NumericOptions) &&
                    !(group.Motion.Orbits && i - group.Start == group.Motion.Orbiter);
        }
    }

    /// <summary>
    /// Keeps each group that would rest in a new frame in its semantic frame's own system while a member that the last update
    /// kept there has been about to leave for less than the split cooldown (see <see cref="NumericFrameRules.LeaveClock"/>).
    /// Then it gives a clock to each body that is not an anchor, that needs a frame by itself and whose group stays in the
    /// system. A body that the system did not keep before, such as a new one, gets its frame at once.
    /// </summary>
    private void HoldInSemanticSystem(double dt)
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        foreach (ref BodyGroup group in CollectionsMarshal.AsSpan(groups))
        {
            ReadOnlySpan<NumericBody> members = bodies[group.Start..group.End];
            NumericMode wanted = group.Mode;
            bool hold = false;
            foreach (ref readonly NumericBody body in members)
                if (!body.Anchor && leaveClocks.TryGetValue(body.Entity, out double apart))
                    hold |= NumericFrameRules.LeaveClock(wanted, group.Motion.Frame, apart, dt, NumericOptions).Hold;
            if (hold)
                group.Mode = NumericMode.None;
            if (group.Mode != NumericMode.None)
                continue;
            foreach (ref readonly NumericBody body in members)
            {
                if (body.Anchor || NumericFrameRules.BodyMode(body, NumericOptions) == NumericMode.None)
                    continue;
                leaveClocks.TryGetValue(body.Entity, out double apart);
                nextLeaveClocks[body.Entity] = NumericFrameRules.LeaveClock(wanted, group.Motion.Frame, apart, dt, NumericOptions).Apart;
            }
        }
    }

    /// <summary>
    /// Gives each numeric frame to the group with the most mass in it, or the first such group on a tie. The other groups
    /// in it split off.
    /// </summary>
    private void ChooseKeepers()
    {
        keepers.Clear();
        Span<BodyGroup> spans = CollectionsMarshal.AsSpan(groups);
        for (int g = 0; g < spans.Length; g++)
        {
            ref BodyGroup group = ref spans[g];
            if (group.Mode == NumericMode.None || group.Motion.Frame.IsNull)
                continue;
            if (keepers.TryGetValue(group.Motion.Frame, out int other))
            {
                if (spans[other].Motion.FrameMass >= group.Motion.FrameMass)
                    continue;
                spans[other].Keeps = false;
            }
            keepers[group.Motion.Frame] = g;
            group.Keeps = true;
        }
    }

    /// <summary>
    /// Gives the kept frame its mode, the velocity of <see cref="NumericFrameRules.DrainVelocity"/> and the origin of
    /// <see cref="NumericFrameRules.Origin"/>.
    /// </summary>
    private void DrainFrame(in BodyGroup group, PhysicsChanges changes)
    {
        FrameHandle frame = group.Motion.Frame;
        NumericMode mode = Numeric(frame);
        // The frame's origin in the semantic frame, where the group's center is. A nesting frame between them does not move.
        Double3 position = HomeOffset(frame, sortedBodies[group.Start].Semantic);
        Double3 velocity = linearVelocity[Node(frame).Id];
        Double3 newVelocity = NumericFrameRules.DrainVelocity(mode, group.Mode, velocity, group.Motion.Velocity, NumericOptions);
        Double3 newPosition = NumericFrameRules.Origin(position, group.Motion.Center, NumericOptions);
        if (group.Mode == NumericMode.Falling && mode == NumericMode.Resting)
            SetNumericMode(frame, NumericMode.Falling);
        if (newPosition != position || newVelocity != velocity)
        {
            ShiftNumeric(frame, newPosition - position, newVelocity - velocity);
            changes.FramesShifted.Add(new FrameShift(frame, newPosition - position, newVelocity - velocity));
        }
        if (group.Mode == NumericMode.Resting && mode == NumericMode.Falling)
            SetNumericMode(frame, NumericMode.Resting);
    }

    /// <summary>
    /// Moves every body of the group that is not in its home there, making a new frame when the group needs one. The frame
    /// that a moving body owns goes under the new home, because a hull lives in the system of its frame's tree parent. A
    /// body that owns a frame that physics does not drive and that no orbit frame holds goes back to its semantic frame
    /// this way: its group gets the mode None. A group that takes the orbit frame of its orbiter, a Kepler hull, gets a new
    /// one for the hull when the hull has none (see <see cref="CreateOrbitFrame"/>). Each other Kepler hull that lives in
    /// an orbit frame of its own stays apart from the group, in that frame, which it gets when it has none. Those hulls go
    /// first, in the order of the members, and then the group, so that two runs make the same frames in the same order;
    /// an orbit frame follows its own orbit, so no hull's frame depends on where the group goes.
    /// </summary>
    private void MoveHome(in BodyGroup group, PhysicsChanges changes)
    {
        Span<NumericBody> members = CollectionsMarshal.AsSpan(sortedBodies)[group.Start..group.End];
        for (int i = 0; i < members.Length; i++)
            if (apart[group.Start + i] && members[i].HomeMode != NumericMode.Orbiting)
                MoveIntoOrbitFrame(ref members[i], changes);
        FrameHandle semantic = members[0].Semantic;
        FrameHandle home = group.Mode == NumericMode.None ? semantic : group.Keeps ? group.Motion.Frame : FrameHandle.Null;
        bool anyMoves = false;
        for (int i = 0; i < members.Length; i++)
            anyMoves |= !apart[group.Start + i] && members[i].Home != home;
        if (!anyMoves)
            return;
        if (home.IsNull)
        {
            Double3 velocity = group.Mode == NumericMode.Falling ? group.Motion.Velocity : Double3.Zero;
            home = group.Mode == NumericMode.Orbiting ? CreateOrbitFrame(members[group.Motion.Orbiter])
                : CreateNumeric(semantic, group.Motion.Center, velocity, group.Mode);
            changes.FramesCreated.Add(home);
        }
        for (int i = 0; i < members.Length; i++)
        {
            ref NumericBody body = ref members[i];
            if (apart[group.Start + i] || body.Home == home)
                continue;
            // A Static or Spin hull, or a Kepler hull without an orbit frame, lives in its semantic frame's own system.
            Contract.Assert(!body.Fixed || home == semantic || group.Mode == NumericMode.Orbiting && i == group.Motion.Orbiter);
            if (!body.OwnedFrame.IsNull)
                Reparent(body.OwnedFrame, home);
            moves.Add(new NumericMove(body.Entity, body.Home, home));
            body = body with { Home = home, HomeMode = numeric[home.Index] };
        }
    }

    /// <summary>
    /// Moves the hull <paramref name="body"/> of a Kepler frame, which lives apart from its group, into a new orbit frame of
    /// its own, with its frame.
    /// </summary>
    private void MoveIntoOrbitFrame(ref NumericBody body, PhysicsChanges changes)
    {
        FrameHandle orbitFrame = CreateOrbitFrame(body);
        changes.FramesCreated.Add(orbitFrame);
        moves.Add(new NumericMove(body.Entity, body.Home, orbitFrame));
        body = body with { Home = orbitFrame, HomeMode = NumericMode.Orbiting };
    }

    /// <summary>
    /// Keeps the position of every falling or resting frame in its tree parent within the nest distance. A far frame, in
    /// dense order:
    /// <list type="number">
    /// <item>Jumps its nesting frame by whole grid steps, when every frame in that nest is then within half the nest
    /// distance of it. The frames in the nest shift back by the same exact amount.</item>
    /// <item>Else moves under the nearest other nesting frame of its semantic frame within half the nest distance of it.</item>
    /// <item>Else moves under a new nesting frame at the grid point nearest it.</item>
    /// </list>
    /// </summary>
    private void NestFarFrames(PhysicsChanges changes)
    {
        treeFrames.Clear();
        GetAllInTree(treeFrames);
        foreach (FrameHandle frame in treeFrames)
        {
            // A nesting frame that this step makes is not in the list, and an earlier step can only have moved the frames in it.
            if (Numeric(frame) is not (NumericMode.Falling or NumericMode.Resting))
                continue;
            Double3 position = TreeLocalPose(frame).Position;
            if (NumericFrameRules.NeedsNest(position, NumericOptions))
                Nest(frame, position, changes);
        }
    }

    private void Nest(FrameHandle frame, Double3 position, PhysicsChanges changes)
    {
        FrameHandle parent = TreeParent(frame);
        bool nested = Numeric(parent) == NumericMode.Nesting;
        Double3 step = NumericFrameRules.GridPoint(position, NumericOptions.NestGrid);
        if (nested)
        {
            nestMembers.Clear();
            frameChildren.Clear();
            GetTreeChildren(parent, frameChildren);
            foreach (FrameHandle child in frameChildren)
                nestMembers.Add(TreeLocalPose(child).Position);
            if (NumericFrameRules.NestCanJump(CollectionsMarshal.AsSpan(nestMembers), step, NumericOptions))
            {
                ShiftNumeric(parent, step, Double3.Zero);
                changes.FramesShifted.Add(new FrameShift(parent, step, Double3.Zero));
                return;
            }
        }

        // The semantic frame that holds the nesting frames, and the place of the frame's parent in it: both grid points.
        FrameHandle semantic = nested ? TreeParent(parent) : parent;
        Double3 parentPlace = nested ? TreeLocalPose(parent).Position : Double3.Zero;
        nestPlaces.Clear();
        frameChildren.Clear();
        GetTreeChildren(semantic, frameChildren);
        foreach (FrameHandle child in frameChildren)
            if (child != parent && Numeric(child) == NumericMode.Nesting)
                nestPlaces.Add((child, TreeLocalPose(child).Position));
        FrameHandle nest = NumericFrameRules.NearestNest(CollectionsMarshal.AsSpan(nestPlaces), parentPlace, position, NumericOptions);
        if (nest.IsNull)
        {
            nest = CreateNumeric(semantic, parentPlace + step, Double3.Zero, NumericMode.Nesting);
            changes.FramesCreated.Add(nest);
        }
        // The difference of two grid points is exact, so only the last addition rounds, at the scale of the nest distance.
        MoveUnderNest(frame, nest, parentPlace - TreeLocalPose(nest).Position + position);
    }

    /// <summary>Counts, for each numeric frame, the bodies whose physics home it is now.</summary>
    private void CountBodies()
    {
        for (int i = 0; i < poses.Count; i++)
        {
            int id = poses.NodeAt(i).Id;
            if (numeric[id] != NumericMode.None)
                numericBodies[id] = 0;
        }
        foreach (NumericBody body in sortedBodies)
            if (body.Home != body.Semantic)
                numericBodies[Node(body.Home).Id]++;
    }

    /// <summary>
    /// Destroys every numeric frame that <see cref="NumericFrameRules.Collects"/>, children before parents, so that a
    /// nesting frame whose last frame goes away goes too.
    /// </summary>
    private void CollectEmptyFrames(PhysicsChanges changes)
    {
        treeFrames.Clear();
        GetAllInTree(treeFrames);
        for (int i = treeFrames.Count - 1; i >= 0; i--)
        {
            FrameHandle frame = treeFrames[i];
            if (!IsNumeric(frame) || !NumericFrameRules.Collects(numericBodies[frame.Index] > 0, HoldsFrame(frame)))
                continue;
            DestroyNumeric(frame);
            changes.FramesDestroyed.Add(frame);
        }
    }

    /// <summary>Destroys the numeric frame <paramref name="frame"/>, which holds no frame. The caller has moved its bodies out.</summary>
    private void DestroyNumeric(FrameHandle frame)
    {
        Contract.Requires(IsNumeric(frame) && !HoldsFrame(frame));
        int count = NumericCount;
        Destroy(frame);
        Contract.Ensures(!IsAlive(frame) && NumericCount == count - 1);
    }

    private bool HoldsFrame(FrameHandle frame)
    {
        frameChildren.Clear();
        GetTreeChildren(frame, frameChildren);
        return frameChildren.Count > 0;
    }

    private int Find(int i)
    {
        while (links[i] != i)
        {
            links[i] = links[links[i]];
            i = links[i];
        }
        return i;
    }

    /// <summary>Joins the two sets under the root with the lower index, so that the result never depends on the order of the calls.</summary>
    private void Union(int a, int b)
    {
        int ra = Find(a), rb = Find(b);
        if (ra < rb)
            links[rb] = ra;
        else if (rb < ra)
            links[ra] = rb;
    }

    /// <summary>
    /// Creates a numeric frame under <paramref name="parent"/>, at <paramref name="position"/> in the parent and with the
    /// axes of the parent, moving at <paramref name="velocity"/> relative to it. A numeric frame has no owner, no bubble,
    /// no gravity of its own and no rotation. A resting frame does not move in its parent, so its velocity must be zero.
    /// The public members that walk the tree (<see cref="Parent"/>, <see cref="GetChildren"/> and <see cref="GetAll"/>)
    /// never show it. Throws <see cref="InvalidOperationException"/> for a stale parent.
    /// </summary>
    private FrameHandle CreateNumeric(FrameHandle parent, Double3 position, Double3 velocity, NumericMode mode)
    {
        Contract.Requires(mode != NumericMode.None && Enum.IsDefined(mode));
        Contract.Requires(mode is NumericMode.Falling or NumericMode.Orbiting || velocity == Double3.Zero);
        Contract.Requires(!IsNumeric(parent));
        Contract.Requires(mode != NumericMode.Nesting || NumericFrameRules.OnGrid(position, NumericOptions.NestGrid));
        Node(parent);
        FrameHandle handle = AddFrame(Entity.Null, parent, new DPose(position, DQuaternion.Identity), default,
            GravityField.None, mode);
        LinearVelocityForWrite(handle.Index) = velocity;
        NumericCount++;
        Contract.Ensures(Numeric(handle) == mode && TreeParent(handle) == parent);
        return handle;
    }

    // Scratch for ShiftNumeric: the child frames of the frame that it moves.
    private readonly List<FrameHandle> shiftChildren = new();

    /// <summary>
    /// Moves the numeric frame <paramref name="frame"/> by <paramref name="offset"/> in its parent and adds
    /// <paramref name="velocity"/> to its velocity there. Its child frames keep their poses and velocities relative to
    /// the parent, so they lose the same amounts in the frame. A numeric frame does not rotate, so nothing turns. The
    /// bodies in its system must lose them too: the caller tells physics. A resting frame takes no velocity.
    /// </summary>
    private void ShiftNumeric(FrameHandle frame, Double3 offset, Double3 velocity)
    {
        PoseNode node = Node(frame);
        Contract.Requires(numeric[node.Id] != NumericMode.None);
        Contract.Requires(numeric[node.Id] == NumericMode.Falling || velocity == Double3.Zero);
        // A nesting frame jumps by whole grid steps only, so that it stays exact.
        Contract.Requires(numeric[node.Id] != NumericMode.Nesting || NumericFrameRules.OnGrid(offset, NumericOptions.NestGrid));
        ref DPose local = ref LocalForWrite(node);
        local = new DPose(local.Position + offset, local.Rotation);
        LinearVelocityForWrite(node.Id) += velocity;
        List<FrameHandle> children = shiftChildren;
        children.Clear();
        GetTreeChildren(frame, children);
        foreach (FrameHandle child in children)
        {
            PoseNode childNode = Node(child);
            ref DPose childLocal = ref LocalForWrite(childNode);
            childLocal = new DPose(childLocal.Position - offset, childLocal.Rotation);
            LinearVelocityForWrite(childNode.Id) -= velocity;
        }
    }

    /// <summary>
    /// Moves the numeric frame <paramref name="frame"/> under the nesting frame <paramref name="nest"/>, at
    /// <paramref name="position"/> in it. The caller computes the position from grid offsets, which are exact, so the frame
    /// stays where it was relative to everything else. A nesting frame does not move in its parent, and it shares the
    /// semantic parent of the frame, so the frame keeps its velocity. No body changes system, so physics needs no change.
    /// </summary>
    private void MoveUnderNest(FrameHandle frame, FrameHandle nest, Double3 position)
    {
        PoseNode node = Node(frame), nestNode = Node(nest);
        Contract.Requires(numeric[node.Id] is NumericMode.Falling or NumericMode.Resting && numeric[nestNode.Id] == NumericMode.Nesting);
        Contract.Requires(SemanticOf(TreeParent(frame)) == TreeParent(nest));
        MoveNode(node, nestNode, keepWorldPose: false);
        LocalForWrite(node) = new DPose(position, DQuaternion.Identity);
        Contract.Ensures(TreeParent(frame) == nest);
    }

    /// <summary>
    /// Changes how the numeric frame <paramref name="frame"/> moves. A frame that comes to rest must have no velocity
    /// left in its parent, and it keeps no step acceleration.
    /// </summary>
    private void SetNumericMode(FrameHandle frame, NumericMode mode)
    {
        PoseNode node = Node(frame);
        Contract.Requires(numeric[node.Id] != NumericMode.None && mode is NumericMode.Falling or NumericMode.Resting);
        Contract.Requires(mode == NumericMode.Falling || linearVelocity[node.Id] == Double3.Zero);
        numeric[node.Id] = mode;
        if (mode == NumericMode.Resting)
        {
            stepAcceleration[node.Id] = Double3.Zero;
            stepPlan[node.Id] = null;
        }
    }

    /// <summary>
    /// Fixes the acceleration that each falling numeric frame keeps for the step that starts now, a step of
    /// <paramref name="step"/> seconds that applies <paramref name="stepIntents"/>: the field of its semantic frame at its
    /// origin, relative to its parent. That is the field minus the acceleration of the numeric frames above it, which the
    /// dense order, parents first, has fixed already. An orbit frame, which lies in its semantic frame, keeps the step
    /// record of its anchor's motion intent as its step plan (see <see cref="StepRecordInStep"/>, and "The step plan of a
    /// following frame" in docs/plans/rails-components.md), and the acceleration that lands it on the plan: the plan's chord
    /// minus the frame's velocity, divided by the step, so that the step of <see cref="AdvanceNumericFrames"/> turns the
    /// velocity into the chord and the place into the plan's, to its rounding. It keeps the plan's instant velocity past
    /// its chord as its own at the end of the step (see <see cref="HomeInstantVelocityOverChord"/>). The plan is kept for the
    /// step because an end pose needs the place where the step starts, and step 2 moves the frame before it reads the plan.
    /// An orbit frame whose anchor has no record for the step falls as a falling frame, with its anchor at rest in it. A
    /// resting frame has none. Call it after <see cref="Update"/> and before the physics step, which reads it through
    /// <see cref="EffectiveGravity"/>, while the tree stands where the step starts.
    /// </summary>
    internal void BeginNumericStep(ReadOnlySpan<PhysicsIntent> stepIntents, double step)
    {
        // The dense order puts parents first only after Update.
        Contract.Requires(!poses.WorldIsStale && step > 0);
        for (int i = 0; i < poses.Count; i++)
        {
            int id = poses.NodeAt(i).Id;
            stepPlan[id] = null;
            if (numeric[id] == NumericMode.Orbiting && StepRecordInStep(orbiter[id], stepIntents, step, out MotionSample plan))
            {
                stepPlan[id] = plan;
                stepAcceleration[id] = (plan.ChordVelocity - linearVelocity[id]) / step;
                continue;
            }
            if (numeric[id] is not (NumericMode.Falling or NumericMode.Orbiting))
                continue;
            FrameHandle frame = AtDense(i);
            FrameHandle semantic = SemanticOf(frame);
            FrameHandle parent = TreeParent(frame);
            stepAcceleration[id] = FieldOf(semantic.Index, HomeOffset(frame, semantic)) - HomeAcceleration(parent, semantic);
        }
    }

    /// <summary>
    /// The step record of <paramref name="frame"/> in a step of <paramref name="step"/> seconds that applies
    /// <paramref name="stepIntents"/>: the record of its owner's motion intent there, in the frame's semantic parent, from the
    /// frame's pose in that parent where the step starts (see <see cref="PhysicsIntent.StepRecord"/>). False when the owner
    /// has no motion intent, or a steer target, which gives no record. Valid only while the tree stands where the step starts:
    /// before the step, during it, and at step 2 for a frame without a hull, which nothing moves between the two. The
    /// intents are by entity slot, as <see cref="PhysicsStepContext.Intents"/> holds them; a slot past their end holds none.
    /// </summary>
    internal bool StepRecordInStep(FrameHandle frame, ReadOnlySpan<PhysicsIntent> stepIntents, double step, out MotionSample record)
    {
        uint slot = frame.Entity.Index;
        if (slot >= (uint)stepIntents.Length || !stepIntents[(int)slot].Move.HasValue)
        {
            record = default;
            return false;
        }
        ref readonly PhysicsIntent intent = ref stepIntents[(int)slot];
        // The World converts every intent into its entity's frame at step 7b, the semantic parent of a frame that it owns,
        // and only a dynamic hull hands off during a step.
        Contract.Assert(intent.Frame == Parent(frame), $"The motion intent of {frame.Entity} is relative to {intent.Frame}, not to the parent {Parent(frame)} of its frame.");
        return intent.StepRecord(LocalPose(frame), step, out record);
    }

    /// <summary>
    /// What made the record that <paramref name="frame"/> takes of the step that applies <paramref name="stepIntents"/>, when
    /// the frame takes the record of its hull or of its owner's motion intent (docs/plans/rails-components.md, "The follow
    /// kind"): physics, when the hull was dynamic in the step, with or without a steer target; rails for a full sample and
    /// kinematic for an end pose, which a kinematic hull or a frame without a hull follows; and static otherwise, for a
    /// teleport, a static hull, which physics moves only by a teleport, and a frame without a motion intent. A write of the
    /// owner's transform wins over the motion intent, so its record is static whatever this says. The motion of the hull in
    /// the step comes from the intents (see <see cref="HullMotionInStep"/>), because the stored motion takes their changes only
    /// at step 4.
    /// </summary>
    internal FollowKind FollowKindInStep(FrameHandle frame, ReadOnlySpan<PhysicsIntent> stepIntents)
    {
        BodyMotion? motion = HullMotionInStep(frame, stepIntents);
        if (motion == BodyMotion.Dynamic)
            return FollowKind.Physics;
        uint slot = frame.Entity.Index;
        if (motion == BodyMotion.Static || slot >= (uint)stepIntents.Length)
            return FollowKind.Static;
        return stepIntents[(int)slot].Move switch
        {
            MotionSample => FollowKind.Rails,
            EndPose => FollowKind.Kinematic,
            _ => FollowKind.Static,
        };
    }

    /// <summary>
    /// Moves each falling numeric frame and each orbit frame over a step of <paramref name="dt"/> seconds, in doubles, with
    /// the scheme that Jolt uses for bodies: the velocity gains the step acceleration times the step, then the position
    /// gains the new velocity times the step. A resting frame does not move. An orbit frame ends the step on its anchor's
    /// step plan, to its rounding, and step 2 puts it there exactly (see <see cref="PlaceFollowingFrame"/>).
    /// </summary>
    internal void AdvanceNumericFrames(double dt)
    {
        Contract.Requires(dt >= 0 && double.IsFinite(dt));
        if (NumericCount == 0)
            return;
        for (int i = 0; i < poses.Count; i++)
        {
            PoseNode node = poses.NodeAt(i);
            int id = node.Id;
            if (numeric[id] is not (NumericMode.Falling or NumericMode.Orbiting))
                continue;
            ref Double3 velocity = ref LinearVelocityForWrite(id);
            velocity += stepAcceleration[id] * dt;
            ref DPose local = ref LocalForWrite(node);
            local = new DPose(local.Position + velocity * dt, local.Rotation);
        }
    }
}
