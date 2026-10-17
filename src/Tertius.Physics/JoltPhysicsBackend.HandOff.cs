using System.Numerics;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    /// <summary>
    /// A hand-off decided after the step, with the body's state already converted to the target frame: its pose, and the
    /// velocities that Jolt gets there (see <see cref="HandedOffVelocities"/>).
    /// </summary>
    private readonly record struct HandOffPlan(
        BodyRecord Record,
        FrameHandle From,
        FrameHandle To,
        DPose Pose,
        Vector3 LinearVelocity,
        Vector3 AngularVelocity,
        DQuaternion Turn);

    /// <summary>
    /// A frame that is not numeric, whose parent in the tree is a semantic frame or a numeric frame below it, with its motion
    /// relative to that parent at the end of the step, with an instant velocity or a chord (see <see cref="BubblesFor"/>).
    /// </summary>
    private readonly record struct BubbleSite(FrameHandle Parent, FrameHandle Frame, FrameMotion Motion);

    /// <summary>
    /// A bubble that the members of one system meet: the frame <see cref="Frame"/>, whose tree parent is
    /// <see cref="Parent"/>, its motion relative to that parent at the end of the step, and the position and velocity of
    /// the system's origin in that parent then. Both are zero for a child of the system's own frame. Numeric frames never
    /// rotate, so a shift of position and velocity takes a member's state from the system into the parent. The velocities
    /// are instant ones or chords, as <see cref="BubblesFor"/> says.
    /// </summary>
    private readonly record struct BubbleEntry(FrameHandle Parent, FrameHandle Frame, FrameMotion Motion, Double3 Offset, Double3 Velocity);

    /// <summary>What a list of bubbles serves, which sets when it is collected and which velocities it holds.</summary>
    private enum BubblesFor
    {
        /// <summary>
        /// The hand-off, before any body moves. The velocities are instant ones (see <see cref="InstantMotionOf"/>),
        /// because a hand-off carries the instant velocity of a body into the new frame.
        /// </summary>
        HandOff,

        /// <summary>
        /// The ghosts, after the hand-offs, which can move a frame to another parent. The velocities are chords (see
        /// <see cref="MotionOf"/>), because a ghost follows the chord of its real body.
        /// </summary>
        Ghosts,
    }

    /// <summary>
    /// The bubble sites of each semantic frame in one step, by the index of the frame. Each list is collected at most once
    /// a step (see <see cref="BubbleSitesOf"/>).
    /// </summary>
    private sealed class BubbleSiteCache
    {
        private List<BubbleSite>?[] lists = new List<BubbleSite>?[16];
        private long[] steps = new long[16];

        /// <summary>
        /// The list of <paramref name="semantic"/>, and in <paramref name="collect"/> true when it is not yet collected in
        /// step <paramref name="step"/>. The caller then fills it.
        /// </summary>
        public List<BubbleSite> For(FrameHandle semantic, long step, out bool collect)
        {
            int index = semantic.Index;
            if (index >= lists.Length)
            {
                int size = Math.Max(index + 1, lists.Length * 2);
                Array.Resize(ref lists, size);
                Array.Resize(ref steps, size);
            }
            List<BubbleSite> sites = lists[index] ??= new List<BubbleSite>();
            collect = steps[index] != step;
            if (collect)
            {
                sites.Clear();
                steps[index] = step;
            }
            return sites;
        }
    }

    private readonly List<HandOffPlan> handOffPlans = new();
    private readonly List<BubbleEntry> bubbleEntries = new();
    private readonly List<BubbleEntry> ghostBubbles = new();
    // The bubbles that the members of the bubble parent of a system's frame meet, for the bodies that stick out of that frame.
    private readonly List<BubbleEntry> siblingBubbles = new();
    private readonly BubbleSiteCache handOffSites = new();
    private readonly BubbleSiteCache ghostSites = new();

    // ---- Hand-off ----

    /// <summary>
    /// Moves bodies between frames by the rules of ARCHITECTURE.md: enter a child on the first touch of its bubble
    /// (the deepest one if two touch), leave only when fully outside plus the exit margin, and a frame enters a
    /// sibling only if it fits. The children are those of the semantic frame, wherever they hang among its numeric frames
    /// (see <see cref="PrepareBubbles"/>). A frame's hull never enters its own frame. Every decision and conversion uses the state
    /// at the end of the step, before any body moves, so the order of execution does not matter.
    /// </summary>
    private void HandOffBodies(PhysicsStepOutput output)
    {
        handOffPlans.Clear();
        foreach (FrameWorld world in worldList)
        {
            if (world.Members.Count == 0)
                continue;
            FrameHandle frame = world.Frame;
            FrameHandle parent = BubbleParentOf(frame);
            FrameBounds bounds = frames.Bounds(frame);
            PrepareBubbles(bubbleEntries, frame, BubblesFor.HandOff);
            bool haveFrameMotion = false;
            FrameMotion frameMotion = default;

            foreach (BodyRecord member in world.Members)
            {
                if (!MovesBetweenFrames(member))
                    continue;
                BodySnapshot s = ReadPrimary(member);
                Double3 center = s.Pose.Position;
                double radius = member.Radius;

                if (!parent.IsNull && bounds.FullyOutside(center, radius, bounds.ExitMargin(radius)))
                {
                    if (!haveFrameMotion)
                    {
                        frameMotion = InstantMotionOf(frame);
                        haveFrameMotion = true;
                    }
                    DPose pose = frameMotion.PoseToParent(s.Pose);
                    var (linear, angular) = HandedOffVelocities(member, parent, frameMotion.Pose.TransformPoint(s.CenterOfMass),
                        frameMotion.VelocityToParent(s.CenterOfMass, InstantVelocity(member, s)),
                        frameMotion.AngularToParent(InstantAngularVelocity(member, s)));
                    handOffPlans.Add(new HandOffPlan(member, frame, parent, pose, linear, angular, frameMotion.Pose.Rotation));
                    continue;
                }

                int best = -1;
                double deepest = double.NegativeInfinity;
                for (int i = 0; i < bubbleEntries.Count; i++)
                {
                    BubbleEntry entry = bubbleEntries[i];
                    // A hull never enters its own frame. The other bubbles are those of its frame's semantic siblings,
                    // never of frames inside it: every hull starts the step in the system of its frame's parent in the
                    // tree, and the walk to the siblings passes only through numeric frames.
                    if (entry.Frame == member.OwnedFrame)
                        continue;
                    FrameBounds entryBounds = frames.Bounds(entry.Frame);
                    if (member.IsHull && !FrameBounds.Fits(frames.Bounds(member.OwnedFrame), entryBounds))
                        continue;
                    double distance = entryBounds.SignedDistance(entry.Motion.Pose.InverseTransformPoint(center + entry.Offset));
                    if (distance <= radius && radius - distance > deepest)
                    {
                        best = i;
                        deepest = radius - distance;
                    }
                }
                if (best >= 0)
                {
                    BubbleEntry entry = bubbleEntries[best];
                    FrameMotion motion = entry.Motion;
                    var poseInParent = new DPose(s.Pose.Position + entry.Offset, s.Pose.Rotation);
                    Double3 centerInParent = s.CenterOfMass + entry.Offset;
                    var (linear, angular) = HandedOffVelocities(member, entry.Frame, motion.Pose.InverseTransformPoint(centerInParent),
                        motion.VelocityFromParent(centerInParent, InstantVelocity(member, s) + entry.Velocity),
                        motion.AngularFromParent(InstantAngularVelocity(member, s)));
                    handOffPlans.Add(new HandOffPlan(member, frame, entry.Frame, motion.PoseFromParent(poseInParent), linear, angular,
                        motion.Pose.Rotation.Conjugate()));
                }
            }
        }

        foreach (HandOffPlan plan in handOffPlans)
            ExecuteHandOff(plan, output);
    }

    /// <summary>
    /// True for a body that a hand-off can move between frames. Static bodies, interiors and the hulls of frames that
    /// physics does not drive never move between frames.
    /// </summary>
    private static bool MovesBetweenFrames(BodyRecord record) =>
        record.IsHull ? record.Motion == BodyMotion.Dynamic : record.Motion != BodyMotion.Static;

    /// <summary>
    /// Fills <paramref name="entries"/> with the bubbles that the members of the system of <paramref name="frame"/> meet:
    /// those of the semantic children of its semantic frame (see <see cref="BubbleSitesOf"/>). Numeric frames are an
    /// engine detail with no bubbles, so they do not change which bubbles a body meets. A fast body enters a parked
    /// station, whose hull is an anchor in the system of the station's parent. A body in a numeric frame of its own enters
    /// a ship that falls in another one, and a body at rest enters a fast ship. A body that touches one of these bubbles
    /// without entering it has a ghost there, and so does an anchor. The ghosts also read the bubbles that the members of
    /// a frame's parent meet, for a body that sticks out of the frame (see <see cref="UpdateGhosts"/>). The place and
    /// velocity of the system's origin in each parent are sums through the tree, in doubles, through their lowest common
    /// ancestor, at the end of the step. Numeric frames never rotate, so each parent has the axes of the system.
    /// </summary>
    private void PrepareBubbles(List<BubbleEntry> entries, FrameHandle frame, BubblesFor use)
    {
        entries.Clear();
        FrameHandle semantic = frames.SemanticOf(frame);
        Double3 velocity = HomeVelocityFor(use, frame, semantic);
        FrameHandle parent = FrameHandle.Null;
        Double3 offsetInParent = default, velocityInParent = default;
        foreach (BubbleSite site in BubbleSitesOf(semantic, use))
        {
            if (site.Parent != parent)
            {
                parent = site.Parent;
                offsetInParent = frames.OffsetAfter(frame, parent, stepDt);
                velocityInParent = velocity - HomeVelocityFor(use, parent, semantic);
            }
            entries.Add(new BubbleEntry(site.Parent, site.Frame, site.Motion, offsetInParent, velocityInParent));
        }
    }

    /// <summary>
    /// The velocity of the origin of <paramref name="home"/> relative to <paramref name="semantic"/>, the home itself or an
    /// ancestor of it, at the end of the step: its instant velocity for the hand-off, or the frame velocity after the step,
    /// which is a chord, for the ghosts.
    /// </summary>
    private Double3 HomeVelocityFor(BubblesFor use, FrameHandle home, FrameHandle semantic) =>
        use == BubblesFor.HandOff ? InstantHomeVelocity(home, semantic) : frames.HomeVelocityAfter(home, semantic, stepDt);

    /// <summary>
    /// The semantic children of <paramref name="semantic"/>: its children in the tree that are not numeric, and those of each
    /// numeric frame below it, found through numeric frames only, with the children of one parent next to each other. Every
    /// system of the semantic frame needs the same list, so it is collected once per step for each semantic frame and each
    /// use. <see cref="HandOffBodies"/> reads its list before any body moves, so the motions in it hold for the whole
    /// hand-off. <see cref="UpdateGhosts"/> reads its own after the hand-offs.
    /// </summary>
    private List<BubbleSite> BubbleSitesOf(FrameHandle semantic, BubblesFor use)
    {
        List<BubbleSite> sites = (use == BubblesFor.HandOff ? handOffSites : ghostSites).For(semantic, stepIndex, out bool collect);
        if (collect)
            CollectBubbleSites(semantic, sites, use);
        return sites;
    }

    private void CollectBubbleSites(FrameHandle parent, List<BubbleSite> sites, BubblesFor use)
    {
        List<FrameHandle> children = ChildrenOf(parent);
        foreach (FrameHandle child in children)
            if (!frames.IsNumeric(child))
                sites.Add(new BubbleSite(parent, child, use == BubblesFor.HandOff ? InstantMotionOf(child) : MotionOf(child)));
        foreach (FrameHandle child in children)
            if (frames.IsNumeric(child))
                CollectBubbleSites(child, sites, use);
    }

    private void ExecuteHandOff(in HandOffPlan plan, PhysicsStepOutput output)
    {
        BodyRecord record = plan.Record;
        if (record.Character is not null)
        {
            // A character's input state is in frame axes. Turn it with the axes.
            record.Up = plan.Turn.Rotate(record.Up);
            record.DesiredVelocity = plan.Turn.Rotate(record.DesiredVelocity);
            record.PendingVelocityChange = plan.Turn.Rotate(record.PendingVelocityChange);
        }
        FrameWorld target = WorldFor(plan.To);
        // The real body replaces its ghost.
        RemoveGhostIn(record, target);
        RemovePrimary(record);
        // A character that stood in this step takes its up from the new frame's gravity. One that did not stand falls
        // freely, so nothing turns it: it keeps its up and its rotation, turned into the new frame's axes.
        CreatePrimary(record, plan.To, plan.Pose, plan.LinearVelocity, plan.AngularVelocity, upFromGravity: record.Standing);
        DPose pose = plan.Pose;
        if (record.Character is { } character)
        {
            // Jolt starts a new character in the air. The character looks up its contacts now, as after a growth, so that
            // its next update knows the ground under it.
            character.RefreshContacts(PrimaryLayer(record), target.System);
            // The hand-off reports the rotation that the character got, which a turn to the new up can change.
            pose = ReadPrimary(record).Pose;
        }
        if (record.IsHull)
            MoveFrame(record.OwnedFrame, plan.From, plan.To);
        // The numeric update links the body to what it touches only at the sync point (see ResolveLooseContacts).
        if (frames.IsNumeric(plan.To))
            handedIntoNumericFrames.Add(record.Entity);
        output.HandOffs.Add(new HandOff(record.Entity, plan.From, plan.To, pose, plan.LinearVelocity, plan.AngularVelocity));
    }

    /// <summary>
    /// The parent that the members of <paramref name="frame"/> leave into when they leave its bubble, and where they get
    /// ghosts: its parent, or null for a root frame and for a numeric frame. A numeric frame has no bubble, so its members
    /// never leave it by a hand-off: only the engine moves them out.
    /// </summary>
    private FrameHandle BubbleParentOf(FrameHandle frame) => frames.IsNumeric(frame) ? FrameHandle.Null : ParentOf(frame);

    /// <summary>Records that a frame has a new parent for the rest of the step. The sync point decides whether the tree takes it.</summary>
    private void MoveFrame(FrameHandle frame, FrameHandle from, FrameHandle to)
    {
        parentOverrides[frame.Index] = (frame, to);
        ChildrenOf(from).Remove(frame);
        if (to.Index >= childLists.Length)
            Array.Resize(ref childLists, Math.Max(to.Index + 1, childLists.Length * 2));
        List<FrameHandle> list = childLists[to.Index] ??= new List<FrameHandle>();
        if (list.Count == 0)
            usedChildLists.Add(to.Index);
        list.Add(frame);
    }

    // ---- Ghosts ----

    /// <summary>
    /// Keeps a kinematic ghost of each moving body whose bounding sphere crosses a bubble boundary, in the system on
    /// the other side, and a ghost of each frame hull in the system of every sibling whose bubble overlaps its own.
    /// Numeric frames are an engine detail with no bubbles, so every body, hull and anchor gets the ghosts in semantic
    /// frames that it gets without numeric frames. The bubbles inside a body's frame are those that the hand-off lets it
    /// enter (see <see cref="PrepareBubbles"/>). An anchor never enters them, but a ghost is about touching, so it meets
    /// them too. A body that sticks out of its frame's bubble has a ghost in the frame's tree parent. When that parent is
    /// a numeric frame, it holds only the bodies that the merge test links to the frame's hull, so the body also has a
    /// ghost in the frame's semantic parent, whose own system holds the other bodies there. The body also meets the
    /// bubbles that the members of the parent's system meet, the semantic siblings of its frame, wherever they hang among
    /// numeric frames. Ghosts get the real body's pose and chord velocity, so that during the next step they move with it.
    /// Each anchor that the numeric update names in <see cref="FrameTree.AnchorGhosts"/> has a ghost in the falling frame
    /// that it names, a numeric frame of the anchor's own semantic frame, and loses it in the first pass after the update
    /// stops naming it, also when the tree did not change. No other ghost goes into such a frame, and the
    /// semantic parent is neither numeric nor a bubble that the body touches, so no frame gets two ghosts of one body.
    /// </summary>
    private void UpdateGhosts()
    {
        ReadOnlySpan<NumericGhost> anchorGhosts = frames.AnchorGhosts;
        // A new ghost can create the system of a frame that had no bodies. That world has no members, so the
        // snapshot of the count skips nothing that needs work.
        int worldCount = worldList.Count;
        for (int w = 0; w < worldCount; w++)
        {
            FrameWorld world = worldList[w];
            if (world.Members.Count == 0)
                continue;
            FrameHandle frame = world.Frame;
            FrameHandle parent = BubbleParentOf(frame);
            // The frame's semantic parent when its tree parent is a numeric frame, or else null.
            FrameHandle semanticParent = !parent.IsNull && frames.IsNumeric(parent) ? frames.SemanticOf(parent) : FrameHandle.Null;
            FrameBounds bounds = frames.Bounds(frame);
            PrepareBubbles(ghostBubbles, frame, BubblesFor.Ghosts);
            bool haveSiblingBubbles = false;
            bool haveFrameMotion = false;
            FrameMotion frameMotion = default;
            // The position and the chord velocity of the tree parent's origin in the semantic parent at the end of the step.
            Double3 parentOffset = default, parentVelocity = default;

            foreach (BodyRecord member in world.Members)
            {
                bool movable = member.IsMovable;
                bool anchor = (!movable || member.IsHull) && HasAnchorGhost(anchorGhosts, member.Entity);
                // A static body that is not a hull has ghosts only as an anchor. One that the numeric update stopped
                // naming still has them, and the pass drops them below, also when nothing changed the frame tree.
                if (!movable && !member.IsHull && !anchor && member.Ghosts is not { Count: > 0 })
                    continue;
                if (member.Ghosts is { Count: > 0 } existing)
                    foreach (ref GhostBody ghost in CollectionsMarshal.AsSpan(existing))
                        ghost.Wanted = false;

                Double3 center = PrimaryPosition(member);
                double radius = member.Radius;
                BodySnapshot s = default;
                bool haveSnapshot = false;

                // A moving body that sticks out of its frame's bubble has a ghost in the parent, and in each sibling
                // frame whose bubble it touches (it lost the tie of rule 3, or the bubbles overlap there). The body is
                // partly in the parent, so its siblings are the bubbles that the members of the parent's system meet,
                // those of the semantic children of the parent's semantic frame (see PrepareBubbles), apart from its own
                // frame. So a body in a ship that falls in a numeric frame meets a parked station beside it. The ghost in
                // the semantic parent sits where the ghost in the tree parent sits, shifted by the tree parent's place
                // and chord velocity in the semantic parent, sums through the numeric frames in doubles.
                if (movable && !parent.IsNull && bounds.SignedDistance(center) > -radius)
                {
                    if (!haveFrameMotion)
                    {
                        frameMotion = MotionOf(frame);
                        if (!semanticParent.IsNull)
                        {
                            parentOffset = frames.HomeOffsetAfter(parent, semanticParent, stepDt);
                            parentVelocity = frames.HomeVelocityAfter(parent, semanticParent, stepDt);
                        }
                        haveFrameMotion = true;
                    }
                    s = ReadPrimary(member);
                    haveSnapshot = true;
                    DPose parentPose = frameMotion.PoseToParent(s.Pose);
                    Double3 parentCenter = frameMotion.Pose.TransformPoint(s.CenterOfMass);
                    Double3 parentLinear = frameMotion.VelocityToParent(s.CenterOfMass, s.Linear);
                    Double3 parentAngular = frameMotion.AngularToParent(s.Angular);
                    WantGhost(member, parent, parentPose, parentLinear, parentAngular);
                    // A numeric tree parent holds only the bodies that the merge test links to the frame's hull. The
                    // semantic parent's own system holds the others, which meet the body without numeric frames, such as
                    // a body beside a ship that rests in a resting frame. So the body has a ghost there too. A nesting
                    // frame between the two holds no bodies, so it gets none.
                    if (!semanticParent.IsNull)
                        WantGhost(member, semanticParent, new DPose(parentPose.Position + parentOffset, parentPose.Rotation),
                            parentLinear + parentVelocity, parentAngular);

                    if (!haveSiblingBubbles)
                    {
                        PrepareBubbles(siblingBubbles, parent, BubblesFor.Ghosts);
                        haveSiblingBubbles = true;
                    }
                    foreach (BubbleEntry sibling in siblingBubbles)
                    {
                        if (sibling.Frame == frame || sibling.Frame == member.OwnedFrame)
                            continue;
                        FrameMotion motion = sibling.Motion;
                        var poseInSiblingParent = new DPose(parentPose.Position + sibling.Offset, parentPose.Rotation);
                        if (frames.Bounds(sibling.Frame).SignedDistance(motion.Pose.InverseTransformPoint(poseInSiblingParent.Position)) > radius)
                            continue;
                        WantGhost(member, sibling.Frame, motion.PoseFromParent(poseInSiblingParent),
                            motion.VelocityFromParent(parentCenter + sibling.Offset, parentLinear + sibling.Velocity),
                            motion.AngularFromParent(parentAngular));
                    }
                }

                // A body that touches the bubble of a semantic child of its semantic frame without being in it (it lost
                // a tie, or it is a hull that does not fit) has a ghost in that child. A hull reaches as far as its own
                // bubble. A ghost is about touching, not entering, so an anchor meets the same bubbles, although it never
                // moves between frames: a parked station's hull has a ghost in a ship that falls past it in a numeric
                // frame. A static body that is not a hull has no ghost in a bubble, with numeric frames or without. None
                // of these bubbles is a numeric frame, so none of them holds a ghost that the numeric update names.
                double reach = member.IsHull ? Math.Max(radius, frames.Bounds(member.OwnedFrame).MaxRadius) : radius;
                for (int i = 0; i < ghostBubbles.Count && (movable || member.IsHull); i++)
                {
                    BubbleEntry bubble = ghostBubbles[i];
                    if (bubble.Frame == member.OwnedFrame)
                        continue;
                    FrameMotion motion = bubble.Motion;
                    if (frames.Bounds(bubble.Frame).SignedDistance(motion.Pose.InverseTransformPoint(center + bubble.Offset)) > reach)
                        continue;
                    if (!haveSnapshot)
                    {
                        s = ReadPrimary(member);
                        haveSnapshot = true;
                    }
                    var poseInParent = new DPose(s.Pose.Position + bubble.Offset, s.Pose.Rotation);
                    Double3 centerInParent = s.CenterOfMass + bubble.Offset;
                    WantGhost(member, bubble.Frame, motion.PoseFromParent(poseInParent),
                        motion.VelocityFromParent(centerInParent, (Double3)s.Linear + bubble.Velocity), motion.AngularFromParent(s.Angular));
                }

                if (anchor)
                    WantAnchorGhosts(member, anchorGhosts, haveSnapshot ? s : ReadPrimary(member));

                DropUnwantedGhosts(member);
            }
        }
    }

    private static bool HasAnchorGhost(ReadOnlySpan<NumericGhost> anchorGhosts, Entity entity)
    {
        foreach (NumericGhost ghost in anchorGhosts)
            if (ghost.Anchor == entity)
                return true;
        return false;
    }

    /// <summary>
    /// Keeps a ghost of the anchor <paramref name="anchor"/>, whose body is at <paramref name="s"/> at the end of the step,
    /// in each falling frame that the numeric update names for it. The ghost goes where the anchor will be relative to the
    /// frame at the end of the step, when the frame has moved on, with the anchor's velocity relative to the frame then.
    /// That velocity is about the frame's speed in its semantic frame, so the ghost moves fast in the frame's system: the
    /// limit that the doc accepts for a static body that a fast frame passes.
    /// </summary>
    private void WantAnchorGhosts(BodyRecord anchor, ReadOnlySpan<NumericGhost> anchorGhosts, in BodySnapshot s)
    {
        FrameHandle home = anchor.Frame;
        FrameHandle semantic = frames.SemanticOf(home);
        foreach (NumericGhost ghost in anchorGhosts)
        {
            if (ghost.Anchor != anchor.Entity || !frames.IsAlive(ghost.Frame))
                continue;
            Double3 offset = frames.OffsetAfter(home, ghost.Frame, stepDt);
            Double3 velocity = frames.HomeVelocityAfter(home, semantic, stepDt) - frames.HomeVelocityAfter(ghost.Frame, semantic, stepDt);
            WantGhost(anchor, ghost.Frame, new DPose(s.Pose.Position + offset, s.Pose.Rotation), (Double3)s.Linear + velocity, s.Angular);
        }
    }

    private void WantGhost(BodyRecord record, FrameHandle frame, in DPose pose, Double3 linearVelocity, Double3 angularVelocity)
    {
        FrameWorld world = WorldFor(frame);
        Vector3 linear = linearVelocity.ToVector3(), angular = angularVelocity.ToVector3();
        List<GhostBody> ghosts = record.Ghosts ??= new List<GhostBody>(2);
        foreach (ref GhostBody ghost in CollectionsMarshal.AsSpan(ghosts))
        {
            if (ghost.World != world)
                continue;
            // Jolt does not simulate the move of a ghost, so the bodies around its old and new places wake.
            bool moves = !IsAt(world.Bodies, ghost.Body, pose);
            if (moves)
                world.WakeAround(ghost.Body);
            if (record.HasMesh)
            {
                if (moves)
                    world.Bodies.SetRPositionAndRotation(ghost.Body, pose.Position.ToJolt(), pose.Rotation.ToJolt(), Activation.DontActivate);
                world.SetStaticGhostMotion(ghost.Body, linear, angular);
            }
            else
            {
                world.Bodies.SetRPositionRotationAndVelocity(ghost.Body, pose.Position.ToJolt(), pose.Rotation.ToJolt(), linear, angular);
            }
            if (moves)
                world.WakeAround(ghost.Body);
            ghost.Wanted = true;
            return;
        }
        BodyID id = CreateRigid(world, GhostSpec(record), pose, linear, angular);
        world.AddGhost(id, ofStaticBody: !record.IsMovable);
        // Jolt never moves a mesh, so the ghost of a mesh is static, and contacts read its real body's velocity.
        if (record.HasMesh)
            world.SetStaticGhostMotion(id, linear, angular);
        ghosts.Add(new GhostBody { Frame = frame, World = world, Body = id, Wanted = true });
    }

    /// <summary>
    /// True when the frame tree allows a ghost of the record's body in <paramref name="frame"/>. These are exactly the
    /// frames where <see cref="UpdateGhosts"/> can put a ghost, apart from the frame that the record owns. Call the frame
    /// whose system holds the body its home.
    /// <list type="bullet">
    /// <item>The home's tree parent, unless the home is numeric: a body that sticks out of the home's bubble is partly
    /// there (see <see cref="BubbleParentOf"/>).</item>
    /// <item>The semantic frame of that tree parent, which is the home's semantic parent. When the tree parent is numeric,
    /// it holds only the bodies that the merge test links to the home's hull, and the semantic parent's own system holds
    /// the others, so a body that sticks out of the home's bubble is partly there too.</item>
    /// <item>A semantic child of the home's semantic frame, wherever it hangs among numeric frames (see
    /// <see cref="BubbleSitesOf"/>): its bubble is one that the body touches.</item>
    /// <item>A semantic child of the semantic frame of that tree parent, wherever it hangs among numeric frames: a sibling
    /// of the home, whose bubble a body that sticks out of the home's bubble touches.</item>
    /// <item>A falling frame that keeps a ghost of the anchor (see <see cref="FrameTree.AnchorGhosts"/>).</item>
    /// </list>
    /// Apart from the tree parent, these frames are never numeric unless they keep a ghost of an anchor. A record without a
    /// body has no place for a ghost.
    /// </summary>
    private bool GhostAllowed(BodyRecord record, FrameHandle frame)
    {
        FrameHandle home = record.Frame;
        if (home.IsNull || frame == home || frame == record.OwnedFrame || !frames.IsAlive(frame))
            return false;
        foreach (NumericGhost ghost in frames.AnchorGhosts)
            if (ghost.Anchor == record.Entity && ghost.Frame == frame)
                return true;
        FrameHandle bubbleParent = frames.IsNumeric(home) ? FrameHandle.Null : frames.TreeParent(home);
        if (!bubbleParent.IsNull && (frame == bubbleParent || frame == frames.SemanticOf(bubbleParent)))
            return true;
        if (frames.IsNumeric(frame))
            return false;
        // A root frame's semantic parent is null, which is the semantic frame of no frame.
        FrameHandle semanticParent = frames.Parent(frame);
        return semanticParent == frames.SemanticOf(home) || (!bubbleParent.IsNull && semanticParent == frames.SemanticOf(bubbleParent));
    }

    /// <summary>
    /// Removes every ghost that the frame tree no longer allows (see <see cref="GhostAllowed"/>). Visits every entity
    /// slot, so the reconcile runs it only when the tree changed or a frame was handed off.
    /// </summary>
    private void DropGhostsThatTheTreeForbids()
    {
        foreach (BodyRecord? record in records)
        {
            if (record?.Ghosts is not { Count: > 0 } ghosts)
                continue;
            for (int i = ghosts.Count - 1; i >= 0; i--)
                if (!GhostAllowed(record, ghosts[i].Frame))
                    RemoveGhostAt(ghosts, i);
        }
    }

    private void DropUnwantedGhosts(BodyRecord record)
    {
        if (record.Ghosts is not { Count: > 0 } ghosts)
            return;
        for (int i = ghosts.Count - 1; i >= 0; i--)
            if (!ghosts[i].Wanted)
                RemoveGhostAt(ghosts, i);
    }

    // ---- Output ----

    /// <summary>
    /// Reports every awake dynamic or kinematic body and every character, and the state of every character. A body that
    /// fell asleep during the step gets one more report, so that the simulation sees its resting pose and zero
    /// velocity. Every body in a falling numeric frame, apart from a hull, reports in every step, because its place in its
    /// semantic frame changes while it sleeps. The hand-offs of the step are done, so a character that a hand-off moved reports from its new frame,
    /// where it has not stood yet.
    /// </summary>
    private void WriteBodies(PhysicsStepOutput output)
    {
        foreach (FrameWorld world in worldList)
        {
            (world.ReportedLast, world.ReportedNow) = (world.ReportedNow, world.ReportedLast);
            world.ReportedNow.Clear();

            BodyInterface bodies = world.Bodies;
            int count = CopyActiveBodies(world);
            for (int i = 0; i < count; i++)
            {
                BodyID id = activeScratch[i];
                ulong data = bodies.GetUserData(id);
                if (BodyUserData.Kind(data) != BodyKind.Primary || RecordFor(data) is not { } record || record.Body != id || record.World != world)
                    continue;
                Report(world, record, output, track: true);
            }
            foreach (BodyRecord member in world.Members)
            {
                if (member.Character is null)
                    continue;
                Report(world, member, output, track: true);
                output.Characters.Add(new CharacterState(member.Entity, member.Standing));
            }
            foreach (BodyRecord record in world.ReportedLast)
                if (record.ReportedStep != stepIndex && record.World == world && Find(record.Entity) == record)
                    Report(world, record, output, track: false);
            // A falling numeric frame moves, so its sleeping and static members move in its semantic frame too.
            if (frames.Numeric(world.Frame) == NumericMode.Falling)
                foreach (BodyRecord member in world.Members)
                    if (member.ReportedStep != stepIndex && !member.IsHull)
                        Report(world, member, output, track: false);
        }
    }

    private void Report(FrameWorld world, BodyRecord record, PhysicsStepOutput output, bool track)
    {
        BodySnapshot s = ReadPrimary(record);
        output.Bodies.Add(new BodyState(record.Entity, record.Frame, s.Pose, s.Linear, s.Angular));
        record.ReportedStep = stepIndex;
        if (track)
            world.ReportedNow.Add(record);
    }

    /// <summary>
    /// Reports every physics-driven frame from its hull: pose and origin velocity relative to the parent, and the
    /// proper acceleration in frame axes. A frame that a hand-off moved this step reports relative to its new parent.
    /// </summary>
    private void WriteFrames(PhysicsStepOutput output)
    {
        foreach (BodyRecord owner in frameOwners)
        {
            if (owner.Motion != BodyMotion.Dynamic || !owner.HasPrimary)
                continue;
            BodySnapshot s = ReadPrimary(owner);
            Double3 originVelocity = (Double3)s.Linear + Double3.Cross(s.Angular, s.Pose.Position - s.CenterOfMass);
            output.Frames.Add(new FrameState(owner.OwnedFrame, s.Pose, originVelocity, s.Angular, owner.ProperAcceleration));
        }
    }
}
