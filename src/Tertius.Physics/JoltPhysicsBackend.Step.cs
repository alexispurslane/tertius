using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Frames.Rails;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    // Bodies that a motion intent moved in the current and the previous step.
    private List<BodyRecord> kinematicMovers = new();
    private List<BodyRecord> kinematicMoversLast = new();
    // Each kinematic body that the current step aims at the pose of its motion intent, with that pose in its home: the snap
    // list (see SnapKinematicBodies).
    private readonly List<(BodyRecord Body, DPose Target)> kinematicTargets = new();
    private BodyID[] activeScratch = new BodyID[256];

    /// <summary>The record that a body's user data names, if it is still alive.</summary>
    private BodyRecord? RecordFor(ulong userData)
    {
        uint index = BodyUserData.Index(userData);
        if (index >= (uint)records.Length || records[index] is not { } record)
            return null;
        return BodyUserData.Names(userData, record.Entity) ? record : null;
    }

    /// <summary>Copies the IDs of the system's awake rigid bodies into <see cref="activeScratch"/>.</summary>
    private unsafe int CopyActiveBodies(FrameWorld world)
    {
        int count = (int)world.System.GetNumActiveBodies(BodyType.Rigid);
        if (count == 0)
            return 0;
        if (count > activeScratch.Length)
            Array.Resize(ref activeScratch, Math.Max(count, activeScratch.Length * 2));
        // The unsafe pointer is Jolt's own array. Copy it before anything can activate or deactivate a body.
        BodyID* source = world.System.GetActiveBodiesUnsafe(BodyType.Rigid);
        new ReadOnlySpan<BodyID>(source, count).CopyTo(activeScratch);
        return count;
    }

    /// <summary>Remembers the velocity of every dynamic hull, for its frame's proper acceleration.</summary>
    private void CaptureHullVelocities()
    {
        foreach (BodyRecord owner in frameOwners)
            if (owner.Motion == BodyMotion.Dynamic && owner.HasPrimary)
                owner.VelocityBefore = owner.World!.Bodies.GetLinearVelocity(owner.Body);
    }

    /// <summary>
    /// Remembers the velocities of every dynamic hull after the intents of the step, for its frame's acceleration over the
    /// step: a velocity that gameplay sets, a teleport and an impulse change the velocity at once, which the accelerometer
    /// feels as a jolt (see <see cref="ComputeProperAccelerations"/>), but the frame's motion over the step starts from them.
    /// </summary>
    private void CaptureHullVelocitiesAfterIntents()
    {
        foreach (BodyRecord owner in frameOwners)
            if (owner.Motion == BodyMotion.Dynamic && owner.HasPrimary)
                owner.World!.Bodies.GetLinearAndAngularVelocity(owner.Body, out owner.VelocityAfterIntents, out owner.AngularVelocityAfterIntents);
    }

    // ---- Intents ----

    private void ApplyIntents(in PhysicsStepContext context)
    {
        (kinematicMoversLast, kinematicMovers) = (kinematicMovers, kinematicMoversLast);
        kinematicMovers.Clear();
        kinematicTargets.Clear();

        ReadOnlySpan<PhysicsIntent> intents = context.Intents.Span;
        foreach (Entity entity in context.IntentEntities.Span)
        {
            if (entity.Index >= (uint)intents.Length || Find(entity) is not { } record)
                continue;
            ref readonly PhysicsIntent intent = ref intents[(int)entity.Index];
            // Every hull took the motion of the step at the start of the step already (see FollowHullMotions), which counts the
            // change of motion in this intent, so it is not read here. A static body and the owner of a root frame, which has
            // only an interior, can change their groups too.
            if ((intent.Flags & IntentFlags.SetCollision) != 0)
                SetCollision(record, intent.CollisionGroups, intent.CollisionMask);
            if (!record.HasPrimary)
                continue;
            // The guard of the writers, the drops of step 4 and the check of a spawn give a body only a motion intent whose
            // case its motion in the step takes, so another comes only from a bug.
            bool move = intent.Move.HasValue;
            if (move && intent.Move.ProblemFor(record.IsCharacter ? BodyMotion.Character : record.Motion, attached: false, ownsRootFrame: false) is { } problem)
            {
                ReportBug($"Physics: the step gives {entity} the motion intent {intent.Move}, but it {problem}. " +
                          "Only a bug gives it that motion intent, so the step ignores it.");
                move = false;
            }
            if (record.Character is not null)
                ApplyCharacterIntent(record, intent);
            else if (record.IsCharacter)
                ApplyRigidCharacterIntent(record, intent);
            else if (record.Motion != BodyMotion.Static)
                ApplyBodyIntent(record, intent, intent.Flags, move);
            else if (record.IsHull && (intent.Flags & IntentFlags.Teleport) != 0)
                TeleportStaticHull(record, intent.TeleportPose);
        }

        AimStillAnchors(intents);

        // A body that a motion intent moved last step stops at its end, unless it got a new motion intent or velocity.
        foreach (BodyRecord record in kinematicMoversLast)
        {
            if (record.KinematicTargetStep == stepIndex || record.VelocitySetStep == stepIndex)
                continue;
            if (Find(record.Entity) != record || !record.HasPrimary || record.Character is not null)
                continue;
            record.World!.Bodies.SetLinearAndAngularVelocity(record.Body, Vector3.Zero, Vector3.Zero);
        }
        kinematicMoversLast.Clear();
    }

    /// <summary>
    /// Puts the static hull <paramref name="hull"/> at <paramref name="pose"/>, which is in the hull's semantic frame, with a
    /// unit rotation, and remembers the pose in its home for the frame's step record (see <see cref="PredictRecord"/>): the
    /// frame takes it at the next sync point. Jolt does not simulate the jump, and a static body wakes nothing, so the bodies
    /// around the hull's old and new places wake.
    /// </summary>
    private void TeleportStaticHull(BodyRecord hull, in DPose pose)
    {
        var inHome = new DPose(InHome(hull, pose.Position), pose.Rotation.Normalized());
        FrameWorld world = hull.World!;
        world.WakeAround(hull.Body);
        world.Bodies.SetRPositionAndRotation(hull.Body, inHome.Position.ToJolt(), inHome.Rotation.ToJolt(), Activation.DontActivate);
        world.WakeAround(hull.Body);
        hull.TeleportStep = stepIndex;
        hull.TeleportPose = inHome;
    }

    /// <summary>
    /// Gives every Jolt body of a record the layer for new collision groups and a new mask: the primary body or the
    /// character's inner body, the interior of the frame that it owns, and its ghosts, each the layer of its role (see
    /// <see cref="GhostBody.Role"/>). A frame's hull keeps its layer, and so does a ghost in the frame hull role.
    /// Jolt wakes nothing when a layer changes. So the bodies around each body wake under its old layer, which wakes what
    /// rested on it, and under its new one, which wakes what it now overlaps. The primary body wakes too when it can
    /// move, so that it falls through what it no longer meets.
    /// </summary>
    private static void SetCollision(BodyRecord record, CollisionGroups groups, CollisionMask mask)
    {
        Contract.Requires(mask.AllOf.IsEmpty && mask.NoneOf.IsEmpty);
        if (groups == record.CollisionGroups && mask == record.CollisionMask)
            return;
        record.CollisionGroups = groups;
        record.CollisionMask = mask;
        if (record.World is { } world && !record.IsHull)
        {
            if (record.Character is { } character)
            {
                SetLayer(world, character.InnerBodyID, PrimaryLayer(record));
            }
            else
            {
                SetLayer(world, record.Body, PrimaryLayer(record));
                if (record.IsMovable)
                    world.Bodies.ActivateBody(record.Body);
            }
        }
        if (record.InteriorWorld is { } interior)
            SetLayer(interior, record.InteriorBody, InteriorLayer(record));
        // The layer of the frame hull role ignores the collision groups.
        if (record.Ghosts is { } ghosts)
            foreach (GhostBody ghost in ghosts)
                if (ghost.Role != BodyLayer.FrameHull)
                    SetLayer(ghost.World, ghost.Body, GhostLayer(record, ghost.Role));
    }

    private static void SetLayer(FrameWorld world, BodyID body, ObjectLayer layer)
    {
        world.WakeAround(body);
        world.Bodies.SetObjectLayer(body, layer);
        world.WakeAround(body);
    }

    /// <summary>
    /// Applies the intent of a rigid body, with the flags <paramref name="flags"/>, which are those of the intent or a part
    /// of them, and with <paramref name="move"/> its motion intent. Impulses and forces act on a dynamic body whatever the
    /// flags say.
    /// </summary>
    private void ApplyBodyIntent(BodyRecord record, in PhysicsIntent intent, IntentFlags flags, bool move)
    {
        BodyInterface bodies = record.World!.Bodies;
        BodyID id = record.Body;

        // Teleport first, so that a motion intent in the same intent moves the body on from the new pose. Jolt does not
        // simulate the jump, so the bodies around the old place wake. Jolt wakes those that the body touches at the new place.
        // Each velocity here is an instant velocity, and Jolt gets its chord. The pose in the home is the step record of a
        // hull's frame when the hull has no motion intent (see PredictRecord).
        if ((flags & IntentFlags.Teleport) != 0)
        {
            Double3 position = InHome(record, intent.TeleportPose.Position);
            record.World!.WakeAround(id);
            bodies.SetRPositionAndRotation(id, position.ToJolt(), intent.TeleportPose.Rotation.ToJolt(), Activation.Activate);
            record.TeleportStep = stepIndex;
            record.TeleportPose = new DPose(position, intent.TeleportPose.Rotation.Normalized());
            // At rest in the semantic frame, which is not at rest in a falling numeric home. A kinematic hull holds still in
            // its home, the frame's tree parent, unless the motion intent below aims it.
            Vector3 linear = record is { IsHull: true, Motion: BodyMotion.Kinematic }
                ? Vector3.Zero
                : LinearChord(record, CenterOfMass(bodies, id), LocalVelocity(record, Double3.Zero));
            bodies.SetLinearAndAngularVelocity(id, linear, Vector3.Zero);
        }
        if ((flags & IntentFlags.SetVelocity) != 0)
        {
            Double3 velocity = LocalVelocity(record, intent.Velocity);
            // A sleeping body that gets no velocity sleeps on, so the step gives it no acceleration either.
            bool sleeps = velocity == Double3.Zero && !bodies.IsActive(id);
            bodies.SetLinearVelocity(id, sleeps ? Vector3.Zero : LinearChord(record, CenterOfMass(bodies, id), velocity));
            record.VelocitySetStep = stepIndex;
        }
        if ((flags & IntentFlags.SetAngularVelocity) != 0)
        {
            bodies.SetAngularVelocity(id, AngularChord(record, intent.AngularVelocity));
            record.VelocitySetStep = stepIndex;
        }
        if (move)
        {
            // One branch for every body that a motion intent moves, after the teleport, so that the stop rule of ApplyIntents
            // never zeroes it first. Physics aims a kinematic body at the end of its end pose or full sample and puts it
            // exactly there after the update (see SnapKinematicBodies), and steers a dynamic body to its steer target. A
            // conversion between frames can leave the rotation off unit length by rounding.
            DPose aim = intent.Move.AimPose;
            DQuaternion rotation = aim.Rotation.Normalized();
            if (record.Motion == BodyMotion.Kinematic)
            {
                DPose target = KinematicTarget(record, aim.Position, rotation);
                bodies.MoveKinematic(id, target.Position.ToJolt(), target.Rotation.ToJolt(), stepDt);
                kinematicTargets.Add((record, target));
                // The step record of a plain body starts where Jolt holds the body now, after the teleport, in the body's
                // semantic frame, in which the intent is (see World.ConvertMovedIntents). The frame tree derives a hull's
                // record (see PredictRecord).
                if (!record.IsHull)
                {
                    FrameHandle semantic = frames.SemanticOf(record.Frame);
                    Contract.Assert(intent.Frame == semantic);
                    DPose start = ReadPrimary(record).Pose;
                    var standing = new DPose(start.Position + frames.HomeOffset(record.Frame, semantic), start.Rotation);
                    intent.StepRecord(standing, stepDt, out record.AimedRecord);
                    record.AimedHome = record.Frame;
                }
            }
            else
            {
                // The body reaches the aim pose at the end of the step, where a falling numeric home will be by then.
                Double3 target = aim.Position - HomeOffsetAfterStep(record.Frame);
                // A dynamic body gets the velocities that carry it to the target in one step. Contacts and gravity
                // still act on it.
                BodySnapshot s = ReadPrimary(record);
                Double3 localCenter = s.Pose.InverseTransformPoint(s.CenterOfMass);
                Double3 targetCenter = target + rotation.Rotate(localCenter);
                Vector3 linear = ((targetCenter - s.CenterOfMass) / stepDt).ToVector3();
                Vector3 angular = s.Pose.Rotation.AngularVelocityTo(rotation, stepDt).ToVector3();
                bodies.SetLinearAndAngularVelocity(id, linear, angular);
            }
            record.KinematicTargetStep = stepIndex;
            kinematicMovers.Add(record);
        }

        if (record.Motion == BodyMotion.Dynamic)
        {
            if (intent.Impulse != Vector3.Zero)
                bodies.AddImpulse(id, intent.Impulse);
            if (intent.AngularImpulse != Vector3.Zero)
                bodies.AddAngularImpulse(id, intent.AngularImpulse);
            if (intent.Force != Vector3.Zero || intent.Torque != Vector3.Zero)
                bodies.AddForceAndTorque(id, intent.Force, intent.Torque);
        }
    }

    /// <summary>
    /// Aims the anchor of each following frame that no motion intent aimed in the step, a hull or a plain body, where the
    /// frame's step plan holds it (docs/plans/numeric-frames.md, "Following frames", ticket ter-zsku). In a step without a
    /// motion intent the plan is the record of a kinematic body without one: it holds still where the step starts, with the
    /// rotation that it has, and the frame lands on that plan and holds still in its semantic frame. So physics aims the
    /// anchor as for any plan: at the frame's origin, or in the step of a teleport at the teleport's place, where the frame
    /// will be at the end of the step (see <see cref="KinematicTarget"/>), and puts it exactly there after the update (see
    /// <see cref="SnapKinematicBodies"/>). The aim gives the anchor no velocity in the frame, also when a move between
    /// systems gave it the difference of the two homes' velocities (see <see cref="ApplyMoves"/>), and the stop rule holds it
    /// still after the step. A plain anchor reports the plan, and a hull releases the record that physics predicts for its
    /// frame, at rest in its home (see <see cref="PredictRecord"/>), so that the sync point places the frame on its plan for
    /// both kinds (see <see cref="FrameTree.PlaceFollowingFrame"/>).
    /// </summary>
    private void AimStillAnchors(ReadOnlySpan<PhysicsIntent> intents)
    {
        if (frames.NumericCount == 0)
            return;
        foreach (FrameWorld world in worldList)
        {
            FrameHandle home = world.Frame;
            if (frames.Numeric(home) != NumericMode.Following)
                continue;
            if (Find(frames.AnchorOf(home)) is not { HasPrimary: true, Motion: BodyMotion.Kinematic, Character: null } record ||
                record.Frame != home || record.KinematicTargetStep == stepIndex)
                continue;
            DPose start = ReadPrimary(record).Pose;
            FrameHandle semantic = frames.SemanticOf(home);
            var standing = new DPose(start.Position + frames.HomeOffset(home, semantic), start.Rotation.Normalized());
            // After a teleport, the step and the plan start at the teleport's pose, which is in the semantic frame.
            DPose planStart = record.TeleportStep == stepIndex ? intents[(int)record.Entity.Index].TeleportPose : standing;
            DPose target = KinematicTarget(record, planStart.Position, standing.Rotation);
            world.Bodies.MoveKinematic(record.Body, target.Position.ToJolt(), target.Rotation.ToJolt(), stepDt);
            kinematicTargets.Add((record, target));
            if (!record.IsHull)
            {
                record.AimedRecord = new MotionSample(new DPose(planStart.Position, standing.Rotation), Double3.Zero, Double3.Zero, Double3.Zero,
                    Double3.Zero, Double3.Zero);
                record.AimedHome = home;
            }
            record.KinematicTargetStep = stepIndex;
            kinematicMovers.Add(record);
        }
    }

    /// <summary>
    /// Applies the intent of a character in its rigid form, a dynamic body that cannot turn. A teleport, a velocity that the
    /// game sets, impulses and forces act on it as on any dynamic body. A walk and a jump do not move it, the same as in the
    /// walking form while it does not stand: physics keeps the desired velocity of the walk, which applies once the
    /// character stands, and drops the jump. A motion intent does nothing to a character, and the rigid form cannot turn,
    /// so an angular velocity, an angular impulse and a torque change nothing either.
    /// </summary>
    private void ApplyRigidCharacterIntent(BodyRecord record, in PhysicsIntent intent)
    {
        if ((intent.Flags & IntentFlags.CharacterMove) != 0)
            record.DesiredVelocity = intent.CharacterVelocity;
        ApplyBodyIntent(record, intent, intent.Flags & (IntentFlags.Teleport | IntentFlags.SetVelocity), move: false);
    }

    private void ApplyCharacterIntent(BodyRecord record, in PhysicsIntent intent)
    {
        CharacterVirtual character = record.Character!;
        IntentFlags flags = intent.Flags;
        if ((flags & IntentFlags.Teleport) != 0)
        {
            // Jolt never simulates the inner body of a character, so the bodies around its old and new places wake.
            FrameWorld world = record.World!;
            world.WakeAround(character.InnerBodyID);
            character.RPosition = InHome(record, intent.TeleportPose.Position).ToJolt();
            character.Rotation = intent.TeleportPose.Rotation.ToJolt();
            character.LinearVelocity = LocalVelocity(record, Double3.Zero).ToVector3();
            record.InstantVelocityStep = stepIndex;
            record.PendingVelocityChange = Vector3.Zero;
            world.WakeAround(character.InnerBodyID);
        }
        if ((flags & IntentFlags.SetVelocity) != 0)
        {
            // An instant velocity, which the character's update in this step turns into the chord of the step.
            character.LinearVelocity = LocalVelocity(record, intent.Velocity).ToVector3();
            record.InstantVelocityStep = stepIndex;
        }
        if ((flags & IntentFlags.CharacterMove) != 0)
            record.DesiredVelocity = intent.CharacterVelocity;
        if ((flags & IntentFlags.Jump) != 0)
            record.PendingJump = MathF.Max(record.PendingJump, intent.JumpSpeed);
        record.PendingVelocityChange += (intent.Impulse + intent.Force * stepDt) / CharacterMass(record.Desc);
    }

    // ---- From the semantic frame to the physics home ----

    /// <summary>
    /// The local velocity for <paramref name="velocity"/>, which is relative to the body's semantic frame: the velocity
    /// minus the frame velocity of the body's physics home relative to that frame, in the step. Physics calls it at the
    /// moment that it applies a request or creates a body, so it uses the home of that moment. The home is the frame whose
    /// system holds the body, and the semantic frame is its nearest ancestor that is not numeric, or the home itself.
    /// The subtraction is in doubles, so only the small local velocity narrows to a float, when Jolt gets it. For an
    /// instant velocity Jolt gets its chord (see <see cref="LinearChord(BodyRecord, FrameHandle, Double3, Double3)"/>).
    /// </summary>
    private Double3 LocalVelocity(FrameHandle home, Double3 velocity) => velocity - frames.HomeVelocity(home, frames.SemanticOf(home));

    private Double3 LocalVelocity(BodyRecord record, Double3 velocity) => LocalVelocity(record.Frame, velocity);

    /// <summary>
    /// <paramref name="position"/>, which is in the coordinates of the body's semantic frame, in the coordinates of its
    /// physics home. See <see cref="LocalVelocity(FrameHandle, Double3)"/>.
    /// </summary>
    private Double3 InHome(FrameHandle home, Double3 position) => position - frames.HomeOffset(home, frames.SemanticOf(home));

    /// <summary>
    /// The offset that <see cref="InHome(FrameHandle, Double3)"/> subtracts, where the numeric frames of the home will be
    /// at the end of the current step.
    /// </summary>
    private Double3 HomeOffsetAfterStep(FrameHandle home) => frames.HomeOffsetAfter(home, frames.SemanticOf(home), stepDt);

    private Double3 InHome(BodyRecord record, Double3 position) => InHome(record.Frame, position);

    /// <summary>
    /// The pose in its home at which the kinematic body <paramref name="body"/> ends the step with a motion intent that ends
    /// at <paramref name="position"/>, in the body's semantic frame, with <paramref name="rotation"/>, which is unit length.
    /// The anchor of a following frame, a hull or a plain body (see <see cref="FrameTree.Anchors"/>), stays at the frame's
    /// origin, because the frame lands on the anchor's step plan, which ends at the same pose, and takes the plan's place at
    /// the sync point (see <see cref="FrameTree.PlaceFollowingFrame"/>). In the step of a teleport the anchor is not at that
    /// origin, so it goes to the plan's place, where the frame will be at the end of the step: it does not sweep back to the
    /// origin, and the frame jumps to it at the sync point (see <see cref="FrameTree.JumpFollowingFrame"/>). Any other body
    /// goes to that place too, where its home will be at the end of the step.
    /// </summary>
    private DPose KinematicTarget(BodyRecord body, Double3 position, DQuaternion rotation)
    {
        if (frames.Anchors(body.Frame, body.Entity) && body.TeleportStep != stepIndex)
            return new DPose(Double3.Zero, rotation);
        return new DPose(position - HomeOffsetAfterStep(body.Frame), rotation);
    }

    /// <summary>
    /// Puts each kinematic body that a motion intent aimed exactly on its target, right after the Jolt update and before
    /// anything reads the end of the step. Jolt reaches a kinematic target only to the float rounding of the velocity that it
    /// gives the body, about 4 µm per step at 7.6 km/s, and it does not move a body in a system that it skipped. The move is
    /// that rounding, so the body's activation stays as it is and nothing around it wakes.
    /// </summary>
    private void SnapKinematicBodies()
    {
        foreach ((BodyRecord body, DPose target) in kinematicTargets)
            body.World!.Bodies.SetRPositionAndRotation(body.Body, target.Position.ToJolt(), target.Rotation.ToJolt(), Activation.DontActivate);
    }

    /// <summary>
    /// Moves each static hull to its frame's pose in the tree when the game placed the frame somewhere else between two
    /// steps, for example with <see cref="FrameTree.SetLocalPose"/>, which the reconcile does not see.
    /// </summary>
    private void FollowStaticHulls()
    {
        foreach (BodyRecord owner in frameOwners)
        {
            if (!owner.HasPrimary || owner.Motion != BodyMotion.Static)
                continue;
            BodyInterface bodies = owner.World!.Bodies;
            // A static frame does not move, unless the game placed it somewhere else. Jolt does not simulate that move, so
            // the bodies around the hull's old and new places wake.
            DPose pose = frames.TreeLocalPose(owner.OwnedFrame);
            if (IsAt(bodies, owner.Body, pose))
                continue;
            owner.World!.WakeAround(owner.Body);
            bodies.SetRPositionAndRotation(owner.Body, pose.Position.ToJolt(), pose.Rotation.ToJolt(), Activation.DontActivate);
            owner.World!.WakeAround(owner.Body);
        }
    }

    // ---- Gravity ----

    /// <summary>
    /// Applies m · g_eff to every awake dynamic body, and gives each system the sleep threshold of the weakest effective
    /// gravity on its awake dynamic bodies (see <see cref="SleepThresholdFor"/>). Wakes a frame's bodies when its gravity
    /// field changes, or when its push (see <see cref="FrameTree.PushInStep"/>) has moved more than
    /// <see cref="JoltBackendOptions.WakeAccelerationThreshold"/> from its value when they last woke: the proper acceleration
    /// of a semantic frame, and for a numeric frame its acceleration less the field at its origin, so that a sleeping body,
    /// which moves with its frame, does not ride along when an anchor speeds up its following frame. A sleeping body gets no
    /// force, and the force does not reset the sleep timer of an awake body, so a body at rest falls asleep.
    /// </summary>
    private void ApplyGravity()
    {
        float wakeThreshold = options.WakeAccelerationThreshold;
        foreach (FrameWorld world in worldList)
        {
            FrameHandle frame = world.Frame;
            Vector3 push = frames.PushInStep(frame);
            GravityField field = frames.Gravity(frame);
            bool wake = world.HasGravityState &&
                (field != world.LastGravity || Vector3.DistanceSquared(push, world.WakePush) > wakeThreshold * wakeThreshold);
            if (wake)
                WakeDynamicMembers(world);
            if (wake || !world.HasGravityState)
                world.WakePush = push;
            world.HasGravityState = true;
            world.LastGravity = field;
            world.HomeAcceleration = frames.IsNumeric(frame) ? frames.HomeAcceleration(frame, frames.SemanticOf(frame)).ToVector3() : Vector3.Zero;

            // BodyInterface.AddForce resets the sleep timer of the body, so a body under gravity would never sleep. The
            // force goes on the locked body instead, which changes nothing else.
            BodyLockInterface locks = world.System.BodyLockInterface;
            int count = CopyActiveBodies(world);
            float weakest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                BodyID id = activeScratch[i];
                locks.LockWrite(id, out BodyLockWrite write);
                if (write.Succeeded && write.Body is { } body)
                    weakest = MathF.Min(weakest, ApplyGravity(frame, id, body));
                locks.UnlockWrite(write);
            }
            if (weakest < float.PositiveInfinity)
                world.SetSleepThreshold(SleepThresholdFor(weakest));
        }

        // A sleeping hull still rests against gravity, and its frame's accelerometer reads that.
        foreach (BodyRecord owner in frameOwners)
        {
            if (owner.Motion != BodyMotion.Dynamic || !owner.HasPrimary)
                continue;
            BodyInterface bodies = owner.World!.Bodies;
            if (bodies.IsActive(owner.Body))
                continue;
            bodies.GetRCenterOfMassPosition(owner.Body, out RVector3 center);
            owner.GravityApplied = frames.EffectiveGravity(owner.Frame, center.ToDouble3());
        }
    }

    /// <summary>
    /// Adds m · g_eff to the locked body <paramref name="id"/> in the system of <paramref name="frame"/>, if it is the
    /// primary body of a dynamic record. Returns the strength of g_eff, in m/s², or positive infinity for any other body.
    /// </summary>
    private float ApplyGravity(FrameHandle frame, BodyID id, Body body)
    {
        ulong data = body.GetUserData();
        if (BodyUserData.Kind(data) != BodyKind.Primary || RecordFor(data) is not { Motion: BodyMotion.Dynamic } record || record.Body != id)
            return float.PositiveInfinity;
        body.GetRCenterOfMassPosition(out RVector3 center);
        Vector3 gravity = frames.EffectiveGravity(frame, center.ToDouble3());
        record.GravityApplied = gravity;
        if (gravity != Vector3.Zero)
            body.AddForce(gravity * record.Material.Mass);
        return gravity.Length();
    }

    /// <summary>Standard gravity, in m/s².</summary>
    private const float StandardGravity = 9.80665f;

    /// <summary>The gravity below which the sleep threshold stops falling, in m/s².</summary>
    private const float SleepGravityFloor = 0.001f;

    /// <summary>
    /// The sleep threshold of a system whose weakest effective gravity on an awake dynamic body is <paramref name="gravity"/>,
    /// in m/s: Jolt's own threshold of 0.03 m/s at standard gravity and above, and a share of it in proportion to the
    /// gravity below standard gravity, down to the share of <see cref="SleepGravityFloor"/>, 3.1 µm/s.
    /// </summary>
    /// <remarks>
    /// Jolt puts a body to sleep when each of its three test points stays within a sphere of radius threshold · 0.5 s for
    /// 0.5 s. A body that a net acceleration a moves from rest covers a · (0.5 s)² / 2 in that time, and it must cover the
    /// diameter of the sphere to stay awake: a must be more than 8 per second times the threshold. With Jolt's own
    /// threshold, that is 0.24 m/s², more than all of a weak gravity. With a threshold in proportion to the gravity, it is
    /// the same share of the gravity in every gravity up to standard gravity: 2.4 %. The weakest gravity sets the threshold
    /// of the whole system, because one system has one threshold.
    /// </remarks>
    private static float SleepThresholdFor(float gravity) =>
        FrameWorld.JoltSleepThreshold * MathF.Min(1f, MathF.Max(gravity, SleepGravityFloor) / StandardGravity);

    private static void WakeDynamicMembers(FrameWorld world)
    {
        BodyInterface bodies = world.Bodies;
        foreach (BodyRecord member in world.Members)
            if (member.Motion == BodyMotion.Dynamic && member.Body.IsValid)
                bodies.ActivateBody(member.Body);
    }

    // ---- Characters ----

    /// <summary>Moves every character in the walking form. Jolt moves a character in the rigid form in the update of its system.</summary>
    private void UpdateCharacters()
    {
        foreach (FrameWorld world in worldList)
        {
            world.ClearCharacterContacts();
            if (world.WalkingCharacterCount == 0)
                continue;
            foreach (BodyRecord member in world.Members)
            {
                if (member.Character is not { } character)
                    continue;
                Vector3 velocity = UpdateCharacter(world, member, character);
                world.NoteCharacterContacts(character, velocity);
            }
        }
    }

    /// <summary>
    /// Moves one character in the walking form, and records in <see cref="BodyRecord.Standing"/> whether it stood. A
    /// character in the rigid form is a dynamic body, which Jolt moves in the update of its system (see
    /// <see cref="NextCharacterMotion"/>), so a walking character nearly always has the gravity to stand. The character
    /// stands when the effective gravity is at least <see cref="FrameTree.ZeroGravityThreshold"/>, Jolt found walkable
    /// ground under it in its last update, and it does not move away from that ground. Then it takes the ground's velocity
    /// plus the part of its desired velocity across its up, and can jump, and Jolt's ExtendedUpdate moves it, which sticks
    /// it to the floor and climbs stairs. A character that does not stand, in the air or on steep ground, keeps its
    /// momentum, and its desired velocity and its jump do not move it. (User) The record keeps the desired velocity, which
    /// applies once the character stands. Jolt's plain Update moves it: ExtendedUpdate would first cancel its velocity
    /// into each steep surface that it touched (see the Jolt pitfalls in project memory), which held it back from a body
    /// that gives way, and its stick to the floor pulled it down when it lost a touch. Gravity changes the velocity of a
    /// character that does not stand, and of one that jumps, because the ground holds a standing character against it.
    /// Forces and impulses change the velocity of every character. After the update, a character that did not stand loses
    /// its motion into what it hit, as <see cref="WithoutMotionIntoContacts"/> says. Under gravity, up turns against the
    /// gravity whenever the character touched anything in its last update, also with its side or its head, so a character
    /// that landed tilted turns upright and can stand. A character in free fall that touches nothing keeps its last up.
    /// Returns the velocity that the character moved with in the update.
    /// </summary>
    private Vector3 UpdateCharacter(FrameWorld world, BodyRecord record, CharacterVirtual character)
    {
        float dt = stepDt;
        Double3 position = character.RPosition.ToDouble3();
        Vector3 gravity = frames.EffectiveGravity(world.Frame, position);
        float strength = gravity.Length();
        bool underGravity = strength >= FrameTree.ZeroGravityThreshold;
        // The ground state comes from the last update. Any touch counts, also on steep ground and on the head or the
        // side, so a character that landed tilted turns upright.
        GroundState groundState = character.GroundState;
        bool touching = groundState != GroundState.InAir;
        Vector3 up = record.Up;
        // Up is against the semantic frame's gravity, which in a falling numeric frame is far stronger than the effective
        // gravity. In a semantic frame the two are the same.
        Vector3 semanticGravity = frames.SemanticGravity(world.Frame, position);
        float semanticStrength = semanticGravity.Length();
        if (semanticStrength >= FrameTree.ZeroGravityThreshold && touching)
        {
            up = -semanticGravity / semanticStrength;
            if (Vector3.DistanceSquared(up, record.Up) > 1e-12f)
            {
                character.Up = up;
                record.Up = up;
            }
            Quaternion rotation = character.Rotation;
            Quaternion aligned = Rotations.AlignUp(rotation, up);
            if (aligned != rotation)
                character.Rotation = aligned;
        }

        // Platforms may have changed velocity since the last update.
        character.UpdateGroundVelocity();
        Vector3 velocity = character.LinearVelocity;
        Vector3 ground = character.GroundVelocity;
        bool standing = underGravity && groundState == GroundState.OnGround && Vector3.Dot(velocity - ground, up) < 0.1f;
        Vector3 next;
        if (standing)
        {
            Vector3 desired = record.DesiredVelocity;
            next = ground + desired - Vector3.Dot(desired, up) * up;
            if (record.PendingJump > 0)
                next += record.PendingJump * up;
        }
        else
        {
            // Nothing holds the character, so it keeps its momentum, including what it took from a moving platform.
            next = velocity;
        }
        // The ground holds a standing character against gravity, until the step in which it jumps off. Gravity in the
        // velocity of a standing character would press it into its ground in every step. Jolt then reported a false
        // velocity of gravity times the step, and pushed every dynamic body that the character leaned on a little in
        // every step, so a stack that the character stood against woke each time that it fell asleep.
        bool held = standing && record.PendingJump == 0;
        if (!held)
        {
            // A jump, and a velocity that the game gave since the last update, are instant velocities. The
            // character moves by the chord of the step, which is half a step of its acceleration less (see LinearChord).
            // A jump is relative to the ground, so to the home's instant velocity: its acceleration is the effective
            // gravity. A velocity that the game gave is relative to the home's frame velocity in the step, as
            // LocalVelocity gives it, so it also loses the home's instant velocity past its frame velocity where the step
            // starts, as LinearChord takes it from a rigid body's.
            if (standing)
                next -= gravity * (dt * 0.5f);
            else if (record.InstantVelocityStep == stepIndex)
                next -= gravity * (dt * 0.5f) + HomeInstantVelocityOverChordAtStart(world.Frame).ToVector3();
            next += gravity * dt;
        }
        record.GravityApplied = held ? Vector3.Zero : gravity;
        next += record.PendingVelocityChange;
        record.PendingVelocityChange = Vector3.Zero;
        record.PendingJump = 0;
        record.Standing = standing;
        character.LinearVelocity = next;

        // Jolt reads the system gravity to press the character's weight onto what it stands on.
        world.System.Gravity = gravity;
        if (standing)
        {
            // A standing character sticks to the floor and climbs stairs.
            character.ExtendedUpdate(dt, new ExtendedUpdateSettings { StickToFloorStepDown = -0.5f * up, WalkStairsStepUp = 0.4f * up },
                PrimaryLayer(record), world.System);
        }
        else
        {
            // ExtendedUpdate starts by cancelling the velocity into each steep surface that the character touched in its
            // last update, which held it back from a body that gives way, and its stick to the floor pulled a character in
            // the air that lost a touch down by up to half a meter. A character that does not stand gets neither.
            character.Update(dt, PrimaryLayer(record), world.System);
        }
        world.System.Gravity = Vector3.Zero;
        if (!standing)
        {
            // Jolt slides the character along what it hits, but keeps the velocity that it was given. Only standing
            // replaces the velocity, so a character that does not stand loses its motion into a surface here.
            character.LinearVelocity = WithoutMotionIntoContacts(character, next);
        }
        return next;
    }

    /// <summary>
    /// The largest angle, in radians, between Jolt's contact normal and the face's own normal at which
    /// <see cref="WithoutMotionIntoContacts"/> takes the face's normal: 10 degrees. On a flat face Jolt's contact normal
    /// can lean several degrees from the face. On the sill of the airlock hatch it leaned 6 degrees, and it turned the
    /// press onto the sill into a drift upward. At an edge or a corner the two normals differ by more, and the face's
    /// normal is the normal of only one face, so it would cancel motion that nothing blocks.
    /// </summary>
    private const float FaceNormalLeanLimit = 10 * MathF.PI / 180;

    /// <summary>
    /// <paramref name="velocity"/> less its part into each static or kinematic surface that <paramref name="character"/>
    /// hit in its last update, relative to the velocity of that surface. So a kinematic wall that moves into the
    /// character gives it the wall's velocity along the normal. A dynamic body is skipped: the character keeps its
    /// momentum against it and pushes it, with Jolt's capped push, which takes no reaction from the character (ticket
    /// ter-w7cw). Only a walking character in the air under gravity does that now, because a weightless character is
    /// rigid. Jolt pushes a character out of a surface that it sank into by moving it, so the push adds no velocity here.
    /// </summary>
    private static Vector3 WithoutMotionIntoContacts(CharacterVirtual character, Vector3 velocity)
    {
        float faceNormalCosine = MathF.Cos(FaceNormalLeanLimit);
        int count = character.GetNumActiveContacts();
        for (int i = 0; i < count; i++)
        {
            character.GetActiveContact(i, out CharacterContact contact);
            if (!contact.HadCollision || contact.WasDiscarded || contact.MotionTypeB == MotionType.Dynamic)
                continue;
            // Both normals point out of the surface, toward the character, so motion into the surface runs against them.
            Vector3 normal = Vector3.Dot(contact.ContactNormal, contact.SurfaceNormal) >= faceNormalCosine
                ? contact.SurfaceNormal
                : contact.ContactNormal;
            float into = Vector3.Dot(velocity - contact.LinearVelocity, normal);
            if (into < 0)
                velocity -= into * normal;
        }
        return velocity;
    }

    // ---- Systems ----

    private void UpdateSystems()
    {
        foreach (FrameWorld world in worldList)
        {
            if (world.WalkingCharacterCount == 0 && world.System.GetNumActiveBodies(BodyType.Rigid) == 0)
                continue;
            PhysicsUpdateError error = world.Update(stepDt, jobs);
            if (error != PhysicsUpdateError.None && !world.ReportedOverflow)
            {
                world.ReportedOverflow = true;
                log($"Physics: the system of {world.Frame} reported {error}. Some contacts were dropped. Raise the per-frame limits in JoltBackendOptions.");
            }
            // Only a system that updated ends a held pair, so the rigid characters of the others need no contact list.
            NoteRigidCharacterContacts(world);
        }
    }

    /// <summary>
    /// The proper acceleration of each frame with a dynamic hull: its hull's velocity change over the step minus the gravity
    /// that the parent applied, turned into the frame's own axes. Also the hull's acceleration and angular acceleration in
    /// the update, in the axes of its home, which leave out what the intents set before it (see
    /// <see cref="CaptureHullVelocitiesAfterIntents"/>).
    /// </summary>
    /// <remarks>
    /// Jolt keeps velocities in float. At orbital speed one float step of velocity, divided by the time step, is
    /// about 0.03 m/s², so a hull in free fall would read that much noise and its loose contents would drift. Jolt also
    /// turns the gravity into a velocity change in float, through the force, the inverse mass and the step. Each
    /// component smaller than the float resolution of the velocity along that axis, or of the gravity along it, reads
    /// as zero.
    /// </remarks>
    private void ComputeProperAccelerations()
    {
        foreach (BodyRecord owner in frameOwners)
        {
            if (owner.Motion != BodyMotion.Dynamic || !owner.HasPrimary)
                continue;
            BodyInterface bodies = owner.World!.Bodies;
            Vector3 before = owner.VelocityBefore;
            Vector3 after = bodies.GetLinearVelocity(owner.Body);
            Vector3 gravity = owner.GravityApplied;
            Vector3 acceleration = (after - before) / stepDt - gravity;
            acceleration = new Vector3(
                AboveResolution(acceleration.X, before.X, after.X, gravity.X, stepDt),
                AboveResolution(acceleration.Y, before.Y, after.Y, gravity.Y, stepDt),
                AboveResolution(acceleration.Z, before.Z, after.Z, gravity.Z, stepDt));
            owner.ProperAcceleration = Vector3.Transform(acceleration, Quaternion.Conjugate(bodies.GetRotation(owner.Body)));
            // The same change of velocity without its float noise, less the part that the intents made before the update.
            owner.StepAcceleration = (Double3)gravity + (Double3)acceleration - ((Double3)owner.VelocityAfterIntents - before) / stepDt;
            owner.AngularAcceleration = (bodies.GetAngularVelocity(owner.Body) - owner.AngularVelocityAfterIntents) / stepDt;
        }
    }

    /// <summary>
    /// Zero when <paramref name="acceleration"/> is within the rounding of a float velocity change, or within two float
    /// steps of the <paramref name="gravity"/> along the same axis, which Jolt rounds on the way to the velocity change.
    /// </summary>
    private static float AboveResolution(float acceleration, float before, float after, float gravity, float dt)
    {
        float magnitude = MathF.Max(MathF.Abs(before), MathF.Abs(after));
        float resolution = (MathF.BitIncrement(magnitude) - magnitude) / dt;
        float strength = MathF.Abs(gravity);
        float gravityResolution = 2 * (MathF.BitIncrement(strength) - strength);
        return MathF.Abs(acceleration) <= MathF.Max(resolution, gravityResolution) ? 0f : acceleration;
    }
}
