using System.Numerics;
using System.Runtime.InteropServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

public sealed partial class JoltPhysicsBackend
{
    /// <summary>How the rigid form of a character can move: along the three axes, but it never turns.</summary>
    private const AllowedDOFs TranslationOnly =
        AllowedDOFs.TranslationX | AllowedDOFs.TranslationY | AllowedDOFs.TranslationZ;

    // Scratch for FollowHullMotions: the owners whose hull took a new motion at the start of this step.
    private readonly List<BodyRecord> changedHulls = [];

    /// <summary>A primary body's state as Jolt has it, in its frame. Velocities are at the center of mass.</summary>
    private readonly record struct BodySnapshot(DPose Pose, Double3 CenterOfMass, Vector3 Linear, Vector3 Angular);

    private static BodySnapshot ReadPrimary(BodyRecord record)
    {
        Contract.Requires(record.HasPrimary);
        if (record.Character is { } character)
        {
            var position = character.RPosition.ToDouble3();
            return new(Pose: new(position, character.Rotation.ToDouble()), CenterOfMass: position,
                Linear: character.LinearVelocity, Angular: Vector3.Zero);
        }

        BodyInterface bodies = record.World.Bodies;
        bodies.GetRPosition(record.Body, out RVector3 origin);
        bodies.GetRCenterOfMassPosition(record.Body, out RVector3 centerOfMass);
        bodies.GetLinearAndAngularVelocity(record.Body, out Vector3 linear, out Vector3 angular);
        Quaternion rotation = bodies.GetRotation(record.Body);
        return new(Pose: new(origin.ToDouble3(), rotation.ToDouble()), CenterOfMass: centerOfMass.ToDouble3(),
            Linear: linear, Angular: angular);
    }

    /// <summary>The body origin of the primary body, in its frame.</summary>
    private static Double3 PrimaryPosition(BodyRecord record)
    {
        Contract.Requires(record.HasPrimary);
        if (record.Character is { } character)
            return character.RPosition.ToDouble3();
        record.World.Bodies.GetRPosition(record.Body, out RVector3 origin);
        return origin.ToDouble3();
    }

    private static MotionType ToMotionType(BodyMotion motion) => motion switch
    {
        BodyMotion.Static => MotionType.Static,
        BodyMotion.Kinematic => MotionType.Kinematic,
        BodyMotion.Dynamic or BodyMotion.Character => MotionType.Dynamic
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
        BodyMotion.Static or BodyMotion.Kinematic or BodyMotion.Dynamic => desc.Layer
    };

    /// <summary>
    /// The object layer of a record's primary body, which is also the layer that a character moves with. A frame's hull
    /// ignores the owner's collision groups (see <see cref="Layers"/>).
    /// </summary>
    private static ObjectLayer PrimaryLayer(BodyRecord record) =>
        record.IsHull
            ? Layers.FrameHull
            : Layers.For(RoleFor(record.Desc), record.CollisionGroups, record.CollisionMask);

    /// <summary>The object layer of the interior of the frame that a record owns.</summary>
    private static ObjectLayer InteriorLayer(BodyRecord record) =>
        Layers.For(BodyLayer.Static, record.CollisionGroups, record.CollisionMask);

    /// <summary>
    /// The object layer of a ghost of a record's body in <paramref name="role"/> (see <see cref="GhostBody.Role"/>): the
    /// layer of every hull for the frame hull role, and otherwise the layer of the role with the record's collision groups
    /// and mask.
    /// </summary>
    private static ObjectLayer GhostLayer(BodyRecord record, BodyLayer role) =>
        role == BodyLayer.FrameHull ? Layers.FrameHull : Layers.For(role, record.CollisionGroups, record.CollisionMask);

    // ---- Creation ----

    /// <summary>
    /// Creates the record and the bodies of one BodyCreate. Throws <see cref="ArgumentException"/> for a body that physics
    /// cannot create (see <see cref="BodyDesc.Problem"/> and <see cref="ShapeProblem"/>) before any state changes. Every body
    /// gets the motion of its description, which for a hull is the motion that the hull has in the next step (see
    /// <see cref="BodyCreate"/>),
    /// and the interior of the owner of a root frame is static. If anything fails after that, the record and every body that
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

        BodyDesc desc = create.Desc ??
                        throw new ArgumentException($"The BodyCreate for {entity} has no body description.");
        bool ownsFrame = frames.TryGetFrame(entity, out FrameHandle owned);
        // A root frame has no parent, so its owner has no hull, and its body is only the frame's static interior.
        BodyRole role = !ownsFrame ? BodyRole.OwnsNoFrame :
            ParentOf(owned).IsNull ? BodyRole.OwnsRootFrame : BodyRole.Hull;
        BodyMotion motion = role == BodyRole.OwnsRootFrame ? BodyMotion.Static : desc.Motion;
        if (desc.Problem(role) is { } problem)
            throw new ArgumentException($"{entity} cannot get a body: it {problem}.");

        var record = new BodyRecord
        {
            Entity = entity,
            Desc = desc,
            Material = desc.Motion == BodyMotion.Character ? RigidForm(desc) : desc,
            CollisionGroups = desc.CollisionGroups,
            CollisionMask = desc.CollisionMask
        };
        record.Motion = motion;
        if (ownsFrame)
        {
            record.OwnedFrame = owned;
        }
        else if (create.Frame.IsNull || !frames.IsAlive(create.Frame))
        {
            log($"Physics: {entity} was not created because its frame {create.Frame} is not alive.");
            return;
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
            log(
                $"Physics: {stale.Entity} still had a body when its slot was reused by {entity}. The old body is removed.");
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
                CreatePrimary(record: record, frame: create.Frame, pose: pose,
                    linearVelocity: LocalVelocity(create.Frame, create.LinearVelocity).ToVector3(),
                    angularVelocity: create.AngularVelocity,
                    upFromGravity: true);
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
    /// another system does. The new body has no velocity shadow: a spawn, a hand-off and a switch of a character's form
    /// give it velocities that no update expected (see <see cref="BodyRecord.Shadow"/>).
    /// </summary>
    private void CreatePrimary(BodyRecord record, FrameHandle frame, in DPose pose, Vector3 linearVelocity,
        Vector3 angularVelocity, bool upFromGravity,
        bool awake = true)
    {
        record.Shadow = default;
        FrameWorld world = WorldFor(frame);
        if (record.Motion == BodyMotion.Character)
            record.Character = CreateCharacter(record: record, world: world, pose: pose, linearVelocity: linearVelocity,
                upFromGravity: upFromGravity);
        else
            record.Body = CreateRigid(world: world, spec: PrimarySpec(record), pose: pose,
                linearVelocity: linearVelocity, angularVelocity: angularVelocity, awake: awake);
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
        (Vector3 linear, Vector3 angular) = HullVelocityFromTree(record, pose);
        CreatePrimary(record: record, frame: parent, pose: pose, linearVelocity: linear, angularVelocity: angular,
            upFromGravity: false);
    }

    /// <summary>
    /// The velocities that the hull of a record's frame needs, in Jolt's terms, to move like the frame in the tree when the
    /// hull is at <paramref name="pose"/>: the linear velocity of the hull's center of mass, and the angular velocity. A
    /// kinematic hull gets none: it moves only by its owner's motion intents, toward the end of each end pose or full sample,
    /// so without one it holds still in its tree parent, also in its first step (docs/plans/rails-components.md, "The flow
    /// per kind of entity").
    /// </summary>
    private (Vector3 Linear, Vector3 Angular) HullVelocityFromTree(BodyRecord record, in DPose pose)
    {
        if (record.Motion == BodyMotion.Kinematic)
            return (Vector3.Zero, Vector3.Zero);
        // The tree holds the frame's velocity relative to its tree parent, the hull's physics home, so it is the local
        // velocity already: there is no frame velocity of the home to subtract. It is the frame's instant velocity, from its
        // last step record or from its spawn, so a dynamic hull waits among the spawned bodies for the next step to turn it
        // into a chord (see ConvertSpawnVelocities).
        FrameHandle frame = record.OwnedFrame;
        Vector3 angular = frames.AngularVelocity(frame);
        Double3 centerOffset = pose.TransformDirection(shapes.Get(record.Desc.Shape).CenterOfMass);
        Double3 linear = frames.TreeLinearVelocity(frame) + Double3.Cross(angular, centerOffset);
        return (linear.ToVector3(), angular);
    }

    private void CreateInterior(BodyRecord record)
    {
        FrameWorld world = WorldFor(record.OwnedFrame);
        record.InteriorBody = CreateRigid(world: world, spec: InteriorSpec(record), pose: DPose.Identity,
            linearVelocity: Vector3.Zero, angularVelocity: Vector3.Zero);
        record.InteriorWorld = world;
    }

    /// <summary>
    /// What a rigid body is apart from its state. The backend changes none of it after the body exists, except the motion
    /// of a body whose <see cref="MotionChanges"/> is true: the hull of a frame takes a change of motion, and its interior
    /// follows it (see <see cref="FollowHullMotions"/>), and a ghost of a static body is static or kinematic as it holds
    /// still or moves (see <see cref="GhostMotion"/>). Its spec then has the new motion. So the spec of a body and its
    /// state rebuild the body exactly.
    /// </summary>
    /// <param name="Shape">The collision shape.</param>
    /// <param name="Motion">Jolt's motion type: static, kinematic or dynamic.</param>
    /// <param name="Layer">Jolt's object layer.</param>
    /// <param name="UserData">The value that Jolt keeps in the body for the backend, which names the body's record.</param>
    /// <param name="Material">The description that gives mass, friction, restitution and damping.</param>
    /// <param name="MotionChanges">
    /// True when the backend can change the body's motion type: the hull of a frame, unless it is a mesh, which never
    /// moves, the interior of a frame, and a ghost of a static body.
    /// </param>
    /// <param name="Freedom">How the body can move: freely, or only along the three axes, for the rigid form of a character.</param>
    private readonly record struct RigidSpec(
        ShapeDesc Shape,
        MotionType Motion,
        ObjectLayer Layer,
        ulong UserData,
        BodyDesc Material,
        bool MotionChanges = false,
        AllowedDOFs Freedom = AllowedDOFs.All);

    /// <summary>
    /// The spec of a record's primary rigid body: its own body, the rigid form of a character, or the hull of the frame that
    /// it owns. Its shape and material come from <see cref="BodyRecord.Material"/>.
    /// </summary>
    private static RigidSpec PrimarySpec(BodyRecord record) =>
        new(Shape: record.Material.Shape, Motion: ToMotionType(record.Motion), Layer: PrimaryLayer(record),
            UserData: BodyUserData.Encode(BodyKind.Primary, record.Entity), Material: record.Material,
            MotionChanges: record.IsHull && !record.HasMesh,
            Freedom: record.IsCharacter ? TranslationOnly : AllowedDOFs.All);

    /// <summary>
    /// The spec of the interior of the frame that a record owns, in the frame's own system, with the Jolt motion of
    /// <see cref="InteriorMotion"/>. Its motion changes with the hull's, so Jolt creates it to allow that and gives it a
    /// mass (see <see cref="Settings"/>).
    /// </summary>
    private static RigidSpec InteriorSpec(BodyRecord record) =>
        new(Shape: record.Desc.InteriorShape ?? record.Desc.Shape, Motion: InteriorMotion(record),
            Layer: InteriorLayer(record),
            UserData: BodyUserData.Encode(BodyKind.Interior, record.Entity),
            Material: record.Desc, MotionChanges: true);

    /// <summary>
    /// The Jolt motion of the interior of the frame that a record owns: static while the hull is static, and kinematic
    /// otherwise, at rest in the frame's own system (fork F4 of docs/plans/complete-ghosts.md). So a sensor detects the
    /// owner in the frame as a body of the hull's motion, as it detects the hull in the frame's tree parent and the hull's
    /// ghosts in other frames: a static or kinematic sensor detects the interior of a ship that moves, and only a dynamic
    /// sensor detects the interior of a parked ship. A kinematic interior at rest falls asleep as any body does. A root
    /// frame has no hull, so its interior is always static: the owner of a frame that is a root from its spawn has the
    /// static motion (see <see cref="CreateRecord"/>), and a frame that becomes a root later loses its hull (see
    /// <see cref="PlaceHullFromTree"/>), whatever the motion of that hull was.
    /// </summary>
    private static MotionType InteriorMotion(BodyRecord record) =>
        record.HasPrimary && record.Motion != BodyMotion.Static ? MotionType.Kinematic : MotionType.Static;

    /// <summary>
    /// Gives the interior of the frame that the record owns the Jolt motion of <see cref="InteriorMotion"/>, and wakes it
    /// when it turns kinematic. The interior keeps its BodyID and its contacts, and it does not move. True when its motion
    /// changed.
    /// </summary>
    private static bool FollowInteriorMotion(BodyRecord owner)
    {
        Contract.Requires(owner.HasInterior);
        BodyInterface bodies = owner.InteriorWorld.Bodies;
        MotionType motion = InteriorMotion(owner);
        if (bodies.GetMotionType(owner.InteriorBody) == motion)
            return false;
        bodies.SetMotionType(owner.InteriorBody, motion,
            motion == MotionType.Static ? Activation.DontActivate : Activation.Activate);
        return true;
    }

    /// <summary>
    /// The spec of a ghost of a record's body with the Jolt motion <paramref name="motion"/>, which
    /// <see cref="GhostMotion"/> gives it, in the object layer <paramref name="layer"/>, the layer of its role (see
    /// <see cref="GhostBody.Role"/>). A ghost meets only what its real body meets (see
    /// <see cref="FrameWorld.GhostContactMotion"/>). A ghost of a static body changes its motion as it starts and stops moving, so Jolt
    /// creates it to allow that and gives it a mass (see <see cref="Settings"/>).
    /// </summary>
    private static RigidSpec GhostSpec(BodyRecord record, MotionType motion, ObjectLayer layer) =>
        new(Shape: record.Desc.Shape, Motion: motion, Layer: layer,
            UserData: BodyUserData.Encode(BodyKind.Ghost, record.Entity), Material: record.Desc,
            MotionChanges: !record.IsMovable);

    /// <summary>
    /// The Jolt motion of a ghost of a record's body that moves in its system with <paramref name="linear"/> m/s and
    /// <paramref name="angular"/> rad/s. A ghost that moves is kinematic, so that Jolt moves it with that velocity during a
    /// step, and a body that hits it meets a body that moves as the real body does relative to the ghost's frame: Jolt
    /// stops the body, bounces it by the real approach, and gives a character the ghost's velocity. Jolt moves a kinematic
    /// mesh too, given a mass (see <see cref="Settings"/>). A ghost of a static body is static while it holds still, as
    /// its real body does, for example the ghost of a parked station's hull in a parked ship beside it, or of the slab
    /// under a parked shuttle in the shuttle's bubble. Jolt never simulates a static body, so the bodies that rest on the
    /// ghost sleep, and a system with no other awake body does not step. A kinematic ghost is awake for good, because the
    /// ghost pass sets its pose in every step, and Jolt searches the contacts of an awake body in every step, also with
    /// the bodies that sleep on it. With 100 boxes asleep on the ghost of a parked station's floor, a tick took 37 µs with
    /// a static ghost and 116 µs with a kinematic one (MeshGhostBenchmarks). A static ghost also meets exactly what its
    /// static body meets by Jolt's rule. A ghost of a body that can move is always kinematic. (Before ter-dq92, the ghost
    /// of a mesh was always static, and the ghost pass moved it after every step, so Jolt saw it at rest: a crate in a
    /// falling frame passed into a mesh anchor. Before ter-imi5, only the ghost of a mesh was static while it held still.)
    /// </summary>
    private static MotionType GhostMotion(BodyRecord record, Vector3 linear, Vector3 angular) =>
        !record.IsMovable && linear == Vector3.Zero && angular == Vector3.Zero
            ? MotionType.Static
            : MotionType.Kinematic;

    /// <summary>
    /// Creates a rigid body in the system of <paramref name="world"/>, which grows first if it is full. A body that moves is
    /// awake unless <paramref name="awake"/> is false.
    /// </summary>
    private BodyID CreateRigid(FrameWorld world, in RigidSpec spec, in DPose pose, Vector3 linearVelocity,
        Vector3 angularVelocity, bool awake = true)
    {
        EnsureRoom(world);
        using BodyCreationSettings settings = Settings(spec: spec, position: pose.Position.ToJolt(),
            rotation: pose.Rotation.ToJolt(),
            linearVelocity: linearVelocity, angularVelocity: angularVelocity);
        BodyID id = world.Bodies.CreateAndAddBody(settings,
            spec.Motion == MotionType.Static || !awake ? Activation.DontActivate : Activation.Activate);
        return id.IsInvalid
            ? throw new InvalidOperationException(
                $"Jolt refused to create a body in {world.Frame}, although its system had room.")
            : id;
    }

    /// <summary>
    /// The Jolt settings that create a rigid body from its spec and its state. Every rigid body comes from here, both when
    /// the backend creates it and when a growth moves it to a larger system.
    /// </summary>
    private BodyCreationSettings Settings(in RigidSpec spec, RVector3 position, Quaternion rotation,
        Vector3 linearVelocity, Vector3 angularVelocity)
    {
        BodyDesc material = spec.Material;
        BodyCreationSettings settings = NativeOwnership.Owned(
            new BodyCreationSettings(shape: shapes.Get(spec.Shape), position: position, rotation: rotation,
                motionType: spec.Motion, objectLayer: spec.Layer)
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
                // A body near the joint of two triangles of a mesh, or of two children of a compound, also meets the edge
                // of the neighbor piece there, and Jolt's contact with that edge tilts away from the joint. Jolt's active
                // edges never drop it for a body that lands, and manifold reduction averages it into the face contact, so a
                // ball that lands near a joint is pushed sideways, and a box that slides across the joints of a compound
                // trips at each one. Enhanced internal edge removal drops such a contact beyond about 5 mm from a flat
                // joint and 10 mm from a concave crease (ter-fb3w). Jolt uses it for a pair when either body has it, but it
                // removes only the internal edges of the pair's second body: the static or kinematic body of a pair with a
                // dynamic one, or of two dynamic bodies the one with the higher Jolt body ID
                // (PhysicsSystem::ProcessBodyPair). So a static or kinematic surface removes its edges for every dynamic
                // body that touches it, and a dynamic one only for a dynamic body of a lower ID (InternalEdgeRemovalTests).
                // Where the bodies move alike with and without it, it costs nothing that EdgeRemovalBenchmarks can see:
                // balls on the hulls of a ring, and balls and boxes on its mesh. Only a shape made of sharp pieces sets it,
                // because only there is the edge at a joint no feature of the surface. Between two rounded children of a
                // compound the groove is real: the flag would keep only one of its two contacts, and a ball would sink
                // 0.8 mm deeper and roll to and fro for seconds, where without it the ball rests in the groove
                // (ConvexRadiusTests). A convex body has no joints. Each body of an entity follows its own shape: a ghost
                // has the body's shape, and an interior its own.
                EnhancedInternalEdgeRemoval = spec.Shape.IsMadeOfSharpPieces()
            });
        // Jolt gives a body that is or can become kinematic motion properties from its mass, but it cannot compute the mass
        // of a mesh, and would divide by zero. A mesh is never dynamic, and Jolt reads the mass only of a dynamic body, so
        // the mass of 1 kg only keeps Jolt's inverse mass finite. Of a mesh, only a ghost moves (see GhostMotion).
        if ((spec.Motion != MotionType.Static || spec.MotionChanges) && spec.Shape.ContainsMesh())
        {
            settings.OverrideMassProperties = OverrideMassProperties.CalculateInertia;
            settings.MassPropertiesOverride = new() { Mass = 1 };
        }
        // A body whose motion can change keeps its mass while it is static or kinematic, for the time that it is dynamic.
        // A body without mass never becomes dynamic, and Jolt gives it the mass of its shape. A ghost of a static body takes
        // its body's mass the same way, though it is never dynamic: Jolt needs a finite mass to move it as a kinematic body.
        else if (spec.Motion == MotionType.Dynamic || (spec.MotionChanges && material.Mass > 0))
        {
            settings.OverrideMassProperties = OverrideMassProperties.CalculateInertia;
            settings.MassPropertiesOverride = new() { Mass = material.Mass };
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
    private CharacterVirtual CreateCharacter(BodyRecord record, FrameWorld world, in DPose pose, Vector3 linearVelocity,
        bool upFromGravity)
    {
        // The character's inner body takes a place in the system.
        EnsureRoom(world);
        Quaternion rotation = pose.Rotation.ToJolt();
        record.Up = Vector3.Normalize(record.Up);
        if (upFromGravity)
        {
            // Up is against the semantic frame's gravity, which equals the effective gravity outside numeric frames.
            var gravity = frames.SemanticGravity(world.Frame, pose.Position).ToVector3();
            float strength = gravity.Length();
            if (strength >= FrameTree.ZeroGravityThreshold)
                record.Up = -gravity / strength;
            rotation = Rotations.AlignUp(rotation, record.Up);
        }

        record.Standing = false;
        record.Held = false;
        CharacterVirtual character =
            NewCharacter(record: record, system: world.System, position: pose.Position.ToJolt(), rotation: rotation,
                linearVelocity: linearVelocity);
        world.WalkingCharacterCount++;
        return character;
    }

    /// <summary>
    /// The character of a record in <paramref name="system"/>, with up along <see cref="BodyRecord.Up"/>. Every character
    /// comes from here, both when the backend creates it and when a growth moves it to a larger system.
    /// </summary>
    private CharacterVirtual NewCharacter(BodyRecord record, PhysicsSystem system, RVector3 position,
        Quaternion rotation, Vector3 linearVelocity)
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
            SupportingVolume = new(Vector3.UnitY, -capsule.Radius),
            // A Jolt character finds its contacts by a shape query of its own, which ignores the flag of the body that
            // it touches, so the flag of a floor made of sharp pieces never reaches it. The query drops a contact with
            // an edge that a mesh marks inactive, but a compound of hulls marks none, so at each joint of the hulls of
            // a ring's floor, a character that walks across it meets the edge of the next hull and is lifted by up to
            // 3.7 mm. Enhanced internal edge removal drops those contacts (ter-fb3w, RevolvedSolidJointTests). The flag
            // acts on every floor that the query touches, a rounded groove and a mesh included. A character that stands
            // in the middle of the groove between two boxes of the default convex radius keeps only one of the groove's
            // two contacts, and it moves 1.1 mm aside and rests 0.15 mm higher; one that walks across the groove walks
            // as without the flag (InternalEdgeRemovalTests). On a ring's mesh, which needs no flag (it lifts a walking
            // character by 0.06 mm at most without it), a character that walks at 3 m/s dips up to 1.0 mm into a
            // joint's crease, against 0.6 mm without the flag. The engine accepts both: each is about 1 mm, and a
            // character's walk sets its own speed, so neither carries it on. The rigid form is a body, which follows
            // the floor's flag, so it keeps both contacts of a groove. The flag adds about 2 % to the tick of 100
            // characters that walk across the joints of a ring's hulls, and nothing on its mesh
            // (EdgeRemovalBenchmarks).
            EnhancedInternalEdgeRemoval = true
        };
        CharacterVirtual character = NativeOwnership.Owned(new CharacterVirtual(settings: settings, position: position,
            rotation: rotation,
            userData: BodyUserData.Encode(BodyKind.CharacterInner, record.Entity), physicsSystem: system));
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
    /// around a new hull that is static, because Jolt does not simulate the move. The interior follows the hull: it turns
    /// static when its frame becomes a root (see <see cref="InteriorMotion"/>), and each started pair of it that the new
    /// motion cannot make ends, as when a ship parks (see <see cref="EndPairsThatCannotTouch"/>).
    /// </summary>
    private void PlaceHullFromTree(BodyRecord owner, FrameHandle parent)
    {
        Contract.Requires(owner.HasInterior);
        if (!parent.IsNull && ExistingWorld(parent) is { } target)
            RemoveGhostIn(owner, target);
        RemovePrimary(owner);
        if (!parent.IsNull)
        {
            CreateHullFromTree(owner, parent);
            Contract.Assert(owner.HasPrimary);
            if (owner.Motion == BodyMotion.Dynamic)
                spawnedRigid.Add(owner);
            if (owner.Motion == BodyMotion.Static)
                owner.World.WakeAround(owner.Body);
        }

        if (FollowInteriorMotion(owner))
            EndPairsThatCannotTouch(owner.InteriorWorld, owner.InteriorBody);
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
        var difference = new Vector4(x: rotation.X - target.X, y: rotation.Y - target.Y, z: rotation.Z - target.Z,
            w: rotation.W - target.W);
        return (position.ToDouble3() - pose.Position).LengthSquared <= 1e-12 && difference.LengthSquared() <= 1e-12f;
    }

    // ---- Changes of a hull's motion ----

    /// <summary>
    /// Gives each hull the motion that it has in the running step, when the hull's record has another motion: the change of
    /// motion in the intent of its owner that the step applies, or else the stored motion of the last finished step (see
    /// <see cref="FrameTree.HullMotionInStep"/>, with the intents of <paramref name="intents"/>). Physics follows that
    /// motion at every step, as the reconcile follows the tree's structure (see <see cref="ReconcileFrames"/>). The tree stores
    /// a change at the sync point after the step that applied it, also when that step failed, so a step that failed loses no
    /// change: the next step that runs takes it from the stored motion. It runs at the start of the step, before anything
    /// reads a record's motion: the spawn velocities, the static hulls, the hull velocities and the intents. The hull and the
    /// interior keep their bodies and their systems, and the hull takes the frame's pose and velocity from the tree (see
    /// <see cref="ApplyHullMotion"/>). Once every hull of the step has its new motion, every pair of their bodies follows the
    /// new motions at once (case 3b of docs/plans/complete-ghosts.md): each ghost of such a hull is made again (see
    /// <see cref="MakeGhostsAgain"/>), so that Jolt offers its pairs to the contact filter with the new motion, and each
    /// started pair of the hull and of the interior that the new motions cannot make ends (see
    /// <see cref="EndPairsThatCannotTouch"/>). So the order of the owners does not change the result. Jolt finds the new
    /// pairs of a hull or an interior that starts to move, since it simulates a body that moves.
    /// </summary>
    /// <remarks>
    /// The simulation lets a hull take only a motion that the hull can take (see <see cref="BodyDesc.Problem"/>), so another
    /// motion comes only from a bug (see <see cref="ReportBug"/>). A Release build skips it in every step that finds it: the
    /// hull keeps its motion.
    /// </remarks>
    private void FollowHullMotions(ReadOnlySpan<PhysicsIntent> intents)
    {
        changedHulls.Clear();
        foreach (BodyRecord owner in frameOwners)
        {
            // The owner of a root frame has no hull, and the tree keeps no motion for it.
            if (!owner.HasPrimary || frames.HullMotionInStep(owner.OwnedFrame, intents) is not { } motion ||
                motion == owner.Motion)
                continue;
            if ((owner.Desc with { Motion = motion }).Problem(BodyRole.Hull) is { } problem)
            {
                ReportBug(
                    $"Physics: the running step gives the hull of {owner.Entity} the motion {motion}, but as a hull its body {problem}. " +
                    $"Only a bug gives it that motion, so the hull keeps the motion {owner.Motion}.");
                continue;
            }

            owner.Motion = motion;
            ApplyHullMotion(owner);
            changedHulls.Add(owner);
        }

        // Every hull of the step has its new motion now, and every ghost of one is new, so each check judges a pair by the
        // new motions of both bodies, whichever owner comes first. A pair of two hulls that both change, such as a lander
        // that parks on a platform that starts to move, goes on when the new motions allow it.
        foreach (BodyRecord owner in changedHulls)
            MakeGhostsAgain(owner);
        foreach (BodyRecord owner in changedHulls)
        {
            Contract.Assert(owner.HasPrimary && owner.HasInterior);
            EndPairsThatCannotTouch(owner.World, owner.Body);
            EndPairsThatCannotTouch(owner.InteriorWorld, owner.InteriorBody);
        }

        changedHulls.Clear();
    }

    /// <summary>
    /// Makes each ghost of a hull whose motion changed again: removes it, which loosens its contacts (see
    /// <see cref="RemoveGhostBody"/>), and puts a new ghost of the same role in its place at once, at the old one's pose
    /// and velocity, with the Jolt motion and the contact motion of the hull's new motion (see <see cref="CreateGhostBody"/>).
    /// A ghost's contact motion never changes, because Jolt offers the pairs of a body to the contact filter only once,
    /// when it adds them: the pairs of a new body. The carry of the end of the step keeps each loose contact that the new
    /// ghost can make, and ends the rest (see <see cref="ResolveLooseContacts"/>). The new ghost lags the hull by one step,
    /// as every ghost lags an intent that the step applies, and the ghost pass of the end of the step puts it where the
    /// hull is. The old ghost goes first, so its system has room for the new one and does not grow.
    /// </summary>
    private void MakeGhostsAgain(BodyRecord record)
    {
        if (record.Ghosts is not { } ghosts)
            return;
        foreach (ref GhostBody ghost in CollectionsMarshal.AsSpan(ghosts))
        {
            BodyInterface bodies = ghost.World.Bodies;
            bodies.GetRPosition(ghost.Body, out RVector3 position);
            var pose = new DPose(position.ToDouble3(), bodies.GetRotation(ghost.Body).ToDouble());
            bodies.GetLinearAndAngularVelocity(ghost.Body, out Vector3 linear, out Vector3 angular);
            RemoveGhostBody(ghost);
            ghost.Body = CreateGhostBody(record: record, world: ghost.World, pose: pose, linear: linear,
                angular: angular, role: ghost.Role);
        }
    }

    /// <summary>
    /// Creates a ghost of the record's body in <paramref name="world"/>, in <paramref name="role"/> (see
    /// <see cref="GhostBody.Role"/>), at <paramref name="pose"/>, moving with <paramref name="linear"/> and
    /// <paramref name="angular"/> there, with the Jolt motion of <see cref="GhostMotion"/>, and counts it among the ghosts
    /// of the system with the record's contact motion (see <see cref="FrameWorld.AddGhost"/>). Every ghost comes from here.
    /// </summary>
    private BodyID CreateGhostBody(BodyRecord record, FrameWorld world, in DPose pose, Vector3 linear, Vector3 angular,
        BodyLayer role)
    {
        BodyID id = CreateRigid(world: world,
            spec: GhostSpec(record, GhostMotion(record, linear, angular), GhostLayer(record, role)), pose: pose,
            linearVelocity: linear, angularVelocity: angular);
        world.AddGhost(id, GhostContactMotion(record));
        return id;
    }

    /// <summary>
    /// Gives a frame owner's hull the Jolt motion type of the record's motion, also when it has that type already, and the
    /// interior the motion of <see cref="InteriorMotion"/>, kinematic and at rest while the hull moves. The hull and the
    /// interior keep their BodyIDs, their systems and their contacts. Like a hull that the reconcile moves to another
    /// system, the hull takes the frame's pose and velocity from the tree. If that moves the hull, the bodies around its old
    /// place wake, and for a static hull the bodies around its new place too. The interior does not move.
    /// </summary>
    private void ApplyHullMotion(BodyRecord owner)
    {
        Contract.Requires(owner.HasPrimary);
        FrameWorld world = owner.World;
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
        bodies.SetRPositionAndRotation(bodyID: hull, position: pose.Position.ToJolt(), rotation: pose.Rotation.ToJolt(),
            activationMode: activation);
        if (moves && motion == MotionType.Static)
            world.WakeAround(hull);
        // Jolt stops a body that becomes static.
        if (motion != MotionType.Static)
        {
            (Vector3 linear, Vector3 angular) = HullVelocityFromTree(owner, pose);
            bodies.SetLinearAndAngularVelocity(hull, linear, angular);
            // The step set the hull's velocity, so a hull that a target moved in the step before, as a kinematic hull on
            // rails, does not stop at that target (see ApplyIntents): a hull whose engines come on coasts on.
            owner.VelocitySetStep = stepIndex;
            if (owner.Motion == BodyMotion.Dynamic)
                spawnedRigid.Add(owner);
        }

        FollowInteriorMotion(owner);
    }
}
