using System.Numerics;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    /// <summary>A hand-off decided after the step, with the body's state already converted to the target frame.</summary>
    private readonly record struct HandOffPlan(
        BodyRecord Record,
        FrameHandle From,
        FrameHandle To,
        DPose Pose,
        Vector3 LinearVelocity,
        Vector3 AngularVelocity,
        DQuaternion Turn);

    private readonly List<HandOffPlan> handOffPlans = new();
    private readonly List<FrameMotion> childMotions = new();
    private readonly List<FrameMotion> siblingMotions = new();
    private readonly List<RawContact> rawContacts = new();
    private readonly List<RawEnd> rawEnds = new();
    private readonly List<ContactStarted> startedContacts = new();
    // The contacts that ended since the last step's report, until WriteEndedContacts reports them.
    private readonly List<ContactEnded> endedContacts = new();
    // The contacts that started of the bodies that left their systems since the last ResolveLooseContacts, by entities in
    // canonical order. Each pair of entities has at most one body pair in contact, so no pair is here twice.
    private readonly List<(Entity A, Entity B)> looseContacts = new();

    /// <summary>Fills <see cref="childMotions"/> with the end-of-step motion of each child, in the same order.</summary>
    private void PrepareChildMotions(List<FrameHandle> children)
    {
        childMotions.Clear();
        foreach (FrameHandle child in children)
            childMotions.Add(MotionOf(child));
    }

    private void PrepareSiblingMotions(List<FrameHandle> siblings)
    {
        siblingMotions.Clear();
        foreach (FrameHandle sibling in siblings)
            siblingMotions.Add(MotionOf(sibling));
    }

    // ---- Contacts ----

    /// <summary>
    /// Reports each body pair that started touching during the step, once, with entities in canonical order. Before that,
    /// it settles the contacts of the bodies that left their systems since the last step (see
    /// <see cref="ResolveLooseContacts"/>), so that a contact that goes on in a new system is held there before Jolt finds
    /// it. The contacts that end go to <see cref="endedContacts"/>, which <see cref="WriteEndedContacts"/> reports.
    /// </summary>
    private void CollectContacts(PhysicsStepOutput output)
    {
        ResolveLooseContacts();
        startedContacts.Clear();
        foreach (FrameWorld world in worldList)
        {
            rawContacts.Clear();
            rawEnds.Clear();
            world.DrainContacts(rawContacts, rawEnds);
            foreach (RawContact raw in rawContacts)
            {
                if (ContactEntity(raw.UserData1, out float inverseMass1) is not { } a ||
                    ContactEntity(raw.UserData2, out float inverseMass2) is not { } b ||
                    a == b)
                    continue;
                // Relative normal speed times the reduced mass: the impulse that stops the approach.
                float inverseMassSum = inverseMass1 + inverseMass2;
                float reducedMass = inverseMassSum > 0 ? 1f / inverseMassSum : 0f;
                float impulse = MathF.Max(0f, -raw.NormalVelocity) * reducedMass;
                // Jolt's normal points from body 1 to body 2. Keep it pointing from A to B.
                Vector3 normal = raw.Normal;
                if (b.CompareTo(a) < 0)
                {
                    (a, b) = (b, a);
                    normal = -normal;
                }
                startedContacts.Add(new ContactStarted(a, b, world.Frame, raw.Point, normal, impulse));
            }
            foreach (RawEnd raw in rawEnds)
                if (EntityPair(raw) is { } pair)
                    endedContacts.Add(new ContactEnded(pair.A, pair.B));
        }
        startedContacts.Sort(static (x, y) =>
        {
            int c = x.A.CompareTo(y.A);
            return c != 0 ? c : x.B.CompareTo(y.B);
        });
        output.Contacts.AddRange(startedContacts);
    }

    /// <summary>
    /// Reports every contact that ended since the last step, in entity order: the ones that the drain ended, the ones of
    /// bodies that a change batch destroyed or moved, and the ones of bodies that a hand-off of this step moved to a
    /// system where the other entity has no body. Call it after the hand-offs.
    /// </summary>
    private void WriteEndedContacts(PhysicsStepOutput output)
    {
        ResolveLooseContacts();
        endedContacts.Sort(static (x, y) =>
        {
            int c = x.A.CompareTo(y.A);
            return c != 0 ? c : x.B.CompareTo(y.B);
        });
        output.ContactsEnded.AddRange(endedContacts);
        endedContacts.Clear();
    }

    /// <summary>
    /// Takes the contacts that started of a body that leaves its system, for <see cref="ResolveLooseContacts"/>. Call it
    /// for every primary or interior body that the backend takes out of a system, while its record still exists.
    /// </summary>
    private void LoosenContacts(FrameWorld world, BodyID body)
    {
        rawEnds.Clear();
        world.ForgetContacts(body, rawEnds);
        foreach (RawEnd raw in rawEnds)
            if (EntityPair(raw) is { } pair)
                looseContacts.Add(pair);
        rawEnds.Clear();
    }

    /// <summary>
    /// Settles each contact whose body left its system since the last call. If the primary bodies of both entities are in
    /// one system now, the contact goes on there as a held pair, which ends as a held pair does: Jolt finds it again, or
    /// the first update that simulates one of the two bodies ends it. So a contact goes on when two touching bodies move
    /// together, by a hand-off or a move between numeric frames. Otherwise the contact ends: the other entity is gone,
    /// has no body, or has none in the system of this entity's body, and contacts with ghosts are not reported.
    /// </summary>
    /// <remarks>
    /// An interior never takes over a contact of its owner's hull. The hull of a frame lies inside the frame's bubble, so
    /// a body moves into the frame before it can touch the hull, and it leaves the frame only far outside the bubble.
    /// </remarks>
    private void ResolveLooseContacts()
    {
        foreach ((Entity a, Entity b) in looseContacts)
        {
            if (Find(a) is { World: { } world } first && Find(b) is { } second && second.World == world)
                world.Hold(PrimaryId(first), PrimaryId(second));
            else
                endedContacts.Add(new ContactEnded(a, b));
        }
        looseContacts.Clear();
    }

    /// <summary>The two entities of a contact, in canonical order, or null when the contact is not reported.</summary>
    private (Entity A, Entity B)? EntityPair(in RawEnd raw)
    {
        if (ContactEntity(raw.UserData1, out _) is not { } a || ContactEntity(raw.UserData2, out _) is not { } b || a == b)
            return null;
        return b.CompareTo(a) < 0 ? (b, a) : (a, b);
    }

    /// <summary>The entity behind a body in a contact, and the inverse mass to use for the impulse estimate.</summary>
    private Entity? ContactEntity(ulong userData, out float inverseMass)
    {
        inverseMass = 0;
        if (RecordFor(userData) is not { } record)
            return null;
        switch (BodyUserData.Kind(userData))
        {
            case BodyKind.Primary when record.Motion == BodyMotion.Dynamic:
            case BodyKind.CharacterInner:
                inverseMass = record.Desc.Mass > 0 ? 1f / record.Desc.Mass : 0f;
                return record.Entity;
            case BodyKind.Primary:
            case BodyKind.Interior:
                return record.Entity;
            default:
                return null;
        }
    }

    // ---- Hand-off ----

    /// <summary>
    /// Moves bodies between frames by the rules of ARCHITECTURE.md: enter a child on the first touch of its bubble
    /// (the deepest one if two touch), leave only when fully outside plus the exit margin, and a frame enters a
    /// sibling only if it fits. A frame's hull never enters its own frame. Every decision and conversion uses the state
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
            List<FrameHandle> children = ChildrenOf(frame);
            PrepareChildMotions(children);
            bool haveFrameMotion = false;
            FrameMotion frameMotion = default;

            foreach (BodyRecord member in world.Members)
            {
                // Static bodies, interiors and the hulls of frames that physics does not drive never move between frames.
                if (member.IsHull ? member.Motion != BodyMotion.Dynamic : member.Motion == BodyMotion.Static)
                    continue;
                BodySnapshot s = ReadPrimary(member);
                Double3 center = s.Pose.Position;
                double radius = member.Radius;

                if (!parent.IsNull && bounds.FullyOutside(center, radius, bounds.ExitMargin(radius)))
                {
                    if (!haveFrameMotion)
                    {
                        frameMotion = MotionOf(frame);
                        haveFrameMotion = true;
                    }
                    DPose pose = frameMotion.PoseToParent(s.Pose);
                    handOffPlans.Add(new HandOffPlan(member, frame, parent, pose,
                        frameMotion.VelocityToParent(s.CenterOfMass, s.Linear).ToVector3(),
                        frameMotion.AngularToParent(s.Angular).ToVector3(),
                        frameMotion.Pose.Rotation));
                    continue;
                }

                int best = -1;
                double deepest = double.NegativeInfinity;
                for (int i = 0; i < children.Count; i++)
                {
                    FrameHandle child = children[i];
                    // A hull never enters its own frame. The other children are its frame's siblings, never frames
                    // inside it: every hull starts the step in the system of its frame's parent in the tree. A numeric
                    // frame has no bubble: only the engine moves bodies into it.
                    if (child == member.OwnedFrame || frames.IsNumeric(child))
                        continue;
                    FrameBounds childBounds = frames.Bounds(child);
                    if (member.IsHull && !FrameBounds.Fits(frames.Bounds(member.OwnedFrame), childBounds))
                        continue;
                    double distance = childBounds.SignedDistance(childMotions[i].Pose.InverseTransformPoint(center));
                    if (distance <= radius && radius - distance > deepest)
                    {
                        best = i;
                        deepest = radius - distance;
                    }
                }
                if (best >= 0)
                {
                    FrameMotion motion = childMotions[best];
                    handOffPlans.Add(new HandOffPlan(member, frame, children[best], motion.PoseFromParent(s.Pose),
                        motion.VelocityFromParent(s.CenterOfMass, s.Linear).ToVector3(),
                        motion.AngularFromParent(s.Angular).ToVector3(),
                        motion.Pose.Rotation.Conjugate()));
                }
            }
        }

        foreach (HandOffPlan plan in handOffPlans)
            ExecuteHandOff(plan, output);
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
            character.RefreshContacts(Layers.Moving, target.System);
            // The hand-off reports the rotation that the character got, which a turn to the new up can change.
            pose = ReadPrimary(record).Pose;
        }
        if (record.IsHull)
            MoveFrame(record.OwnedFrame, plan.From, plan.To);
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
    /// Ghosts get the real body's pose and velocity, so that during the next step they move with it. Each anchor that
    /// the numeric update names in <see cref="FrameTree.AnchorGhosts"/> has a ghost in the falling frame that it names.
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
            FrameBounds bounds = frames.Bounds(frame);
            List<FrameHandle> children = ChildrenOf(frame);
            PrepareChildMotions(children);
            List<FrameHandle> siblings = parent.IsNull ? EmptyChildren : ChildrenOf(parent);
            bool haveSiblingMotions = false;
            bool haveFrameMotion = false;
            FrameMotion frameMotion = default;

            foreach (BodyRecord member in world.Members)
            {
                bool movable = member.IsMovable;
                bool anchor = (!movable || member.IsHull) && HasAnchorGhost(anchorGhosts, member.Entity);
                if (!movable && !member.IsHull && !anchor)
                    continue;
                if (member.Ghosts is { Count: > 0 } existing)
                    foreach (ref GhostBody ghost in CollectionsMarshal.AsSpan(existing))
                        ghost.Wanted = false;

                Double3 center = PrimaryPosition(member);
                double radius = member.Radius;
                BodySnapshot s = default;
                bool haveSnapshot = false;

                // A moving body that sticks out of its frame's bubble has a ghost in the parent, and in each sibling
                // frame whose bubble it touches (it lost the tie of rule 3, or the bubbles overlap there).
                if (movable && !parent.IsNull && bounds.SignedDistance(center) > -radius)
                {
                    if (!haveFrameMotion)
                    {
                        frameMotion = MotionOf(frame);
                        haveFrameMotion = true;
                    }
                    s = ReadPrimary(member);
                    haveSnapshot = true;
                    DPose parentPose = frameMotion.PoseToParent(s.Pose);
                    Double3 parentCenter = frameMotion.Pose.TransformPoint(s.CenterOfMass);
                    Double3 parentLinear = frameMotion.VelocityToParent(s.CenterOfMass, s.Linear);
                    Double3 parentAngular = frameMotion.AngularToParent(s.Angular);
                    WantGhost(member, parent, parentPose, parentLinear, parentAngular);

                    for (int i = 0; i < siblings.Count; i++)
                    {
                        FrameHandle sibling = siblings[i];
                        if (sibling == frame || sibling == member.OwnedFrame || frames.IsNumeric(sibling))
                            continue;
                        if (!haveSiblingMotions)
                        {
                            PrepareSiblingMotions(siblings);
                            haveSiblingMotions = true;
                        }
                        FrameMotion motion = siblingMotions[i];
                        if (frames.Bounds(sibling).SignedDistance(motion.Pose.InverseTransformPoint(parentPose.Position)) > radius)
                            continue;
                        WantGhost(member, sibling, motion.PoseFromParent(parentPose),
                            motion.VelocityFromParent(parentCenter, parentLinear), motion.AngularFromParent(parentAngular));
                    }
                }

                // A body that touches a child's bubble without being in it (it lost a tie, or it is a hull that does
                // not fit) has a ghost in the child. A hull reaches as far as its own bubble.
                double reach = member.IsHull ? Math.Max(radius, frames.Bounds(member.OwnedFrame).MaxRadius) : radius;
                for (int i = 0; i < children.Count && (movable || member.IsHull); i++)
                {
                    FrameHandle child = children[i];
                    if (child == member.OwnedFrame || frames.IsNumeric(child))
                        continue;
                    FrameMotion motion = childMotions[i];
                    if (frames.Bounds(child).SignedDistance(motion.Pose.InverseTransformPoint(center)) > reach)
                        continue;
                    if (!haveSnapshot)
                    {
                        s = ReadPrimary(member);
                        haveSnapshot = true;
                    }
                    WantGhost(member, child, motion.PoseFromParent(s.Pose),
                        motion.VelocityFromParent(s.CenterOfMass, s.Linear), motion.AngularFromParent(s.Angular));
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
        world.AddGhost(id);
        ghosts.Add(new GhostBody { Frame = frame, World = world, Body = id, Wanted = true });
    }

    /// <summary>
    /// True when the frame tree allows a ghost of the record's body in <paramref name="frame"/>: the frame is the parent,
    /// a sibling or a child of the frame whose system holds the body, or a falling frame that keeps a ghost of the anchor
    /// (see <see cref="FrameTree.AnchorGhosts"/>), and not the frame that the record owns. These are
    /// the only frames where <see cref="UpdateGhosts"/> puts a ghost. A record without a body has no place for a ghost.
    /// </summary>
    private bool GhostAllowed(BodyRecord record, FrameHandle frame)
    {
        FrameHandle home = record.Frame;
        if (home.IsNull || frame == home || frame == record.OwnedFrame || !frames.IsAlive(frame))
            return false;
        foreach (NumericGhost ghost in frames.AnchorGhosts)
            if (ghost.Anchor == record.Entity && ghost.Frame == frame)
                return true;
        FrameHandle homeParent = frames.TreeParent(home);
        FrameHandle frameParent = frames.TreeParent(frame);
        return frame == homeParent || frameParent == home || (!homeParent.IsNull && frameParent == homeParent);
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

    private static void DropUnwantedGhosts(BodyRecord record)
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
