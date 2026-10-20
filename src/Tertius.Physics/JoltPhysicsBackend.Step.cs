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
        // hull's frame when the hull has no motion intent (see PredictRecord). A teleport, a velocity that the game sets, a
        // motion intent and an impulse each end the body's velocity shadow: no update expected them.
        if ((flags & IntentFlags.Teleport) != 0)
        {
            Double3 position = InHome(record, intent.TeleportPose.Position);
            record.World!.WakeAround(id);
            bodies.SetRPositionAndRotation(id, position.ToJolt(), intent.TeleportPose.Rotation.ToJolt(), Activation.Activate);
            record.TeleportStep = stepIndex;
            record.TeleportPose = new DPose(position, intent.TeleportPose.Rotation.Normalized());
            record.Shadow = default;
            // At rest in the semantic frame, which is not at rest in a falling numeric home, and which a turning frame's chord
            // rule turns into the chords of the inertial motion of a point at rest in its axes. A kinematic hull holds still in
            // its home, the frame's tree parent, unless the motion intent below aims it.
            if (record is { IsHull: true, Motion: BodyMotion.Kinematic })
                bodies.SetLinearAndAngularVelocity(id, Vector3.Zero, Vector3.Zero);
            else
                bodies.SetLinearAndAngularVelocity(id, LinearChordAtStart(record, CenterOfMass(bodies, id), LocalVelocity(record, Double3.Zero)),
                    AngularChord(record, Vector3.Zero, StepStart(record.Frame)));
        }
        if ((flags & IntentFlags.SetVelocity) != 0)
        {
            Double3 velocity = LocalVelocity(record, intent.Velocity);
            // A sleeping body that gets no velocity sleeps on, so the step gives it no acceleration either.
            bool sleeps = velocity == Double3.Zero && !bodies.IsActive(id);
            bodies.SetLinearVelocity(id, sleeps ? Vector3.Zero : LinearChordAtStart(record, CenterOfMass(bodies, id), velocity));
            record.VelocitySetStep = stepIndex;
            record.Shadow = default;
        }
        if ((flags & IntentFlags.SetAngularVelocity) != 0)
        {
            bodies.SetAngularVelocity(id, AngularChord(record, intent.AngularVelocity, StepStart(record.Frame)));
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
                    record.AimedRecord = intent.StepRecordOrStill(standing, stepDt);
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
            record.Shadow = default;
            kinematicMovers.Add(record);
        }

        if (record.Motion == BodyMotion.Dynamic)
        {
            if (intent.Impulse != Vector3.Zero || intent.AngularImpulse != Vector3.Zero)
            {
                // The update of a turning frame needs the change that the impulses made (see ApplyTurningUpdate).
                bodies.GetLinearAndAngularVelocity(id, out Vector3 linearBefore, out Vector3 angularBefore);
                if (intent.Impulse != Vector3.Zero)
                    bodies.AddImpulse(id, intent.Impulse);
                if (intent.AngularImpulse != Vector3.Zero)
                    bodies.AddAngularImpulse(id, intent.AngularImpulse);
                bodies.GetLinearAndAngularVelocity(id, out Vector3 linearAfter, out Vector3 angularAfter);
                record.ImpulseStep = stepIndex;
                record.ImpulseVelocityChange = (Double3)linearAfter - linearBefore;
                record.ImpulseAngularVelocityChange = (Double3)angularAfter - angularBefore;
                record.Shadow = default;
            }
            if (intent.Force != Vector3.Zero || intent.Torque != Vector3.Zero)
                bodies.AddForceAndTorque(id, intent.Force, intent.Torque);
        }
    }

    /// <summary>
    /// Aims the anchor of each following frame that no motion intent aimed in the step, a hull or a plain body, where the
    /// frame's step plan holds it (docs/plans/numeric-frames.md, "Following frames", ticket ter-zsku). In a step without a
    /// motion intent the plan is the record of a kinematic body without one, which the frame tree and physics both take from
    /// <see cref="PhysicsIntent.StepRecordOrStill"/>: it holds still where the step starts, and the frame lands on that plan
    /// and holds still in its semantic frame. So physics aims the anchor as for any plan: at the frame's origin, or in the
    /// step of a teleport at the teleport's place, where the frame will be at the end of the step (see
    /// <see cref="KinematicTarget"/>), and puts it exactly there after the update (see <see cref="SnapKinematicBodies"/>). The aim gives the anchor no velocity in the frame, also when a move between
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
            // The record of a kinematic body without a motion intent, by the rule of the step plan, in the semantic frame: it
            // holds still where the step starts, at the teleport's pose after a teleport. A conversion between frames can leave
            // a teleport's rotation off unit length by rounding.
            uint slot = record.Entity.Index;
            MotionSample still = slot < (uint)intents.Length
                ? intents[(int)slot].StepRecordOrStill(standing, stepDt)
                : default(PhysicsIntent).StepRecordOrStill(standing, stepDt);
            still = still with { Pose = new DPose(still.Pose.Position, still.Pose.Rotation.Normalized()) };
            DPose target = KinematicTarget(record, still.Pose.Position, still.Pose.Rotation);
            world.Bodies.MoveKinematic(record.Body, target.Position.ToJolt(), target.Rotation.ToJolt(), stepDt);
            kinematicTargets.Add((record, target));
            if (!record.IsHull)
            {
                record.AimedRecord = still;
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
    /// instant velocity Jolt gets its chord (see <see cref="LinearChord(BodyRecord, FrameHandle, Double3, Double3, in TurningMoment?)"/>).
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
    /// Applies m · g to every awake dynamic body, with g the linear gravity at the body (see
    /// <see cref="FrameTree.LinearGravity"/>), and in a turning frame also the two-turn update, which keeps the body on its
    /// inertial line (see <see cref="ApplyTurningUpdate"/>). Gives each system the sleep threshold of the weakest effective
    /// gravity on its awake dynamic bodies, with the centrifugal and the Euler term of a turning frame (see
    /// <see cref="FrameTree.EffectiveGravity"/> and <see cref="SleepThresholdFor"/>). Wakes a frame's bodies when its gravity
    /// field changes, or when the acceleration that they feel relative to the frame may have changed by more than
    /// <see cref="JoltBackendOptions.WakeAccelerationThreshold"/> since they last woke, by its push or by its turn (see
    /// <see cref="FeltChangeWakes"/>). A sleeping body gets no force, and the force does not reset the sleep timer of an
    /// awake body, so a body at rest falls asleep.
    /// </summary>
    private void ApplyGravity()
    {
        float wakeThreshold = options.WakeAccelerationThreshold;
        foreach (FrameWorld world in worldList)
        {
            FrameHandle frame = world.Frame;
            Vector3 push = frames.PushInStep(frame);
            GravityField field = frames.Gravity(frame);
            // A numeric frame reads its semantic frame's turns, and the update acts on the semantic chord of each body: Jolt's
            // velocity plus the frame velocity of its home, the same for every body of the system.
            FrameTurns turns = frames.Turns(frame);
            Double3 angularVelocity = turns.Step.AngularVelocity;
            Double3 angularAcceleration = turns.AngularAcceleration(stepDt);
            // The reach costs a read of every dynamic member, so it is measured only when the turn changed and the push's
            // change alone does not wake the system.
            double turn = TurnChangeSinceWake(world, angularVelocity, angularAcceleration);
            bool wake = world.HasGravityState && (field != world.LastGravity || FeltChangeWakes(push, world.WakePush, 0, 0, wakeThreshold)
                || turn > 0 && FeltChangeWakes(push, world.WakePush, turn, Reach(world), wakeThreshold));
            if (wake)
                WakeDynamicMembers(world);
            if (wake || !world.HasGravityState)
            {
                world.WakePush = push;
                world.WakeAngularVelocity = angularVelocity;
                world.WakeAngularAcceleration = angularAcceleration;
            }
            world.HasGravityState = true;
            world.LastGravity = field;
            world.HomeAcceleration = frames.IsNumeric(frame) ? frames.HomeAcceleration(frame, frames.SemanticOf(frame)).ToVector3() : Vector3.Zero;

            // BodyInterface.AddForce resets the sleep timer of the body, so a body under gravity would never sleep. The
            // force goes on the locked body instead, which changes nothing else.
            BodyLockInterface locks = world.System.BodyLockInterface;
            int count = CopyActiveBodies(world);
            float weakest = float.PositiveInfinity;
            Double3 homeVelocity = turns.Turns ? frames.HomeVelocity(frame, frames.SemanticOf(frame)) : Double3.Zero;
            Double3 homeAcceleration = turns.Turns ? frames.HomeAcceleration(frame, frames.SemanticOf(frame)) : Double3.Zero;
            for (int i = 0; i < count; i++)
            {
                BodyID id = activeScratch[i];
                locks.LockWrite(id, out BodyLockWrite write);
                if (write.Succeeded && write.Body is { } body)
                    weakest = MathF.Min(weakest, ApplyGravity(frame, turns, homeVelocity, homeAcceleration, id, body));
                locks.UnlockWrite(write);
            }
            if (weakest < float.PositiveInfinity)
                world.SetSleepThreshold(SleepThresholdFor(weakest));
        }

        // A sleeping hull still rests against gravity, and its frame's accelerometer reads that: the effective gravity, with
        // the centrifugal and the Euler term of a turning frame, against which it rests. The field is the linear part, and the
        // two terms are the turning part (V4 of docs/plans/spin-gravity.md).
        foreach (BodyRecord owner in frameOwners)
        {
            if (owner.Motion != BodyMotion.Dynamic || !owner.HasPrimary)
                continue;
            BodyInterface bodies = owner.World!.Bodies;
            if (bodies.IsActive(owner.Body))
                continue;
            bodies.GetRCenterOfMassPosition(owner.Body, out RVector3 center);
            Double3 place = center.ToDouble3();
            Double3 linear = frames.LinearGravity(owner.Frame, place);
            owner.FrameAcceleration = frames.Turns(owner.Frame).Turns
                ? new AppliedFrameAcceleration(linear.ToVector3(), (frames.AddRotationTerms(owner.Frame, place, linear) - linear).ToVector3())
                : new AppliedFrameAcceleration(linear.ToVector3(), Vector3.Zero);
        }
    }

    /// <summary>
    /// The wake test (docs/plans/spin-gravity.md, "The wake"): true when a bound, in m/s², on how much the acceleration that
    /// a dynamic body of a system feels relative to the system's frame, at any place of those bodies, has changed since they
    /// last woke passes <paramref name="threshold"/>. A sleeping body has no local velocity, so it moves with its frame,
    /// which is right only while what it would feel there stays as it was when it fell asleep. One cause changes that
    /// acceleration, a change of how the frame moves against a free body, and it has two parts, which the bound adds:
    /// <list type="bullet">
    /// <item>The push, <paramref name="push"/> (see <see cref="FrameTree.PushInStep"/>), what an accelerometer at the frame's
    /// origin reads apart from the field: the proper acceleration of a semantic frame, and for a numeric frame its
    /// acceleration less that of a free body at its origin, as when an anchor speeds up its following frame. Its part is
    /// |p − p_wake|, with p_wake <paramref name="wakePush"/>.</item>
    /// <item>The turn of the semantic frame, which changes the centrifugal and the Euler acceleration from place to place.
    /// Its part is <paramref name="turn"/> (see <see cref="TurnChangeSinceWake"/>) times <paramref name="reach"/>, the reach
    /// of the system (see <see cref="Reach"/>).</item>
    /// </list>
    /// The change at a place is the change of the rotation terms there less the change of the push, so the sum bounds it.
    /// The push's part alone is tested first, in floats, as the float square of the change against the float square of
    /// the threshold, so that a frame that does not turn wakes exactly as it did before the rotation terms. The sum is
    /// tested only when the push's part alone does not wake the system and the turn changed, so its caller measures the
    /// reach only then.
    /// </summary>
    internal static bool FeltChangeWakes(Vector3 push, Vector3 wakePush, double turn, double reach, float threshold) =>
        Vector3.DistanceSquared(push, wakePush) > threshold * threshold || turn > 0 && Vector3.Distance(push, wakePush) + turn * reach > threshold;

    /// <summary>
    /// The change of the turn of the semantic frame of <paramref name="world"/>'s system since its last wake, in 1/s²: with ω
    /// and α the semantic frame's inertial angular velocity and angular acceleration for the step in flight,
    /// <paramref name="angularVelocity"/> and <paramref name="angularAcceleration"/>, and their values at the last wake,
    /// (|ω| + |ω_wake|) · |ω − ω_wake| + |α − α_wake|. Times the reach of the system, it bounds the change of the centrifugal
    /// and the Euler acceleration at any place of its bodies. A frame that does not turn, and one that turns steadily on its
    /// rails, changes neither, so its change is zero.
    /// </summary>
    private static double TurnChangeSinceWake(FrameWorld world, Double3 angularVelocity, Double3 angularAcceleration) =>
        (angularVelocity.Length + world.WakeAngularVelocity.Length) * Double3.Distance(angularVelocity, world.WakeAngularVelocity)
        + Double3.Distance(angularAcceleration, world.WakeAngularAcceleration);

    /// <summary>
    /// The reach of the system of <paramref name="world"/>, in m: the largest distance from its semantic frame's origin to a
    /// point of one of its dynamic bodies, each bounded by the sphere of <see cref="BodyRecord.Radius"/> about the body's
    /// origin. In a numeric home it adds the home's offset. Only the dynamic bodies sleep, so only they count.
    /// </summary>
    private double Reach(FrameWorld world)
    {
        FrameHandle frame = world.Frame;
        Double3 offset = frames.IsNumeric(frame) ? frames.HomeOffset(frame, frames.SemanticOf(frame)) : Double3.Zero;
        BodyInterface bodies = world.Bodies;
        double reach = 0;
        foreach (BodyRecord member in world.Members)
        {
            if (member.Motion != BodyMotion.Dynamic || !member.Body.IsValid)
                continue;
            bodies.GetRPosition(member.Body, out RVector3 position);
            reach = Math.Max(reach, (position.ToDouble3() + offset).Length + member.Radius);
        }
        return reach;
    }

    /// <summary>
    /// Adds m · g to the locked body <paramref name="id"/> in the system of <paramref name="frame"/>, with g the linear
    /// gravity at its center of mass, if it is the primary body of a dynamic record. In a turning frame, whose turns are
    /// <paramref name="turns"/>, the force holds the two-turn update too (see <see cref="ApplyTurningUpdate"/>), unless a
    /// steer target moves the body in the step: the steering velocities carry the body to its target in the frame's
    /// coordinates over the step, so they are the chord of the step in flight already, and the update would turn them a
    /// second time (docs/plans/spin-gravity.md, "The flow per kind of entity"). Writes the acceleration that the frame gave
    /// the body (see <see cref="BodyRecord.FrameAcceleration"/>). Returns the strength of the effective gravity at the body,
    /// in m/s², or positive infinity for any other body. The effective gravity adds the centrifugal and the Euler term in a
    /// turning frame, and is the linear gravity otherwise.
    /// </summary>
    /// <param name="homeVelocity">The frame velocity of the system's home relative to its semantic frame, in the step.</param>
    /// <param name="homeAcceleration">The acceleration of the system's home relative to its semantic frame, in the step.</param>
    private float ApplyGravity(FrameHandle frame, in FrameTurns turns, Double3 homeVelocity, Double3 homeAcceleration, BodyID id, Body body)
    {
        ulong data = body.GetUserData();
        if (BodyUserData.Kind(data) != BodyKind.Primary || RecordFor(data) is not { Motion: BodyMotion.Dynamic } record || record.Body != id)
            return float.PositiveInfinity;
        body.GetRCenterOfMassPosition(out RVector3 center);
        Double3 place = center.ToDouble3();
        Double3 linear = frames.LinearGravity(frame, place);
        Vector3 gravity = linear.ToVector3();
        if (turns.Turns && record.KinematicTargetStep != stepIndex)
            ApplyTurningUpdate(record, body, turns, frame, place, homeVelocity, homeAcceleration, linear);
        else
        {
            record.FrameAcceleration = new AppliedFrameAcceleration(gravity, Vector3.Zero);
            // Only a body whose system's semantic frame turns keeps a shadow (fork F8).
            if (record.Shadow.HasValue)
                record.Shadow = default;
            if (gravity != Vector3.Zero)
                body.AddForce(gravity * record.Material.Mass);
        }
        // The effective gravity adds the rotation terms to the linear gravity, without a second evaluation of the field.
        return (turns.Turns ? frames.AddRotationTerms(frame, place, linear).ToVector3() : gravity).Length();
    }

    /// <summary>
    /// Gives the locked dynamic body <paramref name="body"/> of <paramref name="record"/> in a turning frame, whose turns are
    /// <paramref name="turns"/>, the two-turn update over the step in flight (docs/plans/spin-gravity.md, "The two-turn
    /// update"), plus the linear gravity <paramref name="linear"/> at its place, with its field part turned by the turn of the
    /// step, as one force: the change of its semantic
    /// chord that keeps it on its inertial line, in doubles, narrowed once. Its angular velocity follows the angular form, so
    /// that it keeps its inertial angular velocity ("The angular form"). The rigid form of a character cannot turn, so it gets
    /// no angular part.
    /// <para>
    /// The update starts from the body's velocity shadow while Jolt's velocity matches what the last update expected to
    /// rounding: from the shadow's double chord and place, so that the force also takes back Jolt's float rounding of the last
    /// step, of its velocity and of its place ("V6, the velocity shadow"). When Jolt's velocity differs by more, a contact
    /// changed it: Jolt applied the contact in the frame's axes at the start of the step that ended, while that step's chord
    /// is in its axes at the end, so the update starts from Jolt's velocity with the contact's change turned by that step's
    /// turn, the contact turn. Without a shadow of the last step it starts from Jolt's velocity and place. The force writes
    /// the next shadow, with Jolt's damping in its expected velocity (fork O3).
    /// </para>
    /// <para>
    /// An impulse in the step changed Jolt's velocities at the start of the step, in the frame's axes then, but the update
    /// reads Jolt's velocity as the chord of the step that ended, which it carries by both turns. So the change leaves Jolt's
    /// velocity and comes back as the change of that chord that has the same effect, <see cref="FrameTurn.ChangeBehind"/> on
    /// the turn of the step that ended, which the update carries by the turn of the step in flight alone. The change of the
    /// angular velocity goes to the angular form as the change of the inertial angular velocity at the start of the step
    /// ("Impulses"). An impulse ends the shadow, so the update starts from Jolt's velocity.
    /// </para>
    /// <para>
    /// A force and a torque of the intent are fixed in the frame's axes, like the field, and act at the start of the step,
    /// in the axes then. Jolt adds their changes unturned, so the update adds the rest of the turn of the step: the force's
    /// change R_n F / m · dt in place of F / m · dt, and the torque's change I⁻¹ τ dt to the angular form as a change at the
    /// start of the step, the way an angular impulse goes. The shadow expects the force, turned.
    /// </para>
    /// </summary>
    /// <param name="center">The body's center of mass in its home, as Jolt holds it, in m.</param>
    /// <param name="homeVelocity">The frame velocity of the body's home relative to its semantic frame, in the step.</param>
    /// <param name="homeAcceleration">The acceleration of the body's home relative to its semantic frame, in the step.</param>
    private unsafe void ApplyTurningUpdate(BodyRecord record, Body body, in FrameTurns turns, FrameHandle frame, Double3 center,
        Double3 homeVelocity, Double3 homeAcceleration, Double3 linear)
    {
        double dt = stepDt;
        Vector3 velocity = body.GetLinearVelocity();
        // Where the update starts, relative to the home: the chord of the step that ended and the place at the start of the
        // step in flight.
        Double3 chord = velocity;
        Double3 place = center;
        if (record.Shadow.TryGetValue(out ShadowTracking shadow) && shadow.Step == stepIndex - 1)
        {
            if (shadow.Matches(velocity))
            {
                chord = shadow.Chord;
                place = shadow.Place;
            }
            else
            {
                // The contact turn: the contact's change of velocity, from the axes at the start of the step that ended into
                // its axes at the end.
                Double3 contact = (Double3)velocity - (Double3)shadow.JoltTarget;
                chord += turns.Last.Turn.Rotate(contact) - contact;
            }
        }
        bool impulse = record.ImpulseStep == stepIndex;
        Double3 velocityChange = impulse ? record.ImpulseVelocityChange : Double3.Zero;
        Double3 behind = chord + homeVelocity - velocityChange + turns.Last.ChangeBehind(velocityChange);
        Double3 semanticPlace = frames.InSemantic(frame, frames.SemanticOf(frame), place);
        // A force of the intent, which Jolt adds to the velocity in the step, unturned.
        Vector3 intentForce;
        JoltNative.BodyAccumulatedForce(body.Handle, &intentForce);
        // The update's chord of the step in flight, relative to the home, before Jolt's damping. The field part of the linear
        // gravity, which is the linear gravity plus the home's acceleration, acts at the start of the step, in the axes then,
        // so the turn of the step carries it into the axes at the end: x_(n+1) gains R_n g dt². The home's acceleration is
        // motion in the semantic frame's coordinates, which takes no turn. A force of the intent is fixed in the frame's axes
        // too, and acts at the start of the step as the field does, so the update adds the part of its turn that Jolt leaves
        // out: Jolt adds F / m · dt, and the turned force is R_n F / m · dt.
        Double3 field = linear + homeAcceleration;
        Double3 ahead = behind + turns.FreeChange(semanticPlace, behind, dt) + (turns.Step.Turn.Rotate(field) - homeAcceleration) * dt
                        - homeVelocity;
        if (intentForce != Vector3.Zero)
            ahead += (turns.Step.Turn.Rotate((Double3)intentForce) - (Double3)intentForce) * (dt / record.Material.Mass);
        // Jolt's damping factor for the step, in floats, as Jolt computes it.
        float damping = MathF.Max(0f, 1f - record.Material.LinearDamping * stepDt);
        // The force carries Jolt's velocity to the update's chord, and Jolt's place to the shadow's: Jolt moves the body by
        // its velocity after the damping.
        Double3 acceleration = (ahead - velocity) / dt;
        if (damping > 0)
            acceleration += (place - center) / (damping * dt * dt);
        Vector3 force = (acceleration * record.Material.Mass).ToVector3();
        body.AddForce(force);
        // Everything that the force gives beyond the field is the frame's: the rotation part of the update, the turn of the
        // field and of the intent's force, the move of an impulse's change behind, the take-back of Jolt's rounding and the
        // contact turn. The contact turn belongs there too:
        // a body at rest on a floor gets an update whose rotation part leans across the floor by ω³ r dt, and the contact
        // turn takes that lean out of the force, so a total without it would read as a push of the floor across itself.
        record.FrameAcceleration = new AppliedFrameAcceleration(linear.ToVector3(), (acceleration - linear).ToVector3());
        if (damping > 0)
        {
            // Jolt's step: v + dt (m⁻¹ F) for the accumulated force, which holds a force of the intent, then the damping.
            float inverseMass = body.MotionProperties.InverseMassUnchecked;
            Vector3 target = (velocity + stepDt * (inverseMass * (intentForce + force))) * damping;
            Double3 next = (ahead + (Double3)intentForce * inverseMass * dt) * damping;
            record.Shadow = new VelocityShadow(new ShadowTracking(next, place + next * dt, target, stepIndex));
        }
        else
            record.Shadow = default;
        if (record.IsCharacter)
            return;
        Double3 angularChange = impulse ? record.ImpulseAngularVelocityChange : Double3.Zero;
        // A torque of the intent changes the inertial angular velocity at the start of the step, in the frame's axes then, as
        // an angular impulse does, so the angular form takes its change as the change at the start of the step. Jolt adds the
        // change I⁻¹ τ dt itself, unturned, after the angular form sets the velocity, so the form leaves it out.
        Vector3 torque;
        JoltNative.BodyAccumulatedTorque(body.Handle, &torque);
        Double3 torqueChange = Double3.Zero;
        if (torque != Vector3.Zero)
        {
            Matrix4x4 inverseInertia;
            JoltNative.BodyInverseInertia(body.Handle, &inverseInertia);
            torqueChange = (Double3)Vector3.TransformNormal(torque, inverseInertia) * dt;
        }
        Vector3 angular = (turns.AngularChordAhead((Double3)body.GetAngularVelocity() - angularChange, angularChange + torqueChange, dt)
                           - torqueChange).ToVector3();
        body.SetAngularVelocityClamped(angular);
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
    /// that gives way, and its stick to the floor pulled it down when it lost a touch. Gravity, and in a turning frame the
    /// rotation terms, change the velocity of a character that does not stand, and of one that jumps, because the ground
    /// holds a standing character against the whole acceleration of its frame. Forces and impulses change the velocity of
    /// every character. The velocity rule goes by what the character's velocity means at the start of the update (see
    /// <see cref="StartVelocity"/> and <see cref="NextCharacterVelocity"/>). After the update, a character that did not stand loses
    /// its motion into what it hit, as <see cref="WithoutMotionIntoContacts"/> says. Under gravity, up turns against the
    /// semantic gravity, with the centrifugal and the Euler term of a turning frame, whether the character touches anything
    /// or not (docs/plans/spin-gravity.md, fork F5). So a character that tumbled lands upright and can stand, and a person who
    /// jumps across a spinning station comes down feet first on the far floor. Below the zero-gravity threshold up stays.
    /// Returns the velocity that the character moved with in the update.
    /// </summary>
    private Vector3 UpdateCharacter(FrameWorld world, BodyRecord record, CharacterVirtual character)
    {
        float dt = stepDt;
        Double3 position = character.RPosition.ToDouble3();
        // The linear gravity is the field part of the character's velocity rule. The effective gravity adds the centrifugal
        // and the Euler term of a turning frame: it says whether the character is under gravity, and it presses a standing
        // character's weight onto its ground. In a frame that does not turn, the two are the same.
        Double3 linear = frames.LinearGravity(world.Frame, position);
        Vector3 effective = frames.AddRotationTerms(world.Frame, position, linear).ToVector3();
        float strength = effective.Length();
        bool underGravity = strength >= FrameTree.ZeroGravityThreshold;
        // The ground state comes from the last update.
        GroundState groundState = character.GroundState;
        Vector3 up = record.Up;
        // Up is against the semantic frame's gravity, which in a falling numeric frame is far stronger than the effective
        // gravity. In a semantic frame the two are the same. Both hold the centrifugal and the Euler term of a turning frame.
        // Up follows that gravity whether the character touches anything or not (docs/plans/spin-gravity.md, fork F5), so a
        // person who jumps across a spinning station comes down feet first on the far floor, and one that tumbled lands upright.
        Vector3 semanticGravity = frames.SemanticGravity(world.Frame, position).ToVector3();
        float semanticStrength = semanticGravity.Length();
        if (semanticStrength >= FrameTree.ZeroGravityThreshold)
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
        // The ground holds a standing character against gravity, until the step in which it jumps off. Gravity in the
        // velocity of a standing character would press it into its ground in every step. Jolt then reported a false
        // velocity of gravity times the step, and pushed every dynamic body that the character leaned on a little in
        // every step, so a stack that the character stood against woke each time that it fell asleep.
        bool held = standing && record.PendingJump == 0;
        CharacterStartVelocity start = StartVelocity(record, standing, velocity, ground, up);
        Vector3 next = NextCharacterVelocity(world.Frame, record, position, linear, start);
        record.Held = held;
        record.PendingVelocityChange = Vector3.Zero;
        record.PendingJump = 0;
        record.Standing = standing;
        character.LinearVelocity = next;

        // Jolt reads the system gravity to press the character's weight onto what it stands on.
        world.System.Gravity = effective;
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
    /// What the velocity of a walking character means at the start of its update (docs/plans/spin-gravity.md, "Characters"),
    /// whose Jolt velocity is <paramref name="velocity"/>, whose ground moves at <paramref name="ground"/>, whose up is
    /// <paramref name="up"/>, and which stands in this step when <paramref name="standing"/> is true. A standing character
    /// takes the ground's velocity plus the part of its desired velocity across its up: held by its ground, or with its jump
    /// along up an instant velocity, relative to the ground and so to the home's instant velocity. A character that does not
    /// stand keeps its momentum, including what it took from a moving platform: Jolt's chord, or an instant velocity that the
    /// game gave since its last update. That one is relative to the home's frame velocity in the step, as LocalVelocity gives
    /// it, so it loses the home's instant velocity past its frame velocity where the step starts, as LinearChord takes it from
    /// a rigid body's.
    /// </summary>
    private CharacterStartVelocity StartVelocity(BodyRecord record, bool standing, Vector3 velocity, Vector3 ground, Vector3 up)
    {
        if (standing)
        {
            Vector3 desired = record.DesiredVelocity;
            Vector3 walked = ground + desired - Vector3.Dot(desired, up) * up;
            if (record.PendingJump == 0)
                return new HeldStart(walked);
            return new InstantStart(walked + record.PendingJump * up);
        }
        if (record.InstantVelocityStep == stepIndex)
            return new InstantStart((Double3)velocity - (Double3)HomeInstantVelocityOverChordAtStart(record.Frame).ToVector3());
        return new ChordStart(velocity);
    }

    /// <summary>
    /// The velocity that a walking character in the system of <paramref name="frame"/>, at <paramref name="position"/> in
    /// it, under the linear gravity <paramref name="linear"/> there, moves with in the step, by its case
    /// <paramref name="start"/> (docs/plans/spin-gravity.md, "Characters"), with the velocity change that forces and impulses
    /// gave it since its last update. Writes the acceleration that the frame gave it (see
    /// <see cref="BodyRecord.FrameAcceleration"/>). The character moves by the chord of the step:
    /// <list type="bullet">
    /// <item>A held character moves with its ground and walk, and takes no gravity and no rotation term, because its ground
    /// holds it against the whole acceleration of its frame.</item>
    /// <item>An instant velocity becomes the chord of the step that ended, which is half a step of the linear gravity less
    /// (see LinearChord), and a chord of the step that ended gains a whole step of it.</item>
    /// <item>In a turning frame the two take the turns too: the chord rule turns an instant velocity into the chord behind
    /// it with the turn of the step that ended, and the two-turn update carries the chord on along the character's inertial
    /// line, as for a dynamic body (see <see cref="ApplyTurningUpdate"/>). A velocity change acts at the start of the step,
    /// in the frame's axes then, so it enters as the change of the chord behind that has the same effect
    /// (see <see cref="FrameTurn.ChangeBehind"/>), which the update carries by the turn of the step in flight alone
    /// (docs/plans/spin-gravity.md, "Impulses"). A force is in that change too, and the update turns it by the same turn as
    /// a dynamic body's force.</item>
    /// </list>
    /// In a frame that does not turn, every rule is the float rule of before the rotation terms, to the bit.
    /// </summary>
    private Vector3 NextCharacterVelocity(FrameHandle frame, BodyRecord record, Double3 position, Double3 linear, CharacterStartVelocity start)
    {
        float dt = stepDt;
        Vector3 gravity = linear.ToVector3();
        Vector3 change = record.PendingVelocityChange;
        FrameTurns turns = frames.Turns(frame);
        // The local chord of the step that ended, the same chord without the chord rule's turn, and the velocity of the rule
        // without turns, for the cases that take the frame's acceleration.
        Double3 behind, plainBehind;
        Vector3 plain;
        switch (start)
        {
            case HeldStart held:
                record.FrameAcceleration = new AppliedFrameAcceleration(Vector3.Zero, Vector3.Zero);
                return held.Velocity + change;
            case InstantStart instant:
                // The float rule loses the home's instant velocity past its frame velocity together with the half step of
                // the gravity, as it did before the rotation terms, so that a velocity that the game gave keeps its bits.
                Vector3 over = HomeInstantVelocityOverChordAtStart(frame).ToVector3();
                plain = ((Double3)instant.Velocity + (Double3)over).ToVector3();
                plain -= gravity * (dt * 0.5f) + over;
                plainBehind = instant.Velocity - linear * (0.5 * stepDt);
                behind = plainBehind;
                if (turns.Turns)
                    behind += StepStart(frame)!.Value.ChordBehindChange(position, instant.Velocity + HomeInstantVelocityOverChordAtStart(frame), linear, stepDt);
                break;
            case ChordStart chord:
                plain = chord.Velocity.ToVector3();
                plainBehind = behind = chord.Velocity;
                break;
            default:
                // Only a bug leaves the case out: the update builds one for every character.
                Contract.Assert(false, "a walking character's update needs the case of its start velocity");
                return Vector3.Zero;
        }
        plain += gravity * dt;
        plain += change;
        if (!turns.Turns)
        {
            record.FrameAcceleration = new AppliedFrameAcceleration(gravity, Vector3.Zero);
            return plain;
        }
        // The update on the semantic chord at the semantic place, as ApplyTurningUpdate gives a dynamic body: the field part
        // of the linear gravity, the linear gravity plus the home's acceleration, takes the turn of the step, and the home's
        // acceleration, which is motion in the semantic frame's coordinates, does not.
        FrameHandle semantic = frames.SemanticOf(frame);
        Double3 homeVelocity = frames.HomeVelocity(frame, semantic);
        Double3 homeAcceleration = frames.HomeAcceleration(frame, semantic);
        Double3 semanticChord = behind + turns.Last.ChangeBehind(change) + homeVelocity;
        Double3 field = linear + homeAcceleration;
        Double3 ahead = semanticChord + turns.FreeChange(frames.InSemantic(frame, semantic, position), semanticChord, stepDt)
                        + (turns.Step.Turn.Rotate(field) - homeAcceleration) * stepDt - homeVelocity;
        // What the turns give beyond the rule without them is the frame's.
        record.FrameAcceleration = new AppliedFrameAcceleration(gravity, ((ahead - plainBehind - change) / stepDt - linear).ToVector3());
        return ahead.ToVector3();
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
    /// The proper acceleration of each frame with a dynamic hull: its hull's velocity change over the step minus the whole
    /// acceleration that the parent frame gave it (see <see cref="AppliedFrameAcceleration.Total"/>), the gravity and in a
    /// turning parent the rotation terms, turned into the frame's own axes. So a ship that rests on the floor of a spinning
    /// station reads ω² R toward the axis, and every change of velocity that the frame did not give reads as proper
    /// acceleration (docs/plans/spin-gravity.md, "V4, the acceleration that the frame gave a body"). Also the hull's
    /// acceleration and angular acceleration in
    /// the update, in the axes of its home, which leave out what the intents set before it (see
    /// <see cref="CaptureHullVelocitiesAfterIntents"/>).
    /// </summary>
    /// <remarks>
    /// Jolt keeps velocities in float. At orbital speed one float step of velocity, divided by the time step, is
    /// about 0.03 m/s², so a hull in free fall would read that much noise and its loose contents would drift. Jolt also
    /// turns the frame's acceleration into a velocity change in float, through the force, the inverse mass and the step.
    /// Each component smaller than the float resolution of the velocity along that axis, or of the frame's acceleration
    /// along it, reads as zero.
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
            Vector3 applied = owner.FrameAcceleration.Total;
            Vector3 acceleration = (after - before) / stepDt - applied;
            acceleration = new Vector3(
                AboveResolution(acceleration.X, before.X, after.X, applied.X, stepDt),
                AboveResolution(acceleration.Y, before.Y, after.Y, applied.Y, stepDt),
                AboveResolution(acceleration.Z, before.Z, after.Z, applied.Z, stepDt));
            owner.ProperAcceleration = Vector3.Transform(acceleration, Quaternion.Conjugate(bodies.GetRotation(owner.Body)));
            // The same change of velocity without its float noise, less the part that the intents made before the update.
            owner.StepAcceleration = (Double3)applied + (Double3)acceleration - ((Double3)owner.VelocityAfterIntents - before) / stepDt;
            // When Jolt puts the hull to sleep in the update, it sets its angular velocity to zero. That reset is no motion of
            // the step, so it gives no angular acceleration: otherwise the frame tree would predict the next step's rate as
            // the last rate reversed and turn the sleeping hull's frame back (docs/plans/spin-gravity.md, "V3").
            owner.AngularAcceleration = bodies.IsActive(owner.Body)
                ? (bodies.GetAngularVelocity(owner.Body) - owner.AngularVelocityAfterIntents) / stepDt
                : Vector3.Zero;
        }
    }

    /// <summary>
    /// Zero when <paramref name="acceleration"/> is within the rounding of a float velocity change, or within two float
    /// steps of <paramref name="applied"/>, the acceleration that the frame gave the hull, along the same axis, which Jolt
    /// rounds on the way to the velocity change.
    /// </summary>
    private static float AboveResolution(float acceleration, float before, float after, float applied, float dt)
    {
        float magnitude = MathF.Max(MathF.Abs(before), MathF.Abs(after));
        float resolution = (MathF.BitIncrement(magnitude) - magnitude) / dt;
        float strength = MathF.Abs(applied);
        float appliedResolution = 2 * (MathF.BitIncrement(strength) - strength);
        return MathF.Abs(acceleration) <= MathF.Max(resolution, appliedResolution) ? 0f : acceleration;
    }
}
