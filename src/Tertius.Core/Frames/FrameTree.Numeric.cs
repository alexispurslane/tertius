using System.Numerics;
using System.Runtime.InteropServices;

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
/// The numeric frame <see cref="Frame"/> keeps a ghost of each body that sticks out of the bubble of the semantic frame
/// <see cref="Bubble"/>. The frame is a falling or resting frame of the bubble's semantic parent, other than the bubble's
/// tree parent, and a member of it may touch a body that sticks out of the bubble (see
/// <see cref="NumericFrameRules.MayTouchStickOut"/>). Without numeric frames, that member would lie in the system of the
/// semantic parent, which holds the ghost of each such body, so its numeric frame needs one too (ticket ter-2gg0).
/// </summary>
internal readonly record struct StickOutGhost(FrameHandle Bubble, FrameHandle Frame);

/// <summary>
/// The numeric frames of the tree (docs/plans/numeric-frames.md): frames that the engine makes around a group of bodies,
/// only for precision. Gameplay never sees them. <see cref="UpdateNumericFrames"/> runs their rules once per sync point.
/// </summary>
public sealed partial class FrameTree
{
    /// <summary>The thresholds of the rules of numeric frames. The World sets them before its first update.</summary>
    internal NumericFrameOptions NumericOptions { get; set; } = new();

    /// <summary>
    /// The length of one physics step, in seconds. The World sets it before its first update. The velocity of an orbit
    /// frame is the chord of its orbit over one step (see <see cref="NumericMode.Orbiting"/>).
    /// </summary>
    internal double StepLength { get; set; }

    /// <summary>
    /// Puts the Kepler frame <paramref name="frame"/> at its place on its orbit at <paramref name="time"/>, with the orbit's
    /// rotation, velocity and angular velocity there, and no proper acceleration, because an orbit falls freely. A frame that
    /// hangs in its orbit frame rests at the orbit frame's origin, with the rotation of the orbit, and the orbit frame takes
    /// the place, with the chord of the orbit over the step that ends at <paramref name="time"/> as its velocity (see
    /// <see cref="NumericMode.Orbiting"/>). The frame keeps the rest of the orbit's velocity relative to it, so that a read
    /// in the semantic parent gives the orbit's velocity. The bodies and the other frames in the orbit frame move with it:
    /// at each sync point the orbit frame's place changes only by the rounding of its step, which its contents share. Any
    /// other frame takes the place relative to its tree parent.
    /// </summary>
    internal void PlaceOnOrbit(FrameHandle frame, double time)
    {
        int id = Node(frame).Id;
        Contract.Requires(driver[id] == MotionDriver.Kepler && StepLength > 0);
        KeplerOrbit path = orbit[id];
        double mu = KeplerMu(frame);
        (Double3 position, Double3 velocity) = path.StateAt(time, mu);
        DQuaternion rotation = path.RotationAt(time, mu);
        angularVelocity[id] = path.AngularVelocityAt(time, mu).ToVector3();
        properAcceleration[id] = Vector3.Zero;
        FrameHandle parent = TreeParent(frame);
        if (InOrbitFrame(frame))
        {
            Double3 chord = NumericFrameRules.OrbitChord(position, velocity, mu, StepLength);
            poses.Local(Node(parent)) = new DPose(position, DQuaternion.Identity);
            linearVelocity[parent.Index] = chord;
            poses.Local(Node(frame)) = new DPose(Double3.Zero, rotation);
            linearVelocity[id] = velocity - chord;
            return;
        }
        SetLocalPose(frame, new DPose(position, rotation));
        linearVelocity[id] = parent.IsNull || !IsNumeric(parent) ? velocity : velocity - HomeVelocity(parent, SemanticOf(parent));
    }

    /// <summary>
    /// Like <see cref="PlaceOnOrbit"/>, but outside a tick, after the orbit changed: the frame jumps to its place on the
    /// orbit, and everything else stays where it is, as without numeric frames. So when the frame hangs in its orbit
    /// frame, the orbit frame moves to the new place, and its other frames and its bodies keep their places and velocities
    /// in the semantic frame: they lose the orbit frame's move, and the move goes into <paramref name="changes"/> for
    /// physics and the rows. The frame's hull moves with the frame at the start of the next step, as for any new orbit.
    /// </summary>
    internal void MoveOnOrbit(FrameHandle frame, double time, PhysicsChanges changes)
    {
        if (!InOrbitFrame(frame))
        {
            PlaceOnOrbit(frame, time);
            return;
        }
        FrameHandle parent = TreeParent(frame);
        int parentId = Node(parent).Id;
        Double3 position = poses.GetLocal(Node(parent)).Position, velocity = linearVelocity[parentId];
        PlaceOnOrbit(frame, time);
        Double3 offset = poses.GetLocal(Node(parent)).Position - position, change = linearVelocity[parentId] - velocity;
        if (offset == Double3.Zero && change == Double3.Zero)
            return;
        shiftChildren.Clear();
        GetTreeChildren(parent, shiftChildren);
        foreach (FrameHandle child in shiftChildren)
        {
            if (child == frame)
                continue;
            PoseNode childNode = Node(child);
            ref DPose childLocal = ref poses.Local(childNode);
            childLocal = new DPose(childLocal.Position - offset, childLocal.Rotation);
            linearVelocity[childNode.Id] -= change;
        }
        changes.FramesShifted.Add(new FrameShift(parent, offset, change));
    }

    /// <summary>
    /// The motion of the Kepler frame <paramref name="frame"/> relative to its tree parent at <paramref name="time"/>, as
    /// physics sees it: its pose, its linear velocity and its angular velocity. Physics predicts a Kepler frame with it, so
    /// that it agrees with the tree at the end of a step. The frame hangs in its semantic parent or in its orbit frame
    /// whenever physics steps. In its semantic parent, it has the orbit's place, rotation and instant velocity. In its orbit
    /// frame, it rests at the origin with the orbit's rotation: physics keeps a numeric home's velocity as a chord and
    /// takes the home's instant velocity half a step of its acceleration further on (see "Instant velocities and chords"
    /// in JoltPhysicsBackend.Chords.cs), and both of those of the orbit frame are the frame's own, to the orbit's jerk times
    /// the square of a step, under 1e-6 m/s in low orbit. The tree keeps the frame's velocity relative to the orbit frame's
    /// chord instead (see <see cref="PlaceOnOrbit"/>), so that a read in the semantic parent gives the orbit's instant
    /// velocity.
    /// </summary>
    internal (DPose Pose, Double3 Velocity, Double3 AngularVelocity) KeplerMotion(FrameHandle frame, double time)
    {
        KeplerOrbit path = Orbit(frame);
        double mu = KeplerMu(frame);
        DQuaternion rotation = path.RotationAt(time, mu);
        Double3 angular = path.AngularVelocityAt(time, mu);
        if (InOrbitFrame(frame))
            return (new DPose(Double3.Zero, rotation), Double3.Zero, angular);
        (Double3 position, Double3 velocity) = path.StateAt(time, mu);
        return (new DPose(position, rotation), velocity, angular);
    }

    /// <summary>True when <paramref name="frame"/> hangs in its orbit frame: an orbit frame whose orbiter it is.</summary>
    internal bool InOrbitFrame(FrameHandle frame)
    {
        FrameHandle parent = TreeParent(frame);
        return !parent.IsNull && orbiter[parent.Index] == frame;
    }

    /// <summary>
    /// Creates the orbit frame of the Kepler frame <paramref name="frame"/>, whose hull the numeric update moves into it, and
    /// moves the frame under it: the new frame lies in the frame's semantic parent at the frame's place, with the chord of
    /// the frame's orbit over one step as its velocity, and the frame rests at its origin and keeps the rest of its velocity
    /// (see <see cref="PlaceOnOrbit"/>). The frame's place and velocity in the semantic parent stay what they were, which is
    /// where its orbit put it at this sync point.
    /// </summary>
    private FrameHandle CreateOrbitFrame(FrameHandle frame)
    {
        Contract.Requires(Driver(frame) == MotionDriver.Kepler && !IsNumeric(frame) && !InOrbitFrame(frame) && StepLength > 0);
        FrameHandle treeParent = TreeParent(frame);
        FrameHandle semantic = SemanticOf(treeParent);
        DPose local = TreeLocalPose(frame);
        Double3 position = InSemantic(treeParent, semantic, local.Position);
        Double3 velocity = HomeVelocity(treeParent, semantic) + linearVelocity[frame.Index];
        Double3 chord = NumericFrameRules.OrbitChord(position, velocity, KeplerMu(frame), StepLength);
        FrameHandle orbitFrame = CreateNumeric(semantic, position, chord, NumericMode.Orbiting);
        orbiter[orbitFrame.Index] = frame;
        Reparent(frame, orbitFrame);
        poses.Local(Node(frame)) = new DPose(Double3.Zero, local.Rotation);
        linearVelocity[frame.Index] = velocity - chord;
        Contract.Ensures(InOrbitFrame(frame) && Parent(frame) == semantic);
        return orbitFrame;
    }

    /// <summary>
    /// The orbit frame <paramref name="orbitFrame"/> lost its orbiter, which moved out, died or got another driver. It turns
    /// into a falling frame that falls on from its place, at its velocity: the bodies in it keep their motion, and the next
    /// numeric update takes it as any falling frame. The ghosts of the orbiter's hull go, because the hull no longer lives
    /// in an orbit frame (see <see cref="AnchorGhosts"/>).
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
    private readonly List<StickOutGhost> stickOutGhosts = new();
    // The two lists above as the last update left them, which the current update compares with its own (see WithdrawGhostPlaces).
    private readonly List<NumericGhost> lastAnchorGhosts = new();
    private readonly List<StickOutGhost> lastStickOutGhosts = new();
    // Each semantic frame of the input, with the range of its bodies in sortedBodies, in the order of the frames' indices.
    private readonly List<(FrameHandle Frame, int Start, int End)> semanticRanges = new();
    // The semantic children of the semantic frame that the update works on that have a body that can move: each one with
    // the radius of the sphere around its origin that holds every body that sticks out of its bubble (see
    // NumericFrameRules.StickOutRadius), and the velocity and the place of its origin in the semantic frame.
    private readonly List<(FrameHandle Bubble, double Radius, Double3 Velocity, Double3 Place)> stickOutBubbles = new();
    // The pairs of such a bubble and a group that gets a numeric frame, a member of which may touch a body that sticks out
    // of the bubble, of the semantic frame that the update works on.
    private readonly List<(FrameHandle Bubble, int Group)> stickOutPairs = new();
    // The leave clock of each body that the last update kept in its semantic frame's own system although it needs a frame by
    // itself (see NumericFrameRules.LeaveClock), and the clocks that the current update builds. An update keeps only the
    // clocks of its own bodies, so the clock of a body that is gone from its input goes with it.
    private Dictionary<Entity, double> leaveClocks = new();
    private Dictionary<Entity, double> nextLeaveClocks = new();

    /// <summary>
    /// The ghosts that the homes of groups keep of the Kepler hulls that the groups link but cannot share a home with, which
    /// the last <see cref="UpdateNumericFrames"/> chose (see <see cref="NumericGhost"/>). Physics reads them at each step.
    /// </summary>
    internal ReadOnlySpan<NumericGhost> AnchorGhosts => CollectionsMarshal.AsSpan(anchorGhosts);

    /// <summary>
    /// The numeric frames that keep the ghosts of the bodies that stick out of a bubble, which the last
    /// <see cref="UpdateNumericFrames"/> chose. Physics reads them at each step.
    /// </summary>
    internal ReadOnlySpan<StickOutGhost> StickOutGhosts => CollectionsMarshal.AsSpan(stickOutGhosts);

    /// <summary>
    /// Runs the membership and nesting rules of numeric frames (docs/plans/numeric-frames.md, "Membership" and "Nesting for
    /// vast distances") once, over a sync point of <paramref name="dt"/> seconds. <paramref name="bodies"/> holds every
    /// entity with a body that is not attached, in entity slot order. Within each semantic frame, the update links every
    /// pair of bodies that <see cref="NumericFrameRules.MayTouch"/> links, also an anchor (see <see cref="NumericBody.Anchor"/>)
    /// and a group that falls. It holds the members of each numeric frame that are not anchors together until
    /// <see cref="NumericFrameRules.SplitClock"/> lets them split. An anchor counts as infinitely heavy (see
    /// <see cref="NumericFrameRules.Summarize"/>), so a group with one takes its home (docs/plans/numeric-frames.md,
    /// "Anchors"). The hull of a Kepler frame that its orbit moves fast or far lives in an orbit frame of its own (see
    /// <see cref="NumericMode.Orbiting"/>), which the update makes for it, and the frame hangs at its origin. A group that
    /// links such a hull and no anchor at rest takes that orbit frame; a group that cannot take it, because it links an
    /// anchor at rest or an earlier such hull, lives in that other home, and the hull's frame stays its own and that home
    /// keeps a ghost of the hull (see <see cref="AnchorGhosts"/>).
    /// Each linked group gets the mode of <see cref="NumericFrameRules.GroupMode"/>, but a group that would rest in a new frame
    /// stays in its semantic frame's own system until <see cref="NumericFrameRules.LeaveClock"/> lets a member that the last
    /// update kept there leave. A group keeps the numeric frame that holds most of its mass, which drains and moves its
    /// origin; the bodies of the other groups move. Each group that gets a numeric frame and a member of which may touch a
    /// body that sticks out of the bubble of a semantic child of its semantic frame (see
    /// <see cref="NumericFrameRules.MayTouchStickOut"/>) makes its frame keep the ghosts of such bodies (see
    /// <see cref="StickOutGhosts"/>). A member that is static counts only when it is a sensor. Each frame that the last
    /// update named for a ghost and this one does not goes into <see cref="PhysicsChanges.GhostPlacesWithdrawn"/>. Then far
    /// frames nest.
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
    /// which costs the square of their count. The test of the bodies that stick out compares each body of a group that gets
    /// a numeric frame with each semantic child of that semantic frame that has a body that can move, and finds those
    /// children among the semantic frames of the input.
    /// </remarks>
    internal void UpdateNumericFrames(ReadOnlySpan<NumericBody> bodies, double dt, PhysicsChanges changes, Action<List<NumericMove>> applyMoves)
    {
        Contract.Requires(dt >= 0);
        SortBySemanticFrame(bodies);
        moves.Clear();
        lastAnchorGhosts.Clear();
        lastAnchorGhosts.AddRange(anchorGhosts);
        lastStickOutGhosts.Clear();
        lastStickOutGhosts.AddRange(stickOutGhosts);
        anchorGhosts.Clear();
        stickOutGhosts.Clear();
        nextLeaveClocks.Clear();
        semanticRanges.Clear();
        for (int start = 0; start < sortedBodies.Count;)
        {
            int end = start + 1;
            while (end < sortedBodies.Count && sortedBodies[end].Semantic == sortedBodies[start].Semantic)
                end++;
            semanticRanges.Add((sortedBodies[start].Semantic, start, end));
            start = end;
        }
        // The update of a semantic frame reads the bodies of its semantic children, which keep their ranges.
        for (int r = 0; r < semanticRanges.Count; r++)
        {
            (_, int start, int end) = semanticRanges[r];
            if (NeedsWork(CollectionsMarshal.AsSpan(sortedBodies)[start..end]))
                UpdateSemanticFrame(start, end, dt, changes);
        }
        (leaveClocks, nextLeaveClocks) = (nextLeaveClocks, leaveClocks);
        WithdrawGhostPlaces(changes);
        NestFarFrames(changes);
        CountBodies();
        applyMoves(moves);
        CollectEmptyFrames(changes);
    }

    private void SortBySemanticFrame(ReadOnlySpan<NumericBody> bodies)
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
        // The rules read the mode of each body's home, which only the tree knows. A semantic frame's mode is None, which
        // the record has already, so most bodies need no copy.
        for (int i = 0; i < bodies.Length; i++)
        {
            ref readonly NumericBody body = ref bodies[(int)(uint)sortKeys[i]];
            sortedBodies.Add(body.Home == body.Semantic ? body : body with { HomeMode = numeric[body.Home.Index] });
        }
    }

    /// <summary>
    /// True when a body of the semantic frame is in a numeric frame or needs one by itself. A group's common speed and
    /// center never pass those of its farthest and fastest body, so a semantic frame where no body needs a frame has no
    /// group that does.
    /// </summary>
    private bool NeedsWork(ReadOnlySpan<NumericBody> bodies)
    {
        foreach (ref readonly NumericBody body in bodies)
            if (body.Home != body.Semantic || NumericFrameRules.BodyMode(body, NumericOptions) != NumericMode.None)
                return true;
        return false;
    }

    private void UpdateSemanticFrame(int start, int end, double dt, PhysicsChanges changes)
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        for (int i = start; i < end; i++)
            links[i] = i;
        // Every body links to every group that it may touch, also an anchor to a group that falls, so a static wall is a
        // real body of a fast crate's system, and so is a Kepler hull in the orbit frame that its group takes
        // (docs/plans/numeric-frames.md, "Anchors"). This comes before the hold, so that members that an anchor links count
        // as together.
        for (int i = start; i < end; i++)
            for (int j = i + 1; j < end; j++)
                if (MayTouch(bodies[i], bodies[j]))
                    Union(i, j);
        HoldFramesTogether(start, end, dt);
        GroupBodies(start, end);
        HoldInSemanticSystem(dt);
        ChooseKeepers();
        // Before any frame shifts, while each body's place in its home holds.
        FindStickOutPairs(start);
        // Every shift lands before any move: a body that leaves a shifted frame leaves it from its shifted state. An orbit
        // frame never drains or moves its origin: its orbit gives it its place and velocity.
        foreach (BodyGroup group in groups)
            if (group.Keeps && group.Mode != NumericMode.Orbiting)
                DrainFrame(group, changes);
        foreach (BodyGroup group in groups)
            MoveHome(group, changes);
        // After the moves, which give each group its home and each Kepler hull apart from its group its orbit frame.
        KeepAnchorGhosts();
        KeepStickOutGhosts();
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
    /// Finds each group that gets a numeric frame and a member of which may touch a body that sticks out of the bubble of a
    /// semantic child of the group's semantic frame, by <see cref="NumericFrameRules.MayTouchStickOut"/>. Such a body has a
    /// ghost in the system of that semantic frame, which holds the member without numeric frames, so the member's numeric
    /// frame needs one too (see <see cref="KeepStickOutGhosts"/>). The hull of a Static or Spin frame stays in that system,
    /// so the anchors of a group that gets a numeric frame are static bodies and Kepler hulls, which are kinematic. A ghost
    /// is kinematic, and Jolt makes a contact between a kinematic body and a static or kinematic one only when one of them
    /// is a sensor, so only a sensor among them counts: it detects the ghost. The children are the semantic frames whose
    /// semantic parent is the group's, wherever they hang among its numeric frames, with a body that can move.
    /// </summary>
    private void FindStickOutPairs(int start)
    {
        stickOutPairs.Clear();
        FrameHandle semantic = sortedBodies[start].Semantic;
        CollectStickOutBubbles(semantic);
        if (stickOutBubbles.Count == 0)
            return;
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        Span<BodyGroup> spans = CollectionsMarshal.AsSpan(groups);
        for (int g = 0; g < spans.Length; g++)
        {
            if (spans[g].Mode == NumericMode.None)
                continue;
            foreach ((FrameHandle bubble, double radius, Double3 velocity, Double3 place) in stickOutBubbles)
            {
                FrameHandle home = FrameHandle.Null;
                Double3 origin = default;
                for (int i = spans[g].Start; i < spans[g].End; i++)
                {
                    ref readonly NumericBody body = ref bodies[i];
                    if (body.Anchor && !body.Sensor)
                        continue;
                    // A first test in the semantic frame's coordinates, which round far from its origin, with the sphere grown
                    // by the slack of that rounding, so that it drops no pair that the test through the homes links. It
                    // spares that test, which composes poses through the tree, for most pairs.
                    double slack = NumericFrameRules.PlaceSlack(place, body.Position);
                    if (!NumericFrameRules.MayTouchStickOut(body, place - body.Position, velocity, radius + slack, NumericOptions))
                        continue;
                    // The place of the bubble's origin in the member's home, through their lowest common ancestor. The homes
                    // of a semantic frame have its axes, because numeric frames never rotate.
                    if (body.Home != home)
                        (home, origin) = (body.Home, Relative(bubble, body.Home).Position);
                    if (!NumericFrameRules.MayTouchStickOut(body, origin - body.HomePosition, velocity, radius, NumericOptions))
                        continue;
                    stickOutPairs.Add((bubble, g));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Fills <see cref="stickOutBubbles"/> with the semantic children of <paramref name="semantic"/> that have a body that
    /// can move, from the bodies of the update: a static body has no ghost in a bubble. The radius of each child's sphere
    /// is the farthest reach of those bodies (see <see cref="NumericFrameRules.StickOutReach"/>).
    /// </summary>
    private void CollectStickOutBubbles(FrameHandle semantic)
    {
        stickOutBubbles.Clear();
        foreach ((FrameHandle frame, int start, int end) in semanticRanges)
        {
            if (Parent(frame) != semantic)
                continue;
            FrameBounds bubble = Bounds(frame);
            double radius = -1;
            for (int i = start; i < end; i++)
                if (!sortedBodies[i].Static)
                    radius = Math.Max(radius, NumericFrameRules.StickOutReach(bubble, sortedBodies[i], NumericOptions));
            if (radius >= 0)
                stickOutBubbles.Add((frame, radius, LinearVelocity(frame), InSemantic(TreeParent(frame), semantic, TreeLocalPose(frame).Position)));
        }
    }

    /// <summary>
    /// Names the home of each group of <see cref="FindStickOutPairs"/> to keep the ghosts of the bodies that stick out of its
    /// bubble, unless the home is the bubble's tree parent, which keeps them already. Each group that gets a numeric frame
    /// has a frame of its own, so no pair is named twice.
    /// </summary>
    private void KeepStickOutGhosts()
    {
        foreach ((FrameHandle bubble, int g) in stickOutPairs)
        {
            FrameHandle home = GroupHome(groups[g]);
            Contract.Assert(IsNumeric(home));
            if (home == TreeParent(bubble))
                continue;
            var ghost = new StickOutGhost(bubble, home);
            Contract.Assert(!stickOutGhosts.Contains(ghost));
            stickOutGhosts.Add(ghost);
        }
    }

    /// <summary>
    /// Adds to <see cref="PhysicsChanges.GhostPlacesWithdrawn"/>, once each, the numeric frames of the pairs that the last
    /// update named in <see cref="AnchorGhosts"/> or <see cref="StickOutGhosts"/> and this one does not. Physics drops the
    /// ghosts there at this sync point, so that between updates it keeps a ghost only where the tree allows one, as the
    /// ghost pass does. A frame that leaves the tree takes its pairs with it (see <see cref="RemoveNode"/>), so every frame
    /// of the last update's pairs is still alive. An update that names the same pairs in the same order, as the next one
    /// usually does, costs one comparison of each list.
    /// </summary>
    private void WithdrawGhostPlaces(PhysicsChanges changes)
    {
        if (!CollectionsMarshal.AsSpan(lastAnchorGhosts).SequenceEqual(CollectionsMarshal.AsSpan(anchorGhosts)))
            foreach (NumericGhost ghost in lastAnchorGhosts)
                if (!anchorGhosts.Contains(ghost))
                    Withdraw(ghost.Frame, changes);
        if (!CollectionsMarshal.AsSpan(lastStickOutGhosts).SequenceEqual(CollectionsMarshal.AsSpan(stickOutGhosts)))
            foreach (StickOutGhost ghost in lastStickOutGhosts)
                if (!stickOutGhosts.Contains(ghost))
                    Withdraw(ghost.Frame, changes);
    }

    private static void Withdraw(FrameHandle frame, PhysicsChanges changes)
    {
        if (!changes.GhostPlacesWithdrawn.Contains(frame))
            changes.GhostPlacesWithdrawn.Add(frame);
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
            home = group.Mode == NumericMode.Orbiting ? CreateOrbitFrame(members[group.Motion.Orbiter].OwnedFrame)
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
        FrameHandle orbitFrame = CreateOrbitFrame(body.OwnedFrame);
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
        FrameHandle handle = AddFrame(Entity.Null, parent, new DPose(position, DQuaternion.Identity), default, MotionDriver.Static,
            GravityField.None, default, default, mode);
        linearVelocity[handle.Index] = velocity;
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
        ref DPose local = ref poses.Local(node);
        local = new DPose(local.Position + offset, local.Rotation);
        linearVelocity[node.Id] += velocity;
        List<FrameHandle> children = shiftChildren;
        children.Clear();
        GetTreeChildren(frame, children);
        foreach (FrameHandle child in children)
        {
            PoseNode childNode = Node(child);
            ref DPose childLocal = ref poses.Local(childNode);
            childLocal = new DPose(childLocal.Position - offset, childLocal.Rotation);
            linearVelocity[childNode.Id] -= velocity;
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
        poses.Reparent(node, nestNode, keepWorldPose: false);
        poses.Local(node) = new DPose(position, DQuaternion.Identity);
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
            stepAcceleration[node.Id] = Double3.Zero;
    }

    /// <summary>
    /// Fixes the acceleration that each falling numeric frame keeps for the step that starts now: the field of its semantic
    /// frame at its origin, relative to its parent. That is the field minus the acceleration of the numeric frames above
    /// it, which the dense order, parents first, has fixed already. An orbit frame, which lies in its semantic frame, keeps
    /// the acceleration of its orbit at its origin, the pull of the point gravity whose G·M the orbit takes, so that it
    /// ends the step on the orbit (see <see cref="NumericMode.Orbiting"/>). A resting frame has none. Call it after
    /// <see cref="Update"/> and before the physics step, which reads it through <see cref="EffectiveGravity"/>.
    /// </summary>
    internal void BeginNumericStep()
    {
        // The dense order puts parents first only after Update.
        Contract.Requires(!poses.WorldIsStale);
        for (int i = 0; i < poses.Count; i++)
        {
            int id = poses.NodeAt(i).Id;
            if (numeric[id] == NumericMode.Orbiting)
            {
                stepAcceleration[id] = NumericFrameRules.OrbitAcceleration(poses.LocalPoses[i].Position, KeplerMu(orbiter[id]));
                continue;
            }
            if (numeric[id] != NumericMode.Falling)
                continue;
            FrameHandle frame = AtDense(i);
            FrameHandle semantic = SemanticOf(frame);
            FrameHandle parent = TreeParent(frame);
            stepAcceleration[id] = FieldOf(semantic.Index, HomeOffset(frame, semantic)) - HomeAcceleration(parent, semantic);
        }
    }

    /// <summary>
    /// Moves each falling numeric frame and each orbit frame over a step of <paramref name="dt"/> seconds, in doubles, with
    /// the scheme that Jolt uses for bodies: the velocity gains the step acceleration times the step, then the position
    /// gains the new velocity times the step. A resting frame does not move. An orbit frame ends the step on its orbit, to
    /// its rounding, and the next placement on the orbit puts it there exactly (see <see cref="PlaceOnOrbit"/>).
    /// </summary>
    internal void AdvanceNumericFrames(double dt)
    {
        Contract.Requires(dt >= 0 && double.IsFinite(dt));
        for (int i = 0; i < poses.Count; i++)
        {
            PoseNode node = poses.NodeAt(i);
            int id = node.Id;
            if (numeric[id] is not (NumericMode.Falling or NumericMode.Orbiting))
                continue;
            linearVelocity[id] += stepAcceleration[id] * dt;
            ref DPose local = ref poses.Local(node);
            local = new DPose(local.Position + linearVelocity[id] * dt, local.Rotation);
        }
    }
}
