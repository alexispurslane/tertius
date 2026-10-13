using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    private static readonly ExtendedUpdateSettings ZeroGravityUpdate = new()
    {
        StickToFloorStepDown = Vector3.Zero,
        WalkStairsStepUp = Vector3.Zero,
    };

    // Bodies that a kinematic target moved in the current and the previous step.
    private List<BodyRecord> kinematicMovers = new();
    private List<BodyRecord> kinematicMoversLast = new();
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

    /// <summary>Remembers the velocity of every physics-driven hull, for its frame's proper acceleration.</summary>
    private void CaptureHullVelocities()
    {
        foreach (BodyRecord owner in frameOwners)
            if (owner.Motion == BodyMotion.Dynamic && owner.HasPrimary)
                owner.VelocityBefore = owner.World!.Bodies.GetLinearVelocity(owner.Body);
    }

    // ---- Intents ----

    private void ApplyIntents(in PhysicsStepContext context)
    {
        (kinematicMoversLast, kinematicMovers) = (kinematicMovers, kinematicMoversLast);
        kinematicMovers.Clear();

        ReadOnlySpan<PhysicsIntent> intents = context.Intents.Span;
        foreach (Entity entity in context.IntentEntities.Span)
        {
            if (entity.Index >= (uint)intents.Length || Find(entity) is not { HasPrimary: true } record)
                continue;
            ref readonly PhysicsIntent intent = ref intents[(int)entity.Index];
            if (record.Character is not null)
                ApplyCharacterIntent(record, intent);
            else if (record.Motion != BodyMotion.Static)
                ApplyBodyIntent(record, intent);
        }

        // A body that a target moved last step stops at the target, unless it got a new target or velocity.
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

    private void ApplyBodyIntent(BodyRecord record, in PhysicsIntent intent)
    {
        BodyInterface bodies = record.World!.Bodies;
        BodyID id = record.Body;
        IntentFlags flags = intent.Flags;

        // Teleport first, so that a kinematic target in the same intent moves the body on from the new pose. Jolt does not
        // simulate the jump, so the bodies around the old place wake. Jolt wakes those that the body touches at the new place.
        if ((flags & IntentFlags.Teleport) != 0)
        {
            record.World!.WakeAround(id);
            bodies.SetRPositionAndRotation(id, InHome(record, intent.TeleportPose.Position).ToJolt(), intent.TeleportPose.Rotation.ToJolt(), Activation.Activate);
            // At rest in the semantic frame, which is not at rest in a falling numeric home.
            bodies.SetLinearAndAngularVelocity(id, LocalVelocity(record, Double3.Zero), Vector3.Zero);
        }
        if ((flags & IntentFlags.SetVelocity) != 0)
        {
            bodies.SetLinearVelocity(id, LocalVelocity(record, intent.Velocity));
            record.VelocitySetStep = stepIndex;
        }
        if ((flags & IntentFlags.SetAngularVelocity) != 0)
        {
            bodies.SetAngularVelocity(id, intent.AngularVelocity);
            record.VelocitySetStep = stepIndex;
        }
        if ((flags & IntentFlags.KinematicTarget) != 0)
        {
            DQuaternion rotation = intent.Target.Rotation.Normalized();
            // The body reaches the target at the end of the step, where a falling numeric home will be by then.
            Double3 target = intent.Target.Position - HomeOffsetAfterStep(record.Frame);
            if (record.Motion == BodyMotion.Kinematic)
            {
                bodies.MoveKinematic(id, target.ToJolt(), rotation.ToJolt(), stepDt);
            }
            else
            {
                // A dynamic body gets the velocities that carry it to the target in one step. Contacts and gravity
                // still act on it.
                BodySnapshot s = ReadPrimary(record);
                Double3 localCenter = s.Pose.InverseTransformPoint(s.CenterOfMass);
                Double3 targetCenter = target + rotation.Rotate(localCenter);
                Vector3 linear = ((targetCenter - s.CenterOfMass) / stepDt).ToVector3();
                Vector3 angular = Rotations.AngularVelocity(s.Pose.Rotation, rotation, stepDt).ToVector3();
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
            character.LinearVelocity = LocalVelocity(record, Double3.Zero);
            record.PendingVelocityChange = Vector3.Zero;
            world.WakeAround(character.InnerBodyID);
        }
        if ((flags & IntentFlags.SetVelocity) != 0)
            character.LinearVelocity = LocalVelocity(record, intent.Velocity);
        if ((flags & IntentFlags.CharacterMove) != 0)
            record.DesiredVelocity = intent.CharacterVelocity;
        if ((flags & IntentFlags.Jump) != 0)
            record.PendingJump = MathF.Max(record.PendingJump, intent.JumpSpeed);
        float mass = record.Desc.Mass > 0 ? record.Desc.Mass : 1f;
        record.PendingVelocityChange += (intent.Impulse + intent.Force * stepDt) / mass;
    }

    // ---- From the semantic frame to the physics home ----

    /// <summary>
    /// Jolt's float local velocity for <paramref name="velocity"/>, which is relative to the body's semantic frame: the
    /// velocity minus the frame velocity of the body's physics home relative to that frame. Physics calls it at the moment
    /// that it applies a request or creates a body, so it uses the home of that moment. The home is the frame whose
    /// system holds the body, and the semantic frame is its nearest ancestor that is not numeric, or the home itself.
    /// The subtraction is in doubles, so only the small local velocity narrows to a float.
    /// </summary>
    private Vector3 LocalVelocity(FrameHandle home, Double3 velocity) => (velocity - frames.HomeVelocity(home, frames.SemanticOf(home))).ToVector3();

    private Vector3 LocalVelocity(BodyRecord record, Double3 velocity) => LocalVelocity(record.Frame, velocity);

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

    /// <summary>Moves the kinematic hulls of Kepler and Spin frames to their end-of-step pose, and static hulls to the tree pose.</summary>
    private void DriveKinematicHulls()
    {
        foreach (BodyRecord owner in frameOwners)
        {
            if (!owner.HasPrimary)
                continue;
            BodyInterface bodies = owner.World!.Bodies;
            if (owner.Motion == BodyMotion.Kinematic)
            {
                FrameMotion target = PredictedMotion(owner.OwnedFrame);
                bodies.MoveKinematic(owner.Body, target.Pose.Position.ToJolt(), target.Pose.Rotation.ToJolt(), stepDt);
            }
            else if (owner.Motion == BodyMotion.Static)
            {
                // A static frame does not move, unless the game placed it somewhere else. Jolt does not simulate that
                // move, so the bodies around the hull's old and new places wake.
                DPose pose = frames.TreeLocalPose(owner.OwnedFrame);
                if (IsAt(bodies, owner.Body, pose))
                    continue;
                owner.World!.WakeAround(owner.Body);
                bodies.SetRPositionAndRotation(owner.Body, pose.Position.ToJolt(), pose.Rotation.ToJolt(), Activation.DontActivate);
                owner.World!.WakeAround(owner.Body);
            }
        }
    }

    // ---- Gravity ----

    /// <summary>
    /// Applies m · g_eff to every awake dynamic body, and gives each system the sleep threshold of the weakest effective
    /// gravity on its awake dynamic bodies (see <see cref="SleepThresholdFor"/>). Wakes a frame's bodies when its gravity
    /// field changes, or when its proper acceleration has moved more than <see cref="JoltBackendOptions.WakeAccelerationThreshold"/>
    /// from its value when they last woke. A sleeping body gets no force, and the force does not reset the sleep timer of
    /// an awake body, so a body at rest falls asleep.
    /// </summary>
    private void ApplyGravity()
    {
        float wakeThreshold = options.WakeAccelerationThreshold;
        foreach (FrameWorld world in worldList)
        {
            FrameHandle frame = world.Frame;
            Vector3 properAcceleration = frames.ProperAcceleration(frame);
            GravityField field = frames.Gravity(frame);
            bool wake = world.HasGravityState &&
                (field != world.LastGravity || Vector3.DistanceSquared(properAcceleration, world.WakeProperAcceleration) > wakeThreshold * wakeThreshold);
            if (wake)
                WakeDynamicMembers(world);
            if (wake || !world.HasGravityState)
                world.WakeProperAcceleration = properAcceleration;
            world.HasGravityState = true;
            world.LastGravity = field;

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
            body.AddForce(gravity * record.Desc.Mass);
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

    private void UpdateCharacters()
    {
        foreach (FrameWorld world in worldList)
        {
            world.ClearCharacterContacts();
            if (world.CharacterCount == 0)
                continue;
            foreach (BodyRecord member in world.Members)
            {
                if (member.Character is not { } character)
                    continue;
                UpdateCharacter(world, member, character);
                world.NoteCharacterContacts(character);
            }
        }
    }

    /// <summary>
    /// Moves one character, and records in <see cref="BodyRecord.Standing"/> whether it stood. The character stands
    /// when the effective gravity is at least <see cref="FrameTree.ZeroGravityThreshold"/>, Jolt found walkable ground
    /// under it in its last update, and it does not move away from that ground. Then it takes the ground's velocity
    /// plus the part of its desired velocity across its up, and can jump. A character that does not stand, in the air,
    /// on steep ground or weightless, keeps its momentum, and its desired velocity and its jump do not move it. (User)
    /// The record keeps the desired velocity, which applies once the character stands. Gravity, forces and impulses
    /// change the velocity of every character. After the update, a character that did not stand loses its motion into
    /// what it hit, as <see cref="WithoutMotionIntoContacts"/> says. Under gravity, up turns against the gravity whenever
    /// the character touched anything in its last update, also with its side or its head, so a character that landed
    /// tilted turns upright and can stand. A character in free fall that touches nothing keeps its last up.
    /// </summary>
    private void UpdateCharacter(FrameWorld world, BodyRecord record, CharacterVirtual character)
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
        next += gravity * dt;
        next += record.PendingVelocityChange;
        record.PendingVelocityChange = Vector3.Zero;
        record.PendingJump = 0;
        record.Standing = standing;
        character.LinearVelocity = next;

        // Jolt reads the system gravity to press the character's weight onto what it stands on.
        world.System.Gravity = gravity;
        ExtendedUpdateSettings settings = underGravity
            ? new ExtendedUpdateSettings { StickToFloorStepDown = -0.5f * up, WalkStairsStepUp = 0.4f * up }
            : ZeroGravityUpdate;
        character.ExtendedUpdate(dt, settings, Layers.Moving, world.System);
        world.System.Gravity = Vector3.Zero;
        if (!standing)
        {
            // Jolt slides the character along what it hits, but keeps the velocity that it was given. Only standing
            // replaces the velocity, so a character that does not stand loses its motion into a surface here.
            character.LinearVelocity = WithoutMotionIntoContacts(character, next);
        }
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
    /// momentum against it and pushes it. Jolt pushes a character out of a surface that it sank into by moving it, so
    /// the push adds no velocity here.
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
            if (world.CharacterCount == 0 && world.System.GetNumActiveBodies(BodyType.Rigid) == 0)
                continue;
            PhysicsUpdateError error = world.Update(stepDt, jobs);
            if (error != PhysicsUpdateError.None && !world.ReportedOverflow)
            {
                world.ReportedOverflow = true;
                log($"Physics: the system of {world.Frame} reported {error}. Some contacts were dropped. Raise the per-frame limits in JoltBackendOptions.");
            }
        }
    }

    /// <summary>
    /// The proper acceleration of each physics-driven frame: its hull's velocity change over the step minus the
    /// gravity that the parent applied, turned into the frame's own axes.
    /// </summary>
    /// <remarks>
    /// Jolt keeps velocities in float. At orbital speed one float step of velocity, divided by the time step, is
    /// about 0.03 m/s², so a hull in free fall would read that much noise and its loose contents would drift. Each
    /// component smaller than the float resolution of the velocity along that axis reads as zero.
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
            Vector3 acceleration = (after - before) / stepDt - owner.GravityApplied;
            acceleration = new Vector3(
                AboveResolution(acceleration.X, before.X, after.X, stepDt),
                AboveResolution(acceleration.Y, before.Y, after.Y, stepDt),
                AboveResolution(acceleration.Z, before.Z, after.Z, stepDt));
            owner.ProperAcceleration = Vector3.Transform(acceleration, Quaternion.Conjugate(bodies.GetRotation(owner.Body)));
        }
    }

    /// <summary>Zero when <paramref name="acceleration"/> is within the rounding of a float velocity change.</summary>
    private static float AboveResolution(float acceleration, float before, float after, float dt)
    {
        float magnitude = MathF.Max(MathF.Abs(before), MathF.Abs(after));
        float resolution = (MathF.BitIncrement(magnitude) - magnitude) / dt;
        return MathF.Abs(acceleration) <= resolution ? 0f : acceleration;
    }
}
