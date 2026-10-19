using System.Numerics;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;
using Tertius.Frames.Rails;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    /// <summary>
    /// A hand-off decided after the step, with the body's state already converted to the target frame: its pose, the
    /// velocities that Jolt gets there, the acceleration relative to the target that turned the linear velocity into its
    /// chord (see <see cref="HandedOffVelocities"/>), and the angular acceleration of the step's update in the target's
    /// axes (see <see cref="FrameMotion.AngularAccelerationToParent"/>).
    /// </summary>
    private readonly record struct HandOffPlan(
        BodyRecord Record,
        FrameHandle From,
        FrameHandle To,
        DPose Pose,
        Vector3 LinearVelocity,
        Vector3 AngularVelocity,
        Double3 ChordAcceleration,
        Vector3 AngularAcceleration,
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
    /// (see <see cref="PrepareBubbles"/>). The bubble that a body leaves is that of the semantic frame too, and it leaves
    /// into that frame's tree parent (see <see cref="BubbleOf"/>). A frame's hull never enters its own frame. Every
    /// decision and conversion uses the state at the end of the step, before any body moves, so the order of execution
    /// does not matter.
    /// </summary>
    private void HandOffBodies(PhysicsStepOutput output)
    {
        handOffPlans.Clear();
        foreach (FrameWorld world in worldList)
        {
            if (world.Members.Count == 0)
                continue;
            FrameHandle frame = world.Frame;
            FrameHandle bubble = BubbleOf(frame);
            FrameHandle parent = ParentOf(bubble);
            FrameBounds bounds = frames.Bounds(bubble);
            // The place and the instant velocity of the system's origin in the bubble's frame at the end of the step, sums
            // through the numeric frames in doubles. Both are zero for the bubble's own system.
            Double3 homeOffset = frames.HomeOffsetAfter(frame, bubble, stepDt);
            Double3 homeVelocity = HomeVelocityFor(BubblesFor.HandOff, frame, bubble);
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

                if (!parent.IsNull && bounds.FullyOutside(center + homeOffset, radius, bounds.ExitMargin(radius)))
                {
                    if (!haveFrameMotion)
                    {
                        frameMotion = InstantMotionOf(bubble);
                        haveFrameMotion = true;
                    }
                    DPose pose = frameMotion.PoseToParent(new DPose(s.Pose.Position + homeOffset, s.Pose.Rotation));
                    Double3 centerInBubble = s.CenterOfMass + homeOffset;
                    var (linear, angular, acceleration) = HandedOffVelocities(member, parent, frameMotion.Pose.TransformPoint(centerInBubble),
                        frameMotion.VelocityToParent(centerInBubble, InstantVelocity(member, s) + homeVelocity),
                        frameMotion.AngularToParent(InstantAngularVelocity(member, s)));
                    handOffPlans.Add(new HandOffPlan(member, frame, parent, pose, linear, angular, acceleration,
                        frameMotion.AngularAccelerationToParent(member.AngularAcceleration).ToVector3(), frameMotion.Pose.Rotation));
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
                    var (linear, angular, acceleration) = HandedOffVelocities(member, entry.Frame, motion.Pose.InverseTransformPoint(centerInParent),
                        motion.VelocityFromParent(centerInParent, InstantVelocity(member, s) + entry.Velocity),
                        motion.AngularFromParent(InstantAngularVelocity(member, s)));
                    handOffPlans.Add(new HandOffPlan(member, frame, entry.Frame, motion.PoseFromParent(poseInParent), linear, angular, acceleration,
                        motion.AngularAccelerationFromParent(member.AngularAcceleration).ToVector3(), motion.Pose.Rotation.Conjugate()));
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
        if (record.IsCharacter)
        {
            // A character's input state is in frame axes, in both forms. Turn it with the axes.
            record.Up = plan.Turn.Rotate(record.Up);
            record.DesiredVelocity = plan.Turn.Rotate(record.DesiredVelocity);
            record.PendingVelocityChange = plan.Turn.Rotate(record.PendingVelocityChange);
        }
        FrameWorld target = WorldFor(plan.To);
        // The real body replaces its ghost.
        RemoveGhostIn(record, target);
        RemovePrimary(record);
        // A character that stood in this step takes its up from the new frame's gravity. One that did not stand falls
        // freely, so nothing turns it: it keeps its up and its rotation, turned into the new frame's axes. A character
        // keeps its form, which the next step changes if the new frame's gravity asks for the other one.
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
        {
            MoveFrame(record.OwnedFrame, plan.From, plan.To);
            // Jolt's velocity is the chord of the hull's first step in the new home now, so its frame's record takes the
            // acceleration that made that chord, in the new home's axes (see TryHullRecord). The angular acceleration of the
            // update turns into the new home's axes, as the angular velocity does.
            record.StepAcceleration = plan.ChordAcceleration;
            record.AngularAcceleration = plan.AngularAcceleration;
        }
        // A contact that no system takes over can wait for the numeric update of the sync point (see ResolveLooseContacts).
        handedOff.Add(record.Entity);
        output.HandOffs.Add(new HandOff(record.Entity, plan.From, plan.To, pose, plan.LinearVelocity, plan.AngularVelocity));
    }

    /// <summary>
    /// The frame whose bubble the members of the system of <paramref name="frame"/> stick out of and leave: its semantic
    /// frame, which is the frame itself unless it is numeric. A numeric frame has no bubble, so it does not change which
    /// bubble a body leaves. Its members leave the bubble of its semantic frame, at the same place, into the same tree
    /// parent, and they have ghosts in the same frames while they stick out, as the members of that frame's own system do
    /// (ticket ter-kz89). A free crate thrown at 1,000 m/s inside a ship, beyond the merge test's reach of the ship's
    /// interior, falls in a numeric frame below the ship, and it leaves the ship's bubble into the ship's parent. A root
    /// frame has no parent, so nothing leaves its bubble.
    /// </summary>
    private FrameHandle BubbleOf(FrameHandle frame) => frames.SemanticOf(frame);

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
    /// them too. A body that sticks out of the bubble of its home's semantic frame (see <see cref="BubbleOf"/>) has a
    /// ghost in that frame's tree parent, the home of the frame's hull, also when the home is a numeric frame below it. The
    /// numeric update links each body of the semantic parent that may touch such a ghost to the frame's hull, so that body
    /// lives in the same home (ticket ter-4psc), and no other system of the semantic parent needs a ghost of it. The one
    /// exception is a hull that lives apart from its group, in an orbit frame of its own, and that the home of its group
    /// keeps a ghost of (see <see cref="FrameTree.AnchorGhosts"/>): the frame hangs in the orbit frame, so the body has a
    /// ghost in each such home too. The body also meets the bubbles that the members of the parent's system meet, the
    /// semantic siblings of the frame, wherever they hang among numeric frames.
    /// Ghosts get the real body's pose and chord velocity, so that during the next step they move with it.
    /// Each anchor that the numeric update names in <see cref="FrameTree.AnchorGhosts"/> has a ghost in the home that it
    /// names, a frame of the anchor's own semantic frame, with the anchor's own layer. A ghost loses such a place at the
    /// sync point whose update stops naming it (see <see cref="DropWithdrawnAnchorGhosts"/>), so the pass finds only ghosts
    /// that the tree allows, as <see cref="GhostAllowed"/> says. No frame gets two ghosts of one body: an anchor's homes are
    /// frames of its own semantic frame, which no other ghost of it enters; the homes that keep a ghost of a frame's hull
    /// are frames of the semantic parent other than the frame's tree parent; and the bubbles that a body touches are not
    /// numeric, and none of them is the semantic parent.
    /// </summary>
    private void UpdateGhosts()
    {
        ReadOnlySpan<NumericGhost> anchorGhosts = frames.AnchorGhosts;
        if (!anchorGhosts.SequenceEqual(CollectionsMarshal.AsSpan(followedAnchorGhosts)))
        {
            followedAnchorGhosts.Clear();
            followedAnchorGhosts.AddRange(anchorGhosts);
        }
        // A new ghost can create the system of a frame that had no bodies. That world has no members, so the
        // snapshot of the count skips nothing that needs work.
        int worldCount = worldList.Count;
        for (int w = 0; w < worldCount; w++)
        {
            FrameWorld world = worldList[w];
            if (world.Members.Count == 0)
                continue;
            FrameHandle frame = world.Frame;
            FrameHandle bubble = BubbleOf(frame);
            FrameHandle parent = ParentOf(bubble);
            // True when the bubble's hull lives apart from its group and the group's home keeps a ghost of it, which hangs
            // the bubble in another frame than that home.
            bool hullApart = !parent.IsNull && HasAnchorGhost(anchorGhosts, bubble.Entity);
            FrameBounds bounds = frames.Bounds(bubble);
            // The place and the chord velocity of the system's origin in the bubble's frame at the end of the step, sums
            // through the numeric frames in doubles. Both are zero for the bubble's own system.
            Double3 homeOffset = frames.HomeOffsetAfter(frame, bubble, stepDt);
            Double3 homeVelocity = HomeVelocityFor(BubblesFor.Ghosts, frame, bubble);
            PrepareBubbles(ghostBubbles, frame, BubblesFor.Ghosts);
            bool haveSiblingBubbles = false;
            bool haveFrameMotion = false;
            FrameMotion frameMotion = default;

            foreach (BodyRecord member in world.Members)
            {
                bool movable = member.IsMovable;
                bool anchor = (!movable || member.IsHull) && HasAnchorGhost(anchorGhosts, member.Entity);
                // A static body that is not a hull has ghosts only as an anchor. The sync point drops those whose place the
                // numeric update stopped naming (see DropWithdrawnAnchorGhosts), and the pass drops below any other that it
                // does not want, as for every body.
                if (!movable && !member.IsHull && !anchor && member.Ghosts is not { Count: > 0 })
                    continue;
                if (member.Ghosts is { Count: > 0 } existing)
                    foreach (ref GhostBody ghost in CollectionsMarshal.AsSpan(existing))
                        ghost.Wanted = false;

                Double3 center = PrimaryPosition(member);
                double radius = member.Radius;
                BodySnapshot s = default;
                bool haveSnapshot = false;

                // A moving body that sticks out of the bubble (see BubbleOf) has a ghost in the bubble's tree parent, the
                // home of the bubble's hull, and in each sibling frame whose bubble it touches (it lost the tie of rule 3,
                // or the bubbles overlap there). The body is partly in the parent, so its siblings are the bubbles that
                // the members of the parent's system meet, those of the semantic children of the parent's semantic frame
                // (see PrepareBubbles), apart from the bubble itself. So a body in a ship that falls in a numeric frame
                // meets a parked station beside it, and so does a crate that falls in a numeric frame below the ship.
                if (movable && !parent.IsNull && bounds.SignedDistance(center + homeOffset) > -radius)
                {
                    if (!haveFrameMotion)
                    {
                        frameMotion = MotionOf(bubble);
                        haveFrameMotion = true;
                    }
                    s = ReadPrimary(member);
                    haveSnapshot = true;
                    Double3 centerInBubble = s.CenterOfMass + homeOffset;
                    DPose parentPose = frameMotion.PoseToParent(new DPose(s.Pose.Position + homeOffset, s.Pose.Rotation));
                    Double3 parentCenter = frameMotion.Pose.TransformPoint(centerInBubble);
                    Double3 parentLinear = frameMotion.VelocityToParent(centerInBubble, (Double3)s.Linear + homeVelocity);
                    Double3 parentAngular = frameMotion.AngularToParent(s.Angular);
                    WantGhost(member, parent, parentPose, parentLinear, parentAngular);
                    // The bubble's hull lives apart from its group, so the group's home holds the bodies that the merge
                    // test links to the hull, and the body has a ghost there too.
                    if (hullApart)
                        WantGhostsBesideHull(member, bubble.Entity, anchorGhosts, parent, parentPose, parentLinear, parentAngular);

                    if (!haveSiblingBubbles)
                    {
                        PrepareBubbles(siblingBubbles, parent, BubblesFor.Ghosts);
                        haveSiblingBubbles = true;
                    }
                    foreach (BubbleEntry sibling in siblingBubbles)
                    {
                        if (sibling.Frame == bubble || sibling.Frame == member.OwnedFrame)
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
                    BubbleEntry child = ghostBubbles[i];
                    if (child.Frame == member.OwnedFrame)
                        continue;
                    FrameMotion motion = child.Motion;
                    if (frames.Bounds(child.Frame).SignedDistance(motion.Pose.InverseTransformPoint(center + child.Offset)) > reach)
                        continue;
                    if (!haveSnapshot)
                    {
                        s = ReadPrimary(member);
                        haveSnapshot = true;
                    }
                    var poseInParent = new DPose(s.Pose.Position + child.Offset, s.Pose.Rotation);
                    Double3 centerInParent = s.CenterOfMass + child.Offset;
                    WantGhost(member, child.Frame, motion.PoseFromParent(poseInParent),
                        motion.VelocityFromParent(centerInParent, (Double3)s.Linear + child.Velocity), motion.AngularFromParent(s.Angular));
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
    /// Keeps a ghost of the record's body, which sticks out of the frame of <paramref name="hull"/>, in each home of a group
    /// that the numeric update names to keep a ghost of that hull (see <see cref="FrameTree.AnchorGhosts"/>). The hull lives
    /// apart from its group, in an orbit frame of its own, which is <paramref name="parent"/>, the frame's tree parent, and
    /// the group links the bodies of the semantic parent that may touch the body's ghost (ticket ter-4psc). The body's ghost
    /// in <paramref name="parent"/> is at <paramref name="pose"/> with <paramref name="linearVelocity"/> and
    /// <paramref name="angularVelocity"/> there. The ghost in each home goes where that ghost will be relative to the home at
    /// the end of the step, when the frames have moved on, as for the ghost of the hull itself (see
    /// <see cref="WantAnchorGhosts"/>).
    /// </summary>
    private void WantGhostsBesideHull(BodyRecord record, Entity hull, ReadOnlySpan<NumericGhost> anchorGhosts, FrameHandle parent, in DPose pose,
        Double3 linearVelocity, Double3 angularVelocity)
    {
        FrameHandle semantic = frames.SemanticOf(parent);
        foreach (NumericGhost ghost in anchorGhosts)
        {
            if (ghost.Anchor != hull || !frames.IsAlive(ghost.Frame))
                continue;
            Double3 offset = frames.OffsetAfter(parent, ghost.Frame, stepDt);
            Double3 velocity = frames.HomeVelocityAfter(parent, semantic, stepDt) - frames.HomeVelocityAfter(ghost.Frame, semantic, stepDt);
            WantGhost(record, ghost.Frame, new DPose(pose.Position + offset, pose.Rotation), linearVelocity + velocity, angularVelocity);
        }
    }

    /// <summary>
    /// Keeps a ghost of the anchor <paramref name="anchor"/>, whose body is at <paramref name="s"/> at the end of the step,
    /// in each home of a group that the numeric update names for it. The ghost goes where the anchor will be relative to the
    /// frame at the end of the step, when the frame has moved on, with the anchor's velocity relative to the frame then. The
    /// anchor is a Kepler hull in its orbit frame, so that velocity is about the orbit's speed in the home's system, and
    /// the ghost has no linear cast there: the limit that the doc accepts for anchors that move differently. The ghost has
    /// the hull's own layer, so it meets only static bodies and other hulls, as the hull does.
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
            WantGhost(anchor, ghost.Frame, new DPose(s.Pose.Position + offset, s.Pose.Rotation), (Double3)s.Linear + velocity, s.Angular,
                standsForAnchor: true);
        }
    }

    /// <summary>
    /// Keeps a ghost of the record's body in the system of <paramref name="frame"/> at <paramref name="pose"/>, moving
    /// with <paramref name="linearVelocity"/> and <paramref name="angularVelocity"/> in that frame, and marks it wanted.
    /// The ghost has the motion of <see cref="GhostMotion"/>, so a ghost of a mesh turns kinematic when it starts to move
    /// and static when it holds still. The ghost of an anchor in a home of its own semantic frame has the anchor's own
    /// layer when <paramref name="standsForAnchor"/> is true (see <see cref="GhostBody.Anchor"/>). No frame gets two
    /// ghosts of one body, so the ghost in a frame is always of one kind.
    /// </summary>
    private void WantGhost(BodyRecord record, FrameHandle frame, in DPose pose, Double3 linearVelocity, Double3 angularVelocity,
        bool standsForAnchor = false)
    {
        FrameWorld world = WorldFor(frame);
        Vector3 linear = linearVelocity.ToVector3(), angular = angularVelocity.ToVector3();
        MotionType motion = GhostMotion(record, linear, angular);
        List<GhostBody> ghosts = record.Ghosts ??= new List<GhostBody>(2);
        foreach (ref GhostBody ghost in CollectionsMarshal.AsSpan(ghosts))
        {
            if (ghost.World != world)
                continue;
            Contract.Assert(ghost.Anchor == standsForAnchor);
            BodyInterface bodies = world.Bodies;
            // Jolt does not simulate the move of a ghost, so the bodies around its old and new places wake.
            bool moves = !IsAt(bodies, ghost.Body, pose);
            if (moves)
                world.WakeAround(ghost.Body);
            // Jolt stops a body that becomes static, and a kinematic body moves only while it is awake.
            if (record.HasMesh && bodies.GetMotionType(ghost.Body) != motion)
                bodies.SetMotionType(ghost.Body, motion, motion == MotionType.Static ? Activation.DontActivate : Activation.Activate);
            if (motion == MotionType.Static)
            {
                if (moves)
                    bodies.SetRPositionAndRotation(ghost.Body, pose.Position.ToJolt(), pose.Rotation.ToJolt(), Activation.DontActivate);
            }
            else
            {
                bodies.SetRPositionRotationAndVelocity(ghost.Body, pose.Position.ToJolt(), pose.Rotation.ToJolt(), linear, angular);
            }
            if (moves)
                world.WakeAround(ghost.Body);
            ghost.Wanted = true;
            return;
        }
        BodyID id = CreateRigid(world, GhostSpec(record, motion, standsForAnchor ? PrimaryLayer(record) : GhostLayer(record)), pose, linear, angular);
        world.AddGhost(id, ofStaticBody: !record.IsMovable);
        ghosts.Add(new GhostBody { Frame = frame, World = world, Body = id, Wanted = true, Anchor = standsForAnchor });
    }

    /// <summary>
    /// True when the frame tree allows a ghost of the record's body in <paramref name="frame"/>. These are exactly the
    /// frames where <see cref="UpdateGhosts"/> can put a ghost, apart from the frame that the record owns. Call the frame
    /// whose system holds the body its home.
    /// <list type="bullet">
    /// <item>The tree parent of the home's semantic frame, which is the home itself unless the home is numeric: a body
    /// that sticks out of that frame's bubble is partly there (see <see cref="BubbleOf"/>). That tree parent is the home of
    /// the frame's hull, which holds every body of the semantic parent that the merge test links to the hull.</item>
    /// <item>A semantic child of the home's semantic frame, wherever it hangs among numeric frames (see
    /// <see cref="BubbleSitesOf"/>): its bubble is one that the body touches.</item>
    /// <item>A semantic child of the semantic frame of that tree parent, wherever it hangs among numeric frames: a sibling
    /// of the home's semantic frame, whose bubble a body that sticks out of that frame's bubble touches.</item>
    /// <item>The home of a group that keeps a ghost of the anchor (see <see cref="FrameTree.AnchorGhosts"/>).</item>
    /// <item>The home of a group that keeps a ghost of the hull of the home's semantic frame: that hull lives apart from
    /// the group, in an orbit frame of its own, so a body that sticks out of the frame's bubble is partly there too (see
    /// <see cref="WantGhostsBesideHull"/>).</item>
    /// </list>
    /// Apart from the tree parent, these frames are never numeric unless a group keeps a ghost of an anchor there. A record
    /// without a body has no place for a ghost.
    /// </summary>
    private bool GhostAllowed(BodyRecord record, FrameHandle frame)
    {
        FrameHandle home = record.Frame;
        if (home.IsNull || frame == home || frame == record.OwnedFrame || !frames.IsAlive(frame))
            return false;
        FrameHandle bubble = BubbleOf(home);
        foreach (NumericGhost ghost in frames.AnchorGhosts)
            if (ghost.Frame == frame && (ghost.Anchor == record.Entity || ghost.Anchor == bubble.Entity))
                return true;
        FrameHandle bubbleParent = frames.TreeParent(bubble);
        if (!bubbleParent.IsNull && frame == bubbleParent)
            return true;
        if (frames.IsNumeric(frame))
            return false;
        // A root frame's semantic parent is null, which is the semantic frame of no frame.
        FrameHandle semanticParent = frames.Parent(frame);
        return semanticParent == bubble || (!bubbleParent.IsNull && semanticParent == frames.SemanticOf(bubbleParent));
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

    // The pairs of FrameTree.AnchorGhosts whose ghosts the backend may hold: those that the ghost pass of the last step
    // followed, less the ones that a sync point withdrew since (see DropWithdrawnAnchorGhosts).
    private readonly List<NumericGhost> followedAnchorGhosts = new();
    // Scratch for DropWithdrawnAnchorGhosts: the ghosts of one system.
    private readonly List<BodyID> withdrawnScratch = new();

    /// <summary>
    /// Removes the ghosts whose place the numeric update of this sync point withdrew: for each pair of
    /// <see cref="FrameTree.AnchorGhosts"/> that the ghost pass followed and that the tree no longer names, each ghost in
    /// the system of its frame that the tree no longer allows there (see <see cref="GhostAllowed"/>). These are the ghost
    /// of the pair's anchor and the ghosts of the bodies that stick out of its frame (see
    /// <see cref="WantGhostsBesideHull"/>). The update does not change the tree's version for that, so the reconcile would
    /// not see it. A pair that the update names for the first time has no ghost yet: the next ghost pass makes it. An
    /// update that names the same pairs in the same order, as the next one usually does, costs one comparison of the
    /// lists. Otherwise it costs time in proportion to the ghosts in the systems of the withdrawn pairs.
    /// </summary>
    private void DropWithdrawnAnchorGhosts()
    {
        ReadOnlySpan<NumericGhost> named = frames.AnchorGhosts;
        if (named.SequenceEqual(CollectionsMarshal.AsSpan(followedAnchorGhosts)))
            return;
        for (int i = followedAnchorGhosts.Count - 1; i >= 0; i--)
        {
            if (named.Contains(followedAnchorGhosts[i]))
                continue;
            DropForbiddenGhostsIn(followedAnchorGhosts[i].Frame);
            followedAnchorGhosts.RemoveAt(i);
        }
    }

    /// <summary>Removes each ghost in the system of <paramref name="frame"/> that the frame tree does not allow there (see <see cref="GhostAllowed"/>).</summary>
    private void DropForbiddenGhostsIn(FrameHandle frame)
    {
        if (!frames.IsAlive(frame) || ExistingWorld(frame) is not { } world)
            return;
        withdrawnScratch.Clear();
        world.CopyGhosts(withdrawnScratch);
        foreach (BodyID body in withdrawnScratch)
        {
            // A system holds only the ghosts that the records keep in it (see AssertInvariants).
            BodyRecord record = RecordFor(world.Bodies.GetUserData(body))!;
            if (GhostAllowed(record, frame))
                continue;
            List<GhostBody> ghosts = record.Ghosts!;
            for (int i = ghosts.Count - 1; i >= 0; i--)
                if (ghosts[i].Body == body && ghosts[i].World == world)
                    RemoveGhostAt(ghosts, i);
        }
        withdrawnScratch.Clear();
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
    /// Reports every awake dynamic or kinematic body and every character in either form, also one in the rigid form that
    /// sleeps, and the state of every character. A body that
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
            // A character reports in every step. The loop above reported a character in the rigid form while it is awake.
            if (world.CharacterCount > 0)
            {
                foreach (BodyRecord member in world.Members)
                {
                    if (!member.IsCharacter)
                        continue;
                    if (member.ReportedStep != stepIndex)
                        Report(world, member, output, track: true);
                    output.Characters.Add(new CharacterState(member.Entity, member.Standing));
                }
            }
            foreach (BodyRecord record in world.ReportedLast)
                if (record.ReportedStep != stepIndex && record.World == world && Find(record.Entity) == record)
                    Report(world, record, output, track: false);
            // A falling numeric frame or an orbit frame moves, so its sleeping and static members move in its semantic frame
            // too.
            if (frames.Numeric(world.Frame) is NumericMode.Falling or NumericMode.Orbiting)
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
    /// Releases the step record of the frame of every hull that moved in the step (see <see cref="StepRecordOf"/>): every
    /// dynamic hull, from Jolt's state after the update; every kinematic hull that followed a sample, from the sample that
    /// it applied, exactly; and every static hull, or kinematic hull without a sample, that teleported in the step, at the
    /// teleport's pose in its home, at rest there: a kinematic hull holds still in its home after a teleport too (see
    /// <see cref="ApplyBodyIntent"/>). Any other static hull, and a kinematic hull without a sample, held still, and the
    /// sync point holds its frame still in its home (see <see cref="FrameTree.HoldStill"/>). A record is relative to the
    /// frame's tree parent, the hull's home, so a frame that a hand-off moved this step reports relative to its new parent,
    /// and a hull at the origin of its orbit frame reports at that origin.
    /// <para>
    /// The tree keeps the velocity of a frame in a numeric home relative to the home's frame velocity, the chord of the
    /// step, so that a read in the semantic parent gives the frame's instant velocity, and a hull that physics places from
    /// the tree again takes the velocity that it had (see <see cref="LinearChord(BodyRecord, FrameHandle, Double3, Double3)"/>).
    /// Physics keeps a velocity relative to the home's instant velocity. So each record that physics releases adds the
    /// home's instant velocity past its chord (see <see cref="FrameTree.HomeInstantVelocityOverChord"/>), zero in a semantic
    /// home. A hull at the origin of its orbit frame rests there, so its record keeps just that term: for an orbit frame
    /// whose hull has a sample, the sample's instant velocity less the sample's chord, which is the orbit frame's frame
    /// velocity after the step (see <see cref="FrameTree.PlaceFollowingFrame"/>). Every other frame in that orbit frame adds
    /// the same term, so it reads its velocity in the semantic parent relative to the hull's.
    /// </para>
    /// </summary>
    private void WriteFrames(PhysicsStepOutput output)
    {
        foreach (BodyRecord owner in frameOwners)
        {
            if (!owner.HasPrimary || owner.Character is not null)
                continue;
            FrameHandle frame = owner.OwnedFrame;
            bool sampled = owner.Motion == BodyMotion.Kinematic && stepSamples.TryGet(frame, out _);
            if (owner.Motion != BodyMotion.Dynamic && !sampled && owner.TeleportStep != stepIndex)
                continue;
            MotionSample record = StepRecordOf(frame);
            // Physics measures the proper acceleration of a dynamic hull (see ComputeProperAccelerations). The frame tree
            // derives every other frame's from its record (see FrameTree.ProperAcceleration).
            Vector3? measured = owner.Motion == BodyMotion.Dynamic ? owner.ProperAcceleration : null;
            output.Frames.Add(new FrameStepRecord(frame, record with { Velocity = record.Velocity + HomeInstantVelocityOverChord(owner.Frame) }, measured));
        }
    }
}
