using System.Runtime.InteropServices;

namespace Tertius;

/// <summary>
/// A body whose physics home changes at <see cref="FrameTree.UpdateNumericFrames"/>: it moves from <see cref="From"/>
/// into <see cref="To"/>, two frames of its semantic frame (the semantic frame itself or a numeric frame below it).
/// </summary>
internal readonly record struct NumericMove(Entity Entity, FrameHandle From, FrameHandle To);

/// <summary>
/// A ghost of the anchor <see cref="Anchor"/> in the system of the falling numeric frame <see cref="Frame"/>. An anchor is
/// a static body or the hull of a frame that physics does not drive. It stays in its own system when a group of bodies
/// near it falls, so the frame of that group keeps a ghost of it (docs/plans/numeric-frames.md, "Limits").
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
    // True for each body, by index in sortedBodies, that is not an anchor and whose group without anchors falls.
    private bool[] falls = [];
    // The pairs of an anchor and a body that falls, which the merge test links, of the semantic frame that the update works on.
    private readonly List<(Entity Anchor, Entity Body)> anchorPairs = new();
    private readonly List<NumericGhost> anchorGhosts = new();
    // The leave clock of each body that the last update kept in its semantic frame's own system although it needs a frame by
    // itself (see NumericFrameRules.LeaveClock), and the clocks that the current update builds. An update keeps only the
    // clocks of its own bodies, so the clock of a body that is gone from its input goes with it.
    private Dictionary<Entity, double> leaveClocks = new();
    private Dictionary<Entity, double> nextLeaveClocks = new();

    /// <summary>
    /// The ghosts that the falling numeric frames keep of the anchors near their members, which the last
    /// <see cref="UpdateNumericFrames"/> chose. Physics reads them at each step.
    /// </summary>
    internal ReadOnlySpan<NumericGhost> AnchorGhosts => CollectionsMarshal.AsSpan(anchorGhosts);

    /// <summary>
    /// Runs the membership and nesting rules of numeric frames (docs/plans/numeric-frames.md, "Membership" and "Nesting for
    /// vast distances") once, over a sync point of <paramref name="dt"/> seconds. <paramref name="bodies"/> holds every
    /// entity with a body that is not attached, in entity slot order. Within each semantic frame, the update links every
    /// pair of bodies that are not anchors (see <see cref="NumericBody.Anchor"/>) that
    /// <see cref="NumericFrameRules.MayTouch"/> links, and holds the members of each numeric frame that are not anchors
    /// together until <see cref="NumericFrameRules.SplitClock"/> lets them split. Then it links each anchor to the anchors
    /// and to the groups that do not fall that the merge test links to it: an anchor never changes the mode of a group that
    /// falls, and stays in its own system, and the frame of that group gets a ghost of it (see <see cref="AnchorGhosts"/>).
    /// Each linked group gets the mode of <see cref="NumericFrameRules.Mode"/>, but a group that would rest in a new frame
    /// stays in its semantic frame's own system until <see cref="NumericFrameRules.LeaveClock"/> lets a member that the last
    /// update kept there leave. A group keeps the numeric frame that holds most of its mass, which drains and moves its
    /// origin; the bodies of the other groups move. Then far frames nest.
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
    /// which costs the square of their count.
    /// </remarks>
    internal void UpdateNumericFrames(ReadOnlySpan<NumericBody> bodies, double dt, PhysicsChanges changes, Action<List<NumericMove>> applyMoves)
    {
        Contract.Requires(dt >= 0);
        SortBySemanticFrame(bodies);
        moves.Clear();
        anchorGhosts.Clear();
        nextLeaveClocks.Clear();
        for (int start = 0; start < sortedBodies.Count;)
        {
            int end = start + 1;
            while (end < sortedBodies.Count && sortedBodies[end].Semantic == sortedBodies[start].Semantic)
                end++;
            if (NeedsWork(CollectionsMarshal.AsSpan(sortedBodies)[start..end]))
                UpdateSemanticFrame(start, end, dt, changes);
            start = end;
        }
        (leaveClocks, nextLeaveClocks) = (nextLeaveClocks, leaveClocks);
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
            falls = new bool[sortKeys.Length];
        }
        for (int i = 0; i < bodies.Length; i++)
        {
            Contract.Requires(IsAlive(bodies[i].Home) && SemanticOf(bodies[i].Home) == bodies[i].Semantic);
            sortKeys[i] = ((long)bodies[i].Semantic.Index << 32) | (uint)i;
        }
        Array.Sort(sortKeys, 0, bodies.Length);
        sortedBodies.Clear();
        for (int i = 0; i < bodies.Length; i++)
            sortedBodies.Add(bodies[(int)(uint)sortKeys[i]]);
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
        for (int i = start; i < end; i++)
            for (int j = i + 1; j < end; j++)
                if (!bodies[i].Anchor && !bodies[j].Anchor && MayTouch(bodies[i], bodies[j]))
                    Union(i, j);
        HoldFramesTogether(start, end, dt);
        MarkFallingGroups(start, end);
        LinkAnchors(start, end);
        GroupBodies(start, end);
        HoldInSemanticSystem(dt);
        ChooseKeepers();
        // Every shift lands before any move: a body that leaves a shifted frame leaves it from its shifted state.
        foreach (BodyGroup group in groups)
            if (group.Keeps)
                DrainFrame(group, changes);
        foreach (BodyGroup group in groups)
            MoveHome(group, changes);
        KeepAnchorGhosts(start, end);
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
    /// Marks each body that is not an anchor and whose group, which holds no anchor yet, gets the mode Falling by itself.
    /// The root of a group is its body with the lowest index, so a group is summed once, from its root.
    /// </summary>
    private void MarkFallingGroups(int start, int end)
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        for (int i = start; i < end; i++)
            falls[i] = false;
        for (int root = start; root < end; root++)
        {
            if (bodies[root].Anchor || Find(root) != root)
                continue;
            bodyScratch.Clear();
            for (int i = root; i < end; i++)
                if (Find(i) == root)
                    bodyScratch.Add(bodies[i]);
            NumericGroup motion = NumericFrameRules.Summarize(CollectionsMarshal.AsSpan(bodyScratch), homeMasses);
            NumericMode current = motion.Frame.IsNull ? NumericMode.None : Numeric(motion.Frame);
            if (NumericFrameRules.Mode(current, motion.Velocity, motion.Center, motion.Anchored, motion.Fixed, NumericOptions) != NumericMode.Falling)
                continue;
            for (int i = root; i < end; i++)
                if (Find(i) == root)
                    falls[i] = true;
        }
    }

    /// <summary>
    /// Links each anchor to every anchor and to every group that does not fall that the merge test links to it. A pair of
    /// an anchor and a body that falls stays apart, and <see cref="KeepAnchorGhosts"/> gives the body's frame a ghost of
    /// the anchor.
    /// </summary>
    private void LinkAnchors(int start, int end)
    {
        Span<NumericBody> bodies = CollectionsMarshal.AsSpan(sortedBodies);
        anchorPairs.Clear();
        for (int i = start; i < end; i++)
        {
            for (int j = i + 1; j < end; j++)
            {
                if (!bodies[i].Anchor && !bodies[j].Anchor || !MayTouch(bodies[i], bodies[j]))
                    continue;
                if (falls[i])
                    anchorPairs.Add((bodies[j].Entity, bodies[i].Entity));
                else if (falls[j])
                    anchorPairs.Add((bodies[i].Entity, bodies[j].Entity));
                else
                    Union(i, j);
            }
        }
    }

    /// <summary>
    /// Adds a ghost of the anchor of each pair of <see cref="LinkAnchors"/> to the falling frame that is the home of its
    /// body now, once for each frame.
    /// </summary>
    private void KeepAnchorGhosts(int start, int end)
    {
        foreach ((Entity anchor, Entity body) in anchorPairs)
        {
            FrameHandle home = FrameHandle.Null;
            for (int i = start; i < end && home.IsNull; i++)
                if (sortedBodies[i].Entity == body)
                    home = sortedBodies[i].Home;
            // The body's group holds no anchor and falls, so its home is the falling frame that it kept or got.
            Contract.Assert(Numeric(home) == NumericMode.Falling);
            var ghost = new NumericGhost(anchor, home);
            if (!anchorGhosts.Contains(ghost))
                anchorGhosts.Add(ghost);
        }
    }

    /// <summary>
    /// Numbers the groups by their first body, puts the bodies of the range in the order of their groups, keeping their
    /// order within a group, and sums the motion of each group.
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
            group.Motion = NumericFrameRules.Summarize(bodies[group.Start..group.End], homeMasses);
            NumericMode current = group.Motion.Frame.IsNull ? NumericMode.None : Numeric(group.Motion.Frame);
            group.Mode = NumericFrameRules.Mode(current, group.Motion.Velocity, group.Motion.Center, group.Motion.Anchored,
                group.Motion.Fixed, NumericOptions);
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
    /// body that owns a frame that physics does not drive goes back to its semantic frame this way: its group gets the mode
    /// None.
    /// </summary>
    private void MoveHome(in BodyGroup group, PhysicsChanges changes)
    {
        Span<NumericBody> members = CollectionsMarshal.AsSpan(sortedBodies)[group.Start..group.End];
        FrameHandle semantic = members[0].Semantic;
        FrameHandle home = group.Mode == NumericMode.None ? semantic : group.Keeps ? group.Motion.Frame : FrameHandle.Null;
        bool anyMoves = false;
        foreach (ref readonly NumericBody body in members)
            anyMoves |= body.Home != home;
        if (!anyMoves)
            return;
        if (home.IsNull)
        {
            Double3 velocity = group.Mode == NumericMode.Falling ? group.Motion.Velocity : Double3.Zero;
            home = CreateNumeric(semantic, group.Motion.Center, velocity, group.Mode);
            changes.FramesCreated.Add(home);
        }
        foreach (ref NumericBody body in members)
        {
            if (body.Home == home)
                continue;
            Contract.Assert(!body.Fixed || home == semantic);
            if (!body.OwnedFrame.IsNull)
                Reparent(body.OwnedFrame, home);
            moves.Add(new NumericMove(body.Entity, body.Home, home));
            body = body with { Home = home };
        }
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
        Contract.Requires(mode == NumericMode.Falling || velocity == Double3.Zero);
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
    /// it, which the dense order, parents first, has fixed already. A resting frame has none. Call it after
    /// <see cref="Update"/> and before the physics step, which reads it through <see cref="EffectiveGravity"/>.
    /// </summary>
    internal void BeginNumericStep()
    {
        // The dense order puts parents first only after Update.
        Contract.Requires(!poses.WorldIsStale);
        for (int i = 0; i < poses.Count; i++)
        {
            int id = poses.NodeAt(i).Id;
            if (numeric[id] != NumericMode.Falling)
                continue;
            FrameHandle frame = AtDense(i);
            FrameHandle semantic = SemanticOf(frame);
            FrameHandle parent = TreeParent(frame);
            stepAcceleration[id] = FieldOf(semantic.Index, HomeOffset(frame, semantic)) - HomeAcceleration(parent, semantic);
        }
    }

    /// <summary>
    /// Moves each falling numeric frame over a step of <paramref name="dt"/> seconds, in doubles, with the scheme that Jolt
    /// uses for bodies: the velocity gains the step acceleration times the step, then the position gains the new velocity
    /// times the step. A resting frame does not move.
    /// </summary>
    internal void AdvanceNumericFrames(double dt)
    {
        Contract.Requires(dt >= 0 && double.IsFinite(dt));
        for (int i = 0; i < poses.Count; i++)
        {
            PoseNode node = poses.NodeAt(i);
            int id = node.Id;
            if (numeric[id] != NumericMode.Falling)
                continue;
            linearVelocity[id] += stepAcceleration[id] * dt;
            ref DPose local = ref poses.Local(node);
            local = new DPose(local.Position + linearVelocity[id] * dt, local.Rotation);
        }
    }
}
