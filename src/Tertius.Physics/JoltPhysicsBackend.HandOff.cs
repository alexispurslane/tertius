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
    /// chord (see <see cref="HandedOffVelocities"/>), and the angular acceleration of the step's update relative to the
    /// target, in its axes, by the full rule with the frame's angular velocity and angular acceleration (see
    /// <see cref="FrameMotion.AngularAccelerationToParent"/>).
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
    /// relative to that parent at the end of the step, with an instant velocity or a chord (see <see cref="BubblesFor"/>),
    /// and for the ghosts its poses in that parent at both ends of the step when it turns against the parent (see
    /// <see cref="PosesInStep"/>).
    /// </summary>
    private readonly record struct BubbleSite(FrameHandle Parent, FrameHandle Frame, FrameMotion Motion, StepPoses? Poses);

    /// <summary>
    /// A bubble that the members of one system meet: the frame <see cref="Frame"/>, whose tree parent is
    /// <see cref="Parent"/>, its motion relative to that parent at the end of the step, for the ghosts its poses in that
    /// parent at both ends of the step when it turns against the parent, and the position and velocity of the system's origin
    /// in that parent then. Both are zero for a child of the system's own frame. Numeric frames never rotate, so a shift of
    /// position and velocity takes a member's state from the system into the parent. The velocities are instant ones or
    /// chords, as <see cref="BubblesFor"/> says.
    /// </summary>
    private readonly record struct BubbleEntry(FrameHandle Parent, FrameHandle Frame, FrameMotion Motion, StepPoses? Poses, Double3 Offset,
        Double3 Velocity)
    {
        /// <summary>
        /// The bubble's motion relative to <see cref="Parent"/>. It is a field, not a property, so that the scans of
        /// <see cref="HandOffBodies"/> and <see cref="UpdateGhosts"/> read it in place, with no copy per body.
        /// </summary>
        public readonly FrameMotion Motion = Motion;

        /// <summary>
        /// The chord in <see cref="Frame"/> of a body that ends the step at <paramref name="pointInParent"/> with the chord
        /// <paramref name="chord"/> in <see cref="Parent"/>: by the frame's poses at both ends of the step when it turns
        /// against the parent (see <see cref="StepPoses.ChordFromParent"/>), else by its motion at the end of the step, which
        /// holds no turn then (see <see cref="PosesInStep"/>). Only the entries of the ghosts hold chords.
        /// </summary>
        public Double3 ChordFromParent(Double3 pointInParent, Double3 chord, double dt) =>
            Poses is { } poses ? poses.ChordFromParent(pointInParent, chord, dt) : Motion.VelocityFromParent(pointInParent, chord);

        /// <summary>The angular chord in <see cref="Frame"/> of a body whose angular chord in <see cref="Parent"/> is <paramref name="chord"/>, by the rule of <see cref="ChordFromParent"/>.</summary>
        public Double3 AngularChordFromParent(Double3 chord, double dt) =>
            Poses is { } poses ? poses.AngularChordFromParent(chord, dt) : Motion.AngularFromParent(chord);
    }

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
        /// <see cref="MotionOf"/>), because a ghost follows the chord of its real body, and a frame that turns against its
        /// parent converts them by its poses at both ends of the step (see <see cref="PosesInStep"/>).
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
    // Scratch of the ghost pass (see UpdateGhosts): the content excess of each semantic frame with a parent whose systems
    // hold a member that can move, and the body origin of each such member at the end of the step, at the index of its
    // system's first member in ghostCenterStarts plus its own index among the members. A frame without an entry has an
    // excess of zero.
    private readonly Dictionary<FrameHandle, double> contentExcess = new();
    private readonly List<Double3> ghostCenters = new();
    private readonly List<int> ghostCenterStarts = new();

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
                    Double3 angularInBubble = InstantAngularVelocity(member, s);
                    var (linear, angular, acceleration) = HandedOffVelocities(member, parent, frameMotion.Pose.TransformPoint(centerInBubble),
                        frameMotion.VelocityToParent(centerInBubble, InstantVelocity(member, s) + homeVelocity),
                        frameMotion.AngularToParent(angularInBubble));
                    handOffPlans.Add(new HandOffPlan(member, frame, parent, pose, linear, angular, acceleration,
                        frameMotion.AngularAccelerationToParent(member.AngularAcceleration, angularInBubble).ToVector3(), frameMotion.Pose.Rotation));
                    continue;
                }

                int best = -1;
                double deepest = double.NegativeInfinity;
                // The scan reads each entry in place: a copy would move the whole entry, frame motion and all, for every body.
                ReadOnlySpan<BubbleEntry> entries = CollectionsMarshal.AsSpan(bubbleEntries);
                for (int i = 0; i < entries.Length; i++)
                {
                    ref readonly BubbleEntry entry = ref entries[i];
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
                    ref readonly BubbleEntry entry = ref entries[best];
                    ref readonly FrameMotion motion = ref entry.Motion;
                    var poseInParent = new DPose(s.Pose.Position + entry.Offset, s.Pose.Rotation);
                    Double3 centerInParent = s.CenterOfMass + entry.Offset;
                    Double3 angularInParent = InstantAngularVelocity(member, s);
                    var (linear, angular, acceleration) = HandedOffVelocities(member, entry.Frame, motion.Pose.InverseTransformPoint(centerInParent),
                        motion.VelocityFromParent(centerInParent, InstantVelocity(member, s) + entry.Velocity),
                        motion.AngularFromParent(angularInParent));
                    handOffPlans.Add(new HandOffPlan(member, frame, entry.Frame, motion.PoseFromParent(poseInParent), linear, angular, acceleration,
                        motion.AngularAccelerationFromParent(member.AngularAcceleration, angularInParent).ToVector3(), motion.Pose.Rotation.Conjugate()));
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
            entries.Add(new BubbleEntry(site.Parent, site.Frame, site.Motion, site.Poses, offsetInParent, velocityInParent));
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
        {
            if (frames.IsNumeric(child))
                continue;
            if (use == BubblesFor.HandOff)
            {
                sites.Add(new BubbleSite(parent, child, InstantMotionOf(child), null));
                continue;
            }
            FrameMotion motion = MotionOf(child);
            sites.Add(new BubbleSite(parent, child, motion, PosesInStep(child, parent, motion.Pose)));
        }
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
    /// The poses of <paramref name="frame"/> in <paramref name="parent"/> at both ends of the step, by which a ghost converts
    /// its real body's chords between the two (docs/plans/spin-gravity.md, "The two conversion rules"), when the frame turns
    /// against its parent in the step: when the turn of the step in flight of the frame or of the parent is not the identity
    /// (see <see cref="FrameTree.StepTurn"/>). The end is <paramref name="end"/>, the pose of the frame's step record (see
    /// <see cref="MotionOf"/>), and the start the frame's pose where the step started (see <see cref="StartPoseOf"/>). Null
    /// otherwise: the frame's rotation in its parent is the same at both ends, and the ghost converts by the frame's motion at
    /// the end of the step, which keeps the results of frames that do not turn to the bit. Null too for a frame whose hull
    /// handed off into <paramref name="parent"/> in the step: its pose where the step started is in its old parent, which
    /// the design gives no rule for, so for that one step its ghosts keep the rule of the end of the step.
    /// </summary>
    private StepPoses? PosesInStep(FrameHandle frame, FrameHandle parent, in DPose end)
    {
        if (frames.StepTurn(frame).IsIdentity && frames.StepTurn(parent).IsIdentity)
            return null;
        if (frames.TreeParent(frame) != parent)
            return null;
        return new StepPoses(StartPoseOf(frame), end);
    }

    /// <summary>
    /// The pose of <paramref name="frame"/> in its tree parent where the step started: the pose of the teleport that its
    /// level took in the step, else its tree pose, as the frame tree's BeginStep takes the start of a level
    /// (docs/plans/spin-gravity.md, "V1, the turn of the step in flight"). A hull's teleport is in the hull's home, the
    /// frame's tree parent (see <see cref="ApplyBodyIntent"/> and <see cref="TeleportStaticHull"/>). A frame without a hull
    /// takes the teleport of an intent whose motion intent gives the frame its step record (see
    /// <see cref="PhysicsIntent.StepRecord"/>), and holds still where it stands otherwise (see <see cref="PredictRecord"/>).
    /// </summary>
    private DPose StartPoseOf(FrameHandle frame)
    {
        if (OwnerRecord(frame) is { } hull)
            return hull.HasPrimary && hull.TeleportStep == stepIndex ? hull.TeleportPose : frames.TreeLocalPose(frame);
        ReadOnlySpan<PhysicsIntent> intents = stepIntents.Span;
        int slot = (int)frame.Entity.Index;
        return slot < intents.Length && (intents[slot].Flags & IntentFlags.Teleport) != 0 && intents[slot].Move.FollowKind != FollowKind.Static
            ? intents[slot].TeleportPose
            : frames.TreeLocalPose(frame);
    }

    /// <summary>
    /// Keeps a ghost of each body in every system whose bodies can touch it (docs/plans/complete-ghosts.md): in each child
    /// frame whose content it reaches, and, for a body that can move and sticks out of its bubble, in the bubble's tree
    /// parent and in each sibling frame whose content it reaches. The content of a frame is its bubble grown by its
    /// excess, how far the bounding spheres of the members of the frame's systems that can move reach beyond the bubble
    /// (fork F2). Only a static body, a kinematic body or a hull reaches the content; a dynamic plain body or a character
    /// reaches only the bubble (see <see cref="BodyRecord.GhostsReachContent"/>). A first pass over the members of every
    /// system measures the excess, and keeps the origin of each member that can move for the second pass, which places the
    /// ghosts, so no position is read twice.
    /// Numeric frames are an engine detail with no bubbles, so every body, hull and anchor gets the ghosts in semantic
    /// frames that it gets without numeric frames. The bubbles inside a body's frame are those that the hand-off lets it
    /// enter (see <see cref="PrepareBubbles"/>). A static body and an anchor never enter them, but a ghost is about
    /// touching, so they meet them too. A body that sticks out of the bubble of its home's semantic frame (see
    /// <see cref="BubbleOf"/>) has a ghost in that frame's tree parent, the home of the frame's hull, also when the home is
    /// a numeric frame below it. The numeric update links each body of the semantic parent that may touch such a ghost to
    /// the frame's hull, so that body lives in the same home (ticket ter-4psc), and no other system of the semantic parent
    /// needs a ghost of it. The one exception is a hull that lives apart from its group, in a following frame of its own, and
    /// that the home of its group keeps a ghost of (see <see cref="FrameTree.AnchorGhosts"/>): the frame hangs in the following
    /// frame, so the body has a ghost in each such home too. The body also meets the bubbles that the members of the
    /// parent's system meet, the semantic siblings of the frame, wherever they hang among numeric frames. A static body
    /// has no stick-out ghost, and an interior has no ghost, until the follow-up of fork F3.
    /// Ghosts get the real body's pose and chord velocity, so that during the next step they move with it.
    /// Each anchor that the numeric update names in <see cref="FrameTree.AnchorGhosts"/> has a ghost in the home that it
    /// names, a frame of the anchor's own semantic frame, with the anchor's own layer. A ghost loses such a place at the
    /// sync point whose update stops naming it (see <see cref="DropWithdrawnAnchorGhosts"/>), so the pass finds only ghosts
    /// that the tree allows, as <see cref="GhostAllowed"/> says. No frame gets two ghosts of one body: an anchor's homes are
    /// frames of its own semantic frame, which no other ghost of it enters; the homes that keep a ghost of a frame's hull
    /// are frames of the semantic parent other than the frame's tree parent; and the bubbles that a body touches are not
    /// numeric, and none of them is the semantic parent. Each ghost has the role of its kind (see <see cref="GhostBody.Role"/>).
    /// A system whose semantic frame has no child bubble skips its static members, unless one keeps a ghost to drop.
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
        MeasureContent(worldCount);
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
            StepPoses? framePoses = null;
            int centerStart = ghostCenterStarts[w];

            for (int m = 0; m < world.Members.Count; m++)
            {
                BodyRecord member = world.Members[m];
                bool movable = member.IsMovable;
                // A static body that is not a hull has only child ghosts. The pass drops below any ghost that it does not
                // want, as for every body.
                if (!movable && !member.IsHull && ghostBubbles.Count == 0 && member.Ghosts is not { Count: > 0 })
                    continue;
                // The numeric update names the anchors of following frames, hulls and plain kinematic bodies (see
                // FrameTree.AnchorGhosts).
                bool anchor = HasAnchorGhost(anchorGhosts, member.Entity);
                if (member.Ghosts is { Count: > 0 } existing)
                    foreach (ref GhostBody ghost in CollectionsMarshal.AsSpan(existing))
                        ghost.Wanted = false;

                Double3 center = movable ? ghostCenters[centerStart + m] : PrimaryPosition(member);
                double radius = member.Radius;
                BodySnapshot s = default;
                bool haveSnapshot = false;
                // The role of a child ghost, which a ghost in a sibling of the bubble has too: neither system holds a hull of
                // a frame that holds the body, so a ghost of a static body or of a hull there meets hulls, as its body does
                // (see GhostBody.Role).
                BodyLayer childRole = member.IsHull ? BodyLayer.Static : movable ? BodyLayer.Ghost : RoleFor(member.Desc);

                // A moving body that sticks out of the bubble (see BubbleOf) has a ghost in the bubble's tree parent, the
                // home of the bubble's hull, and in each sibling frame whose content it reaches (it lost the tie of rule 3,
                // or the bubbles overlap there). A dynamic plain body or a character reaches only the sibling's bubble (see
                // BodyRecord.GhostsReachContent). The body is partly in the parent, so its siblings are the bubbles that
                // the members of the parent's system meet, those of the semantic children of the parent's semantic frame
                // (see PrepareBubbles), apart from the bubble itself. So a body in a ship that falls in a numeric frame
                // meets a parked station beside it, and so does a crate that falls in a numeric frame below the ship.
                // The ghost in the parent has the ghost role, since the parent's system holds the bubble's hull. A sibling's
                // system holds no hull of a frame that holds the body, so a ghost there has the role of a child ghost.
                if (movable && !parent.IsNull && bounds.SignedDistance(center + homeOffset) > -radius)
                {
                    if (!haveFrameMotion)
                    {
                        frameMotion = MotionOf(bubble);
                        framePoses = PosesInStep(bubble, parent, frameMotion.Pose);
                        haveFrameMotion = true;
                    }
                    s = ReadPrimary(member);
                    haveSnapshot = true;
                    Double3 centerInBubble = s.CenterOfMass + homeOffset;
                    DPose parentPose = frameMotion.PoseToParent(new DPose(s.Pose.Position + homeOffset, s.Pose.Rotation));
                    Double3 parentCenter = frameMotion.Pose.TransformPoint(centerInBubble);
                    Double3 chordInBubble = (Double3)s.Linear + homeVelocity;
                    // A bubble that turns against its parent in the step converts the chords by its poses at both ends of the
                    // step (see PosesInStep).
                    Double3 parentLinear = framePoses is { } poses
                        ? poses.ChordToParent(centerInBubble, chordInBubble, stepDt)
                        : frameMotion.VelocityToParent(centerInBubble, chordInBubble);
                    Double3 parentAngular = framePoses is { } turning
                        ? turning.AngularChordToParent(s.Angular, stepDt)
                        : frameMotion.AngularToParent(s.Angular);
                    WantGhost(member, parent, parentPose, parentLinear, parentAngular, BodyLayer.Ghost);
                    // The bubble's hull lives apart from its group, so the group's home holds the bodies that the merge
                    // test links to the hull, and the body has a ghost there too.
                    if (hullApart)
                        WantGhostsBesideHull(member, bubble.Entity, anchorGhosts, parent, parentPose, parentLinear, parentAngular);

                    if (!haveSiblingBubbles)
                    {
                        PrepareBubbles(siblingBubbles, parent, BubblesFor.Ghosts);
                        haveSiblingBubbles = true;
                    }
                    foreach (ref readonly BubbleEntry sibling in CollectionsMarshal.AsSpan(siblingBubbles))
                    {
                        if (sibling.Frame == bubble || sibling.Frame == member.OwnedFrame)
                            continue;
                        ref readonly FrameMotion motion = ref sibling.Motion;
                        var poseInSiblingParent = new DPose(parentPose.Position + sibling.Offset, parentPose.Rotation);
                        if (frames.Bounds(sibling.Frame).SignedDistance(motion.Pose.InverseTransformPoint(poseInSiblingParent.Position)) >
                            radius + ContentExcessFor(member, sibling.Frame))
                            continue;
                        WantGhost(member, sibling.Frame, motion.PoseFromParent(poseInSiblingParent),
                            sibling.ChordFromParent(parentCenter + sibling.Offset, parentLinear + sibling.Velocity, stepDt),
                            sibling.AngularChordFromParent(parentAngular, stepDt), childRole);
                    }
                }

                // A body that reaches the content of a semantic child of its semantic frame without being in it (it lost a
                // tie, it is a hull that does not fit, or it never moves between frames) has a ghost in that child. A hull
                // reaches as far as its own bubble. A dynamic plain body or a character reaches only the child's bubble
                // (see BodyRecord.GhostsReachContent). A ghost is about touching, not entering, so a static body and an
                // anchor meet the same bubbles, although they never move between frames: the slab under a parked shuttle
                // has a ghost in the shuttle's bubble, and a parked station's hull has a ghost in a ship that falls past it
                // in a numeric frame. None of these bubbles is a numeric frame, so none of them holds a ghost that the numeric
                // update names. The child's system holds no hull of a frame that holds the body, so a child ghost of a
                // static body or a hull meets hulls, as its body does (see GhostBody.Role).
                double reach = member.IsHull ? Math.Max(radius, frames.Bounds(member.OwnedFrame).MaxRadius) : radius;
                foreach (ref readonly BubbleEntry child in CollectionsMarshal.AsSpan(ghostBubbles))
                {
                    if (child.Frame == member.OwnedFrame)
                        continue;
                    ref readonly FrameMotion motion = ref child.Motion;
                    if (frames.Bounds(child.Frame).SignedDistance(motion.Pose.InverseTransformPoint(center + child.Offset)) > reach + ContentExcessFor(member, child.Frame))
                        continue;
                    if (!haveSnapshot)
                    {
                        s = ReadPrimary(member);
                        haveSnapshot = true;
                    }
                    var poseInParent = new DPose(s.Pose.Position + child.Offset, s.Pose.Rotation);
                    Double3 centerInParent = s.CenterOfMass + child.Offset;
                    WantGhost(member, child.Frame, motion.PoseFromParent(poseInParent),
                        child.ChordFromParent(centerInParent, (Double3)s.Linear + child.Velocity, stepDt),
                        child.AngularChordFromParent(s.Angular, stepDt), childRole);
                }

                if (anchor)
                    WantAnchorGhosts(member, anchorGhosts, haveSnapshot ? s : ReadPrimary(member));

                DropUnwantedGhosts(member);
            }
        }
    }

    /// <summary>
    /// The first pass of <see cref="UpdateGhosts"/> over the first <paramref name="worldCount"/> systems: keeps the body
    /// origin of each member that can move in <see cref="ghostCenters"/>, and the content excess of each semantic frame with
    /// a parent in <see cref="contentExcess"/>: the largest signed distance of such a member's origin from the frame's bubble
    /// plus its radius, over the members of every system of the frame, its own and those of its numeric frames, when that
    /// is above zero. A static member never sticks out of its bubble (fork F3), so it does not count, and its origin is read
    /// only by the second pass, which needs it only in a system whose semantic frame has a child bubble.
    /// </summary>
    private void MeasureContent(int worldCount)
    {
        contentExcess.Clear();
        ghostCenters.Clear();
        ghostCenterStarts.Clear();
        for (int w = 0; w < worldCount; w++)
        {
            FrameWorld world = worldList[w];
            ghostCenterStarts.Add(ghostCenters.Count);
            if (world.Members.Count == 0)
                continue;
            FrameHandle bubble = BubbleOf(world.Frame);
            bool hasParent = !ParentOf(bubble).IsNull;
            FrameBounds bounds = hasParent ? frames.Bounds(bubble) : default;
            Double3 homeOffset = hasParent ? frames.HomeOffsetAfter(world.Frame, bubble, stepDt) : default;
            double excess = 0;
            foreach (BodyRecord member in world.Members)
            {
                if (!member.IsMovable)
                {
                    ghostCenters.Add(default);
                    continue;
                }
                Double3 center = PrimaryPosition(member);
                ghostCenters.Add(center);
                if (hasParent)
                    excess = Math.Max(excess, bounds.SignedDistance(center + homeOffset) + member.Radius);
            }
            if (excess > 0)
            {
                ref double stored = ref CollectionsMarshal.GetValueRefOrAddDefault(contentExcess, bubble, out _);
                stored = Math.Max(stored, excess);
            }
        }
    }

    /// <summary>
    /// How far beyond the bubble of <paramref name="frame"/> a ghost of <paramref name="member"/> reaches: the frame's content
    /// excess that the first pass of the ghost pass measured (see <see cref="MeasureContent"/>) when the member's ghosts reach
    /// the content, and zero when they reach only the bubble (see <see cref="BodyRecord.GhostsReachContent"/>).
    /// </summary>
    private double ContentExcessFor(BodyRecord member, FrameHandle frame) =>
        member.GhostsReachContent && contentExcess.TryGetValue(frame, out double excess) ? excess : 0;

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
    /// apart from its group, in a following frame of its own, which is <paramref name="parent"/>, the frame's tree parent, and
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
            WantGhost(record, ghost.Frame, new DPose(pose.Position + offset, pose.Rotation), linearVelocity + velocity, angularVelocity, BodyLayer.Ghost);
        }
    }

    /// <summary>
    /// Keeps a ghost of the anchor <paramref name="anchor"/>, whose body is at <paramref name="s"/> at the end of the step,
    /// in each home of a group that the numeric update names for it. The ghost goes where the anchor will be relative to the
    /// frame at the end of the step, when the frame has moved on, with the anchor's velocity relative to the frame then. The
    /// anchor rests in a following frame of its own, so that velocity is about the anchor's speed in the home's system, and
    /// the ghost has no linear cast there: the limit that the doc accepts for anchors that move differently. The ghost has
    /// the anchor's own layer, so it meets what the anchor meets: a hull's ghost only static bodies and other hulls, and a
    /// plain kinematic body's ghost what that body's layer and collision groups name (docs/plans/numeric-frames.md,
    /// "Following frames").
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
                anchor.IsHull ? BodyLayer.FrameHull : RoleFor(anchor.Desc));
        }
    }

    /// <summary>
    /// Keeps a ghost of the record's body in <paramref name="role"/> (see <see cref="GhostBody.Role"/>) in the system of
    /// <paramref name="frame"/> at <paramref name="pose"/>, moving with <paramref name="linearVelocity"/> and
    /// <paramref name="angularVelocity"/> in that frame, and marks it wanted. The ghost has the motion of
    /// <see cref="GhostMotion"/>, so a ghost of a static body turns kinematic when it starts to move and static when it
    /// holds still. No frame gets two ghosts of one body, so a second want of one ghost in another role before the next
    /// pass is a bug, which the backend reports and skips. A ghost keeps its role, so one that needs another role is made
    /// again: when a frame enters a sibling, a hull in it that sticks out turns its ghost in that sibling, which had the role
    /// of a child ghost, into a stick-out ghost in the frame's new tree parent, in the ghost role.
    /// Removing the old ghost loosens its contacts, and the carry of the end of the step keeps each one that the new ghost
    /// can make (see <see cref="ResolveLooseContacts"/>).
    /// </summary>
    private void WantGhost(BodyRecord record, FrameHandle frame, in DPose pose, Double3 linearVelocity, Double3 angularVelocity, BodyLayer role)
    {
        FrameWorld world = WorldFor(frame);
        Vector3 linear = linearVelocity.ToVector3(), angular = angularVelocity.ToVector3();
        MotionType motion = GhostMotion(record, linear, angular);
        List<GhostBody> ghosts = record.Ghosts ??= new List<GhostBody>(2);
        for (int i = 0; i < ghosts.Count; i++)
        {
            ref GhostBody ghost = ref CollectionsMarshal.AsSpan(ghosts)[i];
            if (ghost.World != world)
                continue;
            if (ghost.Role != role)
            {
                // No frame gets two ghosts of one body, so a pass wants each ghost once, and a ghost that a pass kept
                // counts as wanted until the next pass. Only one that the pass has not wanted yet can change its role.
                if (ghost.Wanted)
                {
                    ReportBug($"Physics: the ghost of {record.Entity} in {frame} has the role {ghost.Role}, and the same pass wants it in the role {role}. " +
                        "Only a bug wants one ghost twice, so the second want was skipped.");
                    return;
                }
                RemoveGhostAt(ghosts, i);
                break;
            }
            BodyInterface bodies = world.Bodies;
            // Jolt does not simulate the move of a ghost, so the bodies around its old and new places wake.
            bool moves = !IsAt(bodies, ghost.Body, pose);
            if (moves)
                world.WakeAround(ghost.Body);
            // Jolt stops a body that becomes static, and a kinematic body moves only while it is awake. A ghost of a
            // static body changes its motion as it starts and stops moving. A change of a hull's motion made the hull's
            // ghosts again (see MakeGhostsAgain), so a ghost of a body that can move is kinematic already.
            if (bodies.GetMotionType(ghost.Body) != motion)
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
        ghosts.Add(new GhostBody { Frame = frame, World = world, Body = CreateGhostBody(record, world, pose, linear, angular, role), Wanted = true, Role = role });
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
    /// the group, in a following frame of its own, so a body that sticks out of the frame's bubble is partly there too (see
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
            {
                if (record.ReportedStep != stepIndex && record.World == world && Find(record.Entity) == record)
                {
                    Report(world, record, output, track: false);
                    // The body fell asleep in the step, and Jolt zeroed its velocity, which no update expected.
                    record.Shadow = default;
                }
            }
            // A falling numeric frame or a following frame moves, so its sleeping and static members move in its semantic frame
            // too.
            if (frames.Numeric(world.Frame) is NumericMode.Falling or NumericMode.Following)
                foreach (BodyRecord member in world.Members)
                    if (member.ReportedStep != stepIndex && !member.IsHull)
                        Report(world, member, output, track: false);
        }
    }

    /// <summary>
    /// Reports the state of the body of <paramref name="record"/> after the step. A plain kinematic body that a motion intent
    /// aimed in the step, and that no hand-off moved, reports the step record of that motion intent (see
    /// <see cref="TryAimedRecord"/>): the record's pose, exact in doubles with its rotation, where the snap put the body, and
    /// the record's instant velocity at the end of the step, not Jolt's chord. As for every body, the linear velocity is
    /// that of the centre of mass relative to the home, which a read adds to the home's velocity after the sync point (see
    /// <see cref="FrameTree.HomeVelocityAfter"/>).
    /// </summary>
    private void Report(FrameWorld world, BodyRecord record, PhysicsStepOutput output, bool track)
    {
        BodySnapshot s = ReadPrimary(record);
        if (TryAimedRecord(record, out MotionSample aimed))
        {
            Double3 center = s.CenterOfMass - s.Pose.Position;
            Double3 linear = aimed.Velocity + Double3.Cross(aimed.AngularVelocity, center)
                             - frames.HomeVelocityAfter(record.Frame, frames.SemanticOf(record.Frame), stepDt);
            output.Bodies.Add(new BodyState(record.Entity, record.Frame, new DPose(s.Pose.Position, aimed.Pose.Rotation), linear.ToVector3(),
                aimed.AngularVelocity.ToVector3()));
        }
        else
            output.Bodies.Add(new BodyState(record.Entity, record.Frame, s.Pose, s.Linear, s.Angular));
        record.ReportedStep = stepIndex;
        if (track)
            world.ReportedNow.Add(record);
    }

    /// <summary>
    /// Releases the step record of the frame of every hull that moved in the step (see <see cref="StepRecordOf"/>): every
    /// dynamic hull, from Jolt's state after the update; every kinematic hull that physics aimed, from the record of its
    /// motion intent, exactly, or for a hull at the origin of a following frame without a motion intent, at that origin and
    /// at rest, where physics aimed it at its frame's still plan (see <see cref="AimStillAnchors"/>); and every static hull,
    /// or kinematic hull without a motion intent, that teleported in the step, at the teleport's pose in its home, at rest
    /// there: a kinematic hull holds still in its home after a teleport too (see <see cref="ApplyBodyIntent"/>). Any other
    /// static hull, and a kinematic hull without a motion intent that anchors no following frame, held still, and the sync
    /// point holds its frame still in its home (see <see cref="FrameTree.TakeStepRecords"/>). A record is relative to the
    /// frame's tree parent, the hull's home, so a frame that a hand-off moved this step reports relative to its new parent,
    /// and a hull at the origin of its following frame reports at that origin.
    /// <para>
    /// The tree keeps the velocity of a frame in a numeric home relative to the home's frame velocity, the chord of the
    /// step, so that a read in the semantic parent gives the frame's instant velocity, and a hull that physics places from
    /// the tree again takes the velocity that it had (see <see cref="LinearChord(BodyRecord, FrameHandle, Double3, Double3, in TurningMoment?)"/>).
    /// A record from <see cref="StepRecordOf"/> is relative to the home's instant velocity of every hand-off (see
    /// <see cref="InstantHomeVelocity"/>; an awake dynamic hull's record converts to it, see <see cref="TryHullRecord"/>). So
    /// each record that physics releases adds the home's instant velocity past its chord (see
    /// <see cref="FrameTree.HomeInstantVelocityOverChord"/>), zero in a semantic home. A hull at the origin of its following
    /// frame rests there, so its record keeps just that term: the plan's instant velocity less the plan's chord, which is the
    /// following frame's frame velocity after the step (see <see cref="FrameTree.PlaceFollowingFrame"/>). Every other frame in
    /// that following frame adds the same term. One whose hull moves with the frame, a kinematic hull or a dynamic hull that
    /// sleeps, reads its velocity in the semantic parent relative to the anchor's. One whose hull is awake reads its velocity
    /// as Jolt moves it in the semantic frame's own system.
    /// </para>
    /// </summary>
    private void WriteFrames(PhysicsStepOutput output)
    {
        foreach (BodyRecord owner in frameOwners)
        {
            if (!owner.HasPrimary || owner.Character is not null)
                continue;
            FrameHandle frame = owner.OwnedFrame;
            // A kinematic hull that physics aimed in the step moved by its motion intent (see ApplyBodyIntent), or held still
            // at the origin of its following frame (see AimStillAnchors).
            bool aimed = owner.Motion == BodyMotion.Kinematic && owner.KinematicTargetStep == stepIndex;
            if (owner.Motion != BodyMotion.Dynamic && !aimed && owner.TeleportStep != stepIndex)
                continue;
            MotionSample record = StepRecordOf(frame);
            // Physics measures the proper acceleration of a dynamic hull (see ComputeProperAccelerations). The frame tree
            // derives every other frame's from its record (see FrameTree.ProperAcceleration).
            Vector3? measured = owner.Motion == BodyMotion.Dynamic ? owner.ProperAcceleration : null;
            output.Frames.Add(new FrameStepRecord(frame, record with { Velocity = record.Velocity + HomeInstantVelocityOverChord(owner.Frame) }, measured));
        }
    }
}
