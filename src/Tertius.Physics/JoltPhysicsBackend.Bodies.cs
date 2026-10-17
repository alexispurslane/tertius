using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    /// <summary>A primary body's state as Jolt has it, in its frame. Velocities are at the center of mass.</summary>
    private readonly record struct BodySnapshot(DPose Pose, Double3 CenterOfMass, Vector3 Linear, Vector3 Angular);

    private static BodySnapshot ReadPrimary(BodyRecord record)
    {
        if (record.Character is { } character)
        {
            Double3 position = character.RPosition.ToDouble3();
            return new BodySnapshot(new DPose(position, character.Rotation.ToDouble()), position, character.LinearVelocity, Vector3.Zero);
        }
        BodyInterface bodies = record.World!.Bodies;
        bodies.GetRPosition(record.Body, out RVector3 origin);
        bodies.GetRCenterOfMassPosition(record.Body, out RVector3 centerOfMass);
        bodies.GetLinearAndAngularVelocity(record.Body, out Vector3 linear, out Vector3 angular);
        Quaternion rotation = bodies.GetRotation(record.Body);
        return new BodySnapshot(new DPose(origin.ToDouble3(), rotation.ToDouble()), centerOfMass.ToDouble3(), linear, angular);
    }

    /// <summary>The body origin of the primary body, in its frame.</summary>
    private static Double3 PrimaryPosition(BodyRecord record)
    {
        if (record.Character is { } character)
            return character.RPosition.ToDouble3();
        record.World!.Bodies.GetRPosition(record.Body, out RVector3 origin);
        return origin.ToDouble3();
    }

    private static MotionType ToMotionType(BodyMotion motion) => motion switch
    {
        BodyMotion.Static => MotionType.Static,
        BodyMotion.Kinematic => MotionType.Kinematic,
        _ => MotionType.Dynamic,
    };

    /// <summary>
    /// The role of an ordinary body's layer, which is the description's layer with two exceptions. A static body on the
    /// Moving layer (the <see cref="BodyDesc"/> default) gets the Static role: it collides with the same bodies that
    /// matter, and Jolt keeps static bodies out of the moving broad-phase tree. A character always has the Moving role,
    /// as its inner body and its movement always had the Moving layer.
    /// </summary>
    private static BodyLayer RoleFor(BodyDesc desc) => desc.Motion switch
    {
        BodyMotion.Character => BodyLayer.Moving,
        BodyMotion.Static when desc.Layer == BodyLayer.Moving => BodyLayer.Static,
        _ => desc.Layer,
    };

    /// <summary>
    /// The object layer of a record's primary body, which is also the layer that a character moves with. A frame's hull
    /// ignores the owner's collision groups (see <see cref="Layers"/>).
    /// </summary>
    private static ObjectLayer PrimaryLayer(BodyRecord record) =>
        record.IsHull ? Layers.FrameHull : Layers.For(RoleFor(record.Desc), record.CollisionGroups, record.CollisionMask);

    /// <summary>The object layer of the static interior of the frame that a record owns.</summary>
    private static ObjectLayer InteriorLayer(BodyRecord record) => Layers.For(BodyLayer.Static, record.CollisionGroups, record.CollisionMask);

    /// <summary>The object layer of each ghost of a record's body.</summary>
    private static ObjectLayer GhostLayer(BodyRecord record) => Layers.For(BodyLayer.Ghost, record.CollisionGroups, record.CollisionMask);

    // ---- Creation ----

    /// <summary>
    /// Creates the record and the bodies of one BodyCreate. Throws <see cref="ArgumentException"/> for a body that physics
    /// cannot create (see <see cref="BodyDesc.Problem"/>, <see cref="BodyDesc.FrameHullProblem"/> and
    /// <see cref="ShapeProblem"/>) before any state changes. If anything fails after that, the record and every body that
    /// it got are removed before the exception leaves, so a failed creation leaves nothing behind.
    /// </summary>
    private void CreateRecord(in BodyCreate create)
    {
        Entity entity = create.Entity;
        if (entity.IsNull)
        {
            log("Physics: ignored a BodyCreate for the null entity.");
            return;
        }
        BodyDesc desc = create.Desc ?? throw new ArgumentException($"The BodyCreate for {entity} has no body description.");
        bool ownsFrame = frames.TryGetFrame(entity, out FrameHandle owned);
        if (desc.Problem(ownsFrame) is { } problem)
            throw new ArgumentException($"{entity} cannot get a body: it {problem}.");

        var record = new BodyRecord
        {
            Entity = entity,
            Desc = desc,
            Material = desc.Motion == BodyMotion.Character ? RigidForm(desc) : desc,
            CollisionGroups = desc.CollisionGroups,
            CollisionMask = desc.CollisionMask,
        };
        if (ownsFrame)
        {
            record.OwnedFrame = owned;
            record.Driver = frames.Driver(owned);
            record.DriverVersion = frames.DriverVersion(owned);
            record.Motion = BodyDesc.FrameHullMotion(record.Driver);
            // A root frame has no parent, so its owner has no hull, and only a hull has to follow the driver.
            if (!ParentOf(owned).IsNull && desc.FrameHullProblem(record.Driver) is { } hull)
                throw new ArgumentException($"{entity} owns {owned}, which has the {record.Driver} driver, but its hull {hull}.");
        }
        else
        {
            if (create.Frame.IsNull || !frames.IsAlive(create.Frame))
            {
                log($"Physics: {entity} was not created because its frame {create.Frame} is not alive.");
                return;
            }
            record.Motion = desc.Motion;
        }

        // Build every native shape before any state changes, so that a shape that Jolt cannot build throws cleanly.
        record.HasMesh = desc.Shape.ContainsMesh();
        shapes.Get(desc.Shape);
        // A character's rigid form has a shape of its own.
        shapes.Get(record.Material.Shape);
        if (ownsFrame)
            shapes.Get(desc.InteriorShape ?? desc.Shape);
        record.Radius = BoundingRadius(desc.Shape);

        if (Find(entity) is not null)
        {
            log($"Physics: {entity} already had a body. The old body is replaced.");
            DestroyRecord(entity);
        }
        else if (entity.Index < (uint)records.Length && records[entity.Index] is { } stale)
        {
            log($"Physics: {stale.Entity} still had a body when its slot was reused by {entity}. The old body is removed.");
            DestroyRecord(stale.Entity);
        }
        Store(record);

        try
        {
            if (ownsFrame)
            {
                frameOwners.Add(record);
                // The tree is the authority on a frame's pose. A root frame has no parent, so it has no hull.
                FrameHandle parent = ParentOf(owned);
                if (!parent.IsNull)
                {
                    // A new frame has the velocities that the game spawned it with: instant velocities, as for any
                    // other body, so that a ship and the crates spawned with it move alike.
                    CreateHullFromTree(record, parent);
                    spawnedRigid.Add(record);
                }
                CreateInterior(record);
            }
            else
            {
                record.Up = Vector3.Transform(Vector3.UnitY, create.Pose.Rotation.ToJolt());
                var pose = new DPose(InHome(create.Frame, create.Pose.Position), create.Pose.Rotation);
                // The spawn velocities are instant velocities. The next step makes their chords.
                CreatePrimary(record, create.Frame, pose, LocalVelocity(create.Frame, create.LinearVelocity).ToVector3(), create.AngularVelocity, upFromGravity: true);
                if (record.Character is not null)
                    record.InstantVelocityStep = stepIndex + 1;
                else
                    spawnedRigid.Add(record);
            }
        }
        catch
        {
            DestroyRecord(entity);
            throw;
        }
    }

    private void Store(BodyRecord record)
    {
        uint index = record.Entity.Index;
        if (index >= (uint)records.Length)
            Array.Resize(ref records, (int)Math.Max(index + 1, (uint)records.Length * 2));
        records[index] = record;
    }

    /// <summary>
    /// Creates the primary body or character of a record in the system of <paramref name="frame"/>. For a character,
    /// <paramref name="upFromGravity"/> says where its up comes from (see <see cref="CreateCharacter"/>). A rigid body
    /// ignores it. A rigid body that is not <paramref name="awake"/> starts asleep, as a sleeping body that moves to
    /// another system does.
    /// </summary>
    private void CreatePrimary(BodyRecord record, FrameHandle frame, in DPose pose, Vector3 linearVelocity, Vector3 angularVelocity, bool upFromGravity,
        bool awake = true)
    {
        FrameWorld world = WorldFor(frame);
        if (record.Motion == BodyMotion.Character)
            record.Character = CreateCharacter(record, world, pose, linearVelocity, upFromGravity);
        else
            record.Body = CreateRigid(world, PrimarySpec(record), pose, linearVelocity, angularVelocity, awake);
        record.Frame = frame;
        record.World = world;
        world.AddMember(record);
    }

    /// <summary>
    /// Creates a frame's hull at the frame's pose and velocity in the tree. The tree holds the velocity of the frame
    /// origin; Jolt wants the velocity of the center of mass, which differs for a spinning hull whose center of
    /// mass is off the origin.
    /// </summary>
    private void CreateHullFromTree(BodyRecord record, FrameHandle parent)
    {
        DPose pose = frames.TreeLocalPose(record.OwnedFrame);
        var (linear, angular) = HullVelocityFromTree(record, pose);
        CreatePrimary(record, parent, pose, linear, angular, upFromGravity: false);
    }

    /// <summary>
    /// The velocities that the hull of a record's frame needs, in Jolt's terms, to move like the frame in the tree when the
    /// hull is at <paramref name="pose"/>: the linear velocity of the hull's center of mass, and the angular velocity.
    /// </summary>
    private (Vector3 Linear, Vector3 Angular) HullVelocityFromTree(BodyRecord record, in DPose pose)
    {
        // The tree holds the frame's velocity relative to its tree parent, the hull's physics home, so it is the local
        // velocity already: there is no frame velocity of the home to subtract. Jolt gets it as it is: for a
        // physics-driven frame, the tree's velocity is the hull's own chord from the last step. Only a new frame's hull
        // has the game's instant velocities, which the next step turns into chords (see ConvertSpawnVelocities).
        FrameHandle frame = record.OwnedFrame;
        Vector3 angular = frames.AngularVelocity(frame);
        Double3 centerOffset = pose.TransformDirection(shapes.Get(record.Desc.Shape).CenterOfMass);
        Double3 linear = frames.TreeLinearVelocity(frame) + Double3.Cross(angular, centerOffset);
        return (linear.ToVector3(), angular);
    }

    private void CreateInterior(BodyRecord record)
    {
        FrameWorld world = WorldFor(record.OwnedFrame);
        record.InteriorBody = CreateRigid(world, InteriorSpec(record), DPose.Identity, Vector3.Zero, Vector3.Zero);
        record.InteriorWorld = world;
    }

    /// <summary>
    /// What a rigid body is apart from its state. The backend changes none of it after the body exists, except the motion
    /// of a body whose <see cref="MotionChanges"/> is true: the hull of a frame follows the frame's driver (see
    /// <see cref="FollowDriver"/>), and a ghost of a mesh is static or kinematic as it holds still or moves (see
    /// <see cref="GhostMotion"/>). Its spec then has the new motion. So the spec of a body and its state rebuild the body
    /// exactly.
    /// </summary>
    /// <param name="MotionChanges">
    /// True when the backend can change the body's motion type: the hull of a frame, unless it is a mesh, which never
    /// moves, and a ghost of a mesh.
    /// </param>
    /// <param name="Freedom">How the body can move: freely, or only along the three axes, for the rigid form of a character.</param>
    private readonly record struct RigidSpec(ShapeDesc Shape, MotionType Motion, ObjectLayer Layer, ulong UserData, BodyDesc Material, bool MotionChanges = false,
        AllowedDOFs Freedom = AllowedDOFs.All);

    /// <summary>How the rigid form of a character can move: along the three axes, but it never turns.</summary>
    private const AllowedDOFs TranslationOnly = AllowedDOFs.TranslationX | AllowedDOFs.TranslationY | AllowedDOFs.TranslationZ;

    /// <summary>
    /// The spec of a record's primary rigid body: its own body, the rigid form of a character, or the hull of the frame that
    /// it owns. Its shape and material come from <see cref="BodyRecord.Material"/>.
    /// </summary>
    private static RigidSpec PrimarySpec(BodyRecord record) =>
        new(record.Material.Shape, ToMotionType(record.Motion), PrimaryLayer(record), BodyUserData.Encode(BodyKind.Primary, record.Entity), record.Material,
            MotionChanges: record.IsHull && !record.HasMesh, Freedom: record.IsCharacter ? TranslationOnly : AllowedDOFs.All);

    /// <summary>The spec of the static interior of the frame that a record owns, in the frame's own system.</summary>
    private static RigidSpec InteriorSpec(BodyRecord record) =>
        new(record.Desc.InteriorShape ?? record.Desc.Shape, MotionType.Static, InteriorLayer(record), BodyUserData.Encode(BodyKind.Interior, record.Entity), record.Desc);

    /// <summary>
    /// The spec of a ghost of a record's body with the Jolt motion <paramref name="motion"/>, which
    /// <see cref="GhostMotion"/> gives it. A ghost of a static body meets only what the static body meets (see
    /// <see cref="FrameWorld.StandsForStaticBody"/>). A ghost of a mesh changes its motion as it starts and stops moving.
    /// </summary>
    private static RigidSpec GhostSpec(BodyRecord record, MotionType motion) =>
        new(record.Desc.Shape, motion, GhostLayer(record), BodyUserData.Encode(BodyKind.Ghost, record.Entity), record.Desc, MotionChanges: record.HasMesh);

    /// <summary>
    /// The Jolt motion of a ghost of a record's body that moves in its system with <paramref name="linear"/> m/s and
    /// <paramref name="angular"/> rad/s. A ghost is kinematic, so that Jolt moves it with that velocity during a step, and
    /// a body that hits it meets a body that moves as the real body does relative to the ghost's frame: Jolt stops the
    /// body, bounces it by the real approach, and gives a character the ghost's velocity. Jolt moves a kinematic mesh
    /// too, given a mass (see <see cref="Settings"/>). A ghost of a mesh is static while it holds still, as its real body
    /// does, for example the ghost of a parked station's hull in a parked ship beside it. Jolt never simulates a static
    /// body. A kinematic ghost is awake for good, because the ghost pass sets its pose in every step, and Jolt searches
    /// the contacts of an awake body in every step, also with the bodies that sleep on it. With 100 boxes asleep on the
    /// ghost of a parked station's floor, a tick took 37 µs with a static ghost and 116 µs with a kinematic one
    /// (MeshGhostBenchmarks). A real body with a mesh is always static, so such a ghost holds still exactly when its
    /// velocity in its system is zero. (Before ter-dq92, the ghost of a mesh was always static, and the ghost pass moved it
    /// after every step, so Jolt saw it at rest: a crate in a falling frame passed into a mesh anchor.)
    /// </summary>
    private static MotionType GhostMotion(BodyRecord record, Vector3 linear, Vector3 angular) =>
        record.HasMesh && linear == Vector3.Zero && angular == Vector3.Zero ? MotionType.Static : MotionType.Kinematic;

    /// <summary>
    /// Creates a rigid body in the system of <paramref name="world"/>, which grows first if it is full. A body that moves is
    /// awake unless <paramref name="awake"/> is false.
    /// </summary>
    private BodyID CreateRigid(FrameWorld world, in RigidSpec spec, in DPose pose, Vector3 linearVelocity, Vector3 angularVelocity, bool awake = true)
    {
        EnsureRoom(world);
        using BodyCreationSettings settings = Settings(spec, pose.Position.ToJolt(), pose.Rotation.ToJolt(), linearVelocity, angularVelocity);
        BodyID id = world.Bodies.CreateAndAddBody(settings, spec.Motion == MotionType.Static || !awake ? Activation.DontActivate : Activation.Activate);
        if (id.IsInvalid)
            throw new InvalidOperationException($"Jolt refused to create a body in {world.Frame}, although its system had room.");
        return id;
    }

    /// <summary>
    /// The Jolt settings that create a rigid body from its spec and its state. Every rigid body comes from here, both when
    /// the backend creates it and when a growth moves it to a larger system.
    /// </summary>
    private BodyCreationSettings Settings(in RigidSpec spec, RVector3 position, Quaternion rotation, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        BodyDesc material = spec.Material;
        BodyCreationSettings settings = NativeOwnership.Owned(new BodyCreationSettings(shapes.Get(spec.Shape), position, rotation, spec.Motion, spec.Layer)
        {
            UserData = spec.UserData,
            Friction = material.Friction,
            Restitution = material.Restitution,
            LinearDamping = material.LinearDamping,
            AngularDamping = material.AngularDamping,
            MaxLinearVelocity = options.MaxLinearVelocity,
            // Jolt changes the motion type only of a body that it created to allow the change.
            AllowDynamicOrKinematic = spec.MotionChanges,
            // A ghost of a sensor is a sensor too, so that it pushes nothing in the other frame either.
            IsSensor = material.IsSensor,
            AllowedDOFs = spec.Freedom,
        });
        // Jolt gives a body that is or can become kinematic motion properties from its mass, but it cannot compute the mass
        // of a mesh, and would divide by zero. A mesh is never dynamic, and Jolt reads the mass only of a dynamic body, so
        // the mass of 1 kg only keeps Jolt's inverse mass finite. Only a ghost of a mesh moves (see GhostMotion).
        if ((spec.Motion != MotionType.Static || spec.MotionChanges) && spec.Shape.ContainsMesh())
        {
            settings.OverrideMassProperties = OverrideMassProperties.CalculateInertia;
            settings.MassPropertiesOverride = new MassProperties { Mass = 1 };
        }
        // A body whose motion can change keeps its mass while it is static or kinematic, for the time that it is dynamic.
        // A body without mass never becomes dynamic.
        else if (spec.Motion == MotionType.Dynamic || (spec.MotionChanges && material.Mass > 0))
        {
            settings.OverrideMassProperties = OverrideMassProperties.CalculateInertia;
            settings.MassPropertiesOverride = new MassProperties { Mass = material.Mass };
        }
        // Continuous collision only costs anything when the body moves far in one step.
        if (spec.Motion == MotionType.Dynamic)
            settings.MotionQuality = MotionQuality.LinearCast;
        if (spec.Motion != MotionType.Static)
        {
            settings.LinearVelocity = linearVelocity;
            settings.AngularVelocity = angularVelocity;
        }
        return settings;
    }

    /// <summary>
    /// Creates a character in the system of <paramref name="world"/>, which grows first if it is full. The new
    /// character has not stood yet. With <paramref name="upFromGravity"/>, up is against the gravity of the semantic frame (see
    /// <see cref="FrameTree.SemanticGravity"/>) when that is at least <see cref="FrameTree.ZeroGravityThreshold"/>, or the record's last up below it, and the
    /// rotation turns to that up. Without it, the character keeps the record's last up and the rotation of
    /// <paramref name="pose"/>, as a character in free fall does.
    /// </summary>
    private CharacterVirtual CreateCharacter(BodyRecord record, FrameWorld world, in DPose pose, Vector3 linearVelocity, bool upFromGravity)
    {
        // The character's inner body takes a place in the system.
        EnsureRoom(world);
        Quaternion rotation = pose.Rotation.ToJolt();
        record.Up = Vector3.Normalize(record.Up);
        if (upFromGravity)
        {
            // Up is against the semantic frame's gravity, which equals the effective gravity outside numeric frames.
            Vector3 gravity = frames.SemanticGravity(world.Frame, pose.Position);
            float strength = gravity.Length();
            if (strength >= FrameTree.ZeroGravityThreshold)
                record.Up = -gravity / strength;
            rotation = Rotations.AlignUp(rotation, record.Up);
        }
        record.Standing = false;
        CharacterVirtual character = NewCharacter(record, world.System, pose.Position.ToJolt(), rotation, linearVelocity);
        world.WalkingCharacterCount++;
        return character;
    }

    /// <summary>
    /// The character of a record in <paramref name="system"/>, with up along <see cref="BodyRecord.Up"/>. Every character
    /// comes from here, both when the backend creates it and when a growth moves it to a larger system.
    /// </summary>
    private CharacterVirtual NewCharacter(BodyRecord record, PhysicsSystem system, RVector3 position, Quaternion rotation, Vector3 linearVelocity)
    {
        var capsule = (CapsuleShapeDesc)record.Desc.Shape;
        Shape shape = shapes.Get(capsule);
        var settings = new CharacterVirtualSettings
        {
            Shape = shape,
            // The inner body lets rigid bodies collide with the character and lets it report contacts.
            InnerBodyShape = shape,
            InnerBodyLayer = PrimaryLayer(record),
            Up = record.Up,
            MaxSlopeAngle = options.CharacterMaxSlopeDegrees * (MathF.PI / 180f),
            Mass = record.Desc.Mass,
            MaxStrength = options.CharacterMaxStrength,
            // Jolt's own padding, which the rigid form's capsule includes (see RigidForm).
            CharacterPadding = CharacterPadding,
            // Jolt's own reach, which the rigid form's contact list has too (see NoteRigidCharacterContacts).
            PredictiveContactDistance = CharacterContactReach,
            // Contacts below the center of the lower hemisphere support the character.
            SupportingVolume = new Plane(Vector3.UnitY, -capsule.Radius),
        };
        CharacterVirtual character = NativeOwnership.Owned(new CharacterVirtual(settings, position, rotation, BodyUserData.Encode(BodyKind.CharacterInner, record.Entity), system));
        character.LinearVelocity = linearVelocity;
        return character;
    }

    // ---- Removal ----

    private void DestroyRecord(Entity entity)
    {
        if (Find(entity) is not { } record)
            return;
        RemoveGhosts(record);
        RemovePrimary(record);
        RemoveInterior(record);
        if (record.IsHull)
            frameOwners.Remove(record);
        records[entity.Index] = null;
    }

    private void RemovePrimary(BodyRecord record)
    {
        if (record.World is not { } world)
            return;
        if (record.Character is { } character)
        {
            world.WakeAround(character.InnerBodyID);
            LoosenContacts(world, character.InnerBodyID);
            character.Dispose();
            record.Character = null;
            world.WalkingCharacterCount--;
        }
        else if (record.Body.IsValid)
        {
            LoosenContacts(world, record.Body);
            world.RemoveBody(record.Body);
        }
        record.Body = BodyID.Invalid;
        world.RemoveMember(record);
        record.World = null;
        record.Frame = FrameHandle.Null;
    }

    private void RemoveInterior(BodyRecord record)
    {
        if (record.InteriorWorld is not { } world)
            return;
        LoosenContacts(world, record.InteriorBody);
        world.RemoveBody(record.InteriorBody);
        record.InteriorWorld = null;
        record.InteriorBody = BodyID.Invalid;
    }

    private void RemoveGhosts(BodyRecord record)
    {
        if (record.Ghosts is not { } ghosts)
            return;
        foreach (GhostBody ghost in ghosts)
            RemoveGhostBody(ghost);
        ghosts.Clear();
    }

    private void RemoveGhostIn(BodyRecord record, FrameWorld world)
    {
        if (record.Ghosts is not { } ghosts)
            return;
        for (int i = ghosts.Count - 1; i >= 0; i--)
            if (ghosts[i].World == world)
                RemoveGhostAt(ghosts, i);
    }

    /// <summary>
    /// Removes and destroys the ghost at <paramref name="index"/> of <paramref name="ghosts"/>, a record's ghosts, as
    /// <see cref="RemoveGhostBody"/> does.
    /// </summary>
    private void RemoveGhostAt(List<GhostBody> ghosts, int index)
    {
        RemoveGhostBody(ghosts[index]);
        ghosts.RemoveAt(index);
    }

    /// <summary>
    /// Removes and destroys a ghost and takes it out of its system's ghosts, after it wakes the bodies around it. A ghost's
    /// contacts are contacts of its entity, so they loosen as those of a real body do (see <see cref="LoosenContacts"/>).
    /// Call it while the ghost's record still exists.
    /// </summary>
    private void RemoveGhostBody(in GhostBody ghost)
    {
        LoosenContacts(ghost.World, ghost.Body);
        ghost.World.RemoveGhost(ghost.Body);
    }

    // ---- Moving frame hulls after the tree changed ----

    /// <summary>
    /// Puts a frame hull where the tree says: the simulation refused the hull's hand-off or reparented its frame on its
    /// own, or the frame's parent was destroyed. The hull takes the tree's pose and velocity. A frame that became a root
    /// loses its hull. A full system grows to take the hull. The bodies around the old hull wake, and so do the bodies
    /// around a new hull that is static, because Jolt does not simulate the move.
    /// </summary>
    private void PlaceHullFromTree(BodyRecord owner, FrameHandle parent)
    {
        if (!parent.IsNull && ExistingWorld(parent) is { } target)
            RemoveGhostIn(owner, target);
        RemovePrimary(owner);
        if (parent.IsNull)
            return;
        CreateHullFromTree(owner, parent);
        if (owner.Motion == BodyMotion.Static)
            owner.World!.WakeAround(owner.Body);
    }

    /// <summary>
    /// True when Jolt has <paramref name="body"/> at <paramref name="pose"/>, within the tolerance by which Jolt itself
    /// decides that a pose did not change.
    /// </summary>
    private static bool IsAt(BodyInterface bodies, BodyID body, in DPose pose)
    {
        bodies.GetRPosition(body, out RVector3 position);
        Quaternion rotation = bodies.GetRotation(body);
        Quaternion target = pose.Rotation.ToJolt();
        var difference = new Vector4(rotation.X - target.X, rotation.Y - target.Y, rotation.Z - target.Z, rotation.W - target.W);
        return (position.ToDouble3() - pose.Position).LengthSquared <= 1e-12 && difference.LengthSquared() <= 1e-12f;
    }

    // ---- Following a frame's driver ----
    // A hull gets the motion of BodyDesc.FrameHullMotion. The simulation gives a frame only a driver that its hull can
    // follow (see BodyDesc.FrameHullProblem), and the backend refuses to create a hull that cannot.

    /// <summary>
    /// Makes the record of a frame owner follow its frame's driver, after the simulation gave the frame a driver (see
    /// <see cref="FrameTree.DriverVersion"/>). Sets the record's driver and motion. Changes no body. Returns true when the
    /// frame got a driver since the record last followed it, also the driver that it had: the hull, if the owner has one,
    /// then needs <see cref="ApplyHullMotion"/>, which puts it at the frame's pose and velocity in the tree.
    /// </summary>
    /// <remarks>
    /// The simulation refuses a driver that the hull cannot follow, so such a driver in the tree comes only from a bug (see
    /// <see cref="ReportBug"/>). A Release build skips it: the record keeps its driver and motion, and physics reports no
    /// state for the frame, because the hull is not dynamic.
    /// </remarks>
    private bool FollowDriver(BodyRecord owner)
    {
        int version = frames.DriverVersion(owner.OwnedFrame);
        if (version == owner.DriverVersion)
            return false;
        MotionDriver driver = frames.Driver(owner.OwnedFrame);
        // A root frame has no parent, so its owner has no hull, and only a hull has to follow the driver.
        if (!frames.TreeParent(owner.OwnedFrame).IsNull && owner.Desc.FrameHullProblem(driver) is { } problem)
        {
            ReportBug($"Physics: {owner.OwnedFrame} has the {driver} driver, but the hull of {owner.Entity} {problem}. " +
                      $"Only a bug gives a frame that driver. The hull keeps the {owner.Driver} driver.");
            owner.DriverVersion = version;
            return false;
        }
        owner.DriverVersion = version;
        owner.Driver = driver;
        owner.Motion = BodyDesc.FrameHullMotion(driver);
        // A ghost meets what its real body meets, and a static body meets less (see FrameWorld.StandsForStaticBody).
        if (owner.Ghosts is { } ghosts)
            foreach (GhostBody ghost in ghosts)
                ghost.World.SetGhostOfStaticBody(ghost.Body, !owner.IsMovable);
        return true;
    }

    /// <summary>
    /// Gives a frame owner's hull the Jolt motion type of the record's motion, also when it has that type already. The hull
    /// keeps its BodyID, its system and its contacts. Like a hull that the reconcile moves to another system, it takes the
    /// frame's pose and velocity from the tree. If that moves the hull, the bodies around its old place wake, and for a
    /// static hull the bodies around its new place too.
    /// </summary>
    private void ApplyHullMotion(BodyRecord owner)
    {
        FrameWorld world = owner.World!;
        BodyInterface bodies = world.Bodies;
        BodyID hull = owner.Body;
        MotionType motion = ToMotionType(owner.Motion);
        Activation activation = motion == MotionType.Static ? Activation.DontActivate : Activation.Activate;
        DPose pose = frames.TreeLocalPose(owner.OwnedFrame);
        bool moves = !IsAt(bodies, hull, pose);
        if (moves)
            world.WakeAround(hull);
        bodies.SetMotionType(hull, motion, activation);
        // The same motion quality as a hull that the backend creates with this motion (see Settings).
        bodies.SetMotionQuality(hull, motion == MotionType.Dynamic ? MotionQuality.LinearCast : MotionQuality.Discrete);
        bodies.SetRPositionAndRotation(hull, pose.Position.ToJolt(), pose.Rotation.ToJolt(), activation);
        if (moves && motion == MotionType.Static)
            world.WakeAround(hull);
        // Jolt stops a body that becomes static.
        if (motion != MotionType.Static)
        {
            var (linear, angular) = HullVelocityFromTree(owner, pose);
            bodies.SetLinearAndAngularVelocity(hull, linear, angular);
        }
    }
}
