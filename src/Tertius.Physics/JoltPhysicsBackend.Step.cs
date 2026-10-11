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

        // Teleport first, so that a kinematic target in the same intent moves the body on from the new pose.
        if ((flags & IntentFlags.Teleport) != 0)
        {
            bodies.SetRPositionAndRotation(id, intent.TeleportPose.Position.ToJolt(), intent.TeleportPose.Rotation.ToJolt(), Activation.Activate);
            bodies.SetLinearAndAngularVelocity(id, Vector3.Zero, Vector3.Zero);
        }
        if ((flags & IntentFlags.SetVelocity) != 0)
        {
            bodies.SetLinearVelocity(id, intent.Velocity);
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
            if (record.Motion == BodyMotion.Kinematic)
            {
                bodies.MoveKinematic(id, intent.Target.Position.ToJolt(), rotation.ToJolt(), stepDt);
            }
            else
            {
                // A dynamic body gets the velocities that carry it to the target in one step. Contacts and gravity
                // still act on it.
                BodySnapshot s = ReadPrimary(record);
                Double3 localCenter = s.Pose.InverseTransformPoint(s.CenterOfMass);
                Double3 targetCenter = intent.Target.Position + rotation.Rotate(localCenter);
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
            character.RPosition = intent.TeleportPose.Position.ToJolt();
            character.Rotation = intent.TeleportPose.Rotation.ToJolt();
            character.LinearVelocity = Vector3.Zero;
            record.PendingVelocityChange = Vector3.Zero;
        }
        if ((flags & IntentFlags.SetVelocity) != 0)
            character.LinearVelocity = intent.Velocity;
        if ((flags & IntentFlags.CharacterMove) != 0)
            record.DesiredVelocity = intent.CharacterVelocity;
        if ((flags & IntentFlags.Jump) != 0)
            record.PendingJump = MathF.Max(record.PendingJump, intent.JumpSpeed);
        float mass = record.Desc.Mass > 0 ? record.Desc.Mass : 1f;
        record.PendingVelocityChange += (intent.Impulse + intent.Force * stepDt) / mass;
    }

    /// <summary>Moves the kinematic hulls of Kepler frames to their end-of-step pose, and static hulls to the tree pose.</summary>
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
                // A static frame does not move, unless the simulation placed it somewhere else. Jolt skips the
                // broad-phase update when nothing changed.
                DPose pose = frames.LocalPose(owner.OwnedFrame);
                bodies.SetRPositionAndRotationWhenChanged(owner.Body, pose.Position.ToJolt(), pose.Rotation.ToJolt(), Activation.DontActivate);
            }
        }
    }

    // ---- Gravity ----

    /// <summary>
    /// Applies m · g_eff to every awake dynamic body. Wakes a frame's bodies when its effective gravity changes. A sleeping
    /// body gets no force, and the force does not reset the sleep timer of an awake body, so a body at rest falls asleep.
    /// </summary>
    private void ApplyGravity()
    {
        float wakeThreshold = options.WakeAccelerationThreshold;
        foreach (FrameWorld world in worldList)
        {
            FrameHandle frame = world.Frame;
            Vector3 properAcceleration = frames.ProperAcceleration(frame);
            GravityField field = frames.Gravity(frame);
            if (world.HasGravityState &&
                (field != world.LastGravity || Vector3.DistanceSquared(properAcceleration, world.LastProperAcceleration) > wakeThreshold * wakeThreshold))
                WakeDynamicMembers(world);
            world.HasGravityState = true;
            world.LastGravity = field;
            world.LastProperAcceleration = properAcceleration;

            // BodyInterface.AddForce resets the sleep timer of the body, so a body under gravity would never sleep. The
            // force goes on the locked body instead, which changes nothing else.
            BodyLockInterface locks = world.System.BodyLockInterface;
            int count = CopyActiveBodies(world);
            for (int i = 0; i < count; i++)
            {
                BodyID id = activeScratch[i];
                locks.LockWrite(id, out BodyLockWrite write);
                if (write.Succeeded && write.Body is { } body)
                    ApplyGravity(frame, id, body);
                locks.UnlockWrite(write);
            }
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

    /// <summary>Adds m · g_eff to the locked body <paramref name="id"/> in the system of <paramref name="frame"/>, if it is the primary body of a dynamic record.</summary>
    private void ApplyGravity(FrameHandle frame, BodyID id, Body body)
    {
        ulong data = body.GetUserData();
        if (BodyUserData.Kind(data) != BodyKind.Primary || RecordFor(data) is not { Motion: BodyMotion.Dynamic } record || record.Body != id)
            return;
        body.GetRCenterOfMassPosition(out RVector3 center);
        Vector3 gravity = frames.EffectiveGravity(frame, center.ToDouble3());
        record.GravityApplied = gravity;
        if (gravity != Vector3.Zero)
            body.AddForce(gravity * record.Desc.Mass);
    }

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
            if (world.CharacterCount == 0)
                continue;
            foreach (BodyRecord member in world.Members)
                if (member.Character is { } character)
                    UpdateCharacter(world, member, character);
        }
    }

    /// <summary>
    /// Moves one character. Up is against the effective gravity. On the ground the character takes the ground's
    /// velocity plus its desired horizontal velocity, and can jump. In the air it keeps its momentum. Near zero
    /// gravity, the desired velocity is a full 3D velocity.
    /// </summary>
    private void UpdateCharacter(FrameWorld world, BodyRecord record, CharacterVirtual character)
    {
        float dt = stepDt;
        Vector3 gravity = frames.EffectiveGravity(world.Frame, character.RPosition.ToDouble3());
        float strength = gravity.Length();
        bool zeroGravity = strength < options.ZeroGravityThreshold;
        Vector3 up = record.Up;
        if (!zeroGravity)
        {
            up = -gravity / strength;
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
        Vector3 next;
        if (zeroGravity)
        {
            next = record.DesiredVelocity + (character.IsSupported ? ground : Vector3.Zero);
        }
        else
        {
            bool onGround = character.GroundState == GroundState.OnGround;
            bool towardGround = Vector3.Dot(velocity - ground, up) < 0.1f;
            if (onGround && towardGround)
            {
                Vector3 desired = record.DesiredVelocity;
                next = ground + desired - Vector3.Dot(desired, up) * up;
                if (record.PendingJump > 0)
                    next += record.PendingJump * up;
            }
            else
            {
                // In the air the character keeps its momentum, including what it took from a moving platform.
                next = velocity;
            }
            next += gravity * dt;
        }
        next += record.PendingVelocityChange;
        record.PendingVelocityChange = Vector3.Zero;
        record.PendingJump = 0;
        character.LinearVelocity = next;

        // Jolt reads the system gravity to press the character's weight onto what it stands on.
        world.System.Gravity = gravity;
        ExtendedUpdateSettings settings = zeroGravity
            ? ZeroGravityUpdate
            : new ExtendedUpdateSettings { StickToFloorStepDown = -0.5f * up, WalkStairsStepUp = 0.4f * up };
        character.ExtendedUpdate(dt, settings, Layers.Moving, world.System);
        world.System.Gravity = Vector3.Zero;
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
