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
/// links to it. An anchor is a static body, a hull that is not dynamic, the interior of a frame owner, or a kinematic body
/// that lives in a following frame of its own (see <see cref="NumericFrameRules.IsAnchor"/>), and a group takes the home
/// of its anchors (ticket ter-dhkf). The only anchors whose homes a group cannot share are the anchor of a following frame,
/// a hull or a plain body, which moves with its motion intents, and an anchor at rest or another such anchor: the group
/// takes one home, and each anchor of a following frame that lives elsewhere keeps a ghost in it
/// (docs/plans/numeric-frames.md, "Anchors", "Following frames" and "Limits"). So <see cref="Anchor"/> is always the
/// anchor of a following frame, and <see cref="Frame"/> a frame of its semantic frame: the semantic frame itself, or a
/// numeric frame of it.
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
    /// Step 2 of the sync point, before the rows of the bodies take the end of the step, when the anchor of the following
    /// frame <paramref name="following"/> ended the step at the frame's origin, where physics aims it: the frame takes the
    /// place and the chord of its step plan, the anchor's record of the step that just ended in its semantic parent
    /// (docs/plans/rails-components.md, "The step plan of a following frame"). The landing acceleration of the step put it
    /// there to its rounding (see <see cref="BeginStep"/> and <see cref="AdvanceNumericFrames"/>), so its contents
    /// keep their places in it: the move is that rounding. A frame without a step plan stays where its fall put it. The
    /// anchor itself takes its own record: a hull's frame the record that physics releases for the hull (see
    /// <see cref="TakeStepRecords"/>), and a plain body the row of its body state.
    /// </summary>
    internal void PlaceFollowingFrame(FrameHandle following)
    {
        int id = Node(following).Id;
        Contract.Requires(numeric[id] == NumericMode.Following && !anchor[id].IsNull);
        if (stepPlan[id] is not { } plan)
            return;
        LocalForWrite(Node(following)) = new DPose(plan.Pose.Position, DQuaternion.Identity);
        LinearVelocityForWrite(id) = plan.ChordVelocity;
    }

    /// <summary>
    /// Step 2 of the sync point, after the anchor of the following frame <paramref name="following"/> took a record that put
    /// it away from the frame's origin: it teleported in the step, so physics aimed it at its step plan's place and not at
    /// that origin, or, in a step without a motion intent, left it at the teleport's place (see
    /// docs/plans/rails-components.md, "The step plan of a following frame"). <paramref name="record"/> is the anchor's
    /// record, relative to the frame: the one that physics released for a hull, or the body state of a plain body. The frame
    /// jumps to the anchor's place in the semantic parent: the step plan's place and chord, or without a plan the record's,
    /// converted into the semantic parent as for the proper acceleration (see
    /// <see cref="InSemanticParent(FrameHandle, in MotionSample)"/>). The frame of a hull goes back to the origin. Everything
    /// else in the frame keeps its place and velocity in the semantic frame, as without numeric frames: the other frames
    /// lose the frame's move, and the move goes into <paramref name="changes"/>, so that physics shifts the bodies in the
    /// frame's system, the anchor with them, and the World the rows.
    /// </summary>
    internal void JumpFollowingFrame(FrameHandle following, in MotionSample record, PhysicsChanges changes)
    {
        int id = Node(following).Id;
        Contract.Requires(numeric[id] == NumericMode.Following && !anchor[id].IsNull);
        MotionSample landing = stepPlan[id] ?? InSemanticParent(following, record);
        ref DPose local = ref LocalForWrite(Node(following));
        Double3 offset = landing.Pose.Position - local.Position, change = landing.ChordVelocity - linearVelocity[id];
        local = new DPose(landing.Pose.Position, DQuaternion.Identity);
        LinearVelocityForWrite(id) = landing.ChordVelocity;
        shiftChildren.Clear();
        GetTreeChildren(following, shiftChildren);
        foreach (FrameHandle child in shiftChildren)
        {
            PoseNode childNode = Node(child);
            ref DPose childLocal = ref LocalForWrite(childNode);
            if (child.Entity == anchor[id])
            {
                childLocal = new DPose(Double3.Zero, childLocal.Rotation);
                continue;
            }
            childLocal = new DPose(childLocal.Position - offset, childLocal.Rotation);
            LinearVelocityForWrite(childNode.Id) -= change;
        }
        changes.FramesShifted.Add(new FrameShift(following, offset, change));
    }

    /// <summary>
    /// True when <paramref name="home"/> is a following frame whose anchor is <paramref name="entity"/>
    /// (docs/plans/numeric-frames.md, "Following frames"): a hull whose frame hangs at its origin, or a plain body that is a
    /// member of its system there. False for a stale or null frame and for every other frame.
    /// </summary>
    internal bool Anchors(FrameHandle home, Entity entity) => !entity.IsNull && IsAlive(home) && anchor[home.Index] == entity;

    /// <summary>True when <paramref name="frame"/> hangs in its following frame: the frame of a hull that anchors its tree parent.</summary>
    internal bool InFollowingFrame(FrameHandle frame) => Anchors(TreeParent(frame), frame.Entity);

    /// <summary>
    /// Creates the following frame of <paramref name="body"/>, a body that is kinematic in the step that the update plans
    /// and that follows (<see cref="NumericBody.Follows"/>), its anchor, which the numeric update moves into it
    /// (docs/plans/numeric-frames.md, "Following frames"). The new frame lies in the anchor's semantic frame at the anchor's
    /// place, as the anchor's row holds it after the record of the step that ended. Its velocity is the chord that the
    /// numeric frames expect of a frame at the start of a step (docs/plans/rails-components.md, "The first step of a new
    /// following frame"): the chord of the anchor's motion over the step that ended, for a step as long as the next, of
    /// <paramref name="step"/> seconds. That is the anchor's instant velocity less half a step of its acceleration, as
    /// physics gives a body its chord and as a falling frame's instant velocity lies past its chord (see
    /// <see cref="HomeInstantVelocityOverChord"/>). The acceleration is the gravity at the anchor's place plus its proper
    /// acceleration, the rule of <see cref="ProperAccelerationFrom"/> read backward, with the semantic parent's rotation terms
    /// when it turns, as the rule adds them. The gravity takes the semantic parent's
    /// proper acceleration as the step that ended found it, the value that the rule of the anchor's own value read, because
    /// step 2 may have given the parent a new one (see <see cref="FieldTheStepFound"/>). So the first step plan lands the new
    /// frame on the anchor's motion with the acceleration of the step before, to the jerk of the motion, as every later plan
    /// does, and a first step that falls continues the anchor's free fall.
    /// <list type="bullet">
    /// <item>A hull: the place, the velocity, the rotation and the proper acceleration come from its frame's tree row, and the
    /// frame moves under the new frame, at its origin, keeping the rest of its instant velocity, so its place and velocity
    /// in the semantic parent stay what they were. A frame spawned at this sync point has no proper acceleration yet, so it
    /// starts as one that fell freely.</item>
    /// <item>A plain body: the place and the velocity in the semantic frame come from its transform row, as the World hands
    /// them to the update. A plain body owns no frame, so the tree keeps no proper acceleration of it, and its frame starts as
    /// one that fell freely. The caller moves the body into the frame.</item>
    /// </list>
    /// </summary>
    private FrameHandle CreateFollowingFrame(in NumericBody body, double step)
    {
        FrameHandle frame = body.OwnedFrame;
        Contract.Requires(body.Follows && step > 0);
        Contract.Requires(frame.IsNull || !IsNumeric(frame) && !InFollowingFrame(frame));
        FrameHandle semantic = body.Semantic;
        DPose local = default;
        Double3 position = body.Position, velocity = body.Velocity, properAccelerationOut = Double3.Zero;
        if (!frame.IsNull)
        {
            FrameHandle treeParent = TreeParent(frame);
            semantic = SemanticOf(treeParent);
            local = TreeLocalPose(frame);
            position = InSemantic(treeParent, semantic, local.Position);
            velocity = HomeVelocity(treeParent, semantic) + linearVelocity[frame.Index];
            // A numeric frame never turns, so the frame's rotation in its tree parent is its rotation in the semantic parent.
            properAccelerationOut = local.Rotation.Rotate((Double3)properAcceleration[frame.Index]);
        }
        Double3 acceleration = FieldTheStepFound(semantic.Index, position) + properAccelerationOut;
        ref readonly FrameTurns semanticTurns = ref turns[semantic.Index];
        if (semanticTurns.Turns)
        {
            // The rule reads the parent's rotation terms at the place where the step started, with the Coriolis term on the
            // chord. The row keeps the instant velocity, which is the chord to half a step of the acceleration.
            Double3 start = position - velocity * step;
            acceleration += semanticTurns.Centrifugal(start) + semanticTurns.Euler(start, step) + semanticTurns.Coriolis(velocity);
        }
        Double3 chord = velocity - acceleration * (step / 2);
        FrameHandle following = CreateNumeric(semantic, position, chord, NumericMode.Following);
        anchor[following.Index] = body.Entity;
        if (!frame.IsNull)
        {
            Reparent(frame, following);
            LocalForWrite(Node(frame)) = new DPose(Double3.Zero, local.Rotation);
            LinearVelocityForWrite(frame.Index) = velocity - chord;
            Contract.Ensures(InFollowingFrame(frame) && Parent(frame) == semantic);
        }
        Contract.Ensures(Anchors(following, body.Entity) && TreeParent(following) == semantic);
        return following;
    }

    /// <summary>
    /// The following frame <paramref name="following"/> lost its anchor (docs/plans/numeric-frames.md, "Following frames"):
    /// the update moved the anchor out, a hull's frame moved out or died, a hull got a motion other than kinematic for the
    /// next step (see <see cref="PlanHullMotion"/>), a plain anchor is no body of the frame at the update (see
    /// <see cref="DropLostAnchors"/>), or the frame itself goes. It turns into a falling frame that falls on from its
    /// place, at its velocity: the bodies in it keep their motion, and the next numeric update takes it as any falling frame.
    /// The ghosts of the anchor go, because the anchor no longer lives in a following frame (see <see cref="AnchorGhosts"/>).
    /// </summary>
    private void LeaveFollowingFrame(FrameHandle following)
    {
        int id = Node(following).Id;
        Contract.Requires(numeric[id] == NumericMode.Following && !anchor[id].IsNull);
        Entity anchorEntity = anchor[id];
        for (int i = anchorGhosts.Count - 1; i >= 0; i--)
            if (anchorGhosts[i].Anchor == anchorEntity)
                anchorGhosts.RemoveAt(i);
        numeric[id] = NumericMode.Falling;
        anchor[id] = Entity.Null;
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
    // True for each body, by index in sortedBodies, that follows in a following frame of its own which its group does not
    // take (see GroupBodies): it stays in or moves to that frame, apart from its group.
    private bool[] apart = [];
    // The anchor of each following frame, by entity: a hull whose frame hangs at its origin, or a plain body that is a member
    // of its system there (docs/plans/numeric-frames.md, "Following frames"). Null for every other frame.
    private Entity[] anchor = new Entity[64];
    // Scratch of DropLostAnchors: true for each following frame, by index, that a body of the update anchors.
    private bool[] anchorSeen = new bool[64];
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
    /// The ghosts that the homes of groups keep of the anchors of following frames that the groups link but cannot share a
    /// home with, which
    /// the last <see cref="UpdateNumericFrames"/> chose (see <see cref="NumericGhost"/>). Physics reads them at each step.
    /// </summary>
    internal ReadOnlySpan<NumericGhost> AnchorGhosts => CollectionsMarshal.AsSpan(anchorGhosts);

    /// <summary>
    /// Runs the membership and nesting rules of numeric frames (docs/plans/numeric-frames.md, "Membership" and "Nesting for
    /// vast distances") once, over a sync point of <paramref name="dt"/> seconds, before a physics step of
    /// <paramref name="step"/> seconds, whose length a new following frame needs for its velocity (see
    /// <see cref="CreateFollowingFrame"/>). <paramref name="bodies"/> holds every
    /// entity with a body that is not attached, in entity slot order, and a frame owner twice: its interior in its frame,
    /// then its hull in the frame's parent. First the thrust clock of every body that is not static runs over the sync
    /// point (see <see cref="NumericFrameRules.ThrustClock"/>), and a group with a body that thrusts then uses the lower
    /// speeds of a group that thrusts (see <see cref="NumericBody.Thrusts"/>). Within each semantic frame, the update links every
    /// pair of bodies that <see cref="NumericFrameRules.MayTouch"/> links, also an anchor (see <see cref="NumericFrameRules.IsAnchor"/>)
    /// and a group that falls. It holds the members of each numeric frame that are not anchors together until
    /// <see cref="NumericFrameRules.SplitClock"/> lets them split. An anchor counts as infinitely heavy (see
    /// <see cref="NumericFrameRules.Summarize"/>), so a group with one takes its home (docs/plans/numeric-frames.md,
    /// "Anchors"). So does the ghost of a body of another semantic frame, which no body of the group can move either: the
    /// update links each body to the place of each ghost that physics can keep in a frame of its semantic frame before the
    /// next update, that the body meets as physics has it (see <see cref="NumericFrameRules.MeetsGhost"/>) and that it can
    /// touch (see <see cref="GhostPlace"/>, ticket ter-4psc). The ghost of a body that sticks out of a semantic child lives
    /// in the home of the child's hull, so the body joins the hull's group. Any other ghost is fixed in the semantic frame's
    /// own system, so the body's group stays there. A kinematic body, hull or plain, that its motion intents move fast or far
    /// lives in a following frame of its own (see <see cref="NumericMode.Following"/>), which the update makes for it, at
    /// whose origin it rests, as the frame's anchor (docs/plans/numeric-frames.md, "Following frames"). First the update lets
    /// go each following frame whose anchor is no body in it (see <see cref="DropLostAnchors"/>). A group that links such an
    /// anchor and no anchor at rest takes that following frame; a group that cannot take it, because it links an anchor at
    /// rest or an earlier such anchor, lives in that other home, and the anchor keeps its own frame and that home keeps a
    /// ghost of it (see <see cref="AnchorGhosts"/>).
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
    internal void UpdateNumericFrames(ReadOnlySpan<NumericBody> bodies, double dt, double step, PhysicsChanges changes, Action<List<NumericMove>> applyMoves)
    {
        Contract.Requires(dt >= 0 && step > 0);
        DropLostAnchors(bodies);
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
                UpdateSemanticFrame(start, end, dt, step, changes);
        }
        (leaveClocks, nextLeaveClocks) = (nextLeaveClocks, leaveClocks);
        NestFarFrames(changes);
        CountBodies();
        applyMoves(moves);
        CollectEmptyFrames(changes);
    }

    /// <summary>
    /// Lets each following frame go whose anchor is no body of <paramref name="bodies"/> in it, before the rules of the
    /// update read the frames (docs/plans/numeric-frames.md, "Following frames"): a plain anchor that died, lost its body or
    /// handed off into a bubble in the step since the last update, which the tree learns only here. The frame falls on, and
    /// the bodies in it keep it as any falling frame (see <see cref="LeaveFollowingFrame"/>). A hull's frame lets its
    /// following frame go when it moves out or dies, so a hull that anchors a frame is always a body there.
    /// </summary>
    private void DropLostAnchors(ReadOnlySpan<NumericBody> bodies)
    {
        if (NumericCount == 0)
            return;
        foreach (ref readonly NumericBody body in bodies)
            if (Anchors(body.Home, body.Entity))
                anchorSeen[body.Home.Index] = true;
        for (int i = 0; i < poses.Count; i++)
        {
            int id = poses.NodeAt(i).Id;
            if (numeric[id] == NumericMode.Following && !anchorSeen[id])
                LeaveFollowingFrame(AtDense(i));
            anchorSeen[id] = false;
        }
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
            bool anchorsHome = homeMode == NumericMode.Following && anchor[body.Home.Index] == body.Entity;
            bool thrusts = Thrusts(body, dt);
            sortedBodies.Add(homeMode == body.HomeMode && anchorsHome == body.AnchorsHome && thrusts == body.Thrusts && !body.MayTouchFixedGhost ? body
                : body with { HomeMode = homeMode, AnchorsHome = anchorsHome, Thrusts = thrusts, MayTouchFixedGhost = false });
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

    private void UpdateSemanticFrame(int start, int end, double dt, double step, PhysicsChanges changes)
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        for (int i = start; i < end; i++)
            links[i] = i;
        // Before any frame of the semantic frame shifts and any of its bodies moves, while the tree holds where each ghost
        // lives.
        CollectGhostPlaces(start, end);
        // Every body links to every group that it may touch, also an anchor to a group that falls, so a static wall is a
        // real body of a fast crate's system, and so is the anchor of the following frame that its group takes
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
        // Every shift lands before any move: a body that leaves a shifted frame leaves it from its shifted state. A following
        // frame never drains or moves its origin: its anchor gives it its place and velocity.
        foreach (BodyGroup group in groups)
            if (group.Keeps && group.Mode != NumericMode.Following)
                DrainFrame(group, changes);
        foreach (BodyGroup group in groups)
            MoveHome(group, step, changes);
        // After the moves, which give each group its home and each body that follows apart from its group its following frame.
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
            if (!NumericFrameRules.IsAnchor(bodies[i], NumericOptions) && bodies[i].Home != bodies[i].Semantic && !numericHomes.Contains(bodies[i].Home))
                numericHomes.Add(bodies[i].Home);
        foreach (FrameHandle frame in numericHomes)
        {
            int first = -1;
            bool together = true;
            for (int i = start; i < end && together; i++)
            {
                if (bodies[i].Home != frame || NumericFrameRules.IsAnchor(bodies[i], NumericOptions))
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
                if (bodies[i].Home == frame && !NumericFrameRules.IsAnchor(bodies[i], NumericOptions))
                    Union(first, i);
        }
    }

    /// <summary>
    /// Gives the home of each group a ghost of each body of the group that follows and lives apart from it, in a following
    /// frame of its own (see <see cref="GroupBodies"/>): no home holds both the group's other anchors and that one at rest,
    /// so its ghost stands for it there (docs/plans/numeric-frames.md, "Anchors"). Each body is in one group, so no pair is
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
                Contract.Assert(Anchors(bodies[i].Home, bodies[i].Entity) && bodies[i].Home != home);
                var ghost = new NumericGhost(bodies[i].Entity, home);
                Contract.Assert(!anchorGhosts.Contains(ghost));
                anchorGhosts.Add(ghost);
            }
        }
    }

    /// <summary>
    /// The home of a group after <see cref="MoveHome"/>: the following frame of its anchor when it takes it, the semantic
    /// frame's own system when its mode is None, and else the home of its members that do not live apart. A group that the
    /// place of a fixed ghost fixes can have every member apart, in following frames of their own, and its home is still
    /// that system, where the ghost lives (see <see cref="NumericBody.MayTouchFixedGhost"/>).
    /// </summary>
    private FrameHandle GroupHome(in BodyGroup group)
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        if (group.Motion.Follows)
            return bodies[group.Start + group.Motion.Anchor].Home;
        if (group.Mode == NumericMode.None)
            return bodies[group.Start].Semantic;
        for (int i = group.Start; i < group.End; i++)
            if (!apart[i])
                return bodies[i].Home;
        Contract.Assert(false, "a group that does not take a following frame has a member that does not live apart");
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
    /// ghost lives. A static body, a kinematic body or a hull of the semantic parent or of a sibling reaches the frame's
    /// content: its bubble grown by the frame's excess, how far a body of the frame can stick out of it before the next
    /// update (see <see cref="NumericFrameRules.ContentExcess"/>), which the first of those places measures. A dynamic plain
    /// body or a character reaches only the bubble (see <see cref="NumericBody.GhostsReachContent"/>).
    /// </summary>
    private void CollectGhostPlaces(int start, int end)
    {
        ghostPlaces.Clear();
        FrameHandle semantic = sortedBodies[start].Semantic;
        FrameHandle parent = Parent(semantic);
        ReadOnlySpan<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        Span<SemanticRange> ranges = CollectionsMarshal.AsSpan(semanticRanges);
        double excess = -1;
        for (int r = 0; r < ranges.Length; r++)
        {
            (FrameHandle frame, int from, int to) = ranges[r];
            FrameHandle frameParent = Parent(frame);
            if (frameParent == semantic)
            {
                AddStickOutPlaces(frame, bodies[from..to], start, end);
                continue;
            }
            bool fromParent = frame == parent;
            if (!fromParent && (parent.IsNull || frameParent != parent || frame == semantic))
                continue;
            if (excess < 0)
                excess = NumericFrameRules.ContentExcess(Bounds(semantic), bodies[start..end], NumericOptions);
            if (fromParent)
                AddParentPlaces(semantic, bodies[from..to], excess);
            else
                AddSiblingPlaces(semantic, ref ranges[r], excess);
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
                VelocityToParent(pose, velocity, spin, body.Position, body.Velocity), body.Radius, hull, body.Static, body.Sensor, MeetsHulls: false));
        }
    }

    /// <summary>
    /// Adds the place of each body of <paramref name="members"/>, the bodies of the semantic parent of
    /// <paramref name="semantic"/>, that can touch the content of <paramref name="semantic"/> before the next update, its
    /// bubble grown by <paramref name="excess"/> (see <see cref="NumericFrameRules.MayTouchBubble"/>): physics gives such a
    /// body a ghost in the frame's own system while it reaches the content without being in the frame (tickets ter-tenw and
    /// ter-xxmj). Every body counts, static or not, and the hull of a frame reaches as far as its own bubble, as physics has
    /// it. A dynamic plain body or a character reaches only the bubble, not the content (see
    /// <see cref="NumericBody.GhostsReachContent"/>). The interior of the semantic parent's owner has no ghost in a child
    /// frame yet, and the frame's own hull lives inside its bubble. A child ghost of a static body or of a hull meets hulls
    /// (see <see cref="GhostPlace.MeetsHulls"/>). The ghost is fixed in the frame's own system. A first test on the distance from the bubble's center skips each body
    /// that cannot reach the content (see <see cref="NumericFrameRules.MayReachBubble"/>), before the full test turns the
    /// body's place and velocity into the frame's axes.
    /// </summary>
    private void AddParentPlaces(FrameHandle semantic, ReadOnlySpan<NumericBody> members, double excess)
    {
        FrameBounds bubble = Bounds(semantic);
        DPose pose = LocalPose(semantic);
        Double3 velocity = LinearVelocity(semantic), spin = (Double3)AngularVelocity(semantic);
        double spinRate = spin.Length, center = pose.Position.Length;
        foreach (ref readonly NumericBody body in members)
        {
            // An interior is static and fixed, and it owns no frame.
            if (body.OwnedFrame == semantic || (body.Static && body.Fixed && body.OwnedFrame.IsNull))
                continue;
            double reach = (body.OwnedFrame.IsNull ? body.Radius : Math.Max(body.Radius, Bounds(body.OwnedFrame).MaxRadius)) +
                (body.GhostsReachContent ? excess : 0);
            // The rounding scales with the distances of the body and the center from the semantic parent's origin. The body's is
            // at most the center's plus the distance between them.
            double distance = (body.Position - pose.Position).Length;
            if (!NumericFrameRules.MayReachBubble(distance, bubble.MaxRadius + reach, (body.Velocity - velocity).Length + spinRate * distance,
                2 * center + distance, NumericOptions))
                continue;
            Double3 position = pose.InverseTransformPoint(body.Position);
            Double3 relative = VelocityFromParent(pose, velocity, spin, body.Position, body.Velocity);
            if (NumericFrameRules.MayTouchBubble(bubble, position, relative, reach, NumericOptions))
                ghostPlaces.Add(new GhostPlace(semantic, position, relative, body.Radius, -1, body.Static, body.Sensor,
                    MeetsHulls: body.GhostsMeetHulls));
        }
    }

    /// <summary>
    /// Adds the place of each body of <paramref name="sibling"/>, the bodies of a semantic sibling of
    /// <paramref name="semantic"/>, that can stick out of the sibling's bubble and touch the bubble of
    /// <paramref name="semantic"/> before the next update: physics gives such a body a ghost in the frame's own system, as
    /// the body is partly in their semantic parent (ticket ter-tenw). A kinematic body or a hull reaches the frame's content,
    /// its bubble grown by <paramref name="excess"/>, and a dynamic plain body or a character only its bubble, as for a body
    /// of the semantic parent (see <see cref="AddParentPlaces"/>). The frame's system holds no hull of a frame that holds
    /// the body, so the ghost has the role of a child ghost, and a ghost of a hull meets hulls (see
    /// <see cref="GhostPlace.MeetsHulls"/>). The ghost is fixed in the frame's own system. A first test on the distance between
    /// the two centers skips the whole sibling when none of its bodies can reach the content (see
    /// <see cref="NumericFrameRules.MayReachBubble"/>), from bounds of where its bodies are (see <see cref="SemanticRange"/>).
    /// </summary>
    private void AddSiblingPlaces(FrameHandle semantic, ref SemanticRange sibling, double excess)
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
        if (!NumericFrameRules.MayReachBubble(distance, bubble.MaxRadius + excess + sibling.Extent, speedBound,
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
            if (NumericFrameRules.MayTouchBubble(bubble, position, relative, body.Radius + (body.GhostsReachContent ? excess : 0), NumericOptions))
                ghostPlaces.Add(new GhostPlace(semantic, position, relative, body.Radius, -1, body.Static, body.Sensor,
                    MeetsHulls: body.GhostsMeetHulls));
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
    /// order within a group, sums the motion of each group, and marks the bodies that follow and live apart from their groups.
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
            // A body that lives in a following frame of its own and that its group does not follow there stays apart: its
            // motion intents move it, so no group can take it into another home.
            for (int i = group.Start; i < group.End; i++)
                apart[i] = NumericFrameRules.FollowsInFrame(bodies[i], NumericOptions) &&
                    !(group.Motion.Follows && i - group.Start == group.Motion.Anchor);
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
                if (!NumericFrameRules.IsAnchor(body, NumericOptions) && leaveClocks.TryGetValue(body.Entity, out double apart))
                    hold |= NumericFrameRules.LeaveClock(wanted, group.Motion.Frame, apart, dt, NumericOptions).Hold;
            if (hold)
                group.Mode = NumericMode.None;
            if (group.Mode != NumericMode.None)
                continue;
            foreach (ref readonly NumericBody body in members)
            {
                if (NumericFrameRules.IsAnchor(body, NumericOptions) || NumericFrameRules.BodyMode(body, NumericOptions) == NumericMode.None)
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
    /// body that owns a frame that physics does not drive and that no following frame holds goes back to its semantic frame
    /// this way: its group gets the mode None. A group that takes the following frame of its anchor, a body that follows, gets
    /// a new one for the anchor when the anchor anchors none, whose velocity needs the length of the next physics step,
    /// <paramref name="step"/> seconds (see <see cref="CreateFollowingFrame"/>). Each other body of the group that follows
    /// stays apart from the group, in a following frame of its own, which it gets when it anchors none, also when it lives in
    /// the following frame of another anchor. Those bodies go first, in the order of the members, and then the group, so that
    /// two runs make the same frames in the same order; a following frame follows its own anchor, so no anchor's frame depends
    /// on where the group goes.
    /// </summary>
    private void MoveHome(in BodyGroup group, double step, PhysicsChanges changes)
    {
        Span<NumericBody> members = CollectionsMarshal.AsSpan(sortedBodies)[group.Start..group.End];
        for (int i = 0; i < members.Length; i++)
            if (apart[group.Start + i] && !members[i].AnchorsHome)
                MoveIntoFollowingFrame(ref members[i], step, changes);
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
            home = group.Mode == NumericMode.Following ? CreateFollowingFrame(members[group.Motion.Anchor], step)
                : CreateNumeric(semantic, group.Motion.Center, velocity, group.Mode);
            changes.FramesCreated.Add(home);
        }
        for (int i = 0; i < members.Length; i++)
        {
            ref NumericBody body = ref members[i];
            if (apart[group.Start + i] || body.Home == home)
                continue;
            // A static hull, or a kinematic hull without a following frame, lives in its semantic frame's own system.
            Contract.Assert(!body.Fixed || home == semantic || group.Mode == NumericMode.Following && i == group.Motion.Anchor);
            MoveBody(ref body, home);
        }
    }

    /// <summary>
    /// Moves <paramref name="body"/>, a body that follows and lives apart from its group, into a new following frame of its
    /// own, which it anchors (see <see cref="CreateFollowingFrame"/>). A hull's frame moves under the new frame with it.
    /// </summary>
    private void MoveIntoFollowingFrame(ref NumericBody body, double step, PhysicsChanges changes)
    {
        FrameHandle following = CreateFollowingFrame(body, step);
        changes.FramesCreated.Add(following);
        MoveBody(ref body, following);
    }

    /// <summary>
    /// Moves <paramref name="body"/> of the update into <paramref name="home"/>, another frame of its semantic frame, and
    /// the frame that it owns under that home, because a hull lives in the system of its frame's tree parent. A body that
    /// leaves the following frame that it anchors lets that frame go first (see <see cref="LeaveFollowingFrame"/>), so a
    /// plain body and a hull leave alike.
    /// </summary>
    private void MoveBody(ref NumericBody body, FrameHandle home)
    {
        Contract.Requires(body.Home != home);
        if (body.AnchorsHome)
            LeaveFollowingFrame(body.Home);
        if (!body.OwnedFrame.IsNull)
            Reparent(body.OwnedFrame, home);
        moves.Add(new NumericMove(body.Entity, body.Home, home));
        body = body with { Home = home, HomeMode = numeric[home.Index], AnchorsHome = Anchors(home, body.Entity) };
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
        Contract.Requires(mode is NumericMode.Falling or NumericMode.Following || velocity == Double3.Zero);
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
    /// The part of <see cref="BeginStep"/> for the numeric frame at dense index <paramref name="dense"/>, with stable index
    /// <paramref name="id"/>: the acceleration that it keeps for the step that starts now, a step of <paramref name="step"/>
    /// seconds that applies <paramref name="stepIntents"/>, relative to its parent. A falling frame takes the acceleration
    /// that keeps its origin on the inertial line of a free body at its place: the field of its semantic frame at its origin
    /// minus the semantic frame's proper acceleration, in a turning frame turned by the semantic frame's turn of the step,
    /// plus the two-turn update of its semantic place and chord over the step, divided by the step, minus the acceleration of
    /// the numeric frames above it, which the dense order, parents first, has fixed already (docs/plans/spin-gravity.md, "The
    /// origin of a falling frame"). The semantic frame's turns are fixed earlier in the same pass. Its members then feel only the difference between their own rotation terms and
    /// the origin's, so a falling group keeps small local velocities. In a semantic frame that does not turn (see
    /// <see cref="FrameTurns.Turns"/>), the update is left out, so the frame keeps the field's acceleration to the bit.
    /// <para>
    /// A following frame, which lies in its semantic frame, keeps the step record of its anchor's motion intent as its step
    /// plan (see <see cref="AnchorRecordInStep"/>, and "The step plan of a following frame" in docs/plans/rails-components.md),
    /// and the acceleration that lands it on the plan: the plan's chord minus the frame's velocity, divided by the step, so
    /// that the step of <see cref="AdvanceNumericFrames"/> turns the velocity into the chord and the place into the plan's,
    /// to its rounding. The plan is motion in the semantic frame's coordinates already, so the frame takes no turn term. It
    /// keeps the plan's instant velocity past its chord as its own at the end of the step (see
    /// <see cref="HomeInstantVelocityOverChord"/>). The plan is kept for the step because an end pose needs the place where
    /// the step starts, and step 2 moves the frame before it reads the plan. A following frame whose anchor has no record for
    /// the step falls as a falling frame, with its anchor at rest in it. A resting frame has none.
    /// </para>
    /// </summary>
    private void BeginNumericFrame(int dense, int id, ReadOnlySpan<PhysicsIntent> stepIntents, double step)
    {
        stepPlan[id] = null;
        if (numeric[id] == NumericMode.Following && AnchorRecordInStep(AtDense(dense), stepIntents, step, out MotionSample plan))
        {
            stepPlan[id] = plan;
            stepAcceleration[id] = (plan.ChordVelocity - linearVelocity[id]) / step;
            return;
        }
        if (numeric[id] is not (NumericMode.Falling or NumericMode.Following))
            return;
        FrameHandle frame = AtDense(dense);
        FrameHandle semantic = SemanticOf(frame);
        stepAcceleration[id] = FreeAccelerationAtOrigin(frame, semantic, step) - HomeAcceleration(TreeParent(frame), semantic);
    }

    /// <summary>
    /// The acceleration relative to <paramref name="semantic"/>, its semantic frame, over the step in flight, a step of
    /// <paramref name="step"/> seconds, of a free body at the origin of the numeric frame <paramref name="frame"/> that moves
    /// with the frame's velocity, in m/s²: the field of the semantic frame there minus the semantic frame's proper
    /// acceleration, and in a turning semantic frame that field turned by the step's turn plus the two-turn update of the
    /// place and the velocity over the step, divided by the step (docs/plans/spin-gravity.md, "The origin of a falling
    /// frame"). A falling frame takes it, so its origin stays on that body's inertial line, and the push of a numeric frame
    /// is its own acceleration less it (see <see cref="PushInStep"/>). In a semantic frame that does not turn (see
    /// <see cref="FrameTurns.Turns"/>) it is the field, to the bit. Valid while the tree stands where the step starts, once
    /// the semantic frame's turns for the step are fixed.
    /// </summary>
    private Double3 FreeAccelerationAtOrigin(FrameHandle frame, FrameHandle semantic, double step)
    {
        Double3 place = HomeOffset(frame, semantic);
        Double3 field = FieldOf(semantic.Index, place);
        ref readonly FrameTurns semanticTurns = ref turns[semantic.Index];
        if (!semanticTurns.Turns)
            return field;
        // In a turning frame the field acts at the start of the step, in the axes then, and the turn of the step carries it
        // into the axes at the end, as in the update of the members (docs/plans/spin-gravity.md, "The two-turn update").
        return semanticTurns.Step.Turn.Rotate(field) + semanticTurns.FreeChange(place, HomeVelocity(frame, semantic), step) / step;
    }

    /// <summary>
    /// The step plan of the following frame <paramref name="following"/> in a step of <paramref name="step"/> seconds that
    /// applies <paramref name="stepIntents"/>: the record of its anchor's motion intent there, in the frame's semantic
    /// parent, from where the anchor stands where the step starts, the frame's origin, by the rule of
    /// <see cref="StepRecordOfIntent"/>. A hull stands there with its frame's rotation, so its plan is the record that physics
    /// and the tree derive for its frame. The tree keeps no pose of a plain body, so a plain anchor stands there with the
    /// rotation of its motion intent's aim pose: the plan of an end pose then turns with no angular velocity, which no
    /// reader of a plan reads (docs/plans/numeric-frames.md, "Following frames"). False when the anchor has no motion
    /// intent, or a steer target, which gives no record.
    /// </summary>
    private bool AnchorRecordInStep(FrameHandle following, ReadOnlySpan<PhysicsIntent> stepIntents, double step, out MotionSample plan)
    {
        Entity anchorEntity = anchor[following.Index];
        FrameHandle semantic = TreeParent(following);
        if (byOwner.TryGetValue(anchorEntity, out FrameHandle frame))
            return StepRecordOfIntent(anchorEntity.Index, semantic, LocalPose(frame), stepIntents, step, out plan);
        uint slot = anchorEntity.Index;
        DQuaternion rotation = slot < (uint)stepIntents.Length ? stepIntents[(int)slot].Move.AimPose.Rotation : DQuaternion.Identity;
        return StepRecordOfIntent(slot, semantic, new DPose(TreeLocalPose(following).Position, rotation), stepIntents, step, out plan);
    }

    /// <summary>
    /// The step record of <paramref name="frame"/> in a step of <paramref name="step"/> seconds that applies
    /// <paramref name="stepIntents"/>: the record of its owner's motion intent there, in the frame's semantic parent, from the
    /// frame's pose in that parent where the step starts (see <see cref="PhysicsIntent.StepRecord"/>). False when the owner
    /// has no motion intent, or a steer target, which gives no record. Valid only while the tree stands where the step starts:
    /// before the step, during it, and at step 2 for a frame without a hull, which nothing moves between the two. The
    /// intents are by entity slot, as <see cref="PhysicsStepContext.Intents"/> holds them; a slot past their end holds none.
    /// Step 2 derives the record of a frame without a hull by the same rule (see <see cref="StepRecordOfIntent"/>), so the
    /// record that physics predicts for such a frame and the one that the frame takes agree to the last bit.
    /// </summary>
    internal bool StepRecordInStep(FrameHandle frame, ReadOnlySpan<PhysicsIntent> stepIntents, double step, out MotionSample record) =>
        StepRecordOfIntent(frame.Entity.Index, Parent(frame), LocalPose(frame), stepIntents, step, out record);

    /// <summary>
    /// The rule of <see cref="StepRecordInStep"/>, for a frame whose owner has the slot <paramref name="slot"/>, whose
    /// semantic parent is <paramref name="parent"/>, and which stands at <paramref name="standing"/> in that parent where the
    /// step starts. Step 2 calls it for a frame without a hull (see <see cref="TakeStepRecords"/>).
    /// </summary>
    private static bool StepRecordOfIntent(uint slot, FrameHandle parent, in DPose standing, ReadOnlySpan<PhysicsIntent> stepIntents,
        double step, out MotionSample record)
    {
        if (slot >= (uint)stepIntents.Length || !stepIntents[(int)slot].Move.HasValue)
        {
            record = default;
            return false;
        }
        ref readonly PhysicsIntent intent = ref stepIntents[(int)slot];
        // The World converts every intent into its entity's frame at step 7b, the semantic parent of a frame that it owns,
        // and only a dynamic hull hands off during a step. The assertion takes no message of its own, which a Debug build
        // would build for every frame with a motion intent in every step.
        Contract.Assert(intent.Frame == parent);
        return intent.StepRecord(standing, step, out record);
    }

    /// <summary>
    /// Moves each falling numeric frame and each following frame over a step of <paramref name="dt"/> seconds, in doubles, with
    /// the scheme that Jolt uses for bodies: the velocity gains the step acceleration times the step, then the position
    /// gains the new velocity times the step. A resting frame does not move. A following frame ends the step on its anchor's
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
            if (numeric[id] is not (NumericMode.Falling or NumericMode.Following))
                continue;
            ref Double3 velocity = ref LinearVelocityForWrite(id);
            velocity += stepAcceleration[id] * dt;
            ref DPose local = ref LocalForWrite(node);
            local = new DPose(local.Position + velocity * dt, local.Rotation);
        }
    }
}
