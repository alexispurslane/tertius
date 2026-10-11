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
    private readonly List<ContactStarted> startedContacts = new();

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

    /// <summary>Reports each body pair that started touching during the step, once, with entities in canonical order.</summary>
    private void CollectContacts(PhysicsStepOutput output)
    {
        startedContacts.Clear();
        foreach (FrameWorld world in worldList)
        {
            rawContacts.Clear();
            world.DrainContacts(rawContacts);
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
        }
        startedContacts.Sort(static (x, y) =>
        {
            int c = x.A.CompareTo(y.A);
            return c != 0 ? c : x.B.CompareTo(y.B);
        });
        output.Contacts.AddRange(startedContacts);
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
            FrameHandle parent = ParentOf(frame);
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
                    // inside it: every hull starts the step in the system of its frame's parent in the tree.
                    if (child == member.OwnedFrame)
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
        CreatePrimary(record, plan.To, plan.Pose, plan.LinearVelocity, plan.AngularVelocity);
        if (record.IsHull)
            MoveFrame(record.OwnedFrame, plan.From, plan.To);
        output.HandOffs.Add(new HandOff(record.Entity, plan.From, plan.To, plan.Pose, plan.LinearVelocity, plan.AngularVelocity));
    }

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
    /// Ghosts get the real body's pose and velocity, so that during the next step they move with it.
    /// </summary>
    private void UpdateGhosts()
    {
        // A new ghost can create the system of a frame that had no bodies. That world has no members, so the
        // snapshot of the count skips nothing that needs work.
        int worldCount = worldList.Count;
        for (int w = 0; w < worldCount; w++)
        {
            FrameWorld world = worldList[w];
            if (world.Members.Count == 0)
                continue;
            FrameHandle frame = world.Frame;
            FrameHandle parent = ParentOf(frame);
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
                if (!movable && !member.IsHull)
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
                        if (sibling == frame || sibling == member.OwnedFrame)
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
                for (int i = 0; i < children.Count; i++)
                {
                    FrameHandle child = children[i];
                    if (child == member.OwnedFrame)
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

                DropUnwantedGhosts(member);
            }
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
        ghosts.Add(new GhostBody { Frame = frame, World = world, Body = id, Wanted = true });
    }

    /// <summary>
    /// True when the frame tree allows a ghost of the record's body in <paramref name="frame"/>: the frame is the parent,
    /// a sibling or a child of the frame whose system holds the body, and not the frame that the record owns. These are
    /// the only frames where <see cref="UpdateGhosts"/> puts a ghost. A record without a body has no place for a ghost.
    /// </summary>
    private bool GhostAllowed(BodyRecord record, FrameHandle frame)
    {
        FrameHandle home = record.Frame;
        if (home.IsNull || frame == home || frame == record.OwnedFrame || !frames.IsAlive(frame))
            return false;
        FrameHandle homeParent = frames.Parent(home);
        FrameHandle frameParent = frames.Parent(frame);
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
            {
                if (GhostAllowed(record, ghosts[i].Frame))
                    continue;
                ghosts[i].World.RemoveBody(ghosts[i].Body);
                ghosts.RemoveAt(i);
            }
        }
    }

    private static void DropUnwantedGhosts(BodyRecord record)
    {
        if (record.Ghosts is not { Count: > 0 } ghosts)
            return;
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            if (ghosts[i].Wanted)
                continue;
            ghosts[i].World.RemoveBody(ghosts[i].Body);
            ghosts.RemoveAt(i);
        }
    }

    // ---- Output ----

    /// <summary>
    /// Reports every awake dynamic or kinematic body and every character. A body that fell asleep during the step
    /// gets one more report, so that the simulation sees its resting pose and zero velocity.
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
                if (member.Character is not null)
                    Report(world, member, output, track: true);
            foreach (BodyRecord record in world.ReportedLast)
                if (record.ReportedStep != stepIndex && record.World == world && Find(record.Entity) == record)
                    Report(world, record, output, track: false);
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
